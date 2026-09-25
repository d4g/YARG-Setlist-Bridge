using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace YargSetlistBridge
{
    /// <summary>
    /// Newline-delimited JSON over TCP on 127.0.0.1. See PROTOCOL.md.
    ///
    /// Deliberately knows nothing about Unity or YARG, so it can be exercised outside the
    /// game. The main thread only ever calls <see cref="Publish"/>, which stores the latest
    /// message and returns; a sender thread does the socket writes. That way a stuck or
    /// slow client can never stall a frame, and a burst of changes coalesces into the
    /// newest state instead of queueing up stale ones.
    /// </summary>
    internal sealed class BridgeServer : IDisposable
    {
        private const int MaxLineBytes     = 4096;
        private const int AuthTimeoutMs    = 5000;
        private const int WriteTimeoutMs   = 2000;

        private readonly int            _port;
        private readonly string         _token;
        private readonly string         _helloJson;
        private readonly Action<string> _log;

        private readonly object       _clientsLock = new object();
        private readonly List<Client> _clients     = new List<Client>();

        private readonly AutoResetEvent          _publishSignal = new AutoResetEvent(false);
        private readonly CancellationTokenSource _cts           = new CancellationTokenSource();

        private TcpListener     _listener;
        private Thread          _acceptThread;
        private Thread          _sendThread;
        private volatile string _latest;

        public BridgeServer(int port, string token, string helloJson, Action<string> log)
        {
            _port      = port;
            _token     = token;
            _helloJson = helloJson;
            _log       = log;
        }

        /// <summary>The port actually bound; differs from the requested one only when that was 0.</summary>
        public int Port { get; private set; }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            Port = ((IPEndPoint) _listener.LocalEndpoint).Port;

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "SetlistBridge accept" };
            _sendThread   = new Thread(SendLoop)   { IsBackground = true, Name = "SetlistBridge send" };
            _acceptThread.Start();
            _sendThread.Start();
        }

        /// <summary>
        /// Replaces the state every client should have. Cheap and non-blocking; safe to call
        /// from Unity's main thread.
        /// </summary>
        public void Publish(string json)
        {
            _latest = json;
            _publishSignal.Set();
        }

        private void AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = _listener.AcceptTcpClient();
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _log("accept failed: " + ex.Message);
                    continue;
                }

                // Each connection authenticates on its own short-lived thread so a client that
                // connects and says nothing cannot hold up the next one.
                var thread = new Thread(() => Handshake(tcp)) { IsBackground = true, Name = "SetlistBridge client" };
                thread.Start();
            }
        }

        private void Handshake(TcpClient tcp)
        {
            var client = new Client(tcp);
            try
            {
                tcp.NoDelay = true;
                tcp.ReceiveTimeout = AuthTimeoutMs;
                tcp.SendTimeout = WriteTimeoutMs;

                var line = ReadLine(client.Stream);
                if (line == null || !IsValidAuth(line))
                {
                    client.TryWrite("{\"type\":\"error\",\"code\":\"unauthorized\"}");
                    client.Dispose();
                    return;
                }

                client.TryWrite(_helloJson);
                var latest = _latest;
                if (latest != null && !client.TryWrite(latest))
                {
                    client.Dispose();
                    return;
                }

                lock (_clientsLock)
                {
                    _clients.Add(client);
                }

                // Protocol v1 is read-only: nothing the client sends after auth is acted on.
                // Keep reading anyway, so a closed connection is noticed and dropped promptly.
                tcp.ReceiveTimeout = 0;
                while (!_cts.IsCancellationRequested)
                {
                    var message = ReadLine(client.Stream);
                    if (message == null) break;
                    client.TryWrite("{\"type\":\"error\",\"code\":\"unsupported\"}");
                }
            }
            catch (Exception)
            {
                // Timeouts, resets and oversize lines all end the same way: drop the client.
            }

            Remove(client);
        }

        private bool IsValidAuth(string line)
        {
            try
            {
                var message = JObject.Parse(line);
                return (string) message["type"] == "auth" && FixedTimeEquals((string) message["token"], _token);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void SendLoop()
        {
            var handles = new WaitHandle[] { _publishSignal, _cts.Token.WaitHandle };
            string sent = null;

            while (true)
            {
                WaitHandle.WaitAny(handles);
                if (_cts.IsCancellationRequested) return;

                var latest = _latest;
                if (latest == null || ReferenceEquals(latest, sent)) continue;
                sent = latest;

                Client[] snapshot;
                lock (_clientsLock)
                {
                    snapshot = _clients.ToArray();
                }

                foreach (var client in snapshot)
                {
                    if (!client.TryWrite(latest))
                    {
                        Remove(client);
                    }
                }
            }
        }

        private void Remove(Client client)
        {
            lock (_clientsLock)
            {
                _clients.Remove(client);
            }
            client.Dispose();
        }

        /// <summary>Reads one '\n'-terminated UTF-8 line, or null at end of stream.</summary>
        private static string ReadLine(Stream stream)
        {
            var buffer = new MemoryStream();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                if (b == '\n') break;
                if (buffer.Length >= MaxLineBytes) throw new IOException("line too long");
                buffer.WriteByte((byte) b);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int) buffer.Length).TrimEnd('\r');
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener?.Stop(); } catch (Exception) { }

            lock (_clientsLock)
            {
                foreach (var client in _clients) client.Dispose();
                _clients.Clear();
            }

            _sendThread?.Join(1000);
            _acceptThread?.Join(1000);
        }

        private sealed class Client : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly object    _writeLock = new object();

            public Client(TcpClient tcp)
            {
                _tcp = tcp;
                Stream = tcp.GetStream();
            }

            public NetworkStream Stream { get; }

            public bool TryWrite(string json)
            {
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                lock (_writeLock)
                {
                    try
                    {
                        Stream.Write(bytes, 0, bytes.Length);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }

            public void Dispose()
            {
                try { _tcp.Close(); } catch (Exception) { }
            }
        }
    }
}
