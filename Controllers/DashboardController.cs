using FeriasCampos.Models;
using FeriasCampos.Services;

namespace FeriasCampos.Controllers;

public sealed class DashboardController(
    IColaboradorService colaboradores,
    IAgendamentoService agenda,
    IImportacaoPdfService pdf,
    IMovimentacaoService movimentacoes)
{
    public Task<ResultadoValidacao> RegistrarFolgaAsync(int periodoId, int dias, string motivo)
        => movimentacoes.RegistrarFolgaAsync(periodoId, dias, motivo);

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

    public Task<ResultadoValidacao> AlterarColaboradorAsync(int id, NovoColaboradorDto dados)
        => colaboradores.AlterarAsync(id, dados);

    public Task<ResultadoValidacao> ExcluirColaboradorAsync(int id)
        => colaboradores.ExcluirAsync(id);

    public Task<ResultadoValidacao> AgendarAsync(
        int id,
        IReadOnlyList<IntervaloFerias> intervalos,
        int diasAbono,
        int faltasNaoJustificadas,
        IReadOnlyList<long>? excluirAgendamentos = null)
    {
        return agenda.AgendarAsync(
            id,
            intervalos,
            diasAbono,
            faltasNaoJustificadas,
            "Agendamento administrativo", excluirAgendamentos);
    }

    public Task<IReadOnlyList<Feriado>> ListarFeriadosAsync()
    {
        return agenda.ListarFeriadosAsync();
    }

    public Task<ResultadoImportacaoPdf> ImportarAsync(string arquivo, string unidade)
        => pdf.ImportarAsync(arquivo, unidade);
}
