// Assets/Scripts/VoxelEngine/Networking/LanDiscovery.cs
//
// 14.48.0-dev - LAN discovery for the server browser.
//
// No master server, by locked decision: discovery is one UDP broadcast on
// the local network. Every hosting machine (listen or dedicated) runs the
// RESPONDER: a background thread parked on the discovery port that answers
// any probe with one pipe-separated line - server name, world, game port,
// players, cap. The menu runs the SCANNER: it broadcasts the probe, then
// collects replies into a thread-safe queue until its deadline; the UI
// drains the queue on its own schedule. Replies travel unicast back to the
// scanner's ephemeral socket, so a machine hosting and browsing at once
// hears itself through loopback and nothing loops forever.
//
// Threads are background and every socket is closed on stop, so neither
// a scene change nor an editor domain reload can leave a port behind.

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VoxelEngine.Networking
{
    public static class LanDiscovery
    {
        /// <summary>Discovery runs beside the game port, never on it.</summary>
        public const int DiscoveryPort = 47788;

        private const string ProbeToken = "IW_DISCOVER_V1";
        private const string ReplyPrefix = "IW_SERVER_V1|";

        public struct Found
        {
            public string serverName;
            public string worldName;
            public string address;
            public int port;
            public int players;
            public int maxPlayers;
        }

        // ───────────────────────── responder (hosting side) ─────────────────────────

        private static UdpClient _responder;
        private static Thread _responderThread;
        private static volatile string _payload = "";
        private static volatile bool _respond;

        public static bool ResponderRunning => _respond;

        /// <summary>Refresh what the responder answers with. Called from the
        /// main thread whenever the live numbers move; the thread only ever
        /// reads the finished line.</summary>
        public static void UpdateInfo(string serverName, string worldName, int port, int players, int maxPlayers)
        {
            _payload = ReplyPrefix + Clean(serverName) + "|" + Clean(worldName) + "|"
                     + port + "|" + players + "|" + maxPlayers;
        }

        private static string Clean(string s) => (s ?? "").Replace("|", "/");

        public static void StartResponder()
        {
            if (_respond) return;
            try
            {
                _responder = new UdpClient();
                _responder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _responder.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _respond = true;
                _responderThread = new Thread(ResponderLoop) { IsBackground = true, Name = "IW LAN responder" };
                _responderThread.Start();
                Debug.Log($"[LAN] responder listening on UDP {DiscoveryPort}.");
            }
            catch (Exception ex)
            {
                // Two hosts on one machine: the second simply is not
                // discoverable - direct connect still works.
                Debug.LogWarning("[LAN] responder could not start: " + ex.Message);
                _respond = false;
                try { _responder?.Close(); } catch { }
                _responder = null;
            }
        }

        public static void StopResponder()
        {
            _respond = false;
            try { _responder?.Close(); } catch { }
            _responder = null;
            _responderThread = null;
        }

        private static void ResponderLoop()
        {
            var udp = _responder;
            while (_respond && udp != null)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    if (Encoding.UTF8.GetString(data) != ProbeToken) continue;
                    string payload = _payload;
                    if (string.IsNullOrEmpty(payload)) continue;
                    byte[] reply = Encoding.UTF8.GetBytes(payload);
                    udp.Send(reply, reply.Length, from);
                }
                catch (ObjectDisposedException) { return; }   // StopResponder closed the socket
                catch (SocketException) { if (!_respond) return; }
                catch (Exception) { /* one bad packet must not kill the thread */ }
            }
        }

        // ───────────────────────── scanner (menu side) ─────────────────────────

        private static UdpClient _scanner;
        private static Thread _scanThread;
        private static volatile bool _scanning;
        private static readonly ConcurrentQueue<Found> _results = new();

        public static bool Scanning => _scanning;

        /// <summary>Broadcast one probe and collect replies until the
        /// deadline. Results surface through TryTake on the main thread.</summary>
        public static void StartScan(float seconds = 2.5f)
        {
            StopScan();
            while (_results.TryDequeue(out _)) { }
            try
            {
                _scanner = new UdpClient { EnableBroadcast = true };
                _scanner.Client.ReceiveTimeout = 300;   // short ticks so the deadline is honest
                byte[] probe = Encoding.UTF8.GetBytes(ProbeToken);
                _scanner.Send(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                // The same machine may be hosting: broadcast does not always
                // loop back, loopback always does.
                _scanner.Send(probe, probe.Length, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
                _scanning = true;
                long deadline = DateTime.UtcNow.AddSeconds(Mathf.Clamp(seconds, 1f, 10f)).Ticks;
                _scanThread = new Thread(() => ScanLoop(deadline)) { IsBackground = true, Name = "IW LAN scan" };
                _scanThread.Start();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LAN] scan could not start: " + ex.Message);
                _scanning = false;
                try { _scanner?.Close(); } catch { }
                _scanner = null;
            }
        }

        public static void StopScan()
        {
            _scanning = false;
            try { _scanner?.Close(); } catch { }
            _scanner = null;
            _scanThread = null;
        }

        private static void ScanLoop(long deadlineTicks)
        {
            var udp = _scanner;
            while (_scanning && udp != null && DateTime.UtcNow.Ticks < deadlineTicks)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    string text = Encoding.UTF8.GetString(data);
                    if (!text.StartsWith(ReplyPrefix, StringComparison.Ordinal)) continue;
                    var parts = text.Split('|');   // token|name|world|port|players|max
                    if (parts.Length < 6) continue;
                    int.TryParse(parts[3], out int port);
                    int.TryParse(parts[4], out int players);
                    int.TryParse(parts[5], out int max);
                    _results.Enqueue(new Found
                    {
                        serverName = parts[1],
                        worldName = parts[2],
                        address = from.Address.ToString(),
                        port = port,
                        players = players,
                        maxPlayers = max
                    });
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { /* receive timeout - check the deadline and listen again */ }
                catch (Exception) { }
            }
            _scanning = false;
            try { udp?.Close(); } catch { }
        }

        /// <summary>Main thread: drain one discovered server, if any.</summary>
        public static bool TryTake(out Found found) => _results.TryDequeue(out found);
    }
}
