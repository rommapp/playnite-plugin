using System.Collections.ObjectModel;

namespace RomM.Downloads
{
    public class DownloadQueue
    {
        public ObservableCollection<DownloadQueueItem> Items { get; } = new ObservableCollection<DownloadQueueItem>();
        public ObservableCollection<DownloadQueueItem> Completed { get; } = new ObservableCollection<DownloadQueueItem>();

        private DownloadQueueItem currentItem;
        public DownloadQueueItem CurrentItem
        {
            get => currentItem;
            set
            {
                currentItem = value;
            }
        }
    }
}
