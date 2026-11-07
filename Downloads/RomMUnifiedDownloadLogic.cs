using Newtonsoft.Json;
using Playnite.SDK;
using RomM.Games;
using RomM.Models.RomM.Rom;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnifiedDownloadManagerApiNS;
using UnifiedDownloadManagerApiNS.Interfaces;
using UnifiedDownloadManagerApiNS.Models;

namespace RomM.Downloads
{
    // UDM owns scheduling and UI; RomM owns authentication, extraction and Playnite install events.
    internal sealed class RomMUnifiedDownloadLogic : IUnifiedDownloadLogic
    {
        private readonly RomM plugin;
        private readonly RomMDownloadInstaller installer = new RomMDownloadInstaller(HttpClientSingleton.Instance);
        private readonly Dictionary<Guid, DownloadRequest> requests = new Dictionary<Guid, DownloadRequest>();
        private readonly HashSet<Guid> running = new HashSet<Guid>();
        private readonly HashSet<Guid> cleaned = new HashSet<Guid>();
        private readonly Dictionary<Guid, TaskCompletionSource<bool>> stopped = new Dictionary<Guid, TaskCompletionSource<bool>>();

        internal RomMUnifiedDownloadLogic(RomM plugin) { this.plugin = plugin; }

        private IUnifiedTaskManager Manager => (plugin.Playnite.Addons.Plugins.FirstOrDefault(
            p => p.Id == UnifiedDownloadManagerSharedProperties.Id) as IUnifiedDownloadManager)?.Manager;

        internal bool IsAvailable => plugin.Settings?.UseUnifiedDownloadManager == true && Manager != null;

        internal bool HasPending(Guid gameId)
        {
            var task = Manager?.GetTask(gameId.ToString(), plugin.Id.ToString());
            return task != null && (task.status == UnifiedDownloadStatus.Running ||
                task.status == UnifiedDownloadStatus.Queued || task.status == UnifiedDownloadStatus.Paused);
        }

        private string RecordPath(Guid id) => Path.Combine(plugin.GetPluginUserDataPath(), "UnifiedDownloads", id + ".json");

        internal void Enqueue(DownloadRequest request, GameInstallInfo info)
        {
            plugin.Playnite.MainView.UIDispatcher.Invoke(() => EnqueueOnUI(request, info));
        }

        private void EnqueueOnUI(DownloadRequest request, GameInstallInfo info)
        {
            if (HasPending(request.GameId)) return;

            var manager = Manager;
            var oldTask = manager.GetTask(request.GameId.ToString(), plugin.Id.ToString());
            if (oldTask != null)
            {
                oldTask.PropertyChanged -= TaskChanged;
                manager.RemoveTask(oldTask);
            }
            cleaned.Remove(request.GameId);

            var record = new UnifiedInstallRecord
            {
                Host = plugin.Settings.RomMHost,
                MappingId = info.Mapping.MappingId,
                Revision = new RomMRevision
                {
                    Id = info.Id, FileName = info.FileName, FolderName = info.FolderName,
                    HasMultipleFiles = info.HasMultipleFiles, DownloadAsArchive = info.DownloadAsArchive,
                    PlayableFile = info.PlayableFile, DownloadURL = info.DownloadURL
                },
                InstallDir = request.InstallDir,
                GamePath = request.GamePath,
                InstallFlat = request.InstallFlat
            };
            var path = RecordPath(request.GameId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(record));
            requests[request.GameId] = request;

            var task = new UnifiedDownload
            {
                gameID = request.GameId.ToString(), name = request.GameName,
                pluginId = plugin.Id.ToString(), sourceName = plugin.Name,
                fullInstallPath = request.InstallDir, status = UnifiedDownloadStatus.Queued
            };
            Observe(task);
            // AddTasks awaits the whole queue. Do not block Playnite's installation action on it.
            _ = AddTask(manager, task);
        }

        private async Task AddTask(IUnifiedTaskManager manager, UnifiedDownload task)
        {
            try { await manager.AddTasks(new List<UnifiedDownload> { task }); }
            catch (Exception ex)
            {
                task.status = UnifiedDownloadStatus.Error;
                plugin.Logger.Error(ex, "Could not queue RomM download in Unified Download Manager.");
                if (Guid.TryParse(task.gameID, out var id))
                {
                    // The task never reached UDM's queue, so drop the persisted record too.
                    requests.TryGetValue(id, out var request);
                    requests.Remove(id);
                    try { File.Delete(RecordPath(id)); } catch { }
                    request?.OnFailed?.Invoke(ex);
                }
            }
        }

