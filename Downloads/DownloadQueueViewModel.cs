using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RomM.Downloads
{
    public class DownloadQueueViewModel : INotifyPropertyChanged
    {
        // Colecțiile expuse (bindabile în XAML)
        public ObservableCollection<DownloadQueueItem> Items { get; } = new ObservableCollection<DownloadQueueItem>();
        public ObservableCollection<DownloadQueueItem> Completed { get; } = new ObservableCollection<DownloadQueueItem>();

        private DownloadQueueItem currentItem;
        public DownloadQueueItem CurrentItem
        {
            get => currentItem;
            set
            {
                if (currentItem != value)
                {
                    currentItem = value;
                    OnPropChanged(nameof(CurrentItem));
                }
            }
        }

        public object DownloadQueue => this;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void AddItem(DownloadQueueItem item)
        {
            if (item == null) return;
            Items.Add(item);
            OnPropChanged(nameof(Items));
        }

        public void AddCompleted(DownloadQueueItem item)
        {
            if (item == null) return;
            Completed.Add(item);
            OnPropChanged(nameof(Completed));
        }

        public void RemoveItem(DownloadQueueItem item)
        {
            if (item == null) return;
            if (Items.Contains(item)) Items.Remove(item);
            if (Completed.Contains(item)) Completed.Remove(item);
            if (CurrentItem == item) CurrentItem = null;
            OnPropChanged(nameof(Items));
            OnPropChanged(nameof(Completed));
            OnPropChanged(nameof(CurrentItem));
        }
    }
}
