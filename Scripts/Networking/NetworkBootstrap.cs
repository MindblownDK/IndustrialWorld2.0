// Assets/Scripts/VoxelEngine/Networking/NetworkBootstrap.cs
//
// 14.1.0-dev - Multiplayer Foundation, part 2: the Fish-Net bridge.
//
// The ONLY class in the game that talks to the transport. It starts and stops
// the listen server, walks every connection through the identity handshake,
// spawns one PlayerAvatar per connected player and keeps NetworkSession in
// sync with reality. Gameplay code keeps asking NetworkSession - it never
// touches Fish-Net directly (README section 4).
//
// Handshake: when the local client finishes authenticating it broadcasts its
// stable PlayerIdentity (id + name) to the server. Only then does the server
// spawn that player's avatar, with the identity baked into the spawn payload.
// State is keyed by player id everywhere; connection ids are a transport
// detail that never leaves this file.
//
// Built and verified against Fish-Net 4.7.3.

using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace VoxelEngine.Networking
{
    /// <summary>Client -> server: "this is who I am". Sent once, right after
    /// authentication, before the server spawns the player's avatar.</summary>
    public struct IdentityBroadcast : IBroadcast
    {
        public string PlayerId;
        public string PlayerName;
    }

    [RequireComponent(typeof(NetworkManager))]
    public class NetworkBootstrap : MonoBehaviour
    {
        public static NetworkBootstrap Instance { get; private set; }

        [Tooltip("Avatar prefab spawned for every connected player. Needs NetworkObject + PlayerAvatar. Authored by Setup Step 105.")]
        public NetworkObject avatarPrefab;

        private NetworkManager _networkManager;
        private bool _serverStarted;
        private bool _clientStarted;

        /// <summary>Server-side: one avatar per connection, so a chatty client
        /// can never spawn twice.</summary>
        private readonly Dictionary<int, NetworkObject> _avatarsByConnection = new();

        /// <summary>Server-side: the player id each connection was admitted
        /// under - the duplicate-identity guard reads this.</summary>
        private readonly Dictionary<int, string> _playerIdByConnection = new();

        public bool IsOnline => _serverStarted || _clientStarted;

        /// <summary>One human-readable line for the multiplayer menu.</summary>
        public string StatusLine { get; private set; } = "Offline";

        // ─────────────────────────── lifecycle ───────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _networkManager = GetComponent<NetworkManager>();
        }

        /// <summary>Wired in Start, not Awake: NetworkManager creates its
        /// sub-managers in its own Awake and same-object Awake order is not
        /// guaranteed. Nothing can connect before the UI acts anyway.</summary>
        private void Start()
        {
            _networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _networkManager.ClientManager.OnAuthenticated += OnLocalClientAuthenticated;
            _networkManager.ServerManager.RegisterBroadcast<IdentityBroadcast>(OnIdentityReceived);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_networkManager == null) return;
            _networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            _networkManager.ClientManager.OnAuthenticated -= OnLocalClientAuthenticated;
            _networkManager.ServerManager.UnregisterBroadcast<IdentityBroadcast>(OnIdentityReceived);
        }

        // ─────────────────────────── public API (UI calls these) ───────────────────────────

        /// <summary>Open this world as a listen server and join it as a player.</summary>
        public void StartHost()
        {
            if (IsOnline) return;
            StatusLine = "Starting host...";
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection("localhost");
        }

        /// <summary>Join someone else's world at the given address.</summary>
        public void StartClient(string address)
        {
            if (IsOnline) return;
            address = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
            StatusLine = $"Connecting to {address}...";
            _networkManager.ClientManager.StartConnection(address);
        }

        /// <summary>Leave the session (client) or shut it down (host).</summary>
        public void StopSession()
        {
            if (_clientStarted) _networkManager.ClientManager.StopConnection();
            if (_serverStarted) _networkManager.ServerManager.StopConnection(true);
        }

        // ─────────────────────────── connection state ───────────────────────────

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _serverStarted = true;
                NetworkSession.SetMode(SessionMode.Host);
                StatusLine = "Hosting";
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _serverStarted = false;
                _avatarsByConnection.Clear();
                _playerIdByConnection.Clear();
                if (!_clientStarted) GoOffline();
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _clientStarted = true;
                if (!_serverStarted)
                {
                    NetworkSession.SetMode(SessionMode.Client);
                    StatusLine = "Connected";
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _clientStarted = false;
                if (_serverStarted) StatusLine = "Hosting";
                else GoOffline();
            }
        }

        /// <summary>The local client is fully in - introduce ourselves so the
        /// server can spawn our avatar under our stable player id.</summary>
        private void OnLocalClientAuthenticated()
        {
            _networkManager.ClientManager.Broadcast(new IdentityBroadcast
            {
                PlayerId = PlayerIdentity.LocalId,
                PlayerName = PlayerIdentity.LocalName
            });
        }

        // ─────────────────────────── server: identity -> avatar ───────────────────────────

        private void OnIdentityReceived(NetworkConnection connection, IdentityBroadcast msg, Channel channel)
        {
            if (!_serverStarted || connection == null) return;
            if (string.IsNullOrEmpty(msg.PlayerId)) return;

            // Already spawned? Then this is a rename - update for everyone.
            if (_avatarsByConnection.TryGetValue(connection.ClientId, out var existing))
            {
                var existingAvatar = existing != null ? existing.GetComponent<PlayerAvatar>() : null;
                if (existingAvatar != null) existingAvatar.ServerSetName(msg.PlayerName);
                return;
            }

            if (avatarPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] No avatar prefab assigned - run Setup Step 105 in this scene.");
                return;
            }

            // Duplicate-identity guard: two connections must never share one
            // player id, or every per-player system collapses them into one
            // person (roster, '(you)' markers, code locks...). Normally the
            // per-instance identity slots prevent this; if it still happens,
            // admit the newcomer under a visible guest id and say so.
            string playerId = msg.PlayerId;
            foreach (var entry in _playerIdByConnection)
            {
                if (entry.Value == playerId && entry.Key != connection.ClientId)
                {
                    Debug.LogWarning(
                        $"[NetworkBootstrap] Connection {connection.ClientId} presented a player id already in the session " +
                        "(two game instances sharing an identity?). Admitting it under a guest id.");
                    playerId = $"{playerId}-guest{connection.ClientId}";
                    break;
                }
            }
            _playerIdByConnection[connection.ClientId] = playerId;

            NetworkObject nob = Instantiate(avatarPrefab);
            _networkManager.ServerManager.Spawn(nob, connection);
            _avatarsByConnection[connection.ClientId] = nob;

            // Identity is applied AFTER Spawn: set post-spawn, SyncVars
            // replicate as ordinary reliable updates to current observers and
            // ride the spawn payload for late joiners. Values written before
            // Spawn can be treated as defaults and never delivered - that was
            // the 14.1.0 missing-names bug.
            var avatar = nob.GetComponent<PlayerAvatar>();
            if (avatar != null) avatar.SetIdentity(playerId, msg.PlayerName);
        }

        /// <summary>Re-announce the local identity (e.g. after a rename) so
        /// the server updates this player's avatar for everyone.</summary>
        public void AnnounceLocalName()
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new IdentityBroadcast
            {
                PlayerId = PlayerIdentity.LocalId,
                PlayerName = PlayerIdentity.LocalName
            });
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            _playerIdByConnection.Remove(connection.ClientId);
            if (!_avatarsByConnection.TryGetValue(connection.ClientId, out var nob)) return;
            _avatarsByConnection.Remove(connection.ClientId);
            if (nob != null && nob.IsSpawned) _networkManager.ServerManager.Despawn(nob);
        }

        // ─────────────────────────── teardown ───────────────────────────

        private void GoOffline()
        {
            NetworkSession.SetMode(SessionMode.Offline);
            StatusLine = "Offline";

            // Sweep any remote presences the avatar callbacks did not get to
            // (e.g. an abrupt disconnect). The local player always stays.
            var stale = new List<string>();
            foreach (var presence in NetworkSession.Players)
                if (presence.playerId != NetworkSession.LocalPlayerId)
                    stale.Add(presence.playerId);
            foreach (var id in stale) NetworkSession.UnregisterPlayer(id);
        }
    }
}
