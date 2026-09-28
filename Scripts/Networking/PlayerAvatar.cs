// Assets/Scripts/VoxelEngine/Networking/PlayerAvatar.cs
//
// 14.1.0-dev - Multiplayer Foundation, part 2. (14.1.2-dev: identity is now
// applied AFTER spawn and everything reacts to SyncVar changes, so names and
// roster entries can never be missed by a race again.)
//
// The networked body of one player. The server spawns one per connection,
// FishNet's NetworkTransform replicates its movement, and this class does
// three small jobs on top:
//   1. mirror the OWNING player's local rig into the avatar every frame,
//   2. keep NetworkSession's presence registry in step with the identity
//      SyncVars - whenever they arrive, and whenever they change (rename),
//   3. render a nameplate that faces whoever is looking.
// The owner never sees their own avatar - renderers are disabled locally.
//
// Milestone note: movement replication is owner-authoritative for now (the
// standard NetworkTransform setup). Server validation of movement belongs to
// a later hardening pass, once per-player state (milestone 2) is in.

using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace VoxelEngine.Networking
{
    public class PlayerAvatar : NetworkBehaviour
    {
        private readonly SyncVar<string> _playerId = new SyncVar<string>();
        private readonly SyncVar<string> _playerName = new SyncVar<string>();

        [Tooltip("Nameplate above the head. Assigned by Setup Step 105.")]
        public TextMesh nameplate;

        /// <summary>The id this avatar registered into NetworkSession, so it
        /// always unregisters exactly what it registered.</summary>
        private string _registeredId;

        public string PlayerId => _playerId.Value;
        public string PlayerName => _playerName.Value;

        private void Awake()
        {
            _playerId.OnChange += OnIdChanged;
            _playerName.OnChange += OnNameChanged;
        }

        private void OnDestroy()
        {
            _playerId.OnChange -= OnIdChanged;
            _playerName.OnChange -= OnNameChanged;
            Unregister();   // belt and braces; normally OnStopClient/Server did it
        }

        // ─────────────────────────── server API ───────────────────────────

        /// <summary>Server-only, called right AFTER Spawn. Set post-spawn the
        /// values replicate as ordinary SyncVar updates - reliable for current
        /// observers, and included in the spawn payload for late joiners.</summary>
        public void SetIdentity(string playerId, string playerName)
        {
            _playerId.Value = playerId;
            _playerName.Value = string.IsNullOrEmpty(playerName) ? "Crusader" : playerName;
        }

        /// <summary>Server-only: live rename, replicates to everyone.</summary>
        public void ServerSetName(string playerName)
        {
            if (!string.IsNullOrEmpty(playerName)) _playerName.Value = playerName;
        }

        // ─────────────────────────── network lifecycle ───────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();
            TryRegister();      // late joiners get identity in the spawn payload
            ApplyNameplate();

            if (IsOwner)
            {
                // This is ME - my real first-person rig is my body. Hide the
                // puppet locally so it never pokes into my camera.
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Unregister();
        }

        // Server-side registration keeps the presence list correct on future
        // dedicated servers, where no client callbacks run. On a listen host
        // both fire; TryRegister/Unregister are idempotent.

        public override void OnStartServer()
        {
            base.OnStartServer();
            TryRegister();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Unregister();
        }

        // ─────────────────────────── per-frame ───────────────────────────

        private void Update()
        {
            if (!IsOwner) return;
            var stats = VoxelEngine.Player.PlayerStats.Instance;
            if (stats == null) return;
            var rig = stats.transform;
            transform.SetPositionAndRotation(rig.position, rig.rotation);
        }

        private void LateUpdate()
        {
            if (IsOwner || nameplate == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var plate = nameplate.transform;
            Vector3 toPlate = plate.position - cam.transform.position;
            if (toPlate.sqrMagnitude < 0.0001f) return;
            plate.rotation = Quaternion.LookRotation(toPlate);
        }

        // ─────────────────────────── presence ───────────────────────────

        /// <summary>Registers once the player id is known - at spawn if the
        /// value already arrived, otherwise the moment the SyncVar lands.</summary>
        private void TryRegister()
        {
            if (_registeredId != null) return;
            string id = _playerId.Value;
            if (string.IsNullOrEmpty(id)) return;   // identity not delivered yet - OnIdChanged retries
            _registeredId = id;
            NetworkSession.RegisterPlayer(id, _playerName.Value);
            NetworkSession.UpdateDisplayName(id, _playerName.Value);
        }

        private void Unregister()
        {
            if (_registeredId == null) return;
            NetworkSession.UnregisterPlayer(_registeredId);
            _registeredId = null;
        }

        private void OnIdChanged(string previous, string next, bool asServer)
        {
            TryRegister();
        }

        private void OnNameChanged(string previous, string next, bool asServer)
        {
            ApplyNameplate();
            if (_registeredId != null) NetworkSession.UpdateDisplayName(_registeredId, next);
        }

        private void ApplyNameplate()
        {
            if (nameplate == null) return;
            string name = _playerName.Value;
            nameplate.text = string.IsNullOrEmpty(name) ? "..." : name;
        }
    }
}
