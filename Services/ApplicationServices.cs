using System.IO;
using System.Text;
using FeriasCampos.Data;
using FeriasCampos.Models;
using Microsoft.EntityFrameworkCore;

namespace FeriasCampos.Services;

public sealed class ColaboradorService(
    IDbContextFactory<FeriasDbContext> databaseFactory) : IColaboradorService
{
    public async Task<DashboardDto> DashboardAsync(string? busca = null)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        var employees = await database.Colaboradores
            .Include(colaborador => colaborador.Periodos)
            .ThenInclude(periodo => periodo.Movimentacoes)
            .Where(colaborador => colaborador.Ativo)
            .ToListAsync();

        if (employees.Sum(employee =>
                PeriodoAquisitivoMaintenance.Atualizar(employee, DateTime.Today)) > 0)
        {
            await database.SaveChangesAsync();
        }

        var query = database.Periodos
            .AsNoTracking()
            .Include(periodo => periodo.Colaborador)
            .Include(periodo => periodo.Movimentacoes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            query = query.Where(periodo =>
                periodo.Colaborador.Nome.Contains(busca));
        }

        var periods = await query
            .OrderBy(periodo => periodo.Colaborador.Nome)
            .ToListAsync();

        var rows = periods.Select(ToRow).ToList();
        var totalEmployees = await database.Colaboradores
            .CountAsync(colaborador => colaborador.Ativo);

        return new DashboardDto(
            totalEmployees,
            periods.Count(periodo => periodo.Agendados > 0),
            periods.Count(periodo =>
                periodo.Vencimento <= DateTime.Today.AddDays(60) &&
                periodo.Saldo > 0),
            periods.Count(periodo =>
                periodo.Agendados == 0 && periodo.Saldo > 0),
            rows);
    }

    public async Task<PeriodoAquisitivo?> PeriodoAsync(int id)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();

        return await database.Periodos
            .AsNoTracking()
            .Include(periodo => periodo.Colaborador)
            .Include(periodo => periodo.Movimentacoes)
            .FirstOrDefaultAsync(periodo => periodo.Id == id);
    }

    public async Task<IReadOnlyList<Colaborador>> ListarAsync()
    {
        await using var database = await databaseFactory.CreateDbContextAsync();

        return await database.Colaboradores
            .AsNoTracking()
            .OrderBy(colaborador => colaborador.Nome)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<FeriasAgendaItem>> ListarAgendaAsync()
    {
        await using var database = await databaseFactory.CreateDbContextAsync();

        return await database.Movimentacoes
            .AsNoTracking()
            .Where(movement =>
                movement.Tipo == TipoMovimentacao.Agendamento &&
                movement.Inicio != null &&
                movement.Fim != null)
            .OrderBy(movement => movement.Inicio)
            .Select(movement => new FeriasAgendaItem(
                movement.Periodo.ColaboradorId,
                movement.Periodo.Colaborador.Nome,
                movement.Inicio!.Value,
                movement.Fim!.Value))
            .ToListAsync();
    }

    public async Task<ResultadoValidacao> CadastrarAsync(NovoColaboradorDto novo)
    {
        var errors = ValidateNewEmployee(novo);
        var cpf = OnlyDigits(novo.Cpf);
        var registration = novo.Matricula.Trim();

        await using var database = await databaseFactory.CreateDbContextAsync();
        await AddDuplicateErrorsAsync(database, cpf, registration, errors);

        if (errors.Count > 0)
        {
            return new ResultadoValidacao(false, errors, []);
        }

        var employee = CreateEmployee(novo, cpf, registration);
        database.Colaboradores.Add(employee);
        await database.SaveChangesAsync();

        return new ResultadoValidacao(true, [], []);
    }

    private static PeriodoRow ToRow(PeriodoAquisitivo period)
    {
        return new PeriodoRow(
            period.Id,
            period.ColaboradorId,
            period.Colaborador.Iniciais,
            period.Colaborador.Nome,
            $"{period.Inicio:dd/MM/yyyy} a\n{period.Fim:dd/MM/yyyy}",
            period.Vencimento.ToString("dd/MM/yyyy"),
            period.DireitoDias,
            period.Agendados,
            period.Vendidos,
            period.Folgas,
            period.Saldo,
            GetStatusName(period));
    }

    private static List<string> ValidateNewEmployee(NovoColaboradorDto employee)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(employee.Nome) || employee.Nome.Trim().Length < 3)
        {
            errors.Add("Informe o nome completo.");
        }

        if (OnlyDigits(employee.Cpf).Length != 11)
        {
            errors.Add("O CPF deve possuir 11 dígitos.");
        }

        if (string.IsNullOrWhiteSpace(employee.Matricula))
        {
            errors.Add("Informe a matrícula.");
        }

        if (employee.Admissao.Date > DateTime.Today)
        {
            errors.Add("A admissão não pode estar no futuro.");
        }

        if (employee.DireitoDias is < 1 or > 30)
        {
            errors.Add("O direito deve estar entre 1 e 30 dias.");
        }

        return errors;
    }

    private static async Task AddDuplicateErrorsAsync(
        FeriasDbContext database,
        string cpf,
        string registration,
        ICollection<string> errors)
    {
        if (await database.Colaboradores.AnyAsync(employee => employee.Cpf == cpf))
        {
            errors.Add("Já existe um colaborador com este CPF.");
        }

        if (await database.Colaboradores.AnyAsync(employee =>
            employee.Matricula == registration))
        {
            errors.Add("Já existe um colaborador com esta matrícula.");
        }
    }

    private static Colaborador CreateEmployee(
        NovoColaboradorDto source,
        string cpf,
        string registration)
    {
        var admissionDate = source.Admissao.Date;
        var employee = new Colaborador
        {
            Nome = source.Nome.Trim(),
            Cpf = cpf,
            Matricula = registration,
            Admissao = admissionDate,
            Cargo = source.Cargo.Trim(),
            Setor = source.Setor.Trim(),
            Unidade = source.Unidade.Trim()
        };

        var period = new PeriodoAquisitivo
        {
            Inicio = admissionDate,
            Fim = admissionDate.AddYears(1).AddDays(-1),
            Vencimento = admissionDate.AddYears(2).AddDays(-1),
            DireitoDias = source.DireitoDias,
            Status = admissionDate.AddYears(1) <= DateTime.Today
                ? StatusPeriodo.Disponivel
                : StatusPeriodo.EmAquisicao
        };

        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Aquisicao,
            Dias = source.DireitoDias,
            Motivo = "Saldo inicial criado no cadastro"
        });

        employee.Periodos.Add(period);
        return employee;
    }

    private static string OnlyDigits(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static string GetStatusName(PeriodoAquisitivo period)
    {
        if (period.Saldo > 0 && period.Vencimento.Date < DateTime.Today)
        {
            return "Vencido";
        }

        return period.Status switch
        {
            StatusPeriodo.EmAquisicao => "Em aquisição",
            StatusPeriodo.Atencao => "Atenção",
            _ => period.Status.ToString()
        };
    }
}

