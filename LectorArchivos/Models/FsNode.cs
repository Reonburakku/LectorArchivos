namespace LectorArchivos.Models;

/// <summary>Nodo del árbol de archivos escaneado (archivo o carpeta).</summary>
public sealed class FsNode
{
    public FsNode(string name, bool isDirectory, FsNode? parent)
    {
        Name = name;
        IsDirectory = isDirectory;
        Parent = parent;
    }

    public string Name { get; }
    public bool IsDirectory { get; }
    public FsNode? Parent { get; }

    /// <summary>Tamaño en bytes (para carpetas, la suma de todo su contenido).</summary>
    public long Size { get; set; }

    /// <summary>Número de archivos contenidos (recursivo). Para un archivo vale 1.</summary>
    public long FileCount { get; set; }

    /// <summary>True si no se pudo leer el contenido completo de la carpeta (permisos).</summary>
    public bool AccessDenied { get; set; }

    public List<FsNode>? Children { get; set; }

    public bool HasChildren => Children is { Count: > 0 };

    /// <summary>Ruta completa reconstruida a partir de los padres (no se guarda para ahorrar memoria).</summary>
    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            return System.IO.Path.Combine(Parent.FullPath, Name);
        }
    }
}
