using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Games;
using RomM.Models.RomM.Save;
using RomM.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

namespace RomM.Saves
{
    /// <summary>
    /// Synchronises a game's local RetroArch battery save (.srm) with the RomM server using the
    /// API sync mode (see romm PRs #3137 / #3479): register a device once, POST /sync/negotiate to
    /// let the server decide upload / download / conflict / no_op per save, execute the returned
    /// operations, then POST the session complete. Conflicts are resolved most-recent-wins.
    ///
    /// Currently scoped to RetroArch, whose SRAM location is derived from retroarch.cfg.
    /// </summary>
    internal class SaveSyncService
    {
        private const string Emulator = "retroarch";
        private const string DeviceClient = "playnite";

        private readonly IRomM _romM;
        private readonly object _deviceLock = new object();

        public SaveSyncService(IRomM romM)
        {
            _romM = romM;
        }

        private ILogger Logger => _romM.Logger;
        private SettingsViewModel Settings => _romM.Settings;

        public class SyncOutcome
        {
            public bool Applicable { get; set; }
            public int Uploaded { get; set; }
            public int Downloaded { get; set; }
            public int Conflicts { get; set; }
            public int Failed { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Runs a full negotiate + apply cycle for a single game. Safe to call off the UI thread.
        /// Never throws; failures are logged and reflected in the returned <see cref="SyncOutcome"/>.
        /// </summary>
        public SyncOutcome Sync(Game game)
        {
            var outcome = new SyncOutcome();
            try
            {
                if (game == null || game.PluginId != _romM.Id)
                {
                    return outcome;
                }

                if (!RomMGameId.TryParse(game.GameId, out int romId, out string _))
                {
                    Logger.Warn($"[SaveSync] {game?.Name} has a malformed GameId, skipping.");
                    return outcome;
                }

                var target = ResolveRetroArchTarget(game);
                if (target == null)
                {
                    outcome.Message = "Save sync currently only supports RetroArch games.";
                    return outcome;
                }

                outcome.Applicable = true;

                var deviceId = EnsureDeviceRegistered();
                if (string.IsNullOrEmpty(deviceId))
                {
                    outcome.Message = "Could not register this device with RomM (check token scopes).";
                    outcome.Failed++;
                    return outcome;
                }

                var negotiation = Negotiate(deviceId, romId, target);
                if (negotiation == null)
                {
                    outcome.Message = "Save sync negotiation with RomM failed.";
                    outcome.Failed++;
                    return outcome;
                }

                // Negotiate may surface operations for saves we didn't report (e.g. created on another
                // device). We only resolved a local path for THIS game, so apply only its operations;
                // other ROMs are handled when their own games sync.
                foreach (var op in negotiation.Operations.Where(o => o.RomId == romId))
                {
                    ApplyOperation(op, deviceId, negotiation.SessionId, target, outcome);
                }

                CompleteSession(negotiation.SessionId,
                    outcome.Uploaded + outcome.Downloaded,
                    outcome.Failed);

                Logger.Info($"[SaveSync] {game.Name}: {outcome.Uploaded} uploaded, {outcome.Downloaded} downloaded, " +
                            $"{outcome.Conflicts} conflicts, {outcome.Failed} failed.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Unexpected failure syncing {game?.Name}.");
                outcome.Failed++;
                outcome.Message = ex.Message;
            }

            return outcome;
        }

        #region Negotiate / session

        private RomMSyncNegotiateResponse Negotiate(string deviceId, int romId, RetroArchTarget target)
        {
            var payload = new RomMSyncNegotiatePayload { DeviceId = deviceId };

            if (File.Exists(target.LocalSavePath))
            {
                var info = new FileInfo(target.LocalSavePath);
                payload.Saves.Add(new RomMClientSaveState
                {
                    RomId = romId,
                    FileName = Path.GetFileName(target.LocalSavePath),
                    Slot = null,
                    Emulator = Emulator,
                    ContentHash = SaveFileHash.Md5HexFile(target.LocalSavePath),
                    UpdatedAt = info.LastWriteTimeUtc,
                    FileSizeBytes = info.Length,
                });
            }

            var url = RomMUrl.Combine(Settings.RomMHost, "api/sync/negotiate");
            var body = PostJson(url, payload);
            return body == null ? null : JsonConvert.DeserializeObject<RomMSyncNegotiateResponse>(body);
        }

        private void CompleteSession(int sessionId, int completed, int failed)
        {
            try
            {
                var url = RomMUrl.Combine(Settings.RomMHost, $"api/sync/sessions/{sessionId}/complete");
                PostJson(url, new RomMSyncCompletePayload
                {
                    OperationsCompleted = completed,
                    OperationsFailed = failed,
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Failed to complete sync session {sessionId}.");
            }
        }

        #endregion

        #region Operation handling

        private void ApplyOperation(RomMSyncOperation op, string deviceId, int sessionId, RetroArchTarget target, SyncOutcome outcome)
        {
            try
            {
                switch (op.Action)
                {
                    case RomMSyncAction.Upload:
                        if (Upload(op, deviceId, sessionId, target))
                            outcome.Uploaded++;
                        else
                            outcome.Failed++;
                        break;

                    case RomMSyncAction.Download:
                        if (Download(op, deviceId, sessionId, target))
                            outcome.Downloaded++;
                        else
                            outcome.Failed++;
                        break;

                    case RomMSyncAction.Conflict:
                        outcome.Conflicts++;
                        ResolveConflict(op, deviceId, sessionId, target, outcome);
                        break;

                    case RomMSyncAction.NoOp:
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Operation '{op.Action}' failed for save {op.SaveId} (rom {op.RomId}).");
                outcome.Failed++;
            }
        }

        /// <summary>Most-recent-wins: whichever side was modified later overwrites the other.</summary>
        private void ResolveConflict(RomMSyncOperation op, string deviceId, int sessionId, RetroArchTarget target, SyncOutcome outcome)
        {
            var localTime = File.Exists(target.LocalSavePath)
                ? (DateTime?)new FileInfo(target.LocalSavePath).LastWriteTimeUtc
                : null;
            var serverTime = op.ServerUpdatedAt?.ToUniversalTime();

            bool serverWins = serverTime.HasValue && (!localTime.HasValue || serverTime.Value > localTime.Value);

            Logger.Warn($"[SaveSync] Conflict for rom {op.RomId} ({op.Reason}); " +
                        $"resolving most-recent-wins -> {(serverWins ? "download" : "upload")}.");

            if (serverWins)
            {
                if (Download(op, deviceId, sessionId, target)) outcome.Downloaded++; else outcome.Failed++;
            }
            else
            {
                if (Upload(op, deviceId, sessionId, target)) outcome.Uploaded++; else outcome.Failed++;
            }
        }

        private bool Upload(RomMSyncOperation op, string deviceId, int sessionId, RetroArchTarget target)
        {
            if (!File.Exists(target.LocalSavePath))
            {
                Logger.Warn($"[SaveSync] Asked to upload rom {op.RomId} but no local save at {target.LocalSavePath}.");
                return false;
            }

            using (var content = BuildSaveContent(target.LocalSavePath))
            {
                HttpResponseMessage response;
                if (op.SaveId.HasValue)
                {
                    var url = RomMUrl.Combine(Settings.RomMHost,
                        $"api/saves/{op.SaveId.Value}?device_id={WebUtility.UrlEncode(deviceId)}");
                    response = HttpClientSingleton.Instance.PutAsync(url, content).GetAwaiter().GetResult();
                }
                else
                {
                    var url = RomMUrl.Combine(Settings.RomMHost,
                        $"api/saves?rom_id={op.RomId}&emulator={Emulator}" +
                        $"&device_id={WebUtility.UrlEncode(deviceId)}&session_id={sessionId}");
                    response = HttpClientSingleton.Instance.PostAsync(url, content).GetAwaiter().GetResult();
                }

                using (response)
                {
                    response.EnsureSuccessStatusCode();
                }
            }

            return true;
        }

        private bool Download(RomMSyncOperation op, string deviceId, int sessionId, RetroArchTarget target)
        {
            if (!op.SaveId.HasValue)
            {
                Logger.Warn($"[SaveSync] Download requested for rom {op.RomId} without a save id.");
                return false;
            }

            var url = RomMUrl.Combine(Settings.RomMHost,
                $"api/saves/{op.SaveId.Value}/content?device_id={WebUtility.UrlEncode(deviceId)}&session_id={sessionId}");

            byte[] bytes;
            using (var response = HttpClientSingleton.Instance.GetAsync(url).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }

            // Overwrite the file RetroArch already uses when we found one, otherwise write to the
            // path RetroArch would create for this content.
            var destination = target.ExistingSavePath ?? target.LocalSavePath;
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(destination, bytes);

            // Align the local timestamp with the server so the next negotiate sees them as in-sync.
            if (op.ServerUpdatedAt.HasValue)
            {
                try { File.SetLastWriteTimeUtc(destination, op.ServerUpdatedAt.Value.ToUniversalTime()); }
                catch (Exception ex) { Logger.Warn($"[SaveSync] Could not set save timestamp: {ex.Message}"); }
            }

            ConfirmDownloaded(op.SaveId.Value, deviceId);
            return true;
        }

        private void ConfirmDownloaded(int saveId, string deviceId)
        {
            try
            {
                var url = RomMUrl.Combine(Settings.RomMHost, $"api/saves/{saveId}/downloaded");
                PostJson(url, new { device_id = deviceId });
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SaveSync] Could not confirm download of save {saveId}: {ex.Message}");
            }
        }

        private static HttpContent BuildSaveContent(string filePath)
        {
            var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var form = new MultipartFormDataContent();
            form.Add(fileContent, "saveFile", Path.GetFileName(filePath));
            return form;
        }

        #endregion

        #region Device registration

        /// <summary>Registers this machine as a RomM device once, persisting the returned id in settings.</summary>
        private string EnsureDeviceRegistered()
        {
            if (!string.IsNullOrEmpty(Settings.SaveSyncDeviceId))
                return Settings.SaveSyncDeviceId;

            lock (_deviceLock)
            {
                if (!string.IsNullOrEmpty(Settings.SaveSyncDeviceId))
                    return Settings.SaveSyncDeviceId;

                try
                {
                    var payload = new RomMDeviceCreate
                    {
                        Name = Environment.MachineName,
                        Platform = "Windows",
                        Client = DeviceClient,
                        ClientVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                        SyncMode = "api",
                        AllowExisting = true,
                    };

                    var url = RomMUrl.Combine(Settings.RomMHost, "api/devices");
                    var body = PostJson(url, payload);
                    if (body == null)
                        return null;

                    var created = JsonConvert.DeserializeObject<RomMDeviceCreateResponse>(body);
                    if (created == null || string.IsNullOrEmpty(created.DeviceId))
                        return null;

                    Settings.SaveSyncDeviceId = created.DeviceId;
                    Settings.Persist();
                    Logger.Info($"[SaveSync] Registered device '{created.DeviceId}' with RomM.");
                    return created.DeviceId;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[SaveSync] Device registration failed.");
                    return null;
                }
            }
        }

        #endregion

        #region RetroArch resolution

        /// <summary>Where a game's RetroArch save lives locally, plus any already-existing file.</summary>
        private class RetroArchTarget
        {
            public string LocalSavePath { get; set; }
            public string ExistingSavePath { get; set; }
        }

        private RetroArchTarget ResolveRetroArchTarget(Game game)
        {
            try
            {
                var contentPath = game.Roms?.FirstOrDefault()?.Path;
                if (string.IsNullOrEmpty(contentPath))
                    return null;

                contentPath = _romM.Playnite.ExpandGameVariables(game, contentPath);

                var emulator = ResolveEmulator(game);
                if (emulator == null || !IsRetroArch(emulator))
                    return null;

                var cfgPath = FindRetroArchConfig(emulator);
                var cfg = cfgPath != null
                    ? RetroArchConfig.Parse(File.ReadAllText(cfgPath))
                    : new Dictionary<string, string>();

                var baseDir = emulator.InstallDir;
                var localSavePath = RetroArchConfig.ResolveSaveFilePath(cfg, contentPath, null, baseDir);
                if (string.IsNullOrEmpty(localSavePath))
                    return null;

                var existing = File.Exists(localSavePath)
                    ? localSavePath
                    : FindExistingSave(RetroArchConfig.ResolveSaveBaseDirectory(cfg, contentPath, baseDir),
                                       Path.GetFileNameWithoutExtension(contentPath));

                return new RetroArchTarget
                {
                    LocalSavePath = existing ?? localSavePath,
                    ExistingSavePath = existing,
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Failed to resolve RetroArch save path for {game?.Name}.");
                return null;
            }
        }

        private Emulator ResolveEmulator(Game game)
        {
            var action = game.GameActions?.FirstOrDefault(a => a.IsPlayAction && a.Type == GameActionType.Emulator)
                         ?? game.GameActions?.FirstOrDefault(a => a.Type == GameActionType.Emulator);

            if (action != null && action.EmulatorId != Guid.Empty)
                return _romM.Playnite.Database.Emulators?.FirstOrDefault(e => e.Id == action.EmulatorId);

            return null;
        }

        private static bool IsRetroArch(Emulator emulator)
        {
            if (string.Equals(emulator.BuiltInConfigId, "retroarch", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrEmpty(emulator.Name) &&
                emulator.Name.IndexOf("retroarch", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrEmpty(emulator.InstallDir) &&
                File.Exists(Path.Combine(emulator.InstallDir, "retroarch.exe")))
                return true;

            return false;
        }

        private static string FindRetroArchConfig(Emulator emulator)
        {
            if (!string.IsNullOrEmpty(emulator.InstallDir))
            {
                var inInstall = Path.Combine(emulator.InstallDir, "retroarch.cfg");
                if (File.Exists(inInstall))
                    return inInstall;
            }

            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RetroArch", "retroarch.cfg");
            return File.Exists(appData) ? appData : null;
        }

        private static string FindExistingSave(string baseDir, string contentName)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir) || string.IsNullOrEmpty(contentName))
                return null;

            try
            {
                return Directory
                    .EnumerateFiles(baseDir, contentName + RetroArchConfig.SaveExtension, SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region HTTP helper

        private string PostJson(string url, object payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = HttpClientSingleton.Instance.PostAsync(url, content).GetAwaiter().GetResult())
            {
                if (!response.IsSuccessStatusCode)
                {
                    var error = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Logger.Error($"[SaveSync] POST {url} -> {(int)response.StatusCode}: {error}");
                    return null;
                }

                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
        }

        #endregion
    }
}
