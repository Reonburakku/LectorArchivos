using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using LectorArchivos.Models;
using LectorArchivos.Services;
using LectorArchivos.ViewModels;

namespace LectorArchivos;

public partial class MainWindow : Window
{
    private sealed record DriveItem(DriveInfo Drive, string Label);

    /// <summary>Por encima de este número de filas se inserta/elimina en bloque (una sola notificación).</summary>
    private const int BulkThreshold = 300;

    private readonly RangeObservableCollection<TreeRow> _rows = [];
    private readonly Dictionary<string, AiSummary> _aiCache = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings = AppSettings.Load();

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _detailCts;
    private long _driveTotalBytes;

    public MainWindow()
    {
        InitializeComponent();
        TreeList.ItemsSource = _rows;
        LoadDrives();
    }

    // ───────────────────────────── Unidades y escaneo ─────────────────────────────

    private void LoadDrives()
    {
        var items = new List<DriveItem>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? DriveTypeName(d.DriveType) : d.VolumeLabel;
                items.Add(new DriveItem(d,
                    $"{d.Name}  {label} ({d.DriveFormat}) — {Format.Bytes(d.AvailableFreeSpace)} libres de {Format.Bytes(d.TotalSize)}"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        DriveCombo.ItemsSource = items;
        DriveCombo.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private static string DriveTypeName(DriveType type) => type switch
    {
        DriveType.Fixed => "Disco local",
        DriveType.Removable => "Extraíble",
        DriveType.Network => "Red",
        DriveType.CDRom => "CD/DVD",
        _ => "Unidad",
    };

    private void RefreshDrives_Click(object sender, RoutedEventArgs e) => LoadDrives();

    private async void Explore_Click(object sender, RoutedEventArgs e)
    {
        if (DriveCombo.SelectedItem is not DriveItem item) return;

        long total;
        try { total = item.Drive.TotalSize; }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "Unidad no disponible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        SetScanning(true);
        _rows.ReplaceAll([]);
        ShowDetail(null);

        var progress = new Progress<ScanProgress>(p =>
            StatusText.Text = $"Explorando… {p.Files:N0} archivos, {p.Folders:N0} carpetas, {Format.Bytes(p.Bytes)} — {p.CurrentFolder}");

        var clock = Stopwatch.StartNew();
        try
        {
            var root = await RunWithLargeStack(() => DriveScanner.Scan(item.Drive.RootDirectory.FullName, progress, token));
            _driveTotalBytes = total;

            var rootRow = new TreeRow(root, 0, total);
            _rows.ReplaceAll([rootRow]);
            Expand(rootRow);
            TreeList.SelectedItem = null;

            StatusText.Text =
                $"{root.FileCount:N0} archivos analizados en {clock.Elapsed.TotalSeconds:N1} s · " +
                $"{Format.Bytes(root.Size)} contabilizados de {Format.Bytes(total)} " +
                $"(espacio usado según Windows: {Format.Bytes(total - item.Drive.TotalFreeSpace)})" +
                (root.AccessDenied ? " · Algunas carpetas protegidas no se pudieron leer (ejecuta como administrador para incluirlas)." : "");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Exploración cancelada.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error durante la exploración: " + ex.Message;
        }
        finally
        {
            SetScanning(false);
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    /// <summary>El recorrido es recursivo; se ejecuta en un hilo con pila amplia para rutas muy profundas.</summary>
    private static Task<T> RunWithLargeStack<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }, maxStackSize: 64 * 1024 * 1024)
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        thread.Start();
        return tcs.Task;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private void SetScanning(bool scanning)
    {
        ExploreButton.IsEnabled = !scanning;
        DriveCombo.IsEnabled = !scanning;
        RefreshDrivesButton.IsEnabled = !scanning;
        CancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressBar.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = scanning ? Cursors.AppStarting : null;
    }

    // ───────────────────────────── Árbol expandible ─────────────────────────────

    private void Toggle(TreeRow row)
    {
        if (!row.CanExpand) return;
        if (row.IsExpanded) Collapse(row); else Expand(row);
    }

    private void Expand(TreeRow row)
    {
        if (row.IsExpanded || !row.CanExpand) return;
        int index = _rows.IndexOf(row);
        if (index < 0) return;

        var children = row.Node.Children!.Select(c => new TreeRow(c, row.Depth + 1, _driveTotalBytes)).ToList();
        row.IsExpanded = true;

        if (children.Count > BulkThreshold)
        {
            var selected = TreeList.SelectedItem;
            _rows.InsertRange(index + 1, children);
            RestoreSelection(selected, row);
        }
        else
        {
            for (int i = 0; i < children.Count; i++) _rows.Insert(index + 1 + i, children[i]);
        }
    }

    private void Collapse(TreeRow row)
    {
        if (!row.IsExpanded) return;
        int index = _rows.IndexOf(row);
        row.IsExpanded = false;
        if (index < 0) return;

        int end = index + 1;
        while (end < _rows.Count && _rows[end].Depth > row.Depth) end++;
        int count = end - index - 1;

        var selected = TreeList.SelectedItem as TreeRow;
        bool selectionInside = selected is not null && _rows.IndexOf(selected) is var s && s > index && s < end;

        if (count > BulkThreshold)
        {
            _rows.RemoveRange(index + 1, count);
            RestoreSelection(selectionInside ? null : selected, row);
        }
        else
        {
            for (int i = 0; i < count; i++) _rows.RemoveAt(index + 1);
        }

        if (selectionInside) TreeList.SelectedItem = row;
    }

    /// <summary>Tras una notificación Reset, WPF puede perder la selección y el desplazamiento.</summary>
    private void RestoreSelection(object? selected, TreeRow anchor)
    {
        if (selected is not null && !Equals(TreeList.SelectedItem, selected)) TreeList.SelectedItem = selected;
        TreeList.ScrollIntoView(anchor);
    }

    private void Expander_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TreeRow row)
        {
            Toggle(row);
            e.Handled = true;
        }
    }

    private void TreeList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TreeList.SelectedItem is TreeRow row) Toggle(row);
    }

    private void TreeList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TreeList.SelectedItem is not TreeRow row) return;
        switch (e.Key)
        {
            case Key.Right when row.CanExpand && !row.IsExpanded:
                Expand(row);
                e.Handled = true;
                break;
            case Key.Left when row.IsExpanded:
                Collapse(row);
                e.Handled = true;
                break;
            case Key.Left when row.Depth > 0:
                // Ir a la carpeta padre.
                for (int i = _rows.IndexOf(row) - 1; i >= 0; i--)
                {
                    if (_rows[i].Depth < row.Depth)
                    {
                        TreeList.SelectedItem = _rows[i];
                        TreeList.ScrollIntoView(_rows[i]);
                        (TreeList.ItemContainerGenerator.ContainerFromItem(_rows[i]) as ListViewItem)?.Focus();
                        break;
                    }
                }
                e.Handled = true;
                break;
            case Key.Enter:
                Toggle(row);
                e.Handled = true;
                break;
        }
    }

    // ───────────────────────────── Panel de resumen ─────────────────────────────

    private void TreeList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowDetail((TreeList.SelectedItem as TreeRow)?.Node);

    private FsNode? _detailNode;

    private async void ShowDetail(FsNode? node, bool forceRefresh = false)
    {
        _detailCts?.Cancel();
        _detailNode = node;

        // Sin selección: el cuadro queda vacío.
        if (node is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var cts = _detailCts = new CancellationTokenSource();
        var token = cts.Token;

        DetailPanel.Visibility = Visibility.Visible;
        DetailName.Text = node.Name;
        DetailPath.Text = node.FullPath;
        FactsList.ItemsSource = null;
        AiText.Text = "";
        SourcesList.ItemsSource = null;
        SourcesTitle.Visibility = Visibility.Collapsed;
        AiSetupPanel.Visibility = Visibility.Collapsed;
        RetryAiButton.Visibility = Visibility.Collapsed;
        AiLoading.Visibility = Visibility.Collapsed;

        try
        {
            var info = await Task.Run(() => LocalFileInspector.Inspect(node), token);
            if (token.IsCancellationRequested) return;
            FactsList.ItemsSource = info.Facts().Select(f => new { f.Label, f.Value }).ToList();

            var apiKey = _settings.GetApiKey();
            if (apiKey is null)
            {
                AiSetupPanel.Visibility = Visibility.Visible;
                return;
            }

            if (!forceRefresh && _aiCache.TryGetValue(info.Path, out var cached))
            {
                ShowAi(cached);
                return;
            }

            AiLoading.Visibility = Visibility.Visible;
            // Pequeña espera para no consultar la API mientras el usuario recorre la lista con el teclado.
            await Task.Delay(700, token);

            var summary = await new GeminiClient(apiKey, _settings.Model).SummarizeAsync(info, token);
            _aiCache[info.Path] = summary;
            if (!token.IsCancellationRequested) ShowAi(summary);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Se seleccionó otro elemento.
        }
        catch (Exception ex) when (ex is GeminiException or HttpRequestException or TaskCanceledException)
        {
            if (token.IsCancellationRequested) return;
            AiText.Text = ex is TaskCanceledException
                ? "La consulta a Gemini tardó demasiado. Inténtalo de nuevo."
                : ex.Message;
            RetryAiButton.Visibility = Visibility.Visible;
        }
        finally
        {
            if (!token.IsCancellationRequested) AiLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowAi(AiSummary summary)
    {
        AiText.Text = summary.Text;
        SourcesList.ItemsSource = summary.Sources;
        SourcesTitle.Visibility = summary.Sources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryAiButton.Visibility = Visibility.Visible;
    }

    private void RetryAi_Click(object sender, RoutedEventArgs e)
    {
        if (_detailNode is not null) ShowDetail(_detailNode, forceRefresh: true);
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme is "http" or "https")
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings = AppSettings.Load();
            _aiCache.Clear();
            if (_detailNode is not null) ShowDetail(_detailNode);
        }
    }
}
