using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace LectorArchivos.Services;

public enum AppTheme
{
    /// <summary>Sigue el modo claro/oscuro de Windows (opción por defecto).</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Aplica el modo claro u oscuro: el tema Fluent de WPF para los controles y la paleta propia
/// (carpeta Paletas) para los colores de la aplicación. En modo «Sistema» se actualiza solo
/// cuando el usuario cambia el modo de Windows.
/// </summary>
public static class ThemeManager
{
    private const string PaletteFolder = "Paletas/";

    private static AppTheme _preference;
    private static bool _listening;
    private static bool _isDark;

    public static void Apply(AppTheme preference)
    {
        _preference = preference;
        if (!_listening)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            // Las ventanas que se abran después (p. ej. Configurar IA) también reciben la barra de título del tema.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) => SetDarkTitleBar((Window)sender, _isDark)));
            _listening = true;
        }

        var dark = preference switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => !SystemUsesLightTheme(),
        };

        var app = Application.Current;
#pragma warning disable WPF0001 // ThemeMode (tema Fluent) está marcado como experimental en WPF.
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        var dictionaries = app.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.StartsWith(PaletteFolder) == true);
        var palette = new ResourceDictionary { Source = new Uri(PaletteFolder + (dark ? "Oscuro.xaml" : "Claro.xaml"), UriKind.Relative) };
        if (current is null) dictionaries.Add(palette);
        else dictionaries[dictionaries.IndexOf(current)] = palette;

        _isDark = dark;
        foreach (Window window in app.Windows) SetDarkTitleBar(window, dark);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Barra de título clara u oscura (DWMWA_USE_IMMERSIVE_DARK_MODE, Windows 10 20H1+ y Windows 11).</summary>
    private static void SetDarkTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
    }

    /// <summary>Lee «Elegir el modo de aplicación» de Windows (Configuración → Personalización → Colores).</summary>
    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference != AppTheme.System || e.Category != UserPreferenceCategory.General) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
    }
}
