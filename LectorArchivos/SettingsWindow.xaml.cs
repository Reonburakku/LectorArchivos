using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using LectorArchivos.Services;

namespace LectorArchivos;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ModelCombo.ItemsSource = GeminiModels.Free;
        ModelCombo.SelectedItem =
            GeminiModels.Free.FirstOrDefault(m => m.Id.Equals(settings.Model, StringComparison.OrdinalIgnoreCase))
            ?? GeminiModels.Free.First(m => m.Id == AppSettings.DefaultModel);

        if (!string.IsNullOrEmpty(settings.EncryptedApiKey))
            ApiKeyHint.Text = "Hay una clave guardada (cifrada para tu usuario de Windows). Déjalo vacío para conservarla.";
        else if (settings.GetApiKey() is not null)
            ApiKeyHint.Text = "Se está usando la variable de entorno GEMINI_API_KEY. Escribe una clave para guardarla aquí.";
        else
            ApiKeyHint.Text = "La clave se guarda cifrada con DPAPI en %APPDATA%\\LectorArchivos.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ApiKeyBox.Password.Length > 0) _settings.SetApiKey(ApiKeyBox.Password);
        _settings.Model = (ModelCombo.SelectedItem as GeminiModel)?.Id ?? AppSettings.DefaultModel;
        _settings.Save();
        DialogResult = true;
    }

    private void ModelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        ModelNote.Text = (ModelCombo.SelectedItem as GeminiModel)?.Note ?? "";

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
