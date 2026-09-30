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

    /// <summary>Server -> client on join: which world the host is running,
    /// so the client can warn when terrain will not line up.</summary>
    public struct WorldInfoBroadcast : IBroadcast
    {
        public string WorldName;
        public int Seed;
    }

    // ── Building replication (14.4.0). Client -> server -> other clients. ──

    public struct PiecePlacedBroadcast : IBroadcast
    {
        public string Family;
        public int Tier;
        public Vector3 Position;
        public Quaternion Rotation;
        public float RailingRise;
        public float PillarHeight;
    }

    public struct PieceRemovedBroadcast : IBroadcast
    {
        public string Family;
        public Vector3 Position;
    }

    /// <summary>Surviving damage - hp after a decay tick or partial hit (14.5.1).</summary>
    public struct PieceDamagedBroadcast : IBroadcast
    {
        public string Family;
        public Vector3 Position;
        public int Hp;
    }

    public struct PieceUpgradedBroadcast : IBroadcast
    {
        public string Family;
        public Vector3 Position;
        public int NewTier;
    }

    /// <summary>Door, gate, garage or hatch toggled (14.6.0).</summary>
    public struct DoorStateBroadcast : IBroadcast
    {
        public string Family;
        public Vector3 Position;
        public bool Open;
        public float Side;
    }

    /// <summary>Full code-lock state - fit, code, locked flag, guest list (14.6.0).</summary>
    public struct LockStateBroadcast : IBroadcast
    {
        public string Family;
        public Vector3 Position;
        public string Code;
        public bool Locked;
        public List<string> AuthorizedIds;
    }

    public struct LockRemovedBroadcast : IBroadcast
    {
        public string Family;
        public Vector3 Position;
    }

    /// <summary>Voxel brush op in integer voxel space (14.7.0) - deterministic
    /// and floating-origin-proof; Body names the planet it belongs to.</summary>
    public struct TerrainBrushBroadcast : IBroadcast
    {
        public string Body;
        public int X, Y, Z;
        public float Radius;
        public float Strength;
        public bool Subtract;
        public byte Fill;
    }

    /// <summary>Explosion event (14.7.0): scene position for the fireball/shake,
    /// crater in voxel space for the terrain. Carries NO damage.</summary>
    public struct ExplosionBroadcast : IBroadcast
    {
        public string Body;
        public Vector3 Position;
        public float Radius;
        public int CraterX, CraterY, CraterZ;
        public int CraterRadius;
    }

    /// <summary>One item-block placed live (14.9.0).</summary>
    public struct BlockPlacedBroadcast : IBroadcast
    {
        public BlockSnapshot Snap;
    }

    /// <summary>Surviving damage on an item-block (14.9.0).</summary>
    public struct BlockDamagedBroadcast : IBroadcast
    {
        public string ItemId;
        public Vector3 Position;
        public int Hp;
    }

    public struct BlockRemovedBroadcast : IBroadcast
    {
        public string ItemId;
        public Vector3 Position;
    }

    /// <summary>A chunk of standing item-blocks (join merge, 14.9.0).</summary>
    public struct BlockSnapshotBroadcast : IBroadcast
    {
        public int ChunkIndex;
        public int TotalChunks;
        public List<BlockSnapshot> Blocks;
    }

    /// <summary>One block's container contents as save-format JSON (14.10.0).</summary>
    public struct ContainerStateBroadcast : IBroadcast
    {
        public string ItemId;
        public Vector3 Position;
        public string Json;
    }

    /// <summary>A chunk of container states (join merge, 14.10.0).</summary>
    public struct ContainerSnapshotBroadcast : IBroadcast
    {
        public int ChunkIndex;
        public int TotalChunks;
        public List<ContainerRecord> Records;
    }

    /// <summary>One block's machine runtime as save-format JSON (14.12.0).</summary>
    public struct MachineStateBroadcast : IBroadcast
    {
        public string ItemId;
        public Vector3 Position;
        public string Json;
    }

    /// <summary>A chunk of machine runtime states (join merge, 14.12.0).</summary>
    public struct MachineSnapshotBroadcast : IBroadcast
    {
        public int ChunkIndex;
        public int TotalChunks;
        public List<MachineRecord> Records;
    }

    /// <summary>One physical world drop spawned (14.11.0). Stack as save-format JSON.</summary>
    public struct DropSpawnedBroadcast : IBroadcast
    {
        public string Id;
        public string StackJson;
        public Vector3 Position;
        public Vector3 Toss;
    }

    /// <summary>A drop came to rest - converge its position everywhere (14.11.0).</summary>
    public struct DropSettledBroadcast : IBroadcast
    {
        public string Id;
        public Vector3 Position;
    }

    /// <summary>A drop's stack shrank (partial pickup / belt insert, 14.11.0).</summary>
    public struct DropUpdatedBroadcast : IBroadcast
    {
        public string Id;
        public int Count;
    }

    public struct DropRemovedBroadcast : IBroadcast
    {
        public string Id;
    }

    /// <summary>A chunk of live world drops (join merge, 14.11.0).</summary>
    public struct DropSnapshotBroadcast : IBroadcast
    {
        public int ChunkIndex;
        public int TotalChunks;
        public List<DropRecord> Records;
    }

    /// <summary>One edited terrain chunk for the join catch-up (14.8.0):
    /// deflate-compressed full padded voxel grid, planet-tagged.</summary>
    public struct TerrainChunkBroadcast : IBroadcast
    {
        public string Body;
        public int X, Y, Z;
        public byte[] Data;
    }

    /// <summary>Client -> server: reply to WorldInfoBroadcast. Only a matching
    /// seed invites the base snapshot exchange (14.5.0).</summary>
    public struct WorldAckBroadcast : IBroadcast
    {
        public bool SeedMatches;
    }

    /// <summary>A chunk of standing pieces (join-in-progress base sync, 14.5.0).
    /// Server -> joining client with the session's base; joining client -> server
    /// with its own solo-built base for the merge.</summary>
    public struct BaseSnapshotBroadcast : IBroadcast
    {
        public int ChunkIndex;
        public int TotalChunks;
        public List<PieceSnapshot> Pieces;
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

        /// <summary>True on a client whose world seed differs from the host's.</summary>
        public bool WorldMismatch { get; private set; }

        /// <summary>Human line describing the host's world ("name, seed").</summary>
        public string HostWorldLine { get; private set; } = "";

        private string _statusLine = "Offline";

        /// <summary>One human-readable line for the multiplayer menu. Pure
        /// clients get their live ping appended.</summary>
        public string StatusLine
        {
            get
            {
                if (_clientStarted && !_serverStarted)
                    return $"Connected - ping {_networkManager.TimeManager.RoundTripTime} ms";
                return _statusLine;
            }
        }

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
            _networkManager.ServerManager.RegisterBroadcast<PiecePlacedBroadcast>(OnServerPiecePlaced);
            _networkManager.ServerManager.RegisterBroadcast<PieceRemovedBroadcast>(OnServerPieceRemoved);
            _networkManager.ServerManager.RegisterBroadcast<PieceDamagedBroadcast>(OnServerPieceDamaged);
            _networkManager.ServerManager.RegisterBroadcast<PieceUpgradedBroadcast>(OnServerPieceUpgraded);
            _networkManager.ServerManager.RegisterBroadcast<DoorStateBroadcast>(OnServerDoorState);
            _networkManager.ServerManager.RegisterBroadcast<LockStateBroadcast>(OnServerLockState);
            _networkManager.ServerManager.RegisterBroadcast<LockRemovedBroadcast>(OnServerLockRemoved);
            _networkManager.ServerManager.RegisterBroadcast<TerrainBrushBroadcast>(OnServerTerrainBrush);
            _networkManager.ServerManager.RegisterBroadcast<ExplosionBroadcast>(OnServerExplosion);
            _networkManager.ServerManager.RegisterBroadcast<BlockPlacedBroadcast>(OnServerBlockPlaced);
            _networkManager.ServerManager.RegisterBroadcast<BlockDamagedBroadcast>(OnServerBlockDamaged);
            _networkManager.ServerManager.RegisterBroadcast<BlockRemovedBroadcast>(OnServerBlockRemoved);
            _networkManager.ServerManager.RegisterBroadcast<BlockSnapshotBroadcast>(OnServerBlockSnapshot);
            _networkManager.ServerManager.RegisterBroadcast<ContainerStateBroadcast>(OnServerContainerState);
            _networkManager.ServerManager.RegisterBroadcast<ContainerSnapshotBroadcast>(OnServerContainerSnapshot);
            _networkManager.ServerManager.RegisterBroadcast<MachineStateBroadcast>(OnServerMachineState);
            _networkManager.ServerManager.RegisterBroadcast<MachineSnapshotBroadcast>(OnServerMachineSnapshot);
            _networkManager.ServerManager.RegisterBroadcast<DropSpawnedBroadcast>(OnServerDropSpawned);
            _networkManager.ServerManager.RegisterBroadcast<DropSettledBroadcast>(OnServerDropSettled);
            _networkManager.ServerManager.RegisterBroadcast<DropUpdatedBroadcast>(OnServerDropUpdated);
            _networkManager.ServerManager.RegisterBroadcast<DropRemovedBroadcast>(OnServerDropRemoved);
            _networkManager.ServerManager.RegisterBroadcast<DropSnapshotBroadcast>(OnServerDropSnapshot);
            _networkManager.ServerManager.RegisterBroadcast<TerrainChunkBroadcast>(OnServerTerrainChunk);
            _networkManager.ServerManager.RegisterBroadcast<WorldAckBroadcast>(OnWorldAck);
            _networkManager.ServerManager.RegisterBroadcast<BaseSnapshotBroadcast>(OnServerBaseSnapshot);
            _networkManager.ClientManager.RegisterBroadcast<WorldInfoBroadcast>(OnWorldInfo);
            _networkManager.ClientManager.RegisterBroadcast<PiecePlacedBroadcast>(OnClientPiecePlaced);
            _networkManager.ClientManager.RegisterBroadcast<PieceRemovedBroadcast>(OnClientPieceRemoved);
            _networkManager.ClientManager.RegisterBroadcast<PieceDamagedBroadcast>(OnClientPieceDamaged);
            _networkManager.ClientManager.RegisterBroadcast<PieceUpgradedBroadcast>(OnClientPieceUpgraded);
            _networkManager.ClientManager.RegisterBroadcast<DoorStateBroadcast>(OnClientDoorState);
            _networkManager.ClientManager.RegisterBroadcast<LockStateBroadcast>(OnClientLockState);
            _networkManager.ClientManager.RegisterBroadcast<LockRemovedBroadcast>(OnClientLockRemoved);
            _networkManager.ClientManager.RegisterBroadcast<TerrainBrushBroadcast>(OnClientTerrainBrush);
            _networkManager.ClientManager.RegisterBroadcast<ExplosionBroadcast>(OnClientExplosion);
            _networkManager.ClientManager.RegisterBroadcast<BlockPlacedBroadcast>(OnClientBlockPlaced);
            _networkManager.ClientManager.RegisterBroadcast<BlockDamagedBroadcast>(OnClientBlockDamaged);
            _networkManager.ClientManager.RegisterBroadcast<BlockRemovedBroadcast>(OnClientBlockRemoved);
            _networkManager.ClientManager.RegisterBroadcast<BlockSnapshotBroadcast>(OnClientBlockSnapshot);
            _networkManager.ClientManager.RegisterBroadcast<ContainerStateBroadcast>(OnClientContainerState);
            _networkManager.ClientManager.RegisterBroadcast<ContainerSnapshotBroadcast>(OnClientContainerSnapshot);
            _networkManager.ClientManager.RegisterBroadcast<MachineStateBroadcast>(OnClientMachineState);
            _networkManager.ClientManager.RegisterBroadcast<MachineSnapshotBroadcast>(OnClientMachineSnapshot);
            _networkManager.ClientManager.RegisterBroadcast<DropSpawnedBroadcast>(OnClientDropSpawned);
            _networkManager.ClientManager.RegisterBroadcast<DropSettledBroadcast>(OnClientDropSettled);
            _networkManager.ClientManager.RegisterBroadcast<DropUpdatedBroadcast>(OnClientDropUpdated);
            _networkManager.ClientManager.RegisterBroadcast<DropRemovedBroadcast>(OnClientDropRemoved);
            _networkManager.ClientManager.RegisterBroadcast<DropSnapshotBroadcast>(OnClientDropSnapshot);
            _networkManager.ClientManager.RegisterBroadcast<TerrainChunkBroadcast>(OnClientTerrainChunk);
            _networkManager.ClientManager.RegisterBroadcast<BaseSnapshotBroadcast>(OnClientBaseSnapshot);

            // Container-contents poller (14.10.0) - idles while offline.
            if (GetComponent<ContainerSyncManager>() == null)
                gameObject.AddComponent<ContainerSyncManager>();
            // Machine-runtime poller (14.12.0) - same pattern, slower cadence.
            if (GetComponent<MachineSyncManager>() == null)
                gameObject.AddComponent<MachineSyncManager>();
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
            _networkManager.ServerManager.UnregisterBroadcast<PiecePlacedBroadcast>(OnServerPiecePlaced);
            _networkManager.ServerManager.UnregisterBroadcast<PieceRemovedBroadcast>(OnServerPieceRemoved);
            _networkManager.ServerManager.UnregisterBroadcast<PieceDamagedBroadcast>(OnServerPieceDamaged);
            _networkManager.ServerManager.UnregisterBroadcast<PieceUpgradedBroadcast>(OnServerPieceUpgraded);
            _networkManager.ServerManager.UnregisterBroadcast<DoorStateBroadcast>(OnServerDoorState);
            _networkManager.ServerManager.UnregisterBroadcast<LockStateBroadcast>(OnServerLockState);
            _networkManager.ServerManager.UnregisterBroadcast<LockRemovedBroadcast>(OnServerLockRemoved);
            _networkManager.ServerManager.UnregisterBroadcast<TerrainBrushBroadcast>(OnServerTerrainBrush);
            _networkManager.ServerManager.UnregisterBroadcast<ExplosionBroadcast>(OnServerExplosion);
            _networkManager.ServerManager.UnregisterBroadcast<BlockPlacedBroadcast>(OnServerBlockPlaced);
            _networkManager.ServerManager.UnregisterBroadcast<BlockDamagedBroadcast>(OnServerBlockDamaged);
            _networkManager.ServerManager.UnregisterBroadcast<BlockRemovedBroadcast>(OnServerBlockRemoved);
            _networkManager.ServerManager.UnregisterBroadcast<BlockSnapshotBroadcast>(OnServerBlockSnapshot);
            _networkManager.ServerManager.UnregisterBroadcast<ContainerStateBroadcast>(OnServerContainerState);
            _networkManager.ServerManager.UnregisterBroadcast<ContainerSnapshotBroadcast>(OnServerContainerSnapshot);
            _networkManager.ServerManager.UnregisterBroadcast<MachineStateBroadcast>(OnServerMachineState);
            _networkManager.ServerManager.UnregisterBroadcast<MachineSnapshotBroadcast>(OnServerMachineSnapshot);
            _networkManager.ServerManager.UnregisterBroadcast<DropSpawnedBroadcast>(OnServerDropSpawned);
            _networkManager.ServerManager.UnregisterBroadcast<DropSettledBroadcast>(OnServerDropSettled);
            _networkManager.ServerManager.UnregisterBroadcast<DropUpdatedBroadcast>(OnServerDropUpdated);
            _networkManager.ServerManager.UnregisterBroadcast<DropRemovedBroadcast>(OnServerDropRemoved);
            _networkManager.ServerManager.UnregisterBroadcast<DropSnapshotBroadcast>(OnServerDropSnapshot);
            _networkManager.ServerManager.UnregisterBroadcast<TerrainChunkBroadcast>(OnServerTerrainChunk);
            _networkManager.ServerManager.UnregisterBroadcast<WorldAckBroadcast>(OnWorldAck);
            _networkManager.ServerManager.UnregisterBroadcast<BaseSnapshotBroadcast>(OnServerBaseSnapshot);
            _networkManager.ClientManager.UnregisterBroadcast<WorldInfoBroadcast>(OnWorldInfo);
            _networkManager.ClientManager.UnregisterBroadcast<PiecePlacedBroadcast>(OnClientPiecePlaced);
            _networkManager.ClientManager.UnregisterBroadcast<PieceRemovedBroadcast>(OnClientPieceRemoved);
            _networkManager.ClientManager.UnregisterBroadcast<PieceDamagedBroadcast>(OnClientPieceDamaged);
            _networkManager.ClientManager.UnregisterBroadcast<PieceUpgradedBroadcast>(OnClientPieceUpgraded);
            _networkManager.ClientManager.UnregisterBroadcast<DoorStateBroadcast>(OnClientDoorState);
            _networkManager.ClientManager.UnregisterBroadcast<LockStateBroadcast>(OnClientLockState);
            _networkManager.ClientManager.UnregisterBroadcast<LockRemovedBroadcast>(OnClientLockRemoved);
            _networkManager.ClientManager.UnregisterBroadcast<TerrainBrushBroadcast>(OnClientTerrainBrush);
            _networkManager.ClientManager.UnregisterBroadcast<ExplosionBroadcast>(OnClientExplosion);
            _networkManager.ClientManager.UnregisterBroadcast<BlockPlacedBroadcast>(OnClientBlockPlaced);
            _networkManager.ClientManager.UnregisterBroadcast<BlockDamagedBroadcast>(OnClientBlockDamaged);
            _networkManager.ClientManager.UnregisterBroadcast<BlockRemovedBroadcast>(OnClientBlockRemoved);
            _networkManager.ClientManager.UnregisterBroadcast<BlockSnapshotBroadcast>(OnClientBlockSnapshot);
            _networkManager.ClientManager.UnregisterBroadcast<ContainerStateBroadcast>(OnClientContainerState);
            _networkManager.ClientManager.UnregisterBroadcast<ContainerSnapshotBroadcast>(OnClientContainerSnapshot);
            _networkManager.ClientManager.UnregisterBroadcast<MachineStateBroadcast>(OnClientMachineState);
            _networkManager.ClientManager.UnregisterBroadcast<MachineSnapshotBroadcast>(OnClientMachineSnapshot);
            _networkManager.ClientManager.UnregisterBroadcast<DropSpawnedBroadcast>(OnClientDropSpawned);
            _networkManager.ClientManager.UnregisterBroadcast<DropSettledBroadcast>(OnClientDropSettled);
            _networkManager.ClientManager.UnregisterBroadcast<DropUpdatedBroadcast>(OnClientDropUpdated);
            _networkManager.ClientManager.UnregisterBroadcast<DropRemovedBroadcast>(OnClientDropRemoved);
            _networkManager.ClientManager.UnregisterBroadcast<DropSnapshotBroadcast>(OnClientDropSnapshot);
            _networkManager.ClientManager.UnregisterBroadcast<TerrainChunkBroadcast>(OnClientTerrainChunk);
            _networkManager.ClientManager.UnregisterBroadcast<BaseSnapshotBroadcast>(OnClientBaseSnapshot);
        }

        // ─────────────────────────── public API (UI calls these) ───────────────────────────

        /// <summary>Open this world as a listen server and join it as a player.</summary>
        public void StartHost()
        {
            if (IsOnline) return;
            _statusLine = "Starting host...";
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection("localhost");
        }

        /// <summary>Join someone else's world at the given address.</summary>
        public void StartClient(string address)
        {
            if (IsOnline) return;
            address = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
            _statusLine = $"Connecting to {address}...";
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
                _statusLine = "Hosting";
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
                    _statusLine = "Connected";
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                _clientStarted = false;
                if (_serverStarted) _statusLine = "Hosting";
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

            // Tell the newcomer which world this server runs, so their client
            // can warn when terrain will not line up (different seed).
            if (!connection.IsLocalClient)
            {
                var session = VoxelEngine.Menu.WorldSession.Instance;
                if (session != null)
                    _networkManager.ServerManager.Broadcast(connection, new WorldInfoBroadcast
                    { WorldName = session.worldName, Seed = session.seed }, true);
            }
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

        // ─────────────────────────── building sync wire (14.4.0) ───────────────────────────
        // One uniform path: every machine (host included) SENDS as a client;
        // the server applies remote edits locally and relays to everyone else.

        public void SendPiecePlaced(string family, int tier, Vector3 pos, Quaternion rot,
            float railingRise, float pillarHeight)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new PiecePlacedBroadcast
            {
                Family = family, Tier = tier, Position = pos, Rotation = rot,
                RailingRise = railingRise, PillarHeight = pillarHeight
            });
        }

        public void SendPieceRemoved(string family, Vector3 pos)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new PieceRemovedBroadcast
            { Family = family, Position = pos });
        }

        public void SendPieceDamaged(string family, Vector3 pos, int hp)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new PieceDamagedBroadcast
            { Family = family, Position = pos, Hp = hp });
        }

        public void SendPieceUpgraded(string family, Vector3 pos, int newTier)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new PieceUpgradedBroadcast
            { Family = family, Position = pos, NewTier = newTier });
        }

        private void OnServerPiecePlaced(NetworkConnection conn, PiecePlacedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient)
                BuildingSync.ApplyPlaced(msg.Family, msg.Tier, msg.Position, msg.Rotation,
                    msg.RailingRise, msg.PillarHeight);
            RelayToOthers(conn, msg);
        }

        private void OnServerPieceRemoved(NetworkConnection conn, PieceRemovedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplyRemoved(msg.Family, msg.Position);
            RelayToOthers(conn, msg);
        }

        public void SendDoorState(string family, Vector3 pos, bool open, float side)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new DoorStateBroadcast
            { Family = family, Position = pos, Open = open, Side = side });
        }

        public void SendLockState(string family, Vector3 pos, string code, bool locked, List<string> ids)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new LockStateBroadcast
            { Family = family, Position = pos, Code = code, Locked = locked, AuthorizedIds = ids });
        }

        public void SendLockRemoved(string family, Vector3 pos)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new LockRemovedBroadcast
            { Family = family, Position = pos });
        }

        public void SendTerrainBrush(string body, Vector3Int center, float radius,
            float strength, bool subtract, byte fill)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new TerrainBrushBroadcast
            {
                Body = body, X = center.x, Y = center.y, Z = center.z,
                Radius = radius, Strength = strength, Subtract = subtract, Fill = fill
            });
        }

        public void SendExplosion(string body, Vector3 position, float radius,
            Vector3Int craterCenter, int craterRadius)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new ExplosionBroadcast
            {
                Body = body, Position = position, Radius = radius,
                CraterX = craterCenter.x, CraterY = craterCenter.y, CraterZ = craterCenter.z,
                CraterRadius = craterRadius
            });
        }

        private void OnServerTerrainBrush(NetworkConnection conn, TerrainBrushBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient)
                TerrainSync.ApplyBrush(msg.Body, new Vector3Int(msg.X, msg.Y, msg.Z),
                    msg.Radius, msg.Strength, msg.Subtract, msg.Fill);
            RelayToOthers(conn, msg);
        }

        private void OnServerExplosion(NetworkConnection conn, ExplosionBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient)
                TerrainSync.ApplyExplosion(msg.Body, msg.Position, msg.Radius,
                    new Vector3Int(msg.CraterX, msg.CraterY, msg.CraterZ), msg.CraterRadius);
            RelayToOthers(conn, msg);
        }

        private void OnClientTerrainBrush(TerrainBrushBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            TerrainSync.ApplyBrush(msg.Body, new Vector3Int(msg.X, msg.Y, msg.Z),
                msg.Radius, msg.Strength, msg.Subtract, msg.Fill);
        }

        private void OnClientExplosion(ExplosionBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            TerrainSync.ApplyExplosion(msg.Body, msg.Position, msg.Radius,
                new Vector3Int(msg.CraterX, msg.CraterY, msg.CraterZ), msg.CraterRadius);
        }

        private void OnServerDoorState(NetworkConnection conn, DoorStateBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplyDoorState(msg.Family, msg.Position, msg.Open, msg.Side);
            RelayToOthers(conn, msg);
        }

        private void OnServerLockState(NetworkConnection conn, LockStateBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplyLockState(msg.Family, msg.Position, msg.Code, msg.Locked, msg.AuthorizedIds);
            RelayToOthers(conn, msg);
        }

        private void OnServerLockRemoved(NetworkConnection conn, LockRemovedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplyLockRemoved(msg.Family, msg.Position);
            RelayToOthers(conn, msg);
        }

        private void OnClientDoorState(DoorStateBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyDoorState(msg.Family, msg.Position, msg.Open, msg.Side);
        }

        private void OnClientLockState(LockStateBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyLockState(msg.Family, msg.Position, msg.Code, msg.Locked, msg.AuthorizedIds);
        }

        private void OnClientLockRemoved(LockRemovedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyLockRemoved(msg.Family, msg.Position);
        }

        private void OnServerPieceDamaged(NetworkConnection conn, PieceDamagedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplyDamaged(msg.Family, msg.Position, msg.Hp);
            RelayToOthers(conn, msg);
        }

        private void OnServerPieceUpgraded(NetworkConnection conn, PieceUpgradedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplyUpgraded(msg.Family, msg.Position, msg.NewTier);
            RelayToOthers(conn, msg);
        }

        /// <summary>Server relay: everyone except the sender and the host's own
        /// local client (the server path already applied it there).</summary>
        private void RelayToOthers<T>(NetworkConnection sender, T msg) where T : struct, IBroadcast
        {
            foreach (var pair in _networkManager.ServerManager.Clients)
            {
                var client = pair.Value;
                if (client == null || client == sender || client.IsLocalClient) continue;
                _networkManager.ServerManager.Broadcast(client, msg, true);
            }
        }

        private void OnClientPiecePlaced(PiecePlacedBroadcast msg, Channel channel)
        {
            // Host already applied on the server path; a mismatched client's
            // terrain cannot host the piece - drop building traffic entirely.
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyPlaced(msg.Family, msg.Tier, msg.Position, msg.Rotation,
                msg.RailingRise, msg.PillarHeight);
        }

        private void OnClientPieceRemoved(PieceRemovedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyRemoved(msg.Family, msg.Position);
        }

        private void OnClientPieceDamaged(PieceDamagedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyDamaged(msg.Family, msg.Position, msg.Hp);
        }

        private void OnClientPieceUpgraded(PieceUpgradedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplyUpgraded(msg.Family, msg.Position, msg.NewTier);
        }

        private void OnWorldInfo(WorldInfoBroadcast msg, Channel channel)
        {
            if (_serverStarted) return;
            var session = VoxelEngine.Menu.WorldSession.Instance;
            WorldMismatch = session == null || session.seed != msg.Seed;
            HostWorldLine = $"Host world: '{msg.WorldName}', seed {msg.Seed}";
            if (WorldMismatch)
                Debug.LogWarning("[NetworkBootstrap] World mismatch - " + HostWorldLine +
                    $", yours: '{(session != null ? session.worldName : "?")}', seed {(session != null ? session.seed.ToString() : "?")}. " +
                    "Terrain and buildings will NOT line up. Create/load a world with the host's seed to share ground.");

            // Handshake reply: a matching seed opens the two-way base exchange
            // (14.5.0). Our own solo-built base goes up BEFORE any incoming
            // chunks apply (ordered channel), so the gather never sees remote
            // pieces and echoes them back.
            _networkManager.ClientManager.Broadcast(new WorldAckBroadcast { SeedMatches = !WorldMismatch });
            if (!WorldMismatch)
            {
                SendBaseSnapshot(null);
                SendBlockSnapshot(null);
                SendContainerSnapshot(null);
                SendMachineSnapshot(null);
                SendDropSnapshot(null);
                SendTerrainSnapshot(null);
            }
        }

        /// <summary>Server: seed-matching client acknowledged - send it the base.</summary>
        private void OnWorldAck(NetworkConnection conn, WorldAckBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn.IsLocalClient) return;
            if (!msg.SeedMatches) return;
            SendBaseSnapshot(conn);
            SendBlockSnapshot(conn);
            SendContainerSnapshot(conn);
            SendMachineSnapshot(conn);
            SendDropSnapshot(conn);
            SendTerrainSnapshot(conn);
        }

        public void SendBlockPlaced(BlockSnapshot snap)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new BlockPlacedBroadcast { Snap = snap });
        }

        public void SendBlockDamaged(string itemId, Vector3 pos, int hp)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new BlockDamagedBroadcast
            { ItemId = itemId, Position = pos, Hp = hp });
        }

        public void SendBlockRemoved(string itemId, Vector3 pos)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new BlockRemovedBroadcast
            { ItemId = itemId, Position = pos });
        }

        private void OnServerBlockPlaced(NetworkConnection conn, BlockPlacedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BlockSync.ApplyPlaced(msg.Snap);
            RelayToOthers(conn, msg);
        }

        private void OnServerBlockDamaged(NetworkConnection conn, BlockDamagedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BlockSync.ApplyDamaged(msg.ItemId, msg.Position, msg.Hp);
            RelayToOthers(conn, msg);
        }

        private void OnServerBlockRemoved(NetworkConnection conn, BlockRemovedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BlockSync.ApplyRemoved(msg.ItemId, msg.Position);
            RelayToOthers(conn, msg);
        }

        private void OnServerBlockSnapshot(NetworkConnection conn, BlockSnapshotBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BlockSync.ApplySnapshot(msg.Blocks);
            RelayToOthers(conn, msg);
        }

        private void OnClientBlockPlaced(BlockPlacedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BlockSync.ApplyPlaced(msg.Snap);
        }

        private void OnClientBlockDamaged(BlockDamagedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BlockSync.ApplyDamaged(msg.ItemId, msg.Position, msg.Hp);
        }

        private void OnClientBlockRemoved(BlockRemovedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BlockSync.ApplyRemoved(msg.ItemId, msg.Position);
        }

        private void OnClientBlockSnapshot(BlockSnapshotBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BlockSync.ApplySnapshot(msg.Blocks);
        }

        /// <summary>Gather all standing item-blocks and send them chunked - to a joining
        /// connection when called as server, up to the server when target is null.</summary>
        private void SendBlockSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 32;
            var blocks = BlockSync.GatherSnapshot();
            if (blocks.Count == 0) return;
            int total = Mathf.CeilToInt(blocks.Count / (float)ChunkSize);
            for (int i = 0; i < total; i++)
            {
                var chunk = new BlockSnapshotBroadcast
                {
                    ChunkIndex = i,
                    TotalChunks = total,
                    Blocks = blocks.GetRange(i * ChunkSize,
                        Mathf.Min(ChunkSize, blocks.Count - i * ChunkSize))
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, chunk, true);
                else _networkManager.ClientManager.Broadcast(chunk);
            }
        }

        public void SendContainerState(string itemId, Vector3 pos, string json)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new ContainerStateBroadcast
            { ItemId = itemId, Position = pos, Json = json });
        }

        private void OnServerContainerState(NetworkConnection conn, ContainerStateBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) ContainerSync.ApplyState(msg.ItemId, msg.Position, msg.Json);
            RelayToOthers(conn, msg);
        }

        private void OnClientContainerState(ContainerStateBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            ContainerSync.ApplyState(msg.ItemId, msg.Position, msg.Json);
        }

        private void OnServerContainerSnapshot(NetworkConnection conn, ContainerSnapshotBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn.IsLocalClient) return;
            // Joiner upload: only EMPTY host containers accept it, and only the
            // accepted records are redistributed (14.8.1 rule) - never a blind relay.
            ContainerSync.ApplyClientSnapshot(msg.Records);
        }

        private void OnClientContainerSnapshot(ContainerSnapshotBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            ContainerSync.ApplyHostSnapshot(msg.Records);
        }

        /// <summary>Gather every container-carrying block and send it chunked - to a
        /// joining connection when called as server, up to the server when target is null.</summary>
        private void SendContainerSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 16;
            var records = ContainerSync.GatherSnapshot();
            if (records.Count == 0) return;
            int total = Mathf.CeilToInt(records.Count / (float)ChunkSize);
            for (int i = 0; i < total; i++)
            {
                var chunk = new ContainerSnapshotBroadcast
                {
                    ChunkIndex = i,
                    TotalChunks = total,
                    Records = records.GetRange(i * ChunkSize,
                        Mathf.Min(ChunkSize, records.Count - i * ChunkSize))
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, chunk, true);
                else _networkManager.ClientManager.Broadcast(chunk);
            }
        }

        public void SendMachineState(string itemId, Vector3 pos, string json)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new MachineStateBroadcast
            { ItemId = itemId, Position = pos, Json = json });
        }

        private void OnServerMachineState(NetworkConnection conn, MachineStateBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) MachineSync.ApplyState(msg.ItemId, msg.Position, msg.Json);
            RelayToOthers(conn, msg);
        }

        private void OnClientMachineState(MachineStateBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            MachineSync.ApplyState(msg.ItemId, msg.Position, msg.Json);
        }

        private void OnServerMachineSnapshot(NetworkConnection conn, MachineSnapshotBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn.IsLocalClient) return;
            // Joiner upload: only non-busy host machines accept it, and only the
            // accepted records are redistributed (14.8.1 rule) - never a blind relay.
            MachineSync.ApplyClientSnapshot(msg.Records);
        }

        private void OnClientMachineSnapshot(MachineSnapshotBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            MachineSync.ApplyHostSnapshot(msg.Records);
        }

        /// <summary>Gather every machine-runtime-carrying block and send it chunked -
        /// to a joining connection when called as server, up to the server when target is null.</summary>
        private void SendMachineSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 16;
            var records = MachineSync.GatherSnapshot();
            if (records.Count == 0) return;
            int total = Mathf.CeilToInt(records.Count / (float)ChunkSize);
            for (int i = 0; i < total; i++)
            {
                var chunk = new MachineSnapshotBroadcast
                {
                    ChunkIndex = i,
                    TotalChunks = total,
                    Records = records.GetRange(i * ChunkSize,
                        Mathf.Min(ChunkSize, records.Count - i * ChunkSize))
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, chunk, true);
                else _networkManager.ClientManager.Broadcast(chunk);
            }
        }

        public void SendDropSpawned(string id, string stackJson, Vector3 pos, Vector3 toss)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new DropSpawnedBroadcast
            { Id = id, StackJson = stackJson, Position = pos, Toss = toss });
        }

        public void SendDropSettled(string id, Vector3 pos)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new DropSettledBroadcast
            { Id = id, Position = pos });
        }

        public void SendDropUpdated(string id, int count)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new DropUpdatedBroadcast
            { Id = id, Count = count });
        }

        public void SendDropRemoved(string id)
        {
            if (!_clientStarted) return;
            _networkManager.ClientManager.Broadcast(new DropRemovedBroadcast { Id = id });
        }

        private void OnServerDropSpawned(NetworkConnection conn, DropSpawnedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) DropSync.ApplySpawned(msg.Id, msg.StackJson, msg.Position, msg.Toss);
            RelayToOthers(conn, msg);
        }

        private void OnServerDropSettled(NetworkConnection conn, DropSettledBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) DropSync.ApplySettled(msg.Id, msg.Position);
            RelayToOthers(conn, msg);
        }

        private void OnServerDropUpdated(NetworkConnection conn, DropUpdatedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) DropSync.ApplyUpdated(msg.Id, msg.Count);
            RelayToOthers(conn, msg);
        }

        private void OnServerDropRemoved(NetworkConnection conn, DropRemovedBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) DropSync.ApplyRemoved(msg.Id);
            RelayToOthers(conn, msg);
        }

        private void OnServerDropSnapshot(NetworkConnection conn, DropSnapshotBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) DropSync.ApplySnapshot(msg.Records);
            RelayToOthers(conn, msg);
        }

        private void OnClientDropSpawned(DropSpawnedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            DropSync.ApplySpawned(msg.Id, msg.StackJson, msg.Position, msg.Toss);
        }

        private void OnClientDropSettled(DropSettledBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            DropSync.ApplySettled(msg.Id, msg.Position);
        }

        private void OnClientDropUpdated(DropUpdatedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            DropSync.ApplyUpdated(msg.Id, msg.Count);
        }

        private void OnClientDropRemoved(DropRemovedBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            DropSync.ApplyRemoved(msg.Id);
        }

        private void OnClientDropSnapshot(DropSnapshotBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            DropSync.ApplySnapshot(msg.Records);
        }

        /// <summary>Gather every live world drop and send it chunked - to a joining
        /// connection when called as server, up to the server when target is null.</summary>
        private void SendDropSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 32;
            var records = DropSync.GatherSnapshot();
            if (records.Count == 0) return;
            int total = Mathf.CeilToInt(records.Count / (float)ChunkSize);
            for (int i = 0; i < total; i++)
            {
                var chunk = new DropSnapshotBroadcast
                {
                    ChunkIndex = i,
                    TotalChunks = total,
                    Records = records.GetRange(i * ChunkSize,
                        Mathf.Min(ChunkSize, records.Count - i * ChunkSize))
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, chunk, true);
                else _networkManager.ClientManager.Broadcast(chunk);
            }
        }

        private void OnServerTerrainChunk(NetworkConnection conn, TerrainChunkBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn.IsLocalClient) return;
            // Host world is the authority: refuse chunks the host has its own
            // edit of, and relay ONLY accepted chunks - a joiner's stale copies
            // must never reach the other clients (14.8.1).
            bool accepted = TerrainSync.ApplyWireChunk(msg.Body,
                new Vector3Int(msg.X, msg.Y, msg.Z), msg.Data, respectLocalEdits: true);
            if (accepted) RelayToOthers(conn, msg);
        }

        private void OnClientTerrainChunk(TerrainChunkBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            // Server-approved truth: always overwrites local state (14.8.1).
            TerrainSync.ApplyWireChunk(msg.Body, new Vector3Int(msg.X, msg.Y, msg.Z),
                msg.Data, respectLocalEdits: false);
        }

        /// <summary>Send every edited chunk of the current planet - to a joining
        /// connection when called as server, up to the server when target is null.</summary>
        private void SendTerrainSnapshot(NetworkConnection target)
        {
            var chunks = TerrainSync.GatherWireChunks();
            if (chunks.Count == 0) return;
            string body = TerrainSync.CurrentBodyName();
            foreach (var chunk in chunks)
            {
                var msg = new TerrainChunkBroadcast
                {
                    Body = body, X = chunk.Coord.x, Y = chunk.Coord.y, Z = chunk.Coord.z,
                    Data = chunk.Compressed
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, msg, true);
                else _networkManager.ClientManager.Broadcast(msg);
            }
            Debug.Log($"[NetworkBootstrap] Terrain catch-up: {(target != null ? "sent" : "uploaded")} {chunks.Count} edited chunk(s).");
        }

        private void OnServerBaseSnapshot(NetworkConnection conn, BaseSnapshotBroadcast msg, Channel channel)
        {
            if (!_serverStarted) return;
            if (!conn.IsLocalClient) BuildingSync.ApplySnapshot(msg.Pieces);
            RelayToOthers(conn, msg);
        }

        private void OnClientBaseSnapshot(BaseSnapshotBroadcast msg, Channel channel)
        {
            if (_serverStarted || WorldMismatch) return;
            BuildingSync.ApplySnapshot(msg.Pieces);
        }

        /// <summary>Gather everything standing and send it chunked - to a specific
        /// connection when called as server, up to the server when target is null.</summary>
        private void SendBaseSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 32;   // comfortably inside a reliable packet
            var pieces = BuildingSync.GatherSnapshot();
            if (pieces.Count == 0) return;
            int total = Mathf.CeilToInt(pieces.Count / (float)ChunkSize);
            for (int i = 0; i < total; i++)
            {
                var chunk = new BaseSnapshotBroadcast
                {
                    ChunkIndex = i,
                    TotalChunks = total,
                    Pieces = pieces.GetRange(i * ChunkSize,
                        Mathf.Min(ChunkSize, pieces.Count - i * ChunkSize))
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, chunk, true);
                else _networkManager.ClientManager.Broadcast(chunk);
            }
        }

        // ─────────────────────────── teardown ───────────────────────────

        private void GoOffline()
        {
            NetworkSession.SetMode(SessionMode.Offline);
            _statusLine = "Offline";
            WorldMismatch = false;
            HostWorldLine = "";

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
