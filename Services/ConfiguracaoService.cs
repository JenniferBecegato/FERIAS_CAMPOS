using System.IO;
using System.Text.Json;

namespace FeriasCampos.Services;

public interface IConfiguracaoService
{
    bool BloquearAgendamentoMenos30Dias { get; set; }
    bool BloquearInicioAntesRepousoSemanal { get; set; }
    bool DescontarSaldoFeriasPorFaltasNaoJustificadas { get; set; }
    void Salvar();
}

public sealed class ConfiguracaoService : IConfiguracaoService
{
    private readonly string _filePath;

    public ConfiguracaoService(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "configuracoes.json");
        BloquearAgendamentoMenos30Dias = true;
        BloquearInicioAntesRepousoSemanal = true;
        DescontarSaldoFeriasPorFaltasNaoJustificadas = true;

        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var saved = JsonSerializer.Deserialize<ConfiguracoesSalvas>(
                File.ReadAllText(_filePath));
            if (saved is not null)
            {
                BloquearAgendamentoMenos30Dias =
                    saved.BloquearAgendamentoMenos30Dias;
                BloquearInicioAntesRepousoSemanal =
                    saved.BloquearInicioAntesRepousoSemanal ?? true;
                DescontarSaldoFeriasPorFaltasNaoJustificadas =
                    saved.DescontarSaldoFeriasPorFaltasNaoJustificadas ?? true;
            }
        }
        catch (JsonException)
        {
            // Mantém os valores seguros padrão se o arquivo estiver inválido.
        }
    }

    public bool BloquearAgendamentoMenos30Dias { get; set; }
    public bool BloquearInicioAntesRepousoSemanal { get; set; }
    public bool DescontarSaldoFeriasPorFaltasNaoJustificadas { get; set; }

    public void Salvar()
    {
        var json = JsonSerializer.Serialize(
            new ConfiguracoesSalvas(
                BloquearAgendamentoMenos30Dias,
                BloquearInicioAntesRepousoSemanal,
                DescontarSaldoFeriasPorFaltasNaoJustificadas),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private sealed record ConfiguracoesSalvas(
        bool BloquearAgendamentoMenos30Dias,
        bool? BloquearInicioAntesRepousoSemanal = null,
        bool? DescontarSaldoFeriasPorFaltasNaoJustificadas = null);
}
