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
    /// Synchronises a game's local save with the RomM server using the API sync mode (see romm PRs
    /// #3137 / #3479): register a device once, POST /sync/negotiate to let the server decide
    /// upload / download / conflict / no_op per save, execute the returned operations, then POST
    /// the session complete. Conflicts are resolved most-recent-wins.
    ///
    /// Where a save lives, and whether it is one file or a packed directory, is the business of
    /// an <see cref="ISaveHandler"/>; this class only moves bytes. RetroArch is the handler that
    /// exists today.
    /// </summary>
    internal class SaveSyncService
    {
        private const string DeviceClient = "playnite";

        private readonly IRomM _romM;
        private readonly SaveHandlerRegistry _handlers;
        private readonly object _deviceLock = new object();

        public SaveSyncService(IRomM romM)
            : this(romM, new SaveHandlerRegistry())
        {
        }

        public SaveSyncService(IRomM romM, SaveHandlerRegistry handlers)
        {
            _romM = romM;
            _handlers = handlers;
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

                var target = ResolveTarget(game);
                if (target == null)
                {
                    outcome.Message = "Save sync does not know where this game's emulator keeps its saves.";
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

        private RomMSyncNegotiateResponse Negotiate(string deviceId, int romId, SaveTarget target)
        {
            var payload = new RomMSyncNegotiatePayload { DeviceId = deviceId };

            if (target.Exists)
            {
                payload.Saves.Add(new RomMClientSaveState
                {
                    RomId = romId,
                    FileName = target.FileName,
                    Slot = target.Slot,
                    Emulator = target.EmulatorTag,
                    ContentHash = target.ContentHash(),
                    UpdatedAt = target.UpdatedAtUtc,
                    FileSizeBytes = target.SizeBytes,
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

        private void ApplyOperation(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target, SyncOutcome outcome)
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
        private void ResolveConflict(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target, SyncOutcome outcome)
        {
            var localTime = target.Exists ? (DateTime?)target.UpdatedAtUtc : null;
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

        private bool Upload(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target)
        {
            if (!target.Exists)
            {
                Logger.Warn($"[SaveSync] Asked to upload rom {op.RomId} but there is no local save for it.");
                return false;
            }

            using (var prepared = target.PrepareUpload())
            using (var content = BuildSaveContent(prepared))
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
                        $"api/saves?rom_id={op.RomId}&emulator={target.EmulatorTag}" +
                        $"&slot={WebUtility.UrlEncode(target.Slot)}" +
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

        private bool Download(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target)
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

            // The handler decides what "apply" means -- overwrite a file, or unpack an archive over
            // the save directory. It also aligns the local timestamp with the server's so the next
            // negotiate sees the two sides as in sync rather than as a fresh local edit.
            try
            {
                target.ApplyDownload(bytes, op.ServerUpdatedAt);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Could not write the downloaded save for rom {op.RomId}.");
                return false;
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

        private static HttpContent BuildSaveContent(PreparedUpload upload)
        {
            var fileContent = new ByteArrayContent(File.ReadAllBytes(upload.FilePath));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var form = new MultipartFormDataContent();
            form.Add(fileContent, "saveFile", upload.FileName);
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

        #region Save location

        /// <summary>
        /// Finds the emulator Playnite launches this game with, hands it to whichever handler
        /// recognises it, and lets that handler locate the save. Null when the game has no
        /// emulator, no ROM path, or runs on an emulator no handler covers yet.
        /// </summary>
        private SaveTarget ResolveTarget(Game game)
        {
            var contentPath = game.Roms?.FirstOrDefault()?.Path;
            if (string.IsNullOrEmpty(contentPath))
                return null;

            var emulator = ResolveEmulator(game);
            if (emulator == null)
                return null;

            var handler = _handlers.Find(emulator);
            if (handler == null)
            {
                Logger.Info($"[SaveSync] No save handler for emulator '{emulator.Name}', skipping {game.Name}.");
                return null;
            }

            return handler.ResolveTarget(new SaveTargetRequest
            {
                Game = game,
                Emulator = emulator,
                Profile = ResolveProfile(game, emulator),
                ContentPath = _romM.Playnite.ExpandGameVariables(game, contentPath),
                Logger = Logger,
            });
        }

        private Emulator ResolveEmulator(Game game)
        {
            var action = EmulatorAction(game);

            if (action != null && action.EmulatorId != Guid.Empty)
                return _romM.Playnite.Database.Emulators?.FirstOrDefault(e => e.Id == action.EmulatorId);

            return null;
        }

        private static EmulatorProfile ResolveProfile(Game game, Emulator emulator)
        {
            var profileId = EmulatorAction(game)?.EmulatorProfileId;
            if (string.IsNullOrEmpty(profileId))
                return null;

            return emulator.SelectableProfiles?.FirstOrDefault(p => p.Id == profileId);
        }

        private static GameAction EmulatorAction(Game game)
        {
            return game.GameActions?.FirstOrDefault(a => a.IsPlayAction && a.Type == GameActionType.Emulator)
                   ?? game.GameActions?.FirstOrDefault(a => a.Type == GameActionType.Emulator);
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
