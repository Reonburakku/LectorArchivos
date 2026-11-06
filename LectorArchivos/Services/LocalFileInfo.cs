using System.Diagnostics;
using System.IO;
using LectorArchivos.Models;
using Microsoft.Win32;

namespace LectorArchivos.Services;

/// <summary>Programa instalado según el registro de Windows (claves "Uninstall").</summary>
public sealed record InstalledProgram(string Name, string? Publisher, string? Version, string? InstallLocation);

/// <summary>Información que se obtiene del propio PC, sin internet.</summary>
public sealed class LocalFileInfo
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required bool IsDirectory { get; init; }
    public required long Size { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? Modified { get; init; }

    // Metadatos de versión (exe, dll, msi...)
    public string? ProductName { get; init; }
    public string? CompanyName { get; init; }
    public string? FileDescription { get; init; }
    public string? ProductVersion { get; init; }

    /// <summary>Programa instalado cuya carpeta de instalación contiene este archivo.</summary>
    public InstalledProgram? OwnerProgram { get; init; }

    public bool IsInstaller { get; init; }
    public string? InstallerReason { get; init; }

    /// <summary>Lista de líneas "Etiqueta: valor" para mostrar y para enviar a la IA como contexto.</summary>
    public IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Tipo", IsDirectory ? "Carpeta" : "Archivo" + (System.IO.Path.GetExtension(Name) is { Length: > 0 } ext ? $" ({ext})" : ""));
        yield return ("Tamaño", $"{Format.Bytes(Size)} ({Size:N0} bytes)");
        if (Modified is { } m) yield return ("Modificado", m.ToString("g"));
        if (Created is { } c) yield return ("Creado", c.ToString("g"));
        if (!string.IsNullOrWhiteSpace(FileDescription)) yield return ("Descripción", FileDescription!);
        if (!string.IsNullOrWhiteSpace(ProductName)) yield return ("Producto", ProductName!);
        if (!string.IsNullOrWhiteSpace(CompanyName)) yield return ("Fabricante", CompanyName!);
        if (!string.IsNullOrWhiteSpace(ProductVersion)) yield return ("Versión", ProductVersion!);
        if (OwnerProgram is { } p)
            yield return ("Programa instalado", p.Name + (p.Publisher is { Length: > 0 } pub ? $" — {pub}" : ""));
        if (IsInstaller) yield return ("Archivo de instalación", InstallerReason ?? "Sí");
    }
}

public static class LocalFileInspector
{
    private static readonly string[] InstallerExtensions = [".msi", ".msp", ".msu", ".msix", ".msixbundle", ".appx", ".appxbundle", ".cab"];
    private static readonly string[] InstallerNameHints = ["setup", "install", "instalador", "installer", "uninst", "redist", "vc_redist", "update"];
    private static readonly string[] InstallerFolderHints = [@"\windows\installer\", @"\package cache\", @"\downloaded installations\", @"\softwaredistribution\download\"];

    private static List<InstalledProgram>? _programs;

    public static LocalFileInfo Inspect(FsNode node)
    {
        var path = node.FullPath;
        DateTime? created = null, modified = null;
        try
        {
            FileSystemInfo info = node.IsDirectory ? new DirectoryInfo(path) : new FileInfo(path);
            created = info.CreationTime;
            modified = info.LastWriteTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        string? product = null, company = null, description = null, version = null;
        if (!node.IsDirectory)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(path);
                product = v.ProductName;
                company = v.CompanyName;
                description = v.FileDescription;
                version = v.ProductVersion;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

            if (System.IO.Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
                System.IO.Path.GetExtension(path).Equals(".msp", StringComparison.OrdinalIgnoreCase))
            {
                var (subject, author) = ReadMsiSummary(path);
                product ??= subject;
                company ??= author;
            }
        }

        var (isInstaller, reason) = DetectInstaller(path, node.IsDirectory, description);

        return new LocalFileInfo
        {
            Path = path,
            Name = node.Name,
            IsDirectory = node.IsDirectory,
            Size = node.Size,
            Created = created,
            Modified = modified,
            ProductName = product,
            CompanyName = company,
            FileDescription = description,
            ProductVersion = version,
            OwnerProgram = FindOwnerProgram(path),
            IsInstaller = isInstaller,
            InstallerReason = reason,
        };
    }

    private static (bool, string?) DetectInstaller(string path, bool isDirectory, string? description)
    {
        if (isDirectory) return (false, null);
        var lowerPath = path.ToLowerInvariant();
        var name = System.IO.Path.GetFileNameWithoutExtension(lowerPath);
        var ext = System.IO.Path.GetExtension(lowerPath);

        if (InstallerExtensions.Contains(ext))
            return (true, $"Paquete de instalación de Windows ({ext})");
        if (InstallerFolderHints.FirstOrDefault(lowerPath.Contains) is { } folder)
            return (true, $"Ubicado en una carpeta de instaladores ({folder.Trim('\\')})");
        if (ext == ".exe")
        {
            var desc = description?.ToLowerInvariant() ?? "";
            if (InstallerNameHints.Any(h => name.Contains(h)) || desc.Contains("setup") || desc.Contains("install"))
                return (true, "Ejecutable de instalación (por nombre/descripción)");
        }
        return (false, null);
    }

    private static InstalledProgram? FindOwnerProgram(string path)
    {
        var programs = _programs ??= LoadInstalledPrograms();
        InstalledProgram? best = null;
        foreach (var p in programs)
        {
            var loc = p.InstallLocation!;
            if ((path.Equals(loc, StringComparison.OrdinalIgnoreCase) || path.StartsWith(loc + "\\", StringComparison.OrdinalIgnoreCase))
                && (best is null || loc.Length > best.InstallLocation!.Length))
                best = p;
        }
        return best;
    }

    private static List<InstalledProgram> LoadInstalledPrograms()
    {
        var result = new List<InstalledProgram>();
        const string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Default),
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(key);
                if (uninstall is null) continue;
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(sub);
                    var name = k?.GetValue("DisplayName") as string;
                    var location = (k?.GetValue("InstallLocation") as string)?.Trim().Trim('"').TrimEnd('\\');
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location) || location.Length <= 3) continue;
                    result.Add(new InstalledProgram(name, k!.GetValue("Publisher") as string, k.GetValue("DisplayVersion") as string, location));
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return result;
    }

    /// <summary>Lee el "Asunto" (nombre del producto) y el autor del resumen de un .msi/.msp.</summary>
    private static (string? Subject, string? Author) ReadMsiSummary(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WindowsInstaller.Installer");
            if (type is null) return (null, null);
            dynamic installer = Activator.CreateInstance(type)!;
            dynamic summary = installer.SummaryInformation(path, 0);
            string? subject = summary.Property(3) as string;
            string? author = summary.Property(4) as string;
            return (NullIfEmpty(subject), NullIfEmpty(author));
        }
        catch
        {
            return (null, null);
        }

        static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
