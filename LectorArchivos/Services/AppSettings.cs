using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LectorArchivos.Services;

/// <summary>
/// Configuración persistida en %APPDATA%\LectorArchivos\settings.json.
/// La API key se guarda cifrada con DPAPI (solo el usuario actual de Windows puede descifrarla).
/// Si no hay clave guardada, se usa la variable de entorno GEMINI_API_KEY.
/// </summary>
public sealed class AppSettings
{
    public const string DefaultModel = "gemini-flash-latest";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LectorArchivos", "settings.json");

    public string? EncryptedApiKey { get; set; }
    public string Model { get; set; } = DefaultModel;

    public string? GetApiKey()
    {
        if (!string.IsNullOrEmpty(EncryptedApiKey))
        {
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(EncryptedApiKey), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // Clave corrupta o de otro usuario: se ignora.
            }
        }
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    public void SetApiKey(string? apiKey)
    {
        EncryptedApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } settings)
            {
                // Solo se permiten modelos con nivel gratuito; cualquier otro vuelve al predeterminado.
                if (!GeminiModels.IsFree(settings.Model)) settings.Model = DefaultModel;
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
