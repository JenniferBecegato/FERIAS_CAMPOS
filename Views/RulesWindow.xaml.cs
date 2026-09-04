using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace FeriasCampos.Views;

public partial class RulesWindow : Window
{
    public RulesWindow()
    {
        InitializeComponent();
    }

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ReferenceNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