public sealed class PeriodoService(
    IDbContextFactory<FeriasDbContext> databaseFactory) : IPeriodoService
{
    public async Task<IReadOnlyList<PeriodoAquisitivo>> ListarAsync()
    {
        await using var database = await databaseFactory.CreateDbContextAsync();

        return await database.Periodos
            .AsNoTracking()
            .Include(periodo => periodo.Colaborador)
            .Include(periodo => periodo.Movimentacoes)
            .ToListAsync();
    }
}

public sealed class RegraFeriasEngine : IRegraFeriasEngine
{
    public ResultadoValidacao Validar(
        PeriodoAquisitivo periodo,
        DateTime inicio,
        DateTime fim,
        IEnumerable<Feriado> feriados,
        bool bloquearAgendamentoMenos30Dias = false) =>
        Validar(periodo, [new IntervaloFerias(inicio, fim)], feriados,
            bloquearAgendamentoMenos30Dias, false);

    public ResultadoValidacao Validar(
        PeriodoAquisitivo periodo,
        IReadOnlyList<IntervaloFerias> intervalos,
        IEnumerable<Feriado> feriados,
        bool bloquearAgendamentoMenos30Dias = false,
        bool bloquearInicioAntesRepousoSemanal = true,
        int diasAbono = 0)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var novos = intervalos
            .Select(item => new IntervaloFerias(item.Inicio.Date, item.Fim.Date))
            .OrderBy(item => item.Inicio)
            .ToList();

        if (novos.Count == 0)
        {
            errors.Add("Adicione ao menos uma parcela de férias.");
            return new ResultadoValidacao(false, errors, warnings);
        }

