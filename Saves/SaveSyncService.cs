using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Models;
using RomM.Games;
using RomM.Models.RomM.Save;
using RomM.Saves.Handlers;
using RomM.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;

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

        // RomM's timestamps do not always carry a zone, and a naive "2024-05-17T09:30:00" would
        // otherwise deserialise as DateTimeKind.Unspecified and be read as local time -- shifting
        // every comparison against a file's real UTC write time by the machine's offset. Fixing the
        // kind where the value is born means no call site has to remember to.
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        };

        private readonly IRomM _romM;
        private readonly SaveHandlerRegistry _handlers = new SaveHandlerRegistry();
        private readonly object _deviceLock = new object();

        // One sync at a time per ROM: the post-play sync and the manual menu action can be in
        // flight together, and two negotiate sessions writing the same file would clobber each
        // other.
        private readonly ConcurrentDictionary<int, object> _romLocks = new ConcurrentDictionary<int, object>();

        public SaveSyncService(IRomM romM)
        {
            _romM = romM;
        }

        private ILogger Logger => _romM.Logger;
        private SettingsViewModel Settings => _romM.Settings;

        /// <summary>The emulators sync covers today, for the message shown when none applied.</summary>
        public string SupportedEmulators => string.Join(", ", _handlers.SupportedEmulatorTags);

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
        ///
        /// <paramref name="cancellationToken"/> bounds the whole cycle rather than any one request,
        /// which is what the launch path needs: an unreachable RomM would otherwise cost the
        /// HTTP client's default timeout per request before the game is allowed to start.
        /// </summary>
        public SyncOutcome Sync(Game game, CancellationToken cancellationToken = default(CancellationToken))
        {
            var outcome = new SyncOutcome();
            try
            {
                if (game == null || game.PluginId != _romM.Id || !Settings.EnableSaveSync)
                {
                    return outcome;
                }

                if (!RomMGameId.TryParse(game.GameId, out int romId, out string _))
                {
                    Logger.Warn($"[SaveSync] {game?.Name} has a malformed GameId, skipping.");
                    return outcome;
                }

                lock (_romLocks.GetOrAdd(romId, _ => new object()))
                {
                    var target = ResolveTarget(game);
                    if (target == null)
                    {
                        outcome.Message = "Save sync does not know where this game's emulator keeps its saves.";
                        return outcome;
                    }

                    outcome.Applicable = true;

                    var deviceId = EnsureDeviceRegistered(cancellationToken);
                    if (string.IsNullOrEmpty(deviceId))
                    {
                        outcome.Message = "Could not register this device with RomM (check token scopes).";
                        outcome.Failed++;
                        return outcome;
                    }

                    var negotiation = Negotiate(deviceId, romId, target, cancellationToken);
                    if (negotiation == null)
                    {
                        outcome.Message = "Save sync negotiation with RomM failed.";
                        outcome.Failed++;
                        return outcome;
                    }

                    // Negotiate may surface operations for saves we didn't report (e.g. created on another
                    // device). We only resolved a local path for THIS game, so apply only its operations;
                    // other ROMs are handled when their own games sync.
                    var operations = negotiation.Operations ?? new List<RomMSyncOperation>();
                    foreach (var op in operations.Where(o => o.RomId == romId))
                    {
                        ApplyOperation(op, deviceId, negotiation.SessionId, target, outcome, cancellationToken);
                    }

                    CompleteSession(negotiation.SessionId,
                        outcome.Uploaded + outcome.Downloaded,
                        outcome.Failed,
                        cancellationToken);

                    Logger.Info($"[SaveSync] {game.Name}: {outcome.Uploaded} uploaded, {outcome.Downloaded} downloaded, " +
                                $"{outcome.Conflicts} conflicts, {outcome.Failed} failed.");
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Warn($"[SaveSync] Sync of {game?.Name} hit its deadline and was abandoned.");
                outcome.Failed++;
                outcome.Message = "RomM did not answer in time.";
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

        private RomMSyncNegotiateResponse Negotiate(string deviceId, int romId, SaveTarget target, CancellationToken ct)
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
            var body = PostJson(url, payload, ct);
            return body == null ? null : JsonConvert.DeserializeObject<RomMSyncNegotiateResponse>(body, JsonSettings);
        }

        private void CompleteSession(int sessionId, int completed, int failed, CancellationToken ct)
        {
            try
            {
                var url = RomMUrl.Combine(Settings.RomMHost, $"api/sync/sessions/{sessionId}/complete");
                PostJson(url, new RomMSyncCompletePayload
                {
                    OperationsCompleted = completed,
                    OperationsFailed = failed,
                }, ct);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Failed to complete sync session {sessionId}.");
            }
        }

        #endregion

        #region Operation handling

        private void ApplyOperation(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target, SyncOutcome outcome, CancellationToken ct)
        {
            try
            {
                // A conflict is not a third kind of transfer: it resolves to one of the other two,
                // so it decides and then falls into the same dispatch.
                var action = op.Action;
                if (action == RomMSyncAction.Conflict)
                {
                    outcome.Conflicts++;
                    action = ResolveConflict(op, target);
                }

                switch (action)
                {
                    case RomMSyncAction.Upload:
                        if (Upload(op, deviceId, sessionId, target, ct))
                            outcome.Uploaded++;
                        else
                            outcome.Failed++;
                        break;

                    case RomMSyncAction.Download:
                        if (Download(op, deviceId, sessionId, target, ct))
                            outcome.Downloaded++;
                        else
                            outcome.Failed++;
                        break;

                    case RomMSyncAction.NoOp:
                    default:
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // The deadline is the whole cycle's, not this operation's: let it end the sync
                // rather than being counted as one more failed transfer.
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[SaveSync] Operation '{op.Action}' failed for save {op.SaveId} (rom {op.RomId}).");
                outcome.Failed++;
            }
        }

        /// <summary>
        /// Most-recent-wins: whichever side was modified later overwrites the other. Returns the
        /// transfer that decision comes down to.
        /// </summary>
        private string ResolveConflict(RomMSyncOperation op, SaveTarget target)
        {
            var localTime = target.Exists ? (DateTime?)target.UpdatedAtUtc : null;
            var serverTime = op.ServerUpdatedAt;

            bool serverWins = serverTime.HasValue && (!localTime.HasValue || serverTime.Value > localTime.Value);

            Logger.Warn($"[SaveSync] Conflict for rom {op.RomId} ({op.Reason}); " +
                        $"resolving most-recent-wins -> {(serverWins ? "download" : "upload")}.");

            return serverWins ? RomMSyncAction.Download : RomMSyncAction.Upload;
        }

        private bool Upload(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target, CancellationToken ct)
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
                    response = HttpClientSingleton.Instance.PutAsync(url, content, ct).GetAwaiter().GetResult();
                }
                else
                {
                    var url = RomMUrl.Combine(Settings.RomMHost,
                        $"api/saves?rom_id={op.RomId}&emulator={WebUtility.UrlEncode(target.EmulatorTag)}" +
                        $"&slot={WebUtility.UrlEncode(target.Slot)}" +
                        $"&device_id={WebUtility.UrlEncode(deviceId)}&session_id={sessionId}");
                    response = HttpClientSingleton.Instance.PostAsync(url, content, ct).GetAwaiter().GetResult();
                }

                using (response)
                {
                    response.EnsureSuccessStatusCode();
                }
            }

            return true;
        }

        private bool Download(RomMSyncOperation op, string deviceId, int sessionId, SaveTarget target, CancellationToken ct)
        {
            if (!op.SaveId.HasValue)
            {
                Logger.Warn($"[SaveSync] Download requested for rom {op.RomId} without a save id.");
                return false;
            }

            var url = RomMUrl.Combine(Settings.RomMHost,
                $"api/saves/{op.SaveId.Value}/content?device_id={WebUtility.UrlEncode(deviceId)}&session_id={sessionId}");

            byte[] bytes;
            using (var response = HttpClientSingleton.Instance.GetAsync(url, ct).GetAwaiter().GetResult())
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

            ConfirmDownloaded(op.SaveId.Value, deviceId, ct);
            return true;
        }

        private void ConfirmDownloaded(int saveId, string deviceId, CancellationToken ct)
        {
            try
            {
                var url = RomMUrl.Combine(Settings.RomMHost, $"api/saves/{saveId}/downloaded");
                PostJson(url, new { device_id = deviceId }, ct);
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

        /// <summary>
        /// Registers this machine as a RomM device once per server, persisting the returned id in
        /// settings. A device id is only meaningful on the host that issued it, so pointing the
        /// plugin at a different RomM re-registers rather than sending an unknown id forever.
        /// </summary>
        private string EnsureDeviceRegistered(CancellationToken ct)
        {
            var host = Settings.RomMHost ?? string.Empty;

            if (HasDeviceFor(host))
                return Settings.SaveSyncDeviceId;

            lock (_deviceLock)
            {
                if (HasDeviceFor(host))
                    return Settings.SaveSyncDeviceId;

                try
                {
                    // SyncMode and AllowExisting keep the model's defaults; what they are and why
                    // is documented there.
                    var payload = new RomMDeviceCreate
                    {
                        Name = Environment.MachineName,
                        Platform = "Windows",
                        Client = DeviceClient,
                        ClientVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                    };

                    var url = RomMUrl.Combine(host, "api/devices");
                    var body = PostJson(url, payload, ct);
                    if (body == null)
                        return null;

                    var created = JsonConvert.DeserializeObject<RomMDeviceCreateResponse>(body, JsonSettings);
                    if (created == null || string.IsNullOrEmpty(created.DeviceId))
                        return null;

                    Settings.SaveSyncDeviceId = created.DeviceId;
                    Settings.SaveSyncDeviceHost = host;
                    Settings.Persist();
                    Logger.Info($"[SaveSync] Registered device '{created.DeviceId}' with RomM.");
                    return created.DeviceId;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[SaveSync] Device registration failed.");
                    return null;
                }
            }
        }

        private bool HasDeviceFor(string host)
        {
            return !string.IsNullOrEmpty(Settings.SaveSyncDeviceId)
                   && string.Equals(Settings.SaveSyncDeviceHost, host, StringComparison.OrdinalIgnoreCase);
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

            var action = EmulatorAction(game);
            var emulator = ResolveEmulator(action);
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
                EmulatorInstallDir = PlaynitePath.Resolve(_romM.Playnite, emulator.InstallDir),
                Profile = ResolveProfile(action, emulator),
                ContentPath = _romM.Playnite.ExpandGameVariables(game, contentPath),
                Logger = Logger,
            });
        }

        /// <summary>
        /// The emulator comes from the action Playnite actually launches with, not from the
        /// <see cref="EmulatorMapping"/> the game was imported under: a user who repoints the play
        /// action at another emulator should have their saves follow it.
        /// </summary>
        private Emulator ResolveEmulator(GameAction action)
        {
            if (action != null && action.EmulatorId != Guid.Empty)
                return _romM.Playnite.Database.Emulators?.FirstOrDefault(e => e.Id == action.EmulatorId);

            return null;
        }

        private static EmulatorProfile ResolveProfile(GameAction action, Emulator emulator)
        {
            var profileId = action?.EmulatorProfileId;
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

        private string PostJson(string url, object payload, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(payload);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = HttpClientSingleton.Instance.PostAsync(url, content, ct).GetAwaiter().GetResult())
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
