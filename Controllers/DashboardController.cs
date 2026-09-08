using FeriasCampos.Models;
using FeriasCampos.Services;

namespace FeriasCampos.Controllers;

public sealed class DashboardController(
    IColaboradorService colaboradores,
    IAgendamentoService agenda,
    IImportacaoPdfService pdf)
{
    public Task<DashboardDto> CarregarAsync(string? busca = null)
    {
        return colaboradores.DashboardAsync(busca);
    }

    public Task<PeriodoAquisitivo?> SelecionarAsync(int id)
    {
        return colaboradores.PeriodoAsync(id);
    }

    public Task<IReadOnlyList<Colaborador>> ListarColaboradoresAsync()
    {
        return colaboradores.ListarAsync();
    }

    public Task<IReadOnlyList<FeriasAgendaItem>> ListarAgendaAsync()
    {
        return colaboradores.ListarAgendaAsync();
    }

    public Task<ResultadoValidacao> CadastrarColaboradorAsync(
        NovoColaboradorDto novo)
    {
        return colaboradores.CadastrarAsync(novo);
    }

    public Task<ResultadoValidacao> AgendarAsync(
        int id,
        IReadOnlyList<IntervaloFerias> intervalos,
        int diasAbono,
        int faltasNaoJustificadas)
    {
        return agenda.AgendarAsync(
            id,
            intervalos,
            diasAbono,
            faltasNaoJustificadas,
            "Agendamento administrativo");
    }

    public Task<IReadOnlyList<Feriado>> ListarFeriadosAsync()
    {
        return agenda.ListarFeriadosAsync();
    }

    public string EstadoImportacao()
    {
        return pdf.MotivoBloqueio;
    }
}
