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
    // The transfer/extraction pipeline is shared by both queue front ends.
    internal class RomMDownloadInstaller
    {
        private readonly HttpClient httpClient;
        private ILogger Logger => LogManager.GetLogger();

        internal RomMDownloadInstaller(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        internal async Task DownloadAndInstall(DownloadQueueItem item, DownloadRequest req)
        {
            var ct = item.Cts.Token;
            ct.ThrowIfCancellationRequested();

            item.SetStatus(DownloadStatus.Downloading, "Downloading...");
            item.SetProgress(0, 1, true);

            var partialPath = req.GamePath + ".part";
            using (var response = await httpClient.GetAsync(req.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
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
                using (var fileStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
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
                    if (totalBytes.HasValue && downloaded != totalBytes.Value)
                        throw new IOException("The ROM download ended before all bytes were received.");
                }
            }

            ct.ThrowIfCancellationRequested();
            if (File.Exists(req.GamePath)) File.Delete(req.GamePath);
            File.Move(partialPath, req.GamePath);

            // Extract if needed (we treat extract as 0..100 in its own bar)
            // Don't extract archives when install flat is enabled
            if (req.DownloadAsArchive || (req.AutoExtract && IsFileCompressed(req.GamePath) && !req.InstallFlat))
            {
                item.SetStatus(DownloadStatus.Extracting, "Extracting...");
                Logger.Info($"Extracting {req.GamePath}...");

                if (req.Use7z && !string.IsNullOrEmpty(req.PathTo7Z) && req.PathTo7Z.EndsWith("7z.exe", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractArchiveWith7z(req.PathTo7Z, req.GamePath, req.InstallDir, item, ct);
                }
                else
                {
                    ExtractArchiveWithEntryProgress(req.GamePath, req.InstallDir, item, ct);
                }
                try { File.Delete(req.GamePath); } catch { }
            }

            ct.ThrowIfCancellationRequested();

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


        // 7-Zip extracts into the output dir and drops absolute paths and ".." components unless -spf
        // is passed (we never pass it), but the entry names are checked up front anyway so both
        // extraction paths refuse traversal the same way. Formats SharpCompress cannot open fall back
        // to 7-Zip's own handling rather than failing a download that used to work.
        private void EnsureEntriesContained(string archivePath, string installDir)
        {
            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        RomMInstallPaths.ResolveWithin(installDir, entry.Key);
                    }
                }
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not inspect {archivePath} before extraction: {ex.Message}");
            }
        }

        private void ExtractArchiveWith7z(string pathTo7z, string archivePath, string installDir, DownloadQueueItem item, CancellationToken ct)
        {
            if (archivePath == null || archivePath.Contains("../") || archivePath.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid archive path");
            }
            if (installDir == null || installDir.Contains("../") || installDir.Contains(@"..\"))
            {
                throw new ArgumentException("Invalid install directory path");
            }

            EnsureEntriesContained(archivePath, installDir);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = pathTo7z,
                Arguments = $"x \"{archivePath.Replace("\"", "\\\"")}\" -o\"{installDir.Replace("\"", "\\\"")}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            ct.ThrowIfCancellationRequested();
            using (Process process = Process.Start(startInfo))
            { 
                using (ct.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(); }
                    catch (InvalidOperationException) { }
                }))
                {
                    process.WaitForExit();
                }
                ct.ThrowIfCancellationRequested();
                if (process.ExitCode != 0)
                {
                    throw new Exception($"7z extraction failed for {archivePath} with exit code {process.ExitCode}.");
                }
            }
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

                    // Entry names come from the downloaded archive, so they are untrusted: handing a
                    // "../" or rooted key to ExtractFullPath would write outside installDir. Resolve
                    // and verify each destination, then copy the entry there ourselves.
                    var destination = RomMInstallPaths.ResolveWithin(installDir, entry.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));

                    using (var entryStream = entry.OpenEntryStream())
                    using (var file = File.Create(destination))
                    {
                        entryStream.CopyToAsync(file, 81920, ct).GetAwaiter().GetResult();
                    }

                    done++;
                    item.SetProgress(done, total, false);
                    var pct = total > 0 ? (double)done / total * 100.0 : 100.0;
                    item.SetStatus(DownloadStatus.Extracting, "Extracting... " + pct.ToString("0") + "%");
                }
            }
        }

        internal void TryCleanupPartialInstall(DownloadRequest req)
        {
            try
            {
                SafeDeleteFileWithRetry(req.GamePath + ".part");
                // delete partial downloaded file first
                SafeDeleteFileWithRetry(req.GamePath);

                // delete folder (recursively) if it exists
                if(!req.InstallFlat)
                    SafeDeleteDirectoryWithRetry(req.InstallDir);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Cleanup failed for {req.GameName} ({req.GameId}).");
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
