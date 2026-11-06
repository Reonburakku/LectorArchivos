namespace LectorArchivos.Services;

public sealed record GeminiModel(string Id, string Name, string Note)
{
    public string Label => $"{Name}  ({Id})";
}

/// <summary>
/// Modelos de texto de Gemini que tienen nivel gratuito (entrada y salida «Free of charge»),
/// según la página de precios de Google AI Studio (https://aistudio.google.com/docs/pricing),
/// consultada en noviembre de 2026. No se incluyen modelos de voz, audio en vivo, imagen
/// ni robótica, ni los ya obsoletos que Google redirige a otros modelos.
/// Si Google cambia su oferta gratuita, hay que actualizar esta lista.
/// </summary>
public static class GeminiModels
{
    public static IReadOnlyList<GeminiModel> Free { get; } =
    [
        new("gemini-flash-latest", "Gemini Flash (última versión)", "Alias que apunta siempre al Flash más reciente."),
        new("gemini-flash-lite-latest", "Gemini Flash-Lite (última versión)", "Alias del Flash-Lite más reciente: más rápido y con más cuota."),
        new("gemini-3.8-flash", "Gemini 3.8 Flash", "El Flash más reciente."),
        new("gemini-3.6-flash", "Gemini 3.6 Flash", "Versión Flash anterior."),
        new("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite", "Ligero y rápido."),
        new("gemini-3.1-flash-lite", "Gemini 3.1 Flash-Lite", "Ligero, versión anterior."),
        new("gemini-3-flash-preview", "Gemini 3 Flash (preview)", "Versión preliminar."),
        new("gemini-2.5-flash-lite", "Gemini 2.5 Flash-Lite", "Generación anterior."),
    ];

    public static bool IsFree(string? id) =>
        id is not null && Free.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