        // Reattach queued/canceled controls to UDM entries restored from its own persistent queue.
        internal void Restore()
        {
            if (Manager?.Downloads == null) return;
            foreach (var task in Manager.Downloads.Where(t => t.pluginId == plugin.Id.ToString()))
            {
                // No worker survives a Playnite restart, including after a crash.
                if (task.status == UnifiedDownloadStatus.Running || task.status == UnifiedDownloadStatus.Queued)
                    task.status = UnifiedDownloadStatus.Paused;
                Observe(task);
            }
        }

        private void Observe(UnifiedDownload task)
        {
            task.PropertyChanged -= TaskChanged;
            task.PropertyChanged += TaskChanged;
        }

        private async void TaskChanged(object sender, PropertyChangedEventArgs args)
        {
            var task = (UnifiedDownload)sender;
            if (args.PropertyName != nameof(UnifiedDownload.status) || task.status != UnifiedDownloadStatus.Canceled)
                return;
            // Playnite cancellation changes the status directly, whereas UDM's own UI invokes
            // OnCancelDownload. Handle both paths; cleanup is idempotent and, for a running
            // transfer, OnCancelDownload waits for the worker to stop before cleaning up.
            if (Guid.TryParse(task.gameID, out _))
            {
                try { await OnCancelDownload(task); }
                catch (Exception ex) { plugin.Logger.Error(ex, "Could not cancel RomM download."); }
            }
        }

        internal void Cancel(Guid gameId)
        {
            plugin.Playnite.MainView.UIDispatcher.Invoke(() =>
            {
                var task = Manager?.GetTask(gameId.ToString(), plugin.Id.ToString());
                if (task == null || task.status == UnifiedDownloadStatus.Completed ||
                    task.status == UnifiedDownloadStatus.Canceled || task.status == UnifiedDownloadStatus.Error) return;
                task.gracefulCts?.Cancel();
                task.status = UnifiedDownloadStatus.Canceled;
            });
        }

