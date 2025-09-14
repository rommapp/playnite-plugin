using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using RomM.Controllers;
using RomM.Models;
using Playnite.SDK;

namespace RomM.ViewModels
{
    public class DownloadQueueSidebarViewModel : INotifyPropertyChanged
    {
        private readonly DownloadQueueController _downloadQueueController;
        private readonly IPlayniteAPI _playniteApi;
        private readonly ILogger _logger;

        public ObservableCollection<DownloadItem> DownloadQueue => _downloadQueueController.DownloadQueue;

        public DownloadItem CurrentDownload => _downloadQueueController.CurrentDownload;

        public bool HasActiveDownloads => _downloadQueueController.HasActiveDownloads;

        public bool IsProcessing => _downloadQueueController.IsProcessing;

        public ICommand ClearCompletedCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand RemoveDownloadCommand { get; }

        public DownloadQueueSidebarViewModel(DownloadQueueController downloadQueueController, IPlayniteAPI playniteApi)
        {
            _downloadQueueController = downloadQueueController;
            _playniteApi = playniteApi;
            _logger = LogManager.GetLogger();

            // Subscribe to property changes
            _downloadQueueController.PropertyChanged += OnDownloadQueueControllerPropertyChanged;

            // Initialize commands
            ClearCompletedCommand = new RelayCommand(ClearCompleted, () => DownloadQueue.Any(item => item.IsCompleted || item.HasError));
            ClearAllCommand = new RelayCommand(ClearAll, () => DownloadQueue.Any());
            RemoveDownloadCommand = new RelayCommand<DownloadItem>(RemoveDownload, item => item != null && !item.IsDownloading && !item.IsExtracting);
        }

        private void OnDownloadQueueControllerPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            OnPropertyChanged(e.PropertyName);
            
            // Update command states
            if (e.PropertyName == nameof(DownloadQueueController.DownloadQueue))
            {
                ((RelayCommand)ClearCompletedCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ClearAllCommand).RaiseCanExecuteChanged();
            }
        }

        private void ClearCompleted()
        {
            try
            {
                _downloadQueueController.ClearCompleted();
                _logger.Debug("Cleared completed downloads from sidebar");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to clear completed downloads");
            }
        }

        private void ClearAll()
        {
            try
            {
                _downloadQueueController.ClearAll();
                _logger.Debug("Cleared all downloads from sidebar");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to clear all downloads");
            }
        }

        private void RemoveDownload(DownloadItem downloadItem)
        {
            try
            {
                _downloadQueueController.RemoveDownload(downloadItem);
                _logger.Debug($"Removed download from sidebar: {downloadItem?.GameName}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to remove download: {downloadItem?.GameName}");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke((T)parameter) ?? true;
        }

        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
