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

using System.Collections;
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

    /// <summary>Client -> server: one chat line (14.19.0). The server stamps
    /// the sender's name itself - clients are never trusted to sign text.</summary>
    public struct ChatBroadcast : IBroadcast
    {
        public string Text;
    }

    /// <summary>Server -> client: a chat line that passed the proximity check,
    /// stamped with the sender's display name.</summary>
    public struct ChatRelayBroadcast : IBroadcast
    {
        public string SenderName;
        public string Text;
    }

    /// <summary>Client -> server: one 40 ms voice frame (14.20.0). Carries no
    /// identity - the server stamps the speaker from the connection, exactly
    /// like text chat, so nobody can speak in another player's name.</summary>
    public struct VoiceBroadcast : IBroadcast
    {
        public byte[] Data;
        public ushort Sequence;
    }

    /// <summary>Server -> client: a voice frame that passed the proximity
    /// check, stamped with the speaker's player id and display name.</summary>
    public struct VoiceRelayBroadcast : IBroadcast
    {
        public string SenderId;
        public string SenderName;
        public byte[] Data;
        public ushort Sequence;
    }

    /// <summary>Server -> client on join: which world the host is running,
    /// so the client can warn when terrain will not line up.</summary>
    public struct WorldInfoBroadcast : IBroadcast
    {
        public string WorldName;
        public int Seed;

        /// <summary>14.23.0 - the host's world card (JSON): seed, the cosmos
        /// sidecar and the world rules. A client that joined from the main
        /// menu builds its world from this instead of from a local save.
        /// Empty from an older host, which falls back to the seed check.</summary>
        public string WorldCard;
    }

    /// <summary>Per-player state (14.24.0). Client -> server as an upload of
    /// "this is what I am carrying"; server -> client once at join as "this is
    /// what you left here". The payload is the save file's own player block as
    /// JSON, so the wire format cannot drift away from the save format.
    ///
    /// PlayerId is advisory on the way UP: the server uses its own connection
    /// table instead, so a client cannot write over somebody else's record.</summary>
    public struct PlayerStateBroadcast : IBroadcast
    {
        public string PlayerId;
        public string Json;
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
            if (Instance != null && Instance != this)
            {
                // The survivor is whichever Network object got here first, and
                // it outlives scene loads. Say so: a duplicate in the scene the
                // player is standing in is silently discarded, and if THAT is
                // the one carrying the avatar prefab, nothing will spawn.
                Debug.LogWarning($"[NetworkBootstrap] A second Network object in scene " +
                                 $"'{gameObject.scene.name}' was discarded - the one from " +
                                 $"'{Instance.gameObject.scene.name}' is already live and persists " +
                                 "across scene loads. Keep exactly one, in the game scene.");
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _networkManager = GetComponent<NetworkManager>();
            Debug.Log($"[NetworkBootstrap] live from scene '{gameObject.scene.name}', " +
                      $"avatar prefab {(avatarPrefab != null ? "assigned" : "MISSING")}.");
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
            _networkManager.ServerManager.RegisterBroadcast<PlayerStateBroadcast>(OnServerPlayerState);
            _networkManager.ServerManager.RegisterBroadcast<ChatBroadcast>(OnServerChat);
            _networkManager.ServerManager.RegisterBroadcast<VoiceBroadcast>(OnServerVoice);
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
            _networkManager.ClientManager.RegisterBroadcast<ChatRelayBroadcast>(OnClientChat);
            _networkManager.ClientManager.RegisterBroadcast<VoiceRelayBroadcast>(OnClientVoice);
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
            _networkManager.ClientManager.RegisterBroadcast<PlayerStateBroadcast>(OnClientPlayerState);

            // Container-contents poller (14.10.0) - idles while offline.
            if (GetComponent<ContainerSyncManager>() == null)
                gameObject.AddComponent<ContainerSyncManager>();
            // Machine-runtime poller (14.12.0) - same pattern, slower cadence.
            if (GetComponent<MachineSyncManager>() == null)
                gameObject.AddComponent<MachineSyncManager>();
            // Proximity voice (14.20.0) - idles completely while offline or
            // while the player has voice turned off.
            if (GetComponent<VoiceChat>() == null)
                gameObject.AddComponent<VoiceChat>();
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
            _networkManager.ServerManager.UnregisterBroadcast<PlayerStateBroadcast>(OnServerPlayerState);
            _networkManager.ServerManager.UnregisterBroadcast<ChatBroadcast>(OnServerChat);
            _networkManager.ServerManager.UnregisterBroadcast<VoiceBroadcast>(OnServerVoice);
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
            _networkManager.ClientManager.UnregisterBroadcast<ChatRelayBroadcast>(OnClientChat);
            _networkManager.ClientManager.UnregisterBroadcast<VoiceRelayBroadcast>(OnClientVoice);
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
            _networkManager.ClientManager.UnregisterBroadcast<PlayerStateBroadcast>(OnClientPlayerState);
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

        /// <summary>Seconds to wait for the host's world card before giving up.
        /// Generous: a cold host has to open its world and answer.</summary>
        private const float JoinTimeoutSeconds = 20f;

        private bool _autoJoinAttempted;

        /// <summary>Watches for a pending main-menu join.
        ///
        /// 14.23.2 - this MUST NOT hang off Start(). FishNet's NetworkManager
        /// marks itself DontDestroyOnLoad, so the Network object outlives every
        /// scene change and its Awake/Start run exactly ONCE per play session,
        /// in whichever scene it first appeared; the copy sitting in the next
        /// scene is destroyed as a duplicate by the Awake guard above. Start()
        /// therefore fired before the player had chosen anything, found no
        /// pending join, and was never called again - which is why the client
        /// sat on "Connecting..." forever with nothing having been asked to
        /// connect, and why not one [Join] line reached either console.
        /// Polling here costs two field reads a frame and cannot be
        /// out-ordered by a scene load, a duplicate or an execution order.</summary>
        private void Update()
        {
            var pending = VoxelEngine.Menu.WorldSession.Instance;
            if (pending == null || !pending.IsRemoteJoin)
            {
                _autoJoinAttempted = false;   // back in the menu: armed for the next one
                _returningToMenu = false;
                return;
            }
            if (_autoJoinAttempted || pending.hostWorldAdopted || IsOnline) return;
            TryAutoJoin();
        }

        private void LateUpdate()
        {
            // Guest -> host state upload. Offline and hosting both skip on the
            // first condition, so this costs one bool test a frame in the cases
            // that are not multiplayer at all.
            if (!_clientStarted || _serverStarted) return;
            if (Time.unscaledTime < _nextPlayerStateUploadAt) return;
            _nextPlayerStateUploadAt = Time.unscaledTime + PlayerStateUploadSeconds;
            UploadLocalPlayerState();
        }

        /// <summary>Connect straight away when the player chose a host in the
        /// main menu. No-op in every other case.</summary>
        private void TryAutoJoin()
        {
            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session == null || !session.IsRemoteJoin || session.hostWorldAdopted) return;
            if (_autoJoinAttempted) return;
            _autoJoinAttempted = true;

            Debug.Log($"[Join] 1/6 auto-connecting to {session.pendingJoinAddress} with world generation held.");
            VoxelEngine.Menu.WorldBootGate.Report($"Connecting to {session.pendingJoinAddress}...");
            StartClient(session.pendingJoinAddress);
            StartCoroutine(JoinWatchdog());
        }

        /// <summary>A join that never answers must not leave the player in an
        /// empty grey room forever - fail it with something readable.</summary>
        private IEnumerator JoinWatchdog()
        {
            float deadline = Time.unscaledTime + JoinTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (!VoxelEngine.Menu.WorldBootGate.IsPending) yield break;  // adopted, or already failed
                yield return null;
            }
            if (!VoxelEngine.Menu.WorldBootGate.IsPending) yield break;

            VoxelEngine.Menu.WorldBootGate.Fail(
                "No answer from the host. Check the address and that they are hosting, " +
                "and that port forwarding is open on their side.");
            _statusLine = "Join timed out";
            StopSession();
        }

        /// <summary>Leave the session (client) or shut it down (host).</summary>
        public void StopSession()
        {
            // Last word before hanging up: leaving with a pickaxe swing
            // unreported is a bug the player would blame on the save.
            UploadLocalPlayerState();
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
                if (_serverStarted) { _statusLine = "Hosting"; return; }

                GoOffline();

                // A guest who joined from the main menu has no world of their
                // own to fall back into - the one they are standing in belongs
                // to a host who is no longer there. Leaving them in it was the
                // "host quit and nothing happened" fault: send them home.
                var session = VoxelEngine.Menu.WorldSession.Instance;
                if (session != null && session.IsRemoteJoin) ReturnGuestToMenu("The host closed the session.");
            }
        }

        /// <summary>Tear down a guest session and go back to the main menu.
        /// Only ever called for a client that joined from the menu.</summary>
        private void ReturnGuestToMenu(string reason)
        {
            if (_returningToMenu) return;
            _returningToMenu = true;
            Debug.Log("[Join] returning to the main menu: " + reason);

            var session = VoxelEngine.Menu.WorldSession.Instance;
            if (session != null) session.ClearRemoteJoin();
            else VoxelEngine.Menu.WorldBootGate.Reset();

            VoxelEngine.UI.UIState.ClearSceneBlocks();
            Time.timeScale = 1f;

            string menuScene = "MainMenu";
            var pause = FindAnyObjectByType<VoxelEngine.Menu.InGamePauseMenu>(FindObjectsInactive.Include);
            if (pause != null && !string.IsNullOrEmpty(pause.mainMenuScene)) menuScene = pause.mainMenuScene;

            try { UnityEngine.SceneManagement.SceneManager.LoadScene(menuScene); }
            catch (System.Exception ex) { Debug.LogError("[Join] could not load the menu scene: " + ex.Message); }
        }

        private bool _returningToMenu;

        /// <summary>Closing the game must hang up properly. Without this the
        /// host's process just vanishes and every client sits in a world
        /// nobody is serving until the transport finally times out.</summary>
        private void OnApplicationQuit()
        {
            if (IsOnline) StopSession();
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
                if (session == null)
                {
                    Debug.LogError("[Join] HOST has no WorldSession - cannot describe this world to the " +
                                   "joining client, so they will never be able to build it.");
                }
                else
                {
                    string card = session.ExportWorldCardJson();
                    Debug.Log($"[Join] host sending world card for '{session.worldName}' " +
                              $"(seed {session.seed}, {card.Length} chars) to client {connection.ClientId}.");
                    _networkManager.ServerManager.Broadcast(connection, new WorldInfoBroadcast
                    {
                        WorldName = session.worldName,
                        Seed = session.seed,
                        WorldCard = card,
                    }, true);
                }

                // 14.24.0 - and what this player left here last time. Sent even
                // when empty: "I have never seen you" is a real answer, and the
                // joining client waits for one rather than guessing.
                // Keyed by the id the SERVER settled on, not the one the client
                // claimed - a duplicate identity is renamed above, and reading
                // the record under the claimed name would hand a guest somebody
                // else's inventory.
                string stored = VoxelEngine.Persistence.PlayerRecords.Get(playerId);
                Debug.Log($"[Join] host sending player record for '{playerId}': " +
                          (string.IsNullOrEmpty(stored) ? "none on file (first visit)." : stored.Length + " chars."));
                _networkManager.ServerManager.Broadcast(connection, new PlayerStateBroadcast
                { PlayerId = playerId, Json = stored ?? "" }, true);
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
        // ─────────────────────────── proximity text chat (14.19.0) ───────────────────────────

        /// <summary>How far words carry, in metres. Phase 2 (proximity voice)
        /// will reuse this range as its shout radius.</summary>
        public const float ChatRange = 60f;

        /// <summary>Send one chat line from the local player. Works as host or
        /// as client; offline there is nobody to talk to, so it is a no-op.</summary>
        public void SendChatMessage(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || _networkManager == null) return;
            if (text.Length > 240) text = text.Substring(0, 240);
            if (_serverStarted) ServerDistributeChat(null, text);
            else if (NetworkSession.Mode != SessionMode.Offline)
                _networkManager.ClientManager.Broadcast(new ChatBroadcast { Text = text });
        }

        private void OnServerChat(NetworkConnection conn, ChatBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn == null) return;
            var text = (msg.Text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 240) text = text.Substring(0, 240);
            ServerDistributeChat(conn, text);
        }

        /// <summary>Server-side distribution with the proximity rule: only
        /// players whose avatars stand within ChatRange of the speaker hear
        /// the words. A missing avatar (mid-spawn) errs on delivering - a
        /// swallowed message is worse than a loud one. Sender null = the host
        /// itself is speaking.</summary>
        private void ServerDistributeChat(NetworkConnection sender, string text)
        {
            string name;
            Vector3 pos;
            bool hasPos = TryGetChatSource(sender, out name, out pos);
            var relay = new ChatRelayBroadcast { SenderName = name, Text = text };

            // The host is a listener too (its own messages are locally echoed
            // by the overlay, so only remote senders are shown here).
            if (sender != null)
            {
                var cam = Camera.main;
                if (!hasPos || cam == null
                    || (cam.transform.position - pos).sqrMagnitude <= ChatRange * ChatRange)
                    VoxelEngine.UI.ChatOverlay.AddMessage(name, text);
            }

            foreach (var pair in _networkManager.ServerManager.Clients)
            {
                var client = pair.Value;
                if (client == null || client.IsLocalClient) continue;
                if (sender != null && client == sender) continue;
                if (hasPos && _avatarsByConnection.TryGetValue(client.ClientId, out var go) && go != null
                    && (go.transform.position - pos).sqrMagnitude > ChatRange * ChatRange) continue;
                _networkManager.ServerManager.Broadcast(client, relay, true);
            }
        }

        private bool TryGetChatSource(NetworkConnection sender, out string name, out Vector3 pos)
        {
            name = sender == null ? PlayerIdentity.LocalName : ("Crusader " + sender.ClientId);
            pos = Vector3.zero;
            bool hasPos = false;
            int clientId = -1;
            if (sender != null) clientId = sender.ClientId;
            else if (_networkManager.ClientManager.Connection != null)
                clientId = _networkManager.ClientManager.Connection.ClientId;
            if (clientId >= 0 && _avatarsByConnection.TryGetValue(clientId, out var go) && go != null)
            {
                pos = go.transform.position;
                hasPos = true;
                var avatar = go.GetComponent<PlayerAvatar>();
                if (avatar != null && !string.IsNullOrEmpty(avatar.PlayerName)) name = avatar.PlayerName;
            }
            if (sender == null)
            {
                // The host's truest position is its own camera (the avatar
                // mirrors it, but the camera never lags).
                var cam = Camera.main;
                if (cam != null) { pos = cam.transform.position; hasPos = true; }
                name = PlayerIdentity.LocalName;
            }
            return hasPos;
        }

        private void OnClientChat(ChatRelayBroadcast msg, Channel channel)
        {
            if (_serverStarted) return;   // the host was shown via the server path
            VoxelEngine.UI.ChatOverlay.AddMessage(msg.SenderName, msg.Text);
        }

        // ─────────────────────────── proximity voice (14.20.0) ───────────────────────────

        /// <summary>How far a voice carries, in metres. Deliberately the same
        /// radius as text chat: one proximity rule the player can learn once.</summary>
        public const float VoiceRange = ChatRange;

        /// <summary>Send one encoded voice frame. Unreliable by design - a
        /// re-sent 40 ms of speech would arrive far too late to be useful, and
        /// every frame decodes on its own.</summary>
        public void SendVoiceFrame(byte[] data, int length, ushort sequence)
        {
            if (data == null || length <= 0 || _networkManager == null) return;
            if (length > VoiceCodec.MaxPacketBytes) return;

            // The wire struct owns its array; copy out exactly the used bytes.
            if (_voiceWire == null || _voiceWire.Length != length) _voiceWire = new byte[length];
            System.Array.Copy(data, _voiceWire, length);

            if (_serverStarted) ServerDistributeVoice(null, _voiceWire, sequence);
            else if (_clientStarted)
                _networkManager.ClientManager.Broadcast(
                    new VoiceBroadcast { Data = _voiceWire, Sequence = sequence }, Channel.Unreliable);
        }

        /// <summary>Reused send buffer - Broadcast serializes synchronously, so
        /// one array is enough and voice costs no per-frame garbage.</summary>
        private byte[] _voiceWire;

        private void OnServerVoice(NetworkConnection conn, VoiceBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn == null) return;
            if (msg.Data == null || msg.Data.Length == 0) return;
            if (msg.Data.Length > VoiceCodec.MaxPacketBytes) return;   // malformed or hostile
            ServerDistributeVoice(conn, msg.Data, msg.Sequence);
        }

        /// <summary>Same proximity rule as text chat, same reasoning: only
        /// players standing within VoiceRange of the speaker are sent the
        /// frame, so a voice never travels further than the server allows it
        /// to - the range is not a client-side volume trick.</summary>
        private void ServerDistributeVoice(NetworkConnection sender, byte[] data, ushort sequence)
        {
            string name;
            Vector3 pos;
            bool hasPos = TryGetChatSource(sender, out name, out pos);
            string senderId = sender == null
                ? PlayerIdentity.LocalId
                : (_playerIdByConnection.TryGetValue(sender.ClientId, out var id) ? id : null);
            if (string.IsNullOrEmpty(senderId)) return;   // pre-handshake: nobody to attribute it to

            // The host hears remote speakers through the server path.
            if (sender != null)
            {
                var cam = Camera.main;
                if (!hasPos || cam == null
                    || (cam.transform.position - pos).sqrMagnitude <= VoiceRange * VoiceRange)
                    VoiceChat.Deliver(senderId, name, data, data.Length, sequence);
            }

            var relay = new VoiceRelayBroadcast
            { SenderId = senderId, SenderName = name, Data = data, Sequence = sequence };

            foreach (var pair in _networkManager.ServerManager.Clients)
            {
                var client = pair.Value;
                if (client == null || client.IsLocalClient) continue;
                if (sender != null && client == sender) continue;
                if (hasPos && _avatarsByConnection.TryGetValue(client.ClientId, out var go) && go != null
                    && (go.transform.position - pos).sqrMagnitude > VoiceRange * VoiceRange) continue;
                _networkManager.ServerManager.Broadcast(client, relay, true, Channel.Unreliable);
            }
        }

        private void OnClientVoice(VoiceRelayBroadcast msg, Channel channel)
        {
            if (_serverStarted) return;   // the host already heard it on the server path
            if (msg.Data == null || msg.Data.Length == 0) return;
            VoiceChat.Deliver(msg.SenderId, msg.SenderName, msg.Data, msg.Data.Length, msg.Sequence);
        }

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

            // ── main-menu join (14.23.0) ──────────────────────────────
            //
            // The player picked an address, not a save. World generation has
            // been held since the scene loaded; this message is what releases
            // it. There is nothing to mismatch - we adopt the host's world
            // wholesale - so the seed warning below is skipped entirely.
            if (session != null && session.IsRemoteJoin && !session.hostWorldAdopted)
            {
                Debug.Log($"[Join] 2/6 world info received from host: '{msg.WorldName}', seed {msg.Seed}, " +
                          $"card {(string.IsNullOrEmpty(msg.WorldCard) ? "MISSING" : msg.WorldCard.Length + " chars")}.");

                if (string.IsNullOrEmpty(msg.WorldCard) || !session.AdoptWorldCardJson(msg.WorldCard))
                {
                    VoxelEngine.Menu.WorldBootGate.Fail(
                        "This host is running an older version that cannot share its world. " +
                        "Ask them to update, or load a matching save and join from the pause menu.");
                    _statusLine = "Join failed - host too old";
                    StopSession();
                    return;
                }

                WorldMismatch = false;
                HostWorldLine = $"Host world: '{session.hostWorldDisplayName}', seed {session.seed}";
                VoxelEngine.Menu.WorldBootGate.Report("Building " + session.hostWorldDisplayName + "...");
                Debug.Log($"[Join] 3/6 world card adopted: system '{session.chosenSystemName}', " +
                          $"seed {session.seed}, spawn planet {session.spawnPlanetIndex}, " +
                          $"seed table {(session.seedState != null ? "present" : "MISSING")}.");

                // Generate the host's planet, then ask for everything built on
                // it. Order matters: the world must exist before snapshots land.
                VoxelEngine.Menu.WorldBootGate.Open();
                if (VoxelEngine.Cosmos.CosmosBootstrap.Instance != null)
                    VoxelEngine.Cosmos.CosmosBootstrap.Instance.BootWorld();

                _networkManager.ClientManager.Broadcast(new WorldAckBroadcast { SeedMatches = true });
                StartSnapshotStream(null);
                _statusLine = "Connected to " + session.hostWorldDisplayName;
                Debug.Log("[Join] 6/6 handshake acknowledged - requesting the host's base and terrain.");
                return;
            }

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
            if (!WorldMismatch) StartSnapshotStream(null);
        }

        // ───────────── per-player state (14.24.0, milestone 8c) ─────────────

        /// <summary>Seconds between a guest telling the host what it is
        /// carrying. Frequent enough that a crash costs a few swings of a
        /// pickaxe, rare enough to be invisible: the record is a few hundred
        /// bytes on a reliable channel.</summary>
        private const float PlayerStateUploadSeconds = 10f;

        private float _nextPlayerStateUploadAt;

        /// <summary>Server: a guest reported its state. The id is taken from
        /// the CONNECTION, never from the message - that is the whole reason
        /// one client cannot overwrite another client's inventory.</summary>
        private void OnServerPlayerState(NetworkConnection conn, PlayerStateBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn == null || conn.IsLocalClient) return;
            if (string.IsNullOrEmpty(msg.Json)) return;
            if (!_playerIdByConnection.TryGetValue(conn.ClientId, out string playerId)) return;
            if (string.IsNullOrEmpty(playerId)) return;
            VoxelEngine.Persistence.PlayerRecords.Store(playerId, msg.Json);
        }

        /// <summary>Client: the host has told us what we left in this world.</summary>
        private void OnClientPlayerState(PlayerStateBroadcast msg, Channel channel)
        {
            if (_serverStarted) return;   // a host is not a guest in its own world
            VoxelEngine.Persistence.PlayerRecords.ReceiveLocalRecord(msg.Json);
        }

        /// <summary>Client: send the host what we are carrying. Called on a
        /// timer and again on the way out, so the host's copy is never more
        /// than one interval behind what actually happened.</summary>
        private void UploadLocalPlayerState()
        {
            if (!_clientStarted || _serverStarted) return;
            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return;
            string json = persistence.CaptureLocalPlayerJson();
            if (string.IsNullOrEmpty(json)) return;   // mid-load or mid-teleport: say nothing
            _networkManager.ClientManager.Broadcast(new PlayerStateBroadcast
            { PlayerId = PlayerIdentity.LocalId, Json = json });
        }

        /// <summary>Server: seed-matching client acknowledged - send it the base.</summary>
        private void OnWorldAck(NetworkConnection conn, WorldAckBroadcast msg, Channel channel)
        {
            if (!_serverStarted || conn.IsLocalClient) return;
            if (!msg.SeedMatches) return;
            StartSnapshotStream(conn);
        }

        // ── staged join catch-up (14.21.1) ──────────────────────────────
        //
        // This used to be six full-world gathers plus every resulting
        // broadcast, all inside ONE frame. On a built-up world that is the
        // 5-10 second freeze the host saw the moment somebody knocked: the
        // main thread was walking the whole base, every container, every
        // machine, every drop and every edited chunk before it drew again.
        //
        // The work itself is unavoidable - the joiner needs all of it - so it
        // is spread instead: one gather per frame, and a frame break every few
        // broadcasts inside each gather. The join takes the same wall time and
        // the host keeps rendering through it.

        /// <summary>Broadcasts between frame breaks, minus one (power of two).</summary>
        private const int SnapshotYieldMask = 3;

        private Coroutine _snapshotStream;
        private readonly Queue<NetworkConnection> _snapshotQueue = new Queue<NetworkConnection>();

        /// <summary>Queue a full catch-up for one target (null = upload to the
        /// host). Two joiners arriving together are served one after the other
        /// rather than interleaving six gathers each.</summary>
        private void StartSnapshotStream(NetworkConnection target)
        {
            _snapshotQueue.Enqueue(target);
            if (_snapshotStream == null) _snapshotStream = StartCoroutine(SnapshotStreamLoop());
        }

        private IEnumerator SnapshotStreamLoop()
        {
            while (_snapshotQueue.Count > 0)
            {
                var target = _snapshotQueue.Dequeue();
                // A client can disconnect mid-catch-up; broadcasting at a dead
                // connection is wasted serialization, so re-check each phase.
                yield return StartCoroutine(SendBaseSnapshot(target));
                if (!StillWorthSending(target)) continue;
                yield return StartCoroutine(SendBlockSnapshot(target));
                if (!StillWorthSending(target)) continue;
                yield return StartCoroutine(SendContainerSnapshot(target));
                if (!StillWorthSending(target)) continue;
                yield return StartCoroutine(SendMachineSnapshot(target));
                if (!StillWorthSending(target)) continue;
                yield return StartCoroutine(SendDropSnapshot(target));
                if (!StillWorthSending(target)) continue;
                yield return StartCoroutine(SendTerrainSnapshot(target));
            }
            _snapshotStream = null;
        }

        private bool StillWorthSending(NetworkConnection target)
        {
            if (target == null) return _clientStarted;      // uploading to the host
            return _serverStarted
                   && _networkManager.ServerManager.Clients.ContainsKey(target.ClientId);
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
        private IEnumerator SendBlockSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 32;
            var blocks = BlockSync.GatherSnapshot();
            if (blocks.Count == 0) yield break;
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
                if ((i & SnapshotYieldMask) == SnapshotYieldMask) yield return null;
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
        private IEnumerator SendContainerSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 16;
            var records = ContainerSync.GatherSnapshot();
            if (records.Count == 0) yield break;
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
                if ((i & SnapshotYieldMask) == SnapshotYieldMask) yield return null;
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
        private IEnumerator SendMachineSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 16;
            var records = MachineSync.GatherSnapshot();
            if (records.Count == 0) yield break;
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
                if ((i & SnapshotYieldMask) == SnapshotYieldMask) yield return null;
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
        private IEnumerator SendDropSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 32;
            var records = DropSync.GatherSnapshot();
            if (records.Count == 0) yield break;
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
                if ((i & SnapshotYieldMask) == SnapshotYieldMask) yield return null;
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
        private IEnumerator SendTerrainSnapshot(NetworkConnection target)
        {
            var chunks = TerrainSync.GatherWireChunks();
            if (chunks.Count == 0) yield break;
            string body = TerrainSync.CurrentBodyName();
            int sent = 0;
            foreach (var chunk in chunks)
            {
                var msg = new TerrainChunkBroadcast
                {
                    Body = body, X = chunk.Coord.x, Y = chunk.Coord.y, Z = chunk.Coord.z,
                    Data = chunk.Compressed
                };
                if (target != null) _networkManager.ServerManager.Broadcast(target, msg, true);
                else _networkManager.ClientManager.Broadcast(msg);
                if ((++sent & SnapshotYieldMask) == SnapshotYieldMask) yield return null;
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
        private IEnumerator SendBaseSnapshot(NetworkConnection target)
        {
            const int ChunkSize = 32;   // comfortably inside a reliable packet
            var pieces = BuildingSync.GatherSnapshot();
            if (pieces.Count == 0) yield break;
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
                if ((i & SnapshotYieldMask) == SnapshotYieldMask) yield return null;
            }
        }

        // ─────────────────────────── teardown ───────────────────────────

        private void GoOffline()
        {
            // Nothing left to catch up to.
            _snapshotQueue.Clear();
            if (_snapshotStream != null) { StopCoroutine(_snapshotStream); _snapshotStream = null; }

            // Dropped before the world arrived: say so instead of leaving the
            // join overlay spinning on a connection that no longer exists.
            if (VoxelEngine.Menu.WorldBootGate.IsPending)
                VoxelEngine.Menu.WorldBootGate.Fail("Lost the connection to the host before the world arrived.");

            NetworkSession.SetMode(SessionMode.Offline);
            VoxelEngine.Persistence.PlayerRecords.ClearLocal();
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
