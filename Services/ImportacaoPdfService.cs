using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FeriasCampos.Data;
using FeriasCampos.Models;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace FeriasCampos.Services;

public sealed record PeriodoPdf(DateTime Inicio, DateTime Fim, DateTime Vencimento, decimal Saldo);
public sealed record ColaboradorPdf(string Nome, IReadOnlyList<PeriodoPdf> Periodos);
public sealed record LeituraPdf(IReadOnlyList<ColaboradorPdf> Colaboradores, IReadOnlyList<string> Avisos);
public sealed record ResultadoImportacaoPdf(int ColaboradoresCriados, int PeriodosCriados,
    int PeriodosExistentes, IReadOnlyList<string> Avisos);

public static class LeitorPrevisaoFeriasPdf
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Regex LinhaPeriodo = new(
        @"^(\d{2}/\d{2}/\d{4})\s+a\s+(\d{2}/\d{2}/\d{4})\s+(\d{2}/\d{2}/\d{4})\s+(\d{2}/\d{2}/\d{4})\s+(\d{2}/\d{2}/\d{4})\s+(\d+,\d{2})$",
        RegexOptions.CultureInvariant);

    public static LeituraPdf Ler(string arquivo)
    {
        using var document = PdfDocument.Open(arquivo);
        var linhas = new List<(string Nome, string Dados)>();
        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom).ToList();
            if (!page.Text.Contains("Escala de vencimentos", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O PDF deve ser uma Escala de vencimentos no formato SCI fornecido.");
            var emTabela = false;
            while (words.Count > 0)
            {
                var y = words[0].BoundingBox.Bottom;
                var row = words.Where(w => Math.Abs(w.BoundingBox.Bottom - y) < 3)
                    .OrderBy(w => w.BoundingBox.Left).ToList();
                words.RemoveAll(w => row.Contains(w));
                var text = string.Join(" ", row.Select(w => w.Text));
                if (text.Contains("30 dias") && text.Contains("60 dias")) { emTabela = true; continue; }
                if (text.StartsWith("IDEALLY", StringComparison.OrdinalIgnoreCase)) break;
                if (!emTabela) continue;
                linhas.Add((string.Join(" ", row.Where(w => w.BoundingBox.Left < page.Width * .38).Select(w => w.Text)),
                    string.Join(" ", row.Where(w => w.BoundingBox.Left >= page.Width * .38).Select(w => w.Text))));
            }
        }
        return LerLinhas(linhas);
    }

    public static LeituraPdf LerLinhas(IEnumerable<(string Nome, string Dados)> linhas)
    {
        var colaboradores = new List<ColaboradorPdf>();
        var avisos = new List<string>();
        string? nome = null;
        var periodos = new List<PeriodoPdf>();
        var erros = new List<string>();
        var ignorados = new HashSet<string>();
        void Finalizar()
        {
            if (nome is null) return;
            if (periodos.Count == 0 && erros.Count == 0) erros.Add("nenhum período válido");
            if (erros.Count > 0)
            {
                avisos.Add($"{nome}: colaborador não importado — {string.Join("; ", erros.Distinct())}.");
                ignorados.Add(ImportacaoPdfService.Normalizar(nome));
                return;
            }
            colaboradores.Add(new(nome, periodos.ToArray()));
        }
        foreach (var (esquerda, dados) in linhas)
        {
            var novo = Regex.Match(esquerda, @"^\d+\s+(.+)$");
            if (novo.Success)
            {
                Finalizar();
                nome = novo.Groups[1].Value.Trim();
                periodos = [];
                erros = [];
            }
            else if (!string.IsNullOrWhiteSpace(esquerda))
            {
                if (nome is null)
                {
                    avisos.Add($"{esquerda.Trim()}: registro ignorado; nome sem identificação no PDF.");
                    continue;
                }
                nome += " " + esquerda.Trim();
            }
            if (string.IsNullOrWhiteSpace(dados)) continue;
            if (dados.Contains("perdido por afastamento", StringComparison.OrdinalIgnoreCase))
            {
                avisos.Add($"{nome}: período perdido por afastamento ignorado; o PDF não informa início e saldo válidos.");
                continue;
            }
            var match = LinhaPeriodo.Match(dados.Trim());
            if (nome is null || !match.Success)
            {
                if (nome is null) avisos.Add($"Registro sem colaborador identificado ignorado: {dados}");
                else erros.Add($"linha de período não reconhecida: {dados}");
                continue;
            }
            var datas = new DateTime[5];
            var datasValidas = Enumerable.Range(0, 5).All(i => DateTime.TryParseExact(
                match.Groups[i + 1].Value, "dd/MM/yyyy", Cultura, DateTimeStyles.None, out datas[i]));
            if (!datasValidas || !decimal.TryParse(match.Groups[6].Value, NumberStyles.Number, Cultura, out var saldo) ||
                datas[1] < datas[0] || datas[2] != datas[1] ||
                (datas[3] - datas[4]).TotalDays != 30 || saldo < 0 || saldo > 30 ||
                datas[3] > DateTime.MaxValue.AddDays(-29) || datas[3].AddDays(29) < datas[1])
            {
                erros.Add("datas ou saldo inválidos");
                continue;
            }
            var inicio = datas[0];
            var fim = datas[1];
            var prazo30 = datas[3];
            // O relatório fornece o último início para 30 dias; o app guarda o término limite.
            periodos.Add(new(inicio, fim, prazo30.AddDays(29), saldo));
        }
        Finalizar();
        colaboradores.RemoveAll(c => ignorados.Contains(ImportacaoPdfService.Normalizar(c.Nome)));
        if (colaboradores.Count == 0 && avisos.Count == 0) throw new InvalidDataException("Nenhum colaborador encontrado no PDF.");
        return new(colaboradores, avisos);
    }
}