        private DownloadRequest GetRequest(UnifiedDownload task)
        {
            var id = Guid.Parse(task.gameID);
            var game = plugin.Playnite.Database.Games[id];
            if (game == null || game.PluginId != plugin.Id)
                throw new InvalidOperationException("This RomM game is no longer in the Playnite library.");

            var record = JsonConvert.DeserializeObject<UnifiedInstallRecord>(File.ReadAllText(RecordPath(id)));
            if (!string.Equals(record.Host, plugin.Settings.RomMHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The RomM server changed. Remove this download and install the game again.");
            var mapping = plugin.Settings.MappingById(record.MappingId);
            if (mapping == null)
                throw new InvalidOperationException("The emulator mapping was removed. Remove this download and install the game again.");

            var restored = new RomMInstallController(game, plugin, GameInstallInfo.From(record.Revision, mapping)).CreateDownloadRequest();
            if (restored.InstallDir != record.InstallDir || restored.GamePath != record.GamePath)
                throw new InvalidOperationException("The installation folder changed. Remove this download and install the game again.");

            if (!requests.TryGetValue(id, out var request)) requests[id] = request = restored;
            return request;
        }

        public async Task StartDownload(UnifiedDownload task)
        {
            var id = Guid.Parse(task.gameID);
            if (!running.Add(id)) throw new InvalidOperationException("This RomM download is already running.");
            cleaned.Remove(id);
            var completion = new TaskCompletionSource<bool>();
            stopped[id] = completion;
            DownloadRequest request = null;
            var watch = Stopwatch.StartNew();
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(
                task.gracefulCts?.Token ?? CancellationToken.None,
                task.forcefulCts?.Token ?? CancellationToken.None))
            {
                var item = new DownloadQueueItem { GameId = id, GameName = task.name, Cts = cts };
                item.PropertyChanged += (_, __) => plugin.Playnite.MainView.UIDispatcher.Invoke(() =>
                {
                    task.activity = item.StatusText;
                    task.elapsed = watch.Elapsed;
                    task.progress = item.IsIndeterminate || item.ProgressMaximum <= 0 ? 0 : item.ProgressValue / item.ProgressMaximum * 100;
                    if (item.Status == DownloadStatus.Downloading)
                    {
                        task.downloadSizeBytes = item.IsIndeterminate ? 0 : item.ProgressMaximum;
                        task.downloadedBytes = item.ProgressValue;
                        task.downloadSpeedBytes = watch.Elapsed.TotalSeconds > 0 ? item.ProgressValue / watch.Elapsed.TotalSeconds : 0;
                    }
                    else task.downloadSpeedBytes = 0;
                });
                try
                {
                    request = GetRequest(task);
                    var recordPath = RecordPath(id);
                    var record = JsonConvert.DeserializeObject<UnifiedInstallRecord>(File.ReadAllText(recordPath));
                    record.HasStarted = true;
                    File.WriteAllText(recordPath, JsonConvert.SerializeObject(record));
                    task.status = UnifiedDownloadStatus.Running;
                    var game = plugin.Playnite.Database.Games[id];
                    game.IsInstalling = true;
                    task.installSizeBytes = game.InstallSize ?? 0;
                    plugin.Playnite.Database.Games.Update(game);
                    await Task.Run(() => installer.DownloadAndInstall(item, request));
                    task.status = UnifiedDownloadStatus.Completed;
                    task.progress = 100;
                    task.completedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    task.activity = "Completed";
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    if (task.status != UnifiedDownloadStatus.Canceled) task.status = UnifiedDownloadStatus.Paused;
                    task.activity = task.status == UnifiedDownloadStatus.Paused ? "Paused; resuming restarts the transfer" : "Canceled";
                }
                catch (Exception ex)
                {
                    task.status = UnifiedDownloadStatus.Error;
                    task.activity = ex.Message;
                    plugin.Logger.Error(ex, "RomM download failed.");
                    if (request != null)
                    {
                        // Match the built-in queue: don't leave a partial transfer behind.
                        await Task.Run(() => installer.TryCleanupPartialInstall(request));
                        request.OnFailed?.Invoke(ex);
                    }
                    else
                    {
                        var game = plugin.Playnite.Database.Games[id];
                        if (game != null && game.PluginId == plugin.Id)
                        {
                            game.IsInstalling = false;
                            plugin.Playnite.Database.Games.Update(game);
                        }
                        plugin.Playnite.Notifications.Add(task.gameID, ex.Message, NotificationType.Error);
                    }
                }
                finally
                {
                    running.Remove(id);
                    stopped.Remove(id);
                    task.downloadSpeedBytes = 0;
                    completion.TrySetResult(true);
                }
            }
        }

        public async Task OnCancelDownload(UnifiedDownload task)
        {
            if (task.status == UnifiedDownloadStatus.Completed) return;
            // Use the original paths for cleanup even if the mapping/server has since changed.
            var id = Guid.Parse(task.gameID);
            if (stopped.TryGetValue(id, out var completion))
            {
                task.gracefulCts?.Cancel();
                await completion.Task;
            }
            if (task.status == UnifiedDownloadStatus.Completed) return;
            if (!cleaned.Add(id)) return;
            if (File.Exists(RecordPath(id)))
            {
                var record = JsonConvert.DeserializeObject<UnifiedInstallRecord>(File.ReadAllText(RecordPath(id)));
                if (record.HasStarted)
                    await Task.Run(() => installer.TryCleanupPartialInstall(new DownloadRequest
                    {
                        GameId = id, GameName = task.name, GamePath = record.GamePath,
                        InstallDir = record.InstallDir, InstallFlat = record.InstallFlat
                    }));
            }
            var game = plugin.Playnite.Database.Games[id];
            if (game != null && game.PluginId == plugin.Id)
            {
                game.IsInstalling = false;
                plugin.Playnite.Database.Games.Update(game);
            }
            if (requests.TryGetValue(id, out var request)) request.OnCanceled?.Invoke();
        }

        public async Task OnRemoveDownloadEntry(UnifiedDownload task)
        {
            task.PropertyChanged -= TaskChanged;
            if (task.status != UnifiedDownloadStatus.Completed)
            {
                task.status = UnifiedDownloadStatus.Canceled;
                await OnCancelDownload(task);
            }
            var id = Guid.Parse(task.gameID);
            requests.Remove(id);
            cleaned.Remove(id);
            var path = RecordPath(id);
            if (File.Exists(path)) File.Delete(path);
        }

        public void OpenDownloadPropertiesWindow(UnifiedDownload task)
        {
            plugin.Playnite.Dialogs.ShowMessage($"{task.name}\n\nInstall folder: {task.fullInstallPath}\n\n" +
                "Pausing keeps this queue entry. Resuming or retrying restarts the transfer.\n" +
                "To change the ROM version or install folder, remove the entry and install again.", "RomM download");
        }
    }

    internal sealed class UnifiedInstallRecord
    {
        public string Host { get; set; }
        public Guid MappingId { get; set; }
        public RomMRevision Revision { get; set; }
        public string InstallDir { get; set; }
        public string GamePath { get; set; }
        public bool InstallFlat { get; set; }
        public bool HasStarted { get; set; }
    }
}
