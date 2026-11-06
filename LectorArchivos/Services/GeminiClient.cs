using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LectorArchivos.Services;

public sealed record WebSource(string Title, string Uri);

public sealed record AiSummary(string Text, IReadOnlyList<WebSource> Sources);

public sealed class GeminiException(string message) : Exception(message);

/// <summary>
/// Cliente mínimo de la API REST de Google Gemini (generateContent).
/// No usa la herramienta google_search ("Grounding with Google Search") porque agota
/// rápidamente la cuota gratuita: el modelo responde con su propio conocimiento.
/// </summary>
public sealed class GeminiClient
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    private const string SystemPrompt = """
        Eres un experto en sistemas Windows que ayuda a un usuario a entender qué archivos y carpetas ocupan su disco.
        Explica con tu propio conocimiento qué es el elemento indicado (no tienes acceso a internet, no inventes fuentes)
        y responde SIEMPRE en español, en texto plano
        (sin Markdown, sin asteriscos ni almohadillas), con exactamente estas secciones, cada una con su título en una línea:

        RESUMEN
        Qué es este archivo o carpeta y para qué sirve (2-5 frases).

        PROGRAMA AL QUE PERTENECE
        Nombre del programa o componente y su fabricante. Si no se puede determinar, dilo.

        ARCHIVO DE INSTALACIÓN
        Indica si es un instalador, actualización o paquete de instalación y, en ese caso, de qué programa y versión.
        Si no lo es, responde "No es un archivo de instalación."

        ¿SE PUEDE ELIMINAR?
        Indica si es seguro borrarlo y cómo hacerlo correctamente (por ejemplo, desinstalando el programa o con el Liberador de espacio).

        Si la información es incierta, dilo claramente en lugar de inventar.
        """;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly string _apiKey;
    private readonly string _model;

    public GeminiClient(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? AppSettings.DefaultModel : model.Trim();
    }

    public async Task<AiSummary> SummarizeAsync(LocalFileInfo info, CancellationToken token)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(info.IsDirectory ? "Carpeta a investigar:" : "Archivo a investigar:");
        prompt.AppendLine($"Nombre: {info.Name}");
        prompt.AppendLine($"Ruta: {AnonymizePath(info.Path)}");
        foreach (var (label, value) in info.Facts())
            prompt.AppendLine($"{label}: {value}");

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = SystemPrompt }) },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = prompt.ToString() }),
            }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(Endpoint, Uri.EscapeDataString(_model)))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", _apiKey);

        using var response = await Http.SendAsync(request, token);
        var json = await response.Content.ReadAsStringAsync(token);
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { throw new GeminiException($"Respuesta inválida de Gemini (HTTP {(int)response.StatusCode})."); }

        if (!response.IsSuccessStatusCode)
        {
            var message = root?["error"]?["message"]?.GetValue<string>() ?? response.ReasonPhrase;
            throw new GeminiException($"Error de Gemini (HTTP {(int)response.StatusCode}): {message}");
        }

        var candidate = root?["candidates"]?[0];
        if (candidate is null)
        {
            var blockReason = root?["promptFeedback"]?["blockReason"]?.GetValue<string>();
            throw new GeminiException(blockReason is null ? "Gemini no devolvió ninguna respuesta." : $"Gemini bloqueó la consulta ({blockReason}).");
        }

        var text = new StringBuilder();
        foreach (var part in candidate["content"]?["parts"]?.AsArray() ?? [])
        {
            if (part?["thought"]?.GetValue<bool>() == true) continue;
            if (part?["text"]?.GetValue<string>() is { } t) text.Append(t);
        }

        var sources = new List<WebSource>();
        foreach (var chunk in candidate["groundingMetadata"]?["groundingChunks"]?.AsArray() ?? [])
        {
            var web = chunk?["web"];
            if (web?["uri"]?.GetValue<string>() is not { } uri) continue;
            var title = web["title"]?.GetValue<string>() ?? uri;
            if (sources.All(s => s.Uri != uri)) sources.Add(new WebSource(title, uri));
        }

        var result = CleanMarkdown(text.ToString().Trim());
        if (result.Length == 0)
            throw new GeminiException($"Gemini no devolvió texto (motivo: {candidate["finishReason"]?.GetValue<string>() ?? "desconocido"}).");
        return new AiSummary(result, sources);
    }

    /// <summary>No enviamos el nombre de usuario de Windows a internet.</summary>
    private static string AnonymizePath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(profile) && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[profile.Length..]
            : path;
    }

    /// <summary>Por si el modelo usa Markdown de todos modos.</summary>
    private static string CleanMarkdown(string text) =>
        text.Replace("**", "").Replace("__", "").Replace("### ", "").Replace("## ", "").Replace("# ", "");
}
