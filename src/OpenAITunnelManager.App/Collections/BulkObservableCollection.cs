using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OpenAITunnelManager.App.Collections;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        RaiseReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();
        var changed = false;
        foreach (var item in items)
        {
            Items.Add(item);
            changed = true;
        }
        if (changed) RaiseReset();
    }

    public void RemoveFirst(int count)
    {
        if (count <= 0 || Items.Count == 0) return;
        CheckReentrancy();
        count = Math.Min(count, Items.Count);
        for (var index = 0; index < count; index++) Items.RemoveAt(0);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
