using FeriasCampos.Models;

namespace FeriasCampos.Services;

public interface IColaboradorService
{
    Task<DashboardDto> DashboardAsync(string? busca = null);
    Task<PeriodoAquisitivo?> PeriodoAsync(int id);
    Task<IReadOnlyList<Colaborador>> ListarAsync();
    Task<IReadOnlyList<FeriasAgendaItem>> ListarAgendaAsync();
    Task<ResultadoValidacao> CadastrarAsync(NovoColaboradorDto novo);
    Task<ResultadoValidacao> AlterarAsync(int id, NovoColaboradorDto dados);
    Task<ResultadoValidacao> ExcluirAsync(int id);
}

public interface IPeriodoService
{
    Task<IReadOnlyList<PeriodoAquisitivo>> ListarAsync();
}

public interface IAgendamentoService
{
    Task<IReadOnlyList<Feriado>> ListarFeriadosAsync();
    Task<ResultadoValidacao> AgendarAsync(
        int periodoId,
        IReadOnlyList<IntervaloFerias> intervalos,
        int diasAbono,
        int faltasNaoJustificadas,
        string motivo);
}

public interface IRegraFeriasEngine
{
    ResultadoValidacao Validar(
        PeriodoAquisitivo periodo,
        IReadOnlyList<IntervaloFerias> intervalos,
        IEnumerable<Feriado> feriados,
        bool bloquearAgendamentoMenos30Dias = false,
        bool bloquearInicioAntesRepousoSemanal = true,
        int diasAbono = 0);
}

public interface IMovimentacaoService
{
    Task<ResultadoValidacao> RegistrarFolgaAsync(int periodoId, int dias, string motivo);

    Task RegistrarAsync(
        int periodoId,
        TipoMovimentacao tipo,
        int dias,
        string motivo);
}

public interface IRelatorioService
{
    Task<string> ExportarCsvAsync(string destino);
}

public interface IDocumentoService
{
    Task<string> GerarAvisoAsync(int periodoId, string destino);
}

public interface IImportacaoPdfService
{
    bool Habilitada { get; }
    string MotivoBloqueio { get; }
}
