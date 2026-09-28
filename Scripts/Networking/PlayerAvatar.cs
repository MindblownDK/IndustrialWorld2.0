// Assets/Scripts/VoxelEngine/Networking/PlayerAvatar.cs
//
// 14.1.0-dev - Multiplayer Foundation, part 2.
//
// The networked body of one player. The server spawns one per connection
// (identity baked in before spawn, so it arrives with the object), FishNet's
// NetworkTransform replicates its movement, and this class does three small
// jobs on top:
//   1. mirror the OWNING player's local rig into the avatar every frame,
//   2. keep NetworkSession's presence registry in step with spawn/despawn,
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

        private bool _registered;

        public string PlayerId => _playerId.Value;
        public string PlayerName => _playerName.Value;

        private void Awake()
        {
            _playerName.OnChange += OnNameChanged;
        }

        private void OnDestroy()
        {
            _playerName.OnChange -= OnNameChanged;
        }

        /// <summary>Server-only, called BEFORE Spawn so the identity ships
        /// inside the spawn payload and is readable in OnStartClient.</summary>
        public void SetIdentity(string playerId, string playerName)
        {
            _playerId.Value = playerId;
            _playerName.Value = string.IsNullOrEmpty(playerName) ? "Crusader" : playerName;
        }

        // ─────────────────────────── network lifecycle ───────────────────────────

        public override void OnStartClient()
        {
            base.OnStartClient();
            Register();
            if (nameplate != null) nameplate.text = _playerName.Value;

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
        // both fire; Register/Unregister are idempotent.

        public override void OnStartServer()
        {
            base.OnStartServer();
            Register();
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

        private void Register()
        {
            if (_registered || string.IsNullOrEmpty(_playerId.Value)) return;
            _registered = true;
            NetworkSession.RegisterPlayer(_playerId.Value, _playerName.Value);
        }

        private void Unregister()
        {
            if (!_registered) return;
            _registered = false;
            NetworkSession.UnregisterPlayer(_playerId.Value);
        }

        private void OnNameChanged(string previous, string next, bool asServer)
        {
            if (nameplate != null) nameplate.text = next;
        }
    }
}