        var existentes = periodo.Movimentacoes
            .Where(item => item.Tipo == TipoMovimentacao.Agendamento &&
                           item.Inicio is not null && item.Fim is not null)
            .Select(item => new IntervaloFerias(item.Inicio!.Value.Date, item.Fim!.Value.Date))
            .ToList();

        if (existentes.Count + novos.Count > 3)
        {
            errors.Add("O limite de três parcelas já foi atingido.");
        }

        foreach (var item in novos)
        {
            ValidateRange(periodo, item, feriados, bloquearAgendamentoMenos30Dias,
                bloquearInicioAntesRepousoSemanal, errors, warnings);
        }

        ValidateOverlaps(existentes, novos, errors);
        var totalDays = novos.Where(item => item.Dias > 0).Sum(item => item.Dias);
        ValidateAllowance(periodo, diasAbono, errors);
        var consumedDays = (long)totalDays + diasAbono;
        if (consumedDays > periodo.Saldo)
        {
            errors.Add($"O período possui somente {periodo.Saldo} dias disponíveis.");
        }

        ValidateInstallments(
            periodo,
            existentes.Concat(novos).ToList(),
            consumedDays,
            errors);

        return new ResultadoValidacao(
            errors.Count == 0,
            errors.Distinct().ToList(),
            warnings.Distinct().ToList());
    }

    private static void ValidateAllowance(
        PeriodoAquisitivo period,
        int allowanceDays,
        ICollection<string> errors)
    {
        if (allowanceDays < 0)
        {
            errors.Add("A quantidade de dias vendidos não pode ser negativa.");
            return;
        }

        var maximum = period.DireitoDias / 3;
        if (period.Vendidos + allowanceDays > maximum)
        {
            errors.Add(
                $"O abono pecuniário não pode ultrapassar {maximum} dias " +
                "neste período aquisitivo.");
        }
    }

    private static void ValidateRange(
        PeriodoAquisitivo period, IntervaloFerias range, IEnumerable<Feriado> holidays,
        bool blockNotice, bool blockWeeklyRest,
        ICollection<string> errors, ICollection<string> warnings)
    {
        if (range.Fim < range.Inicio)
        {
            errors.Add("A data final não pode ser anterior à inicial.");
            return;
        }

        if (range.Dias < 5)
        {
            errors.Add("Uma parcela de férias deve possuir pelo menos 5 dias corridos.");
        }

        var firstConcessionDay = period.Fim.Date.AddDays(1);
        if (range.Inicio < firstConcessionDay)
        {
            errors.Add(
                $"As férias deste período só podem começar a partir de " +
                $"{firstConcessionDay:dd/MM/yyyy}, após o término da aquisição.");
        }

        if (range.Inicio > period.Vencimento.Date || range.Fim > period.Vencimento.Date)
        {
            errors.Add(
                $"As férias devem terminar até o vencimento do período " +
                $"({period.Vencimento:dd/MM/yyyy}).");
        }

        if (holidays.Any(holiday =>
                holiday.Data.Date == range.Inicio.AddDays(1) ||
                holiday.Data.Date == range.Inicio.AddDays(2)))
        {
            errors.Add("O início ocorre nos dois dias anteriores a um feriado.");
        }

        if (range.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
        {
            const string message =
                "O início ocorre nos dois dias anteriores ao repouso semanal de domingo.";
            if (blockWeeklyRest) errors.Add(message); else warnings.Add(message);
        }

        if (range.Inicio < DateTime.Today.AddDays(30))
        {
            const string message = "O início das férias deve respeitar 30 dias de antecedência.";
            if (blockNotice) errors.Add(message); else warnings.Add(message);
        }
    }

    private static void ValidateOverlaps(
        IReadOnlyList<IntervaloFerias> existing,
        IReadOnlyList<IntervaloFerias> added,
        ICollection<string> errors)
    {
        var all = existing.Select(item => (Range: item, IsNew: false))
            .Concat(added.Select(item => (Range: item, IsNew: true)))
            .OrderBy(item => item.Range.Inicio)
            .ToList();
        for (var index = 1; index < all.Count; index++)
        {
            if (all[index].Range.Inicio <= all[index - 1].Range.Fim &&
                (all[index].IsNew || all[index - 1].IsNew))
            {
                errors.Add("As parcelas de férias não podem se sobrepor.");
                return;
            }
        }
    }

    private static void ValidateInstallments(
        PeriodoAquisitivo period,
        IReadOnlyList<IntervaloFerias> all,
        long newDays,
        ICollection<string> errors)
    {
        if (all.Count <= 1 || all.Any(item => item.Dias >= 14))
        {
            return;
        }

        var remainingBalance = period.Saldo - newDays;
        var remainingSlots = 3 - all.Count;
        if (remainingBalance < 14 || remainingSlots < 1)
        {
            errors.Add("Uma das parcelas deve possuir pelo menos 14 dias.");
        }
    }
}

