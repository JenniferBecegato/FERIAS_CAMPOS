using System.Globalization;
using System.Reflection;
using System.Text;
using FeriasCampos.Properties;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class ScreenTextsTests
{
    [Fact]
    public void TextosTipadosEstaoDisponiveisNoResourceEmbutido()
    {
        var properties = typeof(ScreenTexts).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(string)).ToList();
        Assert.NotEmpty(properties);
        foreach (var property in properties)
        {
            var text = Assert.IsType<string>(property.GetValue(null));
            Assert.False(string.IsNullOrWhiteSpace(text), property.Name);
            Assert.Contains('_', property.Name);
            // Valida também a sintaxe dos placeholders usados pelos textos dinâmicos.
            CompositeFormat.Parse(text);
        }
    }

    [Fact]
    public void MensagemDinamicaPreservaValoresEDatas()
    {
        var text = string.Format(CultureInfo.GetCultureInfo("pt-BR"),
            ScreenTexts.RegisterDayOffDialog_PeriodoA, new DateTime(2025, 7, 1), new DateTime(2026, 6, 30));
        Assert.Equal("Período: 01/07/2025 a 30/06/2026", text);
    }
}
