using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using RomM.Models;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace RomM.Controllers
{
    public class DownloadQueueController : INotifyPropertyChanged
    {
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;
        private readonly ObservableCollection<DownloadItem> _downloadQueue;
        private DownloadItem _currentDownload;
        private bool _isProcessing;

        public ObservableCollection<DownloadItem> DownloadQueue => _downloadQueue;

        public DownloadItem CurrentDownload
        {
            get => _currentDownload;
            private set => SetProperty(ref _currentDownload, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set => SetProperty(ref _isProcessing, value);
        }

        public bool HasActiveDownloads => _downloadQueue.Any(item => item.IsDownloading || item.IsExtracting);

        public DownloadQueueController(IPlayniteAPI playniteApi)
        {
            _playniteApi = playniteApi;
            _logger = LogManager.GetLogger();
            _downloadQueue = new ObservableCollection<DownloadItem>();
            _downloadQueue.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasActiveDownloads));
        }

        public void AddDownload(string gameName, long totalBytes = 0)
        {
            var downloadItem = new DownloadItem(gameName)
            {
                TotalBytes = totalBytes
            };

            _downloadQueue.Add(downloadItem);
            _logger.Debug($"Added download to queue: {gameName}");
            
            // Start processing if not already processing
            if (!IsProcessing)
            {
                _ = Task.Run(ProcessQueue);
            }
        }

        public void RemoveDownload(DownloadItem downloadItem)
        {
            if (_downloadQueue.Contains(downloadItem))
            {
                _downloadQueue.Remove(downloadItem);
                _logger.Debug($"Removed download from queue: {downloadItem.GameName}");
            }
        }

        public void ClearCompleted()
        {
            var completedItems = _downloadQueue.Where(item => item.IsCompleted || item.HasError).ToList();
            foreach (var item in completedItems)
            {
                _downloadQueue.Remove(item);
            }
            _logger.Debug($"Cleared {completedItems.Count} completed downloads from queue");
        }

        public void ClearAll()
        {
            _downloadQueue.Clear();
            CurrentDownload = null;
            _logger.Debug("Cleared all downloads from queue");
        }

        public void UpdateDownloadProgress(string gameName, int progress, long bytesDownloaded, long totalBytes)
        {
            var downloadItem = _downloadQueue.FirstOrDefault(item => item.GameName == gameName);
            if (downloadItem != null)
            {
                downloadItem.Progress = progress;
                downloadItem.BytesDownloaded = bytesDownloaded;
                downloadItem.TotalBytes = totalBytes;
                downloadItem.Status = $"Downloading... {progress}%";
            }
        }

        public void UpdateExtractionProgress(string gameName, int progress)
        {
            var downloadItem = _downloadQueue.FirstOrDefault(item => item.GameName == gameName);
            if (downloadItem != null)
            {
                downloadItem.Progress = progress;
                downloadItem.Status = $"Extracting... {progress}%";
            }
        }

        public void SetDownloading(string gameName)
        {
            var downloadItem = _downloadQueue.FirstOrDefault(item => item.GameName == gameName);
            if (downloadItem != null)
            {
                downloadItem.IsDownloading = true;
                downloadItem.IsExtracting = false;
                downloadItem.Status = "Downloading...";
                CurrentDownload = downloadItem;
            }
        }

        public void SetExtracting(string gameName)
        {
            var downloadItem = _downloadQueue.FirstOrDefault(item => item.GameName == gameName);
            if (downloadItem != null)
            {
                downloadItem.IsDownloading = false;
                downloadItem.IsExtracting = true;
                downloadItem.Status = "Extracting...";
                CurrentDownload = downloadItem;
            }
        }

        public void SetCompleted(string gameName)
        {
            var downloadItem = _downloadQueue.FirstOrDefault(item => item.GameName == gameName);
            if (downloadItem != null)
            {
                downloadItem.IsDownloading = false;
                downloadItem.IsExtracting = false;
                downloadItem.IsCompleted = true;
                downloadItem.Status = "Completed";
                downloadItem.Progress = 100;
                downloadItem.EndTime = DateTime.Now;
                CurrentDownload = null;
                _logger.Debug($"Download completed: {gameName}");
            }
        }

        public void SetError(string gameName, string errorMessage)
        {
            var downloadItem = _downloadQueue.FirstOrDefault(item => item.GameName == gameName);
            if (downloadItem != null)
            {
                downloadItem.IsDownloading = false;
                downloadItem.IsExtracting = false;
                downloadItem.HasError = true;
                downloadItem.ErrorMessage = errorMessage;
                downloadItem.Status = "Error";
                downloadItem.EndTime = DateTime.Now;
                CurrentDownload = null;
                _logger.Error($"Download failed: {gameName} - {errorMessage}");
            }
        }

        private async Task ProcessQueue()
        {
            if (IsProcessing) return;

            IsProcessing = true;

            try
            {
                while (_downloadQueue.Any(item => !item.IsCompleted && !item.HasError))
                {
                    var nextDownload = _downloadQueue.FirstOrDefault(item => !item.IsCompleted && !item.HasError && !item.IsDownloading && !item.IsExtracting);
                    if (nextDownload == null)
                    {
                        await Task.Delay(1000); // Wait 1 second before checking again
                        continue;
                    }

                    // This method will be called by the InstallController when it starts processing
                    // The actual download logic remains in the InstallController
                    await Task.Delay(100);
                }
            }
            finally
            {
                IsProcessing = false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
