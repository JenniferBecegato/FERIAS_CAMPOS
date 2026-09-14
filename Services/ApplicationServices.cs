using FeriasCampos.Properties;
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
            .CountAsync();

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

        await using var database = await databaseFactory.CreateDbContextAsync();
        await AddDuplicateErrorsAsync(database, cpf, errors);

        if (errors.Count > 0)
        {
            return new ResultadoValidacao(false, errors, []);
        }

        var employee = CreateEmployee(novo, cpf);
        database.Colaboradores.Add(employee);
        await database.SaveChangesAsync();

        return new ResultadoValidacao(true, [], []);
    }

    public async Task<ResultadoValidacao> AlterarAsync(int id, NovoColaboradorDto dados)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        var employee = await database.Colaboradores.Include(c => c.Periodos)
            .ThenInclude(p => p.Movimentacoes).SingleOrDefaultAsync(c => c.Id == id);
        if (employee is null)
            return new(false, [ScreenTexts.EmployeesWindow_ColaboradorNaoEncontrado], []);

        var errors = ValidateNewEmployee(dados);
        var cpf = OnlyDigits(dados.Cpf);
        await AddDuplicateErrorsAsync(database, cpf, errors, id);
        var admissionChanged = employee.Admissao.Date != dados.Admissao.Date;
        if (admissionChanged && employee.Periodos.Any(p => p.FaltasNaoJustificadas != 0 ||
                p.Movimentacoes.Any(m => m.Tipo != TipoMovimentacao.Aquisicao)))
            errors.Add(ScreenTexts.EmployeesWindow_AAdmissaoNaoPodeSerAlteradaPorqueHa);
        if (errors.Count > 0)
            return new(false, errors, []);

        if (admissionChanged)
        {
            var direito = employee.Periodos.OrderBy(p => p.Inicio).FirstOrDefault()?.DireitoDias ?? 30;
            database.Movimentacoes.RemoveRange(employee.Periodos.SelectMany(p => p.Movimentacoes));
            database.Periodos.RemoveRange(employee.Periodos);
            employee.Periodos = CreateEmployee(dados with { DireitoDias = direito }, cpf).Periodos;
            PeriodoAquisitivoMaintenance.Atualizar(employee, DateTime.Today);
        }
        employee.Nome = dados.Nome.Trim();
        employee.Cpf = cpf;
        employee.Admissao = dados.Admissao.Date;
        employee.Unidade = dados.Unidade.Trim();
        await database.SaveChangesAsync();
        return new(true, [], []);
    }

    public async Task<ResultadoValidacao> ExcluirAsync(int id)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        var employee = await database.Colaboradores.Include(c => c.Periodos)
            .ThenInclude(p => p.Movimentacoes).SingleOrDefaultAsync(c => c.Id == id);
        if (employee is null)
            return new(false, [ScreenTexts.EmployeesWindow_ColaboradorNaoEncontrado], []);
        database.Movimentacoes.RemoveRange(employee.Periodos.SelectMany(p => p.Movimentacoes));
        database.Periodos.RemoveRange(employee.Periodos);
        database.Colaboradores.Remove(employee);
        await database.SaveChangesAsync();
        return new(true, [], []);
    }

    private static PeriodoRow ToRow(PeriodoAquisitivo period)
    {
        return new PeriodoRow(
            period.Id,
            period.ColaboradorId,
            period.Colaborador.Iniciais,
            period.Colaborador.Nome,
            string.Format(ScreenTexts.EmployeesWindow_PeriodoConcessivo, period.Fim.Date.AddDays(1), period.Vencimento),
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
            errors.Add(ScreenTexts.EmployeesWindow_InformeONomeCompleto);
        }

        if (OnlyDigits(employee.Cpf).Length != 11)
        {
            errors.Add(ScreenTexts.EmployeesWindow_OCPFDevePossuir11Digitos);
        }


        if (employee.Admissao.Date > DateTime.Today)
        {
            errors.Add(ScreenTexts.EmployeesWindow_AAdmissaoNaoPodeEstarNoFuturo);
        }

        if (employee.Unidade is not ("Washington Luiz" or "Gurgel"))
        {
            errors.Add(ScreenTexts.EmployeesWindow_SelecioneUmaUnidadeValida);
        }

        if (employee.DireitoDias is < 1 or > 30)
        {
            errors.Add(ScreenTexts.EmployeesWindow_ODireitoDeveEstarEntre1E30);
        }

        return errors;
    }

    private static async Task AddDuplicateErrorsAsync(
        FeriasDbContext database,
        string cpf,
        ICollection<string> errors,
        int? excludedId = null)
    {
        if (await database.Colaboradores.AnyAsync(employee => employee.Id != excludedId && employee.Cpf == cpf))
        {
            errors.Add(ScreenTexts.EmployeesWindow_JaExisteUmColaboradorComEsteCPF);
        }

    }

    private static Colaborador CreateEmployee(
        NovoColaboradorDto source,
        string cpf)
    {
        var admissionDate = source.Admissao.Date;
        var employee = new Colaborador
        {
            Nome = source.Nome.Trim(),
            Cpf = cpf,
            Admissao = admissionDate,
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
            Motivo = ScreenTexts.EmployeesWindow_SaldoInicialCriadoNoCadastro
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
            return ScreenTexts.EmployeesWindow_Vencido;
        }

        return period.Status switch
        {
            StatusPeriodo.EmAquisicao => ScreenTexts.EmployeesWindow_EmAquisicao,

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
            errors.Add(ScreenTexts.ScheduleVacationDialog_AdicioneAoMenosUmaParcelaDeFerias);
            return new ResultadoValidacao(false, errors, warnings);
        }

        var existentes = periodo.Movimentacoes
            .Where(item => item.Tipo == TipoMovimentacao.Agendamento &&
                           item.Inicio is not null && item.Fim is not null)
            .Select(item => new IntervaloFerias(item.Inicio!.Value.Date, item.Fim!.Value.Date))
            .ToList();

        if (existentes.Count + novos.Count > 3)
        {
            errors.Add(ScreenTexts.ScheduleVacationDialog_OLimiteDeTresParcelasJaFoiAtingido);
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
            errors.Add(string.Format(ScreenTexts.ScheduleVacationDialog_OPeriodoPossuiSomenteDiasDisponiveis, periodo.Saldo));
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
            errors.Add(ScreenTexts.ScheduleVacationDialog_AQuantidadeDeDiasVendidosNaoPodeSer);
            return;
        }

        var maximum = period.DireitoDias / 3;
        if (period.Vendidos + allowanceDays > maximum)
        {
            errors.Add(
                string.Format(ScreenTexts.ScheduleVacationDialog_OAbonoPecuniarioNaoPodeUltrapassarDias, maximum) +
                ScreenTexts.ScheduleVacationDialog_NestePeriodoTrabalhadoQueGerouODireitoAs);
        }
    }

    private static void ValidateRange(
        PeriodoAquisitivo period, IntervaloFerias range, IEnumerable<Feriado> holidays,
        bool blockNotice, bool blockWeeklyRest,
        ICollection<string> errors, ICollection<string> warnings)
    {
        if (range.Fim < range.Inicio)
        {
            errors.Add(ScreenTexts.ScheduleVacationDialog_ADataFinalNaoPodeSerAnteriorA);
            return;
        }

        if (range.Dias < 5)
        {
            errors.Add(ScreenTexts.ScheduleVacationDialog_UmaParcelaDeFeriasDevePossuirPeloMenos);
        }

        var firstConcessionDay = period.Fim.Date.AddDays(1);
        if (range.Inicio < firstConcessionDay)
        {
            errors.Add(
                string.Format(ScreenTexts.ScheduleVacationDialog_AsFeriasDestePeriodoSoPodemComecarA) +
                string.Format(ScreenTexts.ScheduleVacationDialog_AposOTerminoDaAquisicao, firstConcessionDay));
        }

        if (range.Inicio > period.Vencimento.Date || range.Fim > period.Vencimento.Date)
        {
            errors.Add(
                string.Format(ScreenTexts.ScheduleVacationDialog_AsFeriasDevemTerminarAteOVencimentoDo) +
                string.Format(ScreenTexts.ScheduleVacationDialog_VencimentoEntreParenteses, period.Vencimento));
        }

        if (CalendarioFeriados.InicioProibido(range.Inicio, holidays))
        {
            errors.Add(ScreenTexts.ScheduleVacationDialog_OInicioOcorreNosDoisDiasAnterioresA);
        }

        if (range.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
        {
            string message =
                ScreenTexts.ScheduleVacationDialog_OInicioOcorreNosDoisDiasAnterioresAo;
            if (blockWeeklyRest) errors.Add(message); else warnings.Add(message);
        }

        if (range.Inicio < DateTime.Today.AddDays(30))
        {
            string message = ScreenTexts.ScheduleVacationDialog_OInicioDasFeriasDeveRespeitar30Dias;
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
                errors.Add(ScreenTexts.ScheduleVacationDialog_AsParcelasDeFeriasNaoPodemSeSobrepor);
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
            errors.Add(ScreenTexts.ScheduleVacationDialog_UmaDasParcelasDevePossuirPeloMenos142);
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
            .OrderBy(item => item.Mes).ThenBy(item => item.Dia).ThenBy(item => item.Nome)
            .ToListAsync();
    }

    public async Task<ResultadoValidacao> AgendarAsync(
        int periodoId,
        IReadOnlyList<IntervaloFerias> intervalos,
        int diasAbono,
        int faltasNaoJustificadas,
        string motivo,
        IReadOnlyList<long>? excluirAgendamentos = null)
    {
        await using var database = await databaseFactory.CreateDbContextAsync();
        var period = await database.Periodos
            .Include(item => item.Movimentacoes)
            .FirstAsync(item => item.Id == periodoId);
        var removidos = (excluirAgendamentos ?? []).Distinct().ToList();
        foreach (var id in removidos)
        {
            var movimento = period.Movimentacoes.SingleOrDefault(item => item.Id == id);
            if (movimento is null || movimento.Tipo != TipoMovimentacao.Agendamento ||
                movimento.Inicio is null || movimento.Inicio.Value.Date <= DateTime.Today)
                return new(false, [ScreenTexts.ScheduleVacationDialog_ExclusaoNaoPermitida], []);
            period.Movimentacoes.Remove(movimento);
            database.Movimentacoes.Remove(movimento);
        }
        if (removidos.Count > 0 && intervalos.Count == 0 && diasAbono == 0)
        {
            period.Status = period.Saldo <= 0 ? StatusPeriodo.Completo
                : period.Vencimento.Date < DateTime.Today ? StatusPeriodo.Vencido
                : period.Movimentacoes.Any(item => item.Dias < 0 && item.Tipo != TipoMovimentacao.Ajuste)
                    ? StatusPeriodo.Parcial
                : period.Fim.Date >= DateTime.Today ? StatusPeriodo.EmAquisicao
                : StatusPeriodo.Disponivel;
            await database.SaveChangesAsync();
            return new(true, [], []);
        }
        var holidays = await database.Feriados.ToListAsync();
        if (faltasNaoJustificadas < 0)
        {
            return new ResultadoValidacao(false,
                [ScreenTexts.ScheduleVacationDialog_AQuantidadeDeFaltasNaoJustificadasNaoPode], []);
        }

        period.FaltasNaoJustificadas = faltasNaoJustificadas;
        var ajusteAtual = RegraFaltasClt.ObterAjusteAtual(period);
        var ajusteDesejado = configuracao.DescontarSaldoFeriasPorFaltasNaoJustificadas
            ? RegraFaltasClt.CalcularDireito(period.DireitoDias, faltasNaoJustificadas) -
              period.DireitoDias
            : 0;
        var movimentoAjuste = period.Movimentacoes.FirstOrDefault(item =>
            item.Tipo == TipoMovimentacao.Ajuste &&
            item.Motivo == RegraFaltasClt.MotivoAjuste);
        if (movimentoAjuste is null && ajusteDesejado != 0)
        {
            period.Movimentacoes.Add(new MovimentacaoSaldo
            {
                Tipo = TipoMovimentacao.Ajuste,
                Dias = ajusteDesejado,
                Motivo = RegraFaltasClt.MotivoAjuste
            });
        }
        else if (movimentoAjuste is not null && ajusteAtual != ajusteDesejado)
        {
            movimentoAjuste.Dias += ajusteDesejado - ajusteAtual;
        }
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
                Motivo = ScreenTexts.ScheduleVacationDialog_AbonoPecuniarioRegistradoComOAgendamento
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
    public async Task<ResultadoValidacao> RegistrarFolgaAsync(int periodoId, int dias, string motivo)
    {
        if (dias <= 0)
            return new(false, [ScreenTexts.RegisterDayOffDialog_InformeUmaQuantidadeInteiraDeDiasMaiorQue], []);
        if (string.IsNullOrWhiteSpace(motivo))
            return new(false, [ScreenTexts.RegisterDayOffDialog_InformeAJustificativaDaFolga], []);

        await using var database = await databaseFactory.CreateDbContextAsync();
        await using var transaction = await database.Database.BeginTransactionAsync();
        var period = await database.Periodos.Include(item => item.Movimentacoes)
            .SingleOrDefaultAsync(item => item.Id == periodoId);
        if (period is null)
            return new(false, [ScreenTexts.RegisterDayOffDialog_PeriodoNaoEncontrado], []);
        if (period.Vencimento.Date < DateTime.Today)
            return new(false, [string.Format(ScreenTexts.RegisterDayOffDialog_EstePeriodoVenceuEmNaoEPossivelRegistrar, period.Vencimento)], []);
        if (period.Saldo <= 0)
            return new(false, [ScreenTexts.RegisterDayOffDialog_OPeriodoNaoPossuiSaldoDisponivelParaRegistrar], []);
        if (dias > period.Saldo)
            return new(false, [string.Format(ScreenTexts.RegisterDayOffDialog_OPeriodoPossuiSomenteDiasDisponiveis, period.Saldo)], []);

        period.Movimentacoes.Add(new MovimentacaoSaldo
        {
            Tipo = TipoMovimentacao.Folga,
            Dias = -dias,
            Motivo = motivo.Trim()
        });
        period.Status = period.Saldo == 0 ? StatusPeriodo.Completo : StatusPeriodo.Parcial;
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(true, [], []);
    }

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

public sealed class RelatorioService(IPeriodoService periods) : IRelatorioService
{
    public async Task<string> ExportarCsvAsync(string destination)
    {
        var report = RelatorioEngine.Gerar(await periods.ListarAsync(),
            new FiltroRelatorio { Tipo = TipoRelatorio.Saldos }, DateTime.Today);
        await RelatorioExportacao.SalvarCsvAsync(report, destination);
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
        ScreenTexts.MainWindow_AImportacaoSeraHabilitadaAposOFornecimentoDo +
        ScreenTexts.MainWindow_ExemplosValidacoesEPoliticaDeDuplicidadeDoPDF;
}
