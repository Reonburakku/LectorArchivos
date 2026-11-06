using LectorArchivos.Services;
using Velopack;

namespace LectorArchivos;

/// <summary>
/// Punto de entrada. Velopack (el instalador) necesita ejecutarse antes de abrir la ventana
/// para gestionar la instalación, la desinstalación y los accesos directos.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        ThemeManager.Apply(AppSettings.Load().Theme);
        app.Run();
    }
}
