using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LectorArchivos.ViewModels;

/// <summary>ObservableCollection con inserción/eliminación por bloques (una sola notificación).</summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void InsertRange(int index, IEnumerable<T> items)
    {
        CheckReentrancy();
        foreach (var item in items) Items.Insert(index++, item);
        RaiseReset();
    }

    public void RemoveRange(int index, int count)
    {
        if (count <= 0) return;
        CheckReentrancy();
        if (Items is List<T> list) list.RemoveRange(index, count);
        else for (int i = 0; i < count; i++) Items.RemoveAt(index);
        RaiseReset();
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