public sealed class ImportacaoPdfService(IDbContextFactory<FeriasDbContext> databaseFactory) : IImportacaoPdfService
{
    public async Task<ResultadoImportacaoPdf> ImportarAsync(string arquivo, string unidade)
    {
        var leitura = await Task.Run(() => LeitorPrevisaoFeriasPdf.Ler(arquivo));
        return await ImportarLeituraAsync(leitura, unidade);
    }

    public async Task<ResultadoImportacaoPdf> ImportarLeituraAsync(LeituraPdf leitura, string unidade)
    {
        if (unidade is not ("Washington Luiz" or "Gurgel"))
            throw new InvalidDataException("Selecione a unidade dos novos colaboradores.");
        await using var database = await databaseFactory.CreateDbContextAsync();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var existentes = await database.Colaboradores.IgnoreQueryFilters().Include(c => c.Periodos).ThenInclude(p => p.Movimentacoes).ToListAsync();
        int criados = 0, novosPeriodos = 0, repetidos = 0;
        var avisos = leitura.Avisos.ToList();
        foreach (var grupo in leitura.Colaboradores.GroupBy(c => Normalizar(c.Nome)))
        {
            var origem = new ColaboradorPdf(grupo.First().Nome, grupo.SelectMany(c => c.Periodos).ToArray());
            void Avisar(string motivo) => avisos.Add($"{origem.Nome}: colaborador não importado — {motivo}.");
            if (string.IsNullOrWhiteSpace(origem.Nome) || origem.Periodos.Count == 0 ||
                origem.Periodos.Any(p => p.Fim < p.Inicio || p.Vencimento < p.Fim || p.Saldo < 0 || p.Saldo > 30))
            {
                Avisar("nome, datas ou saldo inválidos, ou nenhum período informado");
                continue;
            }
            var encontrados = existentes.Where(c => Normalizar(c.Nome) == Normalizar(origem.Nome)).ToList();
            if (encontrados.Count > 1)
            {
                Avisar("há mais de um cadastro com esse nome; resolva a duplicidade antes de importar novamente");
                continue;
            }
            var colaborador = encontrados.SingleOrDefault();
            if (colaborador is null)
            {
                var semelhantes = existentes.Where(c => NomesSemelhantes(c.Nome, origem.Nome)).ToList();
                if (semelhantes.Count > 0)
                {
                    Avisar($"Possível cadastro duplicado. Já existe: {string.Join(", ", semelhantes.Select(c => c.Nome))}. Confira e padronize o nome antes de importar novamente");
                    continue;
                }
            }
            var datasPeriodos = colaborador?.Periodos.Select(p => (p.Inicio.Date, p.Fim.Date)).ToList() ?? [];
            var conflito = false;
            foreach (var p in origem.Periodos)
            {
                var datas = (p.Inicio.Date, p.Fim.Date);
                if (datasPeriodos.Contains(datas)) continue;
                if (datasPeriodos.Any(d => d.Item1 <= datas.Item2 && d.Item2 >= datas.Item1))
                {
                    conflito = true;
                    break;
                }
                datasPeriodos.Add(datas);
            }
            if (conflito)
            {
                Avisar("períodos do PDF sobrepostos entre si ou a um período cadastrado");
                continue;
            }
            if (colaborador is null)
            {
                colaborador = new Colaborador { Nome = origem.Nome, Cpf = "", Unidade = unidade,
                    Admissao = origem.Periodos.Min(p => p.Inicio) };
                database.Colaboradores.Add(colaborador);
                existentes.Add(colaborador);
                criados++;
            }
            foreach (var periodoPdf in origem.Periodos)
            {
                var source = periodoPdf with { Saldo = decimal.Floor(periodoPdf.Saldo) };
                var iguais = colaborador.Periodos.Where(p => p.Inicio.Date == source.Inicio.Date && p.Fim.Date == source.Fim.Date).ToList();
                if (iguais.Count > 0)
                {
                    repetidos++;
                    if (iguais.Any(p => p.Saldo != source.Saldo))
                        avisos.Add($"{origem.Nome}, {source.Inicio:dd/MM/yyyy}: período existente preservado; saldo atual difere do saldo do PDF arredondado para baixo ({source.Saldo.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"))}).");
                    continue;
                }
                var periodo = new PeriodoAquisitivo { Inicio = source.Inicio, Fim = source.Fim,
                    Vencimento = source.Vencimento, DireitoDias = 30,
                    Status = source.Saldo == 0 ? StatusPeriodo.Completo : source.Vencimento < DateTime.Today ? StatusPeriodo.Vencido
                        : source.Fim >= DateTime.Today ? StatusPeriodo.EmAquisicao : source.Saldo < 30 ? StatusPeriodo.Parcial : StatusPeriodo.Disponivel };
                periodo.Movimentacoes.Add(new() { Tipo = TipoMovimentacao.Aquisicao, Dias = source.Saldo,
                    Motivo = "Saldo inicial importado da previsão de férias SCI" });
                colaborador.Periodos.Add(periodo);
                novosPeriodos++;
            }
        }
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(criados, novosPeriodos, repetidos, avisos);
    }

    internal static string Normalizar(string nome) => Regex.Replace(
        string.Concat(nome.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)),
        @"\s+", " ").Trim().ToUpperInvariant();

    // Sem identificador no PDF, semelhança exige revisão, nunca associação automática.
    private static bool NomesSemelhantes(string primeiro, string segundo)
    {
        var a = Normalizar(primeiro);
        var b = Normalizar(segundo);
        if (a.Length < 10 || b.Length < 10) return false;
        var menor = a.Length <= b.Length ? a : b;
        var maior = a.Length <= b.Length ? b : a;
        var palavras = maior.Split(' ');
        // Nomes abreviados por omissão de sobrenomes.
        if (menor.Split(' ').Length >= 2 &&
            menor.Split(' ').All(p => palavras.Contains(p)) &&
            a.Split(' ')[0] == b.Split(' ')[0]) return true;
        if (Math.Abs(a.Length - b.Length) > 2) return false;
        var anterior = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var atual = new int[b.Length + 1];
            atual[0] = i;
            for (var j = 1; j <= b.Length; j++)
                atual[j] = Math.Min(Math.Min(atual[j - 1] + 1, anterior[j] + 1),
                    anterior[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            anterior = atual;
        }
        return anterior[b.Length] <= 2;
    }
}
