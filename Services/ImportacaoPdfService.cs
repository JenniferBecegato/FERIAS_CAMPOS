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
        void Finalizar()
        {
            if (nome is null) return;
            if (periodos.Count == 0) throw new InvalidDataException($"Nenhum período válido para {nome}.");
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
            }
            else if (!string.IsNullOrWhiteSpace(esquerda))
            {
                if (nome is null) throw new InvalidDataException("Nome de colaborador sem identificação no PDF.");
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
                throw new InvalidDataException($"Linha de período não reconhecida: {nome} — {dados}");
            DateTime Data(int i) => DateTime.ParseExact(match.Groups[i].Value, "dd/MM/yyyy", Cultura);
            var inicio = Data(1);
            var fim = Data(2);
            var prazo30 = Data(4);
            var prazo60 = Data(5);
            var saldo = decimal.Parse(match.Groups[6].Value, Cultura);
            if (fim < inicio || Data(3) != fim || prazo30.AddDays(-30) != prazo60 || saldo > 30)
                throw new InvalidDataException($"Datas ou saldo inválidos para {nome}.");
            // O relatório fornece o último início para 30 dias; o app guarda o término limite.
            periodos.Add(new(inicio, fim, prazo30.AddDays(29), saldo));
        }
        Finalizar();
        if (colaboradores.Count == 0) throw new InvalidDataException("Nenhum colaborador encontrado no PDF.");
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
        var existentes = await database.Colaboradores.Include(c => c.Periodos).ThenInclude(p => p.Movimentacoes).ToListAsync();
        int criados = 0, novosPeriodos = 0, repetidos = 0;
        var avisos = leitura.Avisos.ToList();
        foreach (var origem in leitura.Colaboradores)
        {
            var encontrados = existentes.Where(c => Normalizar(c.Nome) == Normalizar(origem.Nome)).ToList();
            if (encontrados.Count > 1)
                throw new InvalidDataException($"Há mais de um cadastro com o nome {origem.Nome}. Resolva a duplicidade antes de importar.");
            var colaborador = encontrados.SingleOrDefault();
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
                var iguais = colaborador.Periodos.Where(p => p.Inicio.Date == source.Inicio && p.Fim.Date == source.Fim).ToList();
                if (iguais.Count > 0)
                {
                    repetidos++;
                    if (iguais.Any(p => p.Saldo != source.Saldo))
                        avisos.Add($"{origem.Nome}, {source.Inicio:dd/MM/yyyy}: período existente preservado; saldo atual difere do saldo do PDF arredondado para baixo ({source.Saldo.ToString("0.00", CultureInfo.GetCultureInfo("pt-BR"))}).");
                    continue;
                }
                if (colaborador.Periodos.Any(p => p.Inicio.Date <= source.Fim && p.Fim.Date >= source.Inicio))
                    throw new InvalidDataException($"{origem.Nome}: período do PDF sobrepõe um período cadastrado. Nenhuma alteração foi salva.");
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

    private static string Normalizar(string nome) => Regex.Replace(
        string.Concat(nome.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)),
        @"\s+", " ").Trim().ToUpperInvariant();
}
