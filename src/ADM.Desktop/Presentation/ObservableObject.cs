using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ADM.Desktop.Presentation;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(property);
        return true;
    }

    protected void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    protected static void ReplaceIfChanged<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        T[] next = values.ToArray();
        if (target.SequenceEqual(next)) return;
        target.Clear();
        foreach (T item in next) target.Add(item);
    }
}
