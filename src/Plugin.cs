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
        public const int    ProtocolVersion   = 1;
        public const string DiscoveryFileName = "setlist-bridge.json";

        private ConfigEntry<int>   _port;
        private ConfigEntry<float> _pollInterval;

        private BridgeServer    _server;
        private SetlistProbe    _probe;
        private SetlistSnapshot _last;
        private long            _version;
        private float           _nextPoll;
        private bool            _disabled;

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
            if (_disabled || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + Math.Max(0.05f, _pollInterval.Value);

            if (_discoveryPath == null && Time.unscaledTime >= _nextDiscoveryAttempt)
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

            try
            {
                // Created here rather than in Awake: its fields name YARG types, so a YARG
                // update can make constructing it throw, and only this catch handles that.
                _probe ??= new SetlistProbe();

                var snapshot = _probe.Capture();
                if (snapshot.Equals(_last)) return;

                _last = snapshot;
                _version++;
                _server.Publish(snapshot.ToJson(_version));
            }
            catch (Exception ex)
            {
                // Almost always a YARG update having moved something SetlistProbe reads
                // (MissingFieldException, MissingMethodException, TypeLoadException).
                Disable($"reading YARG's setlist failed, so this YARG version is probably unsupported: {ex}");
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
