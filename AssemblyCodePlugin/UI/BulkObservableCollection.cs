using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace AssemblyCodePlugin.UI
{
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
                Items.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void ReplaceAll(IEnumerable<T> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}