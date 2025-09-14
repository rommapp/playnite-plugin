using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RomM.Models
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private string _gameName;
        private string _status;
        private int _progress;
        private long _bytesDownloaded;
        private long _totalBytes;
        private bool _isDownloading;
        private bool _isExtracting;
        private bool _isCompleted;
        private bool _hasError;
        private string _errorMessage;
        private DateTime _startTime;
        private DateTime? _endTime;

        public string GameName
        {
            get => _gameName;
            set => SetProperty(ref _gameName, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public int Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public long BytesDownloaded
        {
            get => _bytesDownloaded;
            set => SetProperty(ref _bytesDownloaded, value);
        }

        public long TotalBytes
        {
            get => _totalBytes;
            set => SetProperty(ref _totalBytes, value);
        }

        public bool IsDownloading
        {
            get => _isDownloading;
            set => SetProperty(ref _isDownloading, value);
        }

        public bool IsExtracting
        {
            get => _isExtracting;
            set => SetProperty(ref _isExtracting, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public DateTime StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        public DateTime? EndTime
        {
            get => _endTime;
            set => SetProperty(ref _endTime, value);
        }

        public string FormattedSize
        {
            get
            {
                if (TotalBytes == 0) return "Unknown size";
                return FormatBytes(TotalBytes);
            }
        }

        public string FormattedDownloaded
        {
            get
            {
                return FormatBytes(BytesDownloaded);
            }
        }

        public string FormattedSpeed
        {
            get
            {
                if (IsCompleted || HasError) return "0 B/s";
                
                var elapsed = DateTime.Now - StartTime;
                if (elapsed.TotalSeconds < 1) return "Calculating...";
                
                var speed = BytesDownloaded / elapsed.TotalSeconds;
                return $"{FormatBytes((long)speed)}/s";
            }
        }

        public string FormattedTimeRemaining
        {
            get
            {
                if (IsCompleted || HasError || Progress == 0) return "Unknown";
                
                var elapsed = DateTime.Now - StartTime;
                var remainingBytes = TotalBytes - BytesDownloaded;
                var speed = BytesDownloaded / elapsed.TotalSeconds;
                
                if (speed <= 0) return "Unknown";
                
                var remainingTime = TimeSpan.FromSeconds(remainingBytes / speed);
                return FormatTimeSpan(remainingTime);
            }
        }

        public DownloadItem(string gameName)
        {
            GameName = gameName;
            Status = "Queued";
            Progress = 0;
            BytesDownloaded = 0;
            TotalBytes = 0;
            IsDownloading = false;
            IsExtracting = false;
            IsCompleted = false;
            HasError = false;
            StartTime = DateTime.Now;
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h {timeSpan.Minutes}m";
            else if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            else
                return $"{timeSpan.Seconds}s";
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
