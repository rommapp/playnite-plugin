using Playnite.SDK;
using Playnite.SDK.Plugins;
using RomM.Games;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RomM.Downloads
{
    public class DownloadQueueController
    {
        private readonly IPlayniteAPI api;
        private readonly RomMDownloadInstaller installer = new RomMDownloadInstaller(HttpClientSingleton.Instance);
        private readonly DownloadQueueViewModel vm;

        private readonly SemaphoreSlim concurrencyGate;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> activeDownloads =
            new System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource>();

        public ILogger Logger => LogManager.GetLogger();
        public int MaxConcurrent { get; }

        public DownloadQueueController(IPlayniteAPI api, DownloadQueueViewModel vm, int maxConcurrent)
        {
            this.api = api;
            this.vm = vm;

            MaxConcurrent = Math.Max(1, maxConcurrent);
            concurrencyGate = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
        }

        public DownloadQueueViewModel ViewModel => vm;

        public void Enqueue(DownloadRequest req)
        {
            var item = new DownloadQueueItem
            {
                GameId = req.GameId,
                GameName = req.GameName,
                QueuedOn = DateTime.Now,
                Cts = new CancellationTokenSource()
            };

            activeDownloads[item.GameId] = item.Cts;

            item.SetStatus(DownloadStatus.Queued, "Queued");
            item.SetProgress(0, 1, true);

            api.MainView.UIDispatcher.Invoke(() => vm.Items.Add(item));

            // fire and forget background worker
            Task.Run(async () => await ProcessItem(item, req));
        }

        public void Cancel(Guid gameId)
        {
            if (activeDownloads.TryGetValue(gameId, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "An error occurred while cancelling a download.");
                }
            }
        }

        private async Task ProcessItem(DownloadQueueItem item, DownloadRequest req)
        {
            await concurrencyGate.WaitAsync().ConfigureAwait(false);

            try
            {
                await installer.DownloadAndInstall(item, req).ConfigureAwait(false);

                item.SetStatus(DownloadStatus.Completed, "Completed");
                item.SetProgress(item.ProgressMaximum, item.ProgressMaximum, false);

                await Task.Delay(1000).ConfigureAwait(false);

                RemoveFromList(item);
            }
            catch (OperationCanceledException)
            {
                item.SetStatus(DownloadStatus.Canceled, "Canceled");
                item.SetProgress(0, 1, false);

                installer.TryCleanupPartialInstall(req);

                req.OnCanceled?.Invoke();

                await Task.Delay(500).ConfigureAwait(false);
                RemoveFromList(item);
            }
            catch (Exception ex)
            {
                item.SetStatus(DownloadStatus.Failed, "Failed");
                installer.TryCleanupPartialInstall(req);

                req.OnFailed?.Invoke(ex);

                await Task.Delay(1500).ConfigureAwait(false);
                RemoveFromList(item);
            }
            finally
            {
                activeDownloads.TryRemove(item.GameId, out _);
                concurrencyGate.Release();
            }
        }

        private void RemoveFromList(DownloadQueueItem item)
        {
            if (item == null) return;

            api.MainView.UIDispatcher.Invoke(() =>
            {
                vm.Items.Remove(item);
            });
        }

    }
}