public sealed class AgendamentoService(
    IDbContextFactory<FeriasDbContext> databaseFactory,
    IRegraFeriasEngine rules,
    IConfiguracaoService configuracao) : IAgendamentoService
{
    public async Task<IReadOnlyList<Feriado>> ListarFeriadosAsync()
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        return await database.Feriados
            .AsNoTracking()
            .OrderBy(item => item.Data)
            .ToListAsync();
    }

    public async Task<ResultadoValidacao> AgendarAsync(
        int periodoId,
        IReadOnlyList<IntervaloFerias> intervalos,
        int diasAbono,
        string motivo)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        var period = await database.Periodos
            .Include(item => item.Movimentacoes)
            .FirstAsync(item => item.Id == periodoId);
        var holidays = await database.Feriados.ToListAsync();
        var result = rules.Validar(
            period,
            intervalos,
            holidays,
            configuracao.BloquearAgendamentoMenos30Dias,
            configuracao.BloquearInicioAntesRepousoSemanal,
            diasAbono);

        if (!result.Valido)
        {
            return result;
        }

        var saldoAnterior = period.Saldo;
        var totalDays = intervalos.Sum(item => item.Dias);
        foreach (var intervalo in intervalos)
        {
            period.Movimentacoes.Add(new MovimentacaoSaldo
            {
                Tipo = TipoMovimentacao.Agendamento,
                Dias = -intervalo.Dias,
                Inicio = intervalo.Inicio.Date,
                Fim = intervalo.Fim.Date,
                Motivo = motivo
            });
        }
        if (diasAbono > 0)
        {
            period.Movimentacoes.Add(new MovimentacaoSaldo
            {
                Tipo = TipoMovimentacao.Venda,
                Dias = -diasAbono,
                Motivo = "Abono pecuniário registrado com o agendamento"
            });
        }
        period.Status = totalDays + diasAbono == saldoAnterior
            ? StatusPeriodo.Completo
            : StatusPeriodo.Parcial;

        await database.SaveChangesAsync();
        return result;
    }
}

public sealed class MovimentacaoService(
    IDbContextFactory<FeriasDbContext> databaseFactory) : IMovimentacaoService
{
    public async Task RegistrarAsync(
        int periodoId,
        TipoMovimentacao tipo,
        int dias,
        string motivo)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        database.Movimentacoes.Add(new MovimentacaoSaldo
        {
            PeriodoAquisitivoId = periodoId,
            Tipo = tipo,
            Dias = dias,
            Motivo = motivo
        });
        await database.SaveChangesAsync();
    }
}

public sealed class RelatorioService(IColaboradorService employees) : IRelatorioService
{
    public async Task<string> ExportarCsvAsync(string destination)
    {
        var dashboard = await employees.DashboardAsync();
        var csv = new StringBuilder(
            "Colaborador;Período;Vencimento;Saldo;Status\r\n");

        foreach (var row in dashboard.Periodos)
        {
            csv.AppendLine(
                $"{row.Colaborador};{row.Periodo.Replace('\n', ' ')};" +
                $"{row.Vencimento};{row.Saldo};{row.Status}");
        }

        await File.WriteAllTextAsync(destination, csv.ToString(), Encoding.UTF8);
        return destination;
    }
}

public sealed class DocumentoService : IDocumentoService
{
    public Task<string> GerarAvisoAsync(int periodoId, string destination)
    {
        return Task.FromResult(destination);
    }
}

public sealed class ImportacaoPdfBloqueadaService : IImportacaoPdfService
{
    public bool Habilitada => false;

    public string MotivoBloqueio =>
        "A importação será habilitada após o fornecimento do modelo, campos, " +
        "exemplos, validações e política de duplicidade do PDF.";
}
