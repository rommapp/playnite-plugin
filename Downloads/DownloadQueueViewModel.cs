using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RomM.Downloads
{
    public class DownloadQueueViewModel : INotifyPropertyChanged
    {
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
    }
}
