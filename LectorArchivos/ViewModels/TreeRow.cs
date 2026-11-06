using System.ComponentModel;
using System.Runtime.CompilerServices;
using LectorArchivos.Models;

namespace LectorArchivos.ViewModels;

/// <summary>
/// Fila visible del árbol. El árbol se "aplana" en una lista virtualizada: al expandir una carpeta
/// se insertan sus hijos justo debajo, con más sangría. Así las columnas quedan siempre alineadas
/// y carpetas con cientos de miles de archivos se muestran sin bloquear la interfaz.
/// </summary>
public sealed class TreeRow : INotifyPropertyChanged
{
    private const double IndentPerLevel = 18;
    private bool _isExpanded;

    public TreeRow(FsNode node, int depth, long driveTotalBytes)
    {
        Node = node;
        Depth = depth;
        Percent = driveTotalBytes > 0 ? node.Size * 100.0 / driveTotalBytes : 0;
    }

    public FsNode Node { get; }
    public int Depth { get; }

    public string Name => Node.Name;
    public bool IsDirectory => Node.IsDirectory;
    public bool CanExpand => Node.HasChildren;
    public double Indent => Depth * IndentPerLevel;

    public string Icon => Node.IsDirectory ? (Node.Parent is null ? "💽" : "📁") : "📄";

    public string SizeText => Format.Bytes(Node.Size);
    public string BytesText => Node.Size.ToString("N0");
    public double Percent { get; }
    public string PercentText => Percent.ToString(Percent < 0.01 && Percent > 0 ? "0.000" : "0.00") + " %";

    public string ToolTip => Node.IsDirectory
        ? $"{Node.FileCount:N0} archivos · {BytesText} bytes" + (Node.AccessDenied ? "\nAlgunas subcarpetas no se pudieron leer (permisos)." : "")
        : $"{BytesText} bytes";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExpanderGlyph));
        }
    }

    public string ExpanderGlyph => !CanExpand ? "" : _isExpanded ? "▾" : "▸";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
