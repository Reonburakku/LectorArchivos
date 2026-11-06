using System.Diagnostics;
using System.IO;

namespace LectorArchivos.Models;

public readonly record struct ScanProgress(long Files, long Folders, long Bytes, string CurrentFolder);

/// <summary>Recorre una unidad completa y construye el árbol de <see cref="FsNode"/> con los tamaños acumulados.</summary>
public static class DriveScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        // Las carpetas sin permiso lanzan excepción, que se captura para marcarlas como no leídas.
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        // Los puntos de reanálisis (junctions/symlinks) se omiten para no contar dos veces ni entrar en bucles.
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public static FsNode Scan(string rootPath, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var root = new FsNode(rootPath, isDirectory: true, parent: null);
        var state = new ScanState(progress);
        ScanDirectory(new DirectoryInfo(rootPath), root, state, token);
        state.Report(rootPath, force: true);
        return root;
    }

    private static void ScanDirectory(DirectoryInfo dir, FsNode node, ScanState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        state.Folders++;
        state.Report(dir.FullName);

        var children = new List<FsNode>();
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos("*", Options))
            {
                if (entry is DirectoryInfo subDir)
                {
                    var child = new FsNode(subDir.Name, isDirectory: true, parent: node);
                    ScanDirectory(subDir, child, state, token);
                    children.Add(child);
                    node.Size += child.Size;
                    node.FileCount += child.FileCount;
                    if (child.AccessDenied) node.AccessDenied = true;
                }
                else if (entry is FileInfo file)
                {
                    long length;
                    try { length = file.Length; }
                    catch (IOException) { length = 0; }

                    children.Add(new FsNode(file.Name, isDirectory: false, parent: node) { Size = length, FileCount = 1 });
                    node.Size += length;
                    node.FileCount++;
                    state.Files++;
                    state.Bytes += length;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.AccessDenied = true;
        }

        // Los más pesados primero.
        children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        children.TrimExcess();
        node.Children = children;
    }

    private sealed class ScanState(IProgress<ScanProgress>? progress)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastReportMs = -1000;

        public long Files;
        public long Folders;
        public long Bytes;

        public void Report(string folder, bool force = false)
        {
            if (progress is null) return;
            var now = _clock.ElapsedMilliseconds;
            if (!force && now - _lastReportMs < 150) return;
            _lastReportMs = now;
            progress.Report(new ScanProgress(Files, Folders, Bytes, folder));
        }
    }
}
