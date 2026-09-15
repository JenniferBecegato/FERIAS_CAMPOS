using System.Windows.Media.Imaging;
using Xunit;

namespace FeriasCampos.Tests;

public sealed class AppResourcesTests
{
    [Fact]
    public void Recursos_da_aplicacao_carregam_sem_arquivos_de_imagem_externos()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                app = new App();
                app.InitializeComponent();
                foreach (var key in new[] { "programIcon", "homeIcon", "dashboardIcon",
                             "personIcon", "importIcon", "travelIcon", "summerIcon",
                             "rulesIcon", "settingsIcon" })
                {
                    var bitmap = Assert.IsType<BitmapImage>(app.Resources[key]);
                    Assert.True(bitmap.PixelWidth > 0, key);
                    Assert.True(bitmap.PixelHeight > 0, key);
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Tempo excedido ao carregar os recursos WPF.");
        Assert.Null(failure);
    }
}
