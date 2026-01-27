using Playnite.SDK;
using Playnite.SDK.Plugins;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
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
        private readonly ILogger logger;
        private readonly DownloadQueueViewModel vm;

        private readonly SemaphoreSlim concurrencyGate;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> activeDownloads =
            new System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource>();

        public int MaxConcurrent { get; }

        public DownloadQueueController(IPlayniteAPI api, DownloadQueueViewModel vm, int maxConcurrent)
        {
            this.api = api;
            this.vm = vm;
            this.logger = LogManager.GetLogger();

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
                    logger.Warn(ex, "An error occurred while cancelling a download.");
                }
            }
        }

        private async Task ProcessItem(DownloadQueueItem item, DownloadRequest req)
        {
            await concurrencyGate.WaitAsync().ConfigureAwait(false);

            try
            {
                await DownloadAndInstall(item, req).ConfigureAwait(false);

                item.SetStatus(DownloadStatus.Completed, "Completed");
                item.SetProgress(item.ProgressMaximum, item.ProgressMaximum, false);

                await Task.Delay(1000).ConfigureAwait(false);

                RemoveFromList(item);
            }
            catch (OperationCanceledException)
            {
                item.SetStatus(DownloadStatus.Canceled, "Canceled");
                item.SetProgress(0, 1, false);

                TryCleanupPartialInstall(req);

                req.OnCanceled?.Invoke();

                await Task.Delay(500).ConfigureAwait(false);
                RemoveFromList(item);
            }
            catch (Exception ex)
            {
                item.SetStatus(DownloadStatus.Failed, "Failed");
                TryCleanupPartialInstall(req);

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

        private async Task DownloadAndInstall(DownloadQueueItem item, DownloadRequest req)
        {
            var ct = item.Cts.Token;

            item.SetStatus(DownloadStatus.Downloading, "Downloading...");
            item.SetProgress(0, 1, true);

            using (var response = await RomM.GetAsync(req.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                                            .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                item.SetProgress(0, totalBytes ?? 1, !totalBytes.HasValue);

                Directory.CreateDirectory(req.InstallDir);

                byte[] buffer = new byte[1024 * 256];
                long downloaded = 0;
                long lastUiUpdate = 0;
                const long uiUpdateThreshold = 1024 * 512; // 512KB

                using (var httpStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(req.GamePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
                {
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();

                        int read = await httpStream.ReadAsync(buffer, 0, buffer.Length, ct);
                        if (read <= 0)
                        {
                            break;
                        }

                        await fileStream.WriteAsync(buffer, 0, read, ct);

                        downloaded += read;

                        if (downloaded - lastUiUpdate >= uiUpdateThreshold)
                        {
                            lastUiUpdate = downloaded;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                item.SetProgress(downloaded, totalBytes.Value, false);
                                var pct = (double)downloaded / totalBytes.Value * 100.0;
                                item.SetStatus(DownloadStatus.Downloading, "Downloading... " + pct.ToString("0") + "%");
                            }
                            else
                            {
                                item.SetProgress(downloaded, Math.Max(1, downloaded), true);
                                item.SetStatus(DownloadStatus.Downloading, "Downloading...");
                            }
                        }
                    }
                }
            }

            // Extract if needed (we treat extract as 0..100 in its own bar)
            if (req.HasMultipleFiles || (req.AutoExtract && IsFileCompressed(req.GamePath)))
            {
                item.SetStatus(DownloadStatus.Extracting, "Extracting...");
                ExtractArchiveWithEntryProgress(req.GamePath, req.InstallDir, item, ct);

                try { File.Delete(req.GamePath); } catch { }
            }

            // Build rom list + signal installed
            var roms = req.BuildRoms != null ? req.BuildRoms() : null;

            req.OnInstalled?.Invoke(new GameInstalledEventArgs(new GameInstallationData
            {
                InstallDirectory = req.InstallDir,
                Roms = roms
            }));
        }

        private static bool IsFileCompressed(string filePath)
        {
            if (Path.GetExtension(filePath).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ArchiveFactory.IsArchive(filePath, out var type);
        }

        private void ExtractArchiveWithEntryProgress(string archivePath, string installDir, DownloadQueueItem item, CancellationToken ct)
        {
            using (var archive = ArchiveFactory.Open(archivePath))
            {
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                int total = entries.Count;
                int done = 0;

                item.SetProgress(0, Math.Max(1, total), false);

                foreach (var entry in entries)
                {
                    ct.ThrowIfCancellationRequested();

                    entry.WriteToDirectory(installDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });

                    done++;
                    item.SetProgress(done, total, false);
                    var pct = total > 0 ? (double)done / total * 100.0 : 100.0;
                    item.SetStatus(DownloadStatus.Extracting, "Extracting... " + pct.ToString("0") + "%");
                }
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

        private void TryCleanupPartialInstall(DownloadRequest req)
        {
            try
            {
                // delete partial downloaded file first
                SafeDeleteFileWithRetry(req.GamePath);

                // delete folder (recursively) if it exists
                SafeDeleteDirectoryWithRetry(req.InstallDir);
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Cleanup failed for {req.GameName} ({req.GameId}).");
                // don't rethrow - cancel should still succeed
            }
        }

        private static void SafeDeleteFileWithRetry(string path, int retries = 6, int delayMs = 150)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    if (!File.Exists(path))
                        return;

                    // clear attributes that can block deletion
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(delayMs);
                }
            }
        }

        private static void SafeDeleteDirectoryWithRetry(string dir, int retries = 6, int delayMs = 150)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;

            for (int i = 0; i < retries; i++)
            {
                try
                {
                    if (!Directory.Exists(dir))
                        return;

                    // make sure children are deletable
                    ClearAttributesRecursive(dir);

                    Directory.Delete(dir, recursive: true);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(delayMs);
                }
            }
        }

        private static void ClearAttributesRecursive(string dir)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
                {
                    try { new DirectoryInfo(d).Attributes = FileAttributes.Normal; } catch { }
                }
                try { new DirectoryInfo(dir).Attributes = FileAttributes.Normal; } catch { }
            }
            catch
            {
                // ignore - best effort
            }
        }
    }
}
