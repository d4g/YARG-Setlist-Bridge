using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace YargSetlistBridge
{
    /// <summary>
    /// Polls YARG's setlist on the main thread and publishes it, when it changes, to local
    /// clients such as YASS. Polling rather than patching is the point: it needs no hooks
    /// into YARG's methods, so a YARG update can only break it by renaming the public
    /// members <see cref="SetlistProbe"/> reads, and then it switches itself off.
    /// </summary>
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const int    ProtocolVersion   = 3;
        public const string DiscoveryFileName = "setlist-bridge.json";

        private ConfigEntry<int>   _port;
        private ConfigEntry<float> _pollInterval;
        private ConfigEntry<bool>  _toastAdds;
        private ConfigEntry<bool>  _showQr;

        /// <summary>Commands applied per frame at most, so a flood cannot stretch one frame.</summary>
        private const int CommandsPerFrame = 16;

        private BridgeServer    _server;
        private SetlistProbe    _probe;
        private SetlistSnapshot _last;
        private long            _version;
        private float           _nextPoll;
        private bool            _disabled;

        /// <summary>How often the screen is checked for where the QR code goes.</summary>
        private const float PlacementInterval = 0.1f;

        private QrOverlay   _qr;
        private ScreenProbe _screens;
        private bool        _qrBroken;
        private float       _nextPlacement;

        private string _token;
        private string _discoveryPath;
        private float  _nextDiscoveryAttempt;
        private bool   _discoveryWarned;

        private void Awake()
        {
            _port = Config.Bind("Server", "Port", 36110,
                "TCP port on 127.0.0.1. 0 picks a free port; clients find it in " + DiscoveryFileName + " either way.");
            _pollInterval = Config.Bind("Server", "PollIntervalSeconds", 0.25f,
                "How often the setlist is checked for changes.");
            _toastAdds = Config.Bind("Game", "ToastOnAdd", true,
                "Show a toast in YARG when a client (such as YASS) adds a song. Never shown during gameplay.");
            _showQr = Config.Bind("Game", "ShowQrCode", true,
                "Show the QR code a client (such as YASS) sends, on the main menu, in the music library, " +
                "and on the score and song-failed screens. Turn off when streaming, if the code carries a key.");

            _token = NewToken();

            try
            {
                var hello = "{\"type\":\"hello\",\"protocol\":" + ProtocolVersion +
                    ",\"plugin\":" + SetlistSnapshot.Quote(MyPluginInfo.PLUGIN_VERSION) + "}";
                _server = new BridgeServer(_port.Value, _token, hello, message => Logger.LogWarning(message));
                _server.Start();
                Logger.LogInfo($"Listening on 127.0.0.1:{_server.Port}");
            }
            catch (Exception ex)
            {
                Disable("could not start the server: " + ex.Message);
            }
        }

        private void Update()
        {
            if (_disabled) return;

            if (_discoveryPath == null && _server != null && Time.unscaledTime >= _nextDiscoveryAttempt)
            {
                try
                {
                    TryWriteDiscoveryFile();
                }
                catch (Exception ex)
                {
                    // Not fatal: the server still runs, clients just cannot find it yet.
                    if (!_discoveryWarned) Logger.LogWarning("Could not write " + DiscoveryFileName + ": " + ex.Message);
                    _discoveryWarned = true;
                    _nextDiscoveryAttempt = Time.unscaledTime + 5f;
                }
            }

            PlaceQrCode();

            try
            {
                // Created here rather than in Awake: its fields name YARG types, so a YARG
                // update can make constructing it throw, and only this catch handles that.
                _probe ??= new SetlistProbe();

                ApplyCommands();

                if (Time.unscaledTime < _nextPoll) return;
                _nextPoll = Time.unscaledTime + Math.Max(0.05f, _pollInterval.Value);
                PublishIfChanged();
            }
            catch (Exception ex)
            {
                // Almost always a YARG update having moved something SetlistProbe reads
                // (MissingFieldException, MissingMethodException, TypeLoadException).
                Disable($"reading YARG's setlist failed, so this YARG version is probably unsupported: {ex}");
            }
        }

        /// <summary>
        /// Puts the QR code where the screen on show wants it, or hides it.
        ///
        /// Its own try, apart from the setlist's: a YARG update that renames one of the
        /// screens <see cref="ScreenProbe"/> looks for switches the code off and leaves the
        /// setlist running. The code also goes when no client is connected, so a YASS that
        /// has quit never leaves an address on screen that nobody answers.
        /// </summary>
        private void PlaceQrCode()
        {
            if (_qr == null || _qrBroken || Time.unscaledTime < _nextPlacement) return;
            _nextPlacement = Time.unscaledTime + PlacementInterval;

            try
            {
                _screens ??= new ScreenProbe();
                var wanted = _showQr.Value && _server.ClientCount > 0 ? _screens.Current() : QrPlacement.Hidden;
                Rect? cover = null;
                if (wanted == QrPlacement.MusicLibrary && _screens.TryAlbumCover(out var rect)) cover = rect;
                _qr.Place(wanted, cover);
            }
            catch (Exception ex)
            {
                _qrBroken = true;
                _qr.Place(QrPlacement.Hidden);
                Logger.LogWarning($"Can't tell YARG's screens apart in this version, so the QR code is off: {ex}");
            }
        }

        private void ApplyQrCommand(BridgeCommand command)
        {
            var code = QrCode.Parse(command.Message, out var modules);
            if (code == null)
            {
                _qr ??= new QrOverlay();
                _qr.SetCode(modules);
            }
            _server.Reply(command, code);
        }

        private void PublishIfChanged()
        {
            var snapshot = _probe.Capture();
            if (snapshot.Equals(_last)) return;

            _last = snapshot;
            _version++;
            _server.Publish(snapshot.ToJson(_version));
        }

        /// <summary>
        /// Applies queued commands, each against the state the one before it left.
        ///
        /// A successful edit is published before the next command is looked at, so the
        /// version a client quotes (to say "move this, in the list as I saw it") is always
        /// compared with the list the command will actually change.
        /// </summary>
        private void ApplyCommands()
        {
            for (int i = 0; i < CommandsPerFrame && _server.TryTakeCommand(out var command); i++)
            {
                if (QrCode.IsCommand(command.Type))
                {
                    ApplyQrCommand(command);
                    continue;
                }

                var code = SetlistCommands.Parse(command.Message, out var args);

                if (code == null && args.Version != null && args.Version.Value != _version)
                {
                    code = SetlistCommands.Conflict;
                }

                if (code == null)
                {
                    try
                    {
                        code = _probe.Apply(args, _toastAdds.Value);
                    }
                    catch (Exception ex) when (!(ex is MissingMemberException || ex is TypeLoadException))
                    {
                        // A YARG UI hiccup around one edit is that edit's problem, not a
                        // reason to switch the whole plugin off. A missing member is.
                        Logger.LogWarning($"'{args.Type}' failed: {ex}");
                        code = SetlistCommands.Failed;
                    }

                    if (code == null) PublishIfChanged();
                }

                _server.Reply(command, code);
            }
        }

        /// <summary>
        /// Clients find the port and token next to YARG's own files, because that folder is
        /// the one thing a companion app like YASS already knows how to locate. YARG sets the
        /// path during its own startup, which may be after this plugin's Awake, hence retrying.
        /// </summary>
        private void TryWriteDiscoveryFile()
        {
            var dataDir = SetlistProbe.PersistentDataPath();
            if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir)) return;

            var path = Path.Combine(dataDir, DiscoveryFileName);
            var json = "{\"protocol\":" + ProtocolVersion +
                ",\"port\":" + _server.Port +
                ",\"token\":" + SetlistSnapshot.Quote(_token) +
                ",\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id +
                ",\"plugin\":" + SetlistSnapshot.Quote(MyPluginInfo.PLUGIN_VERSION) +
                ",\"yarg\":" + SetlistSnapshot.Quote(SetlistProbe.YargVersion() ?? "") + "}\n";

            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);

            _discoveryPath = path;
            Logger.LogInfo("Wrote " + path);
        }

        private void Disable(string reason)
        {
            _disabled = true;
            Logger.LogError("Disabled: " + reason);
            Shutdown();
        }

        private void OnApplicationQuit() => Shutdown();

        private void OnDestroy() => Shutdown();

        private void Shutdown()
        {
            _server?.Dispose();
            _server = null;

            _qr?.Destroy();
            _qr = null;

            // A stale file would point clients at a port nobody is listening on; the pid in it
            // lets them detect that anyway if YARG crashes before this runs.
            if (_discoveryPath != null)
            {
                try { File.Delete(_discoveryPath); } catch (Exception) { }
                _discoveryPath = null;
            }
        }

        private static string NewToken()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
