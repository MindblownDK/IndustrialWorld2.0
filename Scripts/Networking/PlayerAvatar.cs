// Assets/Scripts/VoxelEngine/Networking/PlayerAvatar.cs
//
// 14.1.0-dev - Multiplayer Foundation, part 2.
// 14.1.2-dev - identity applied after spawn, change-driven registration.
// 14.2.0-dev - avatars come alive: held item + crouch replication.
//
// The networked body of one player. The server spawns one per connection,
// FishNet's NetworkTransform replicates its movement, and this class:
//   1. mirrors the OWNING player's local rig into the avatar every frame,
//   2. keeps NetworkSession's presence registry in step with the identity
//      SyncVars - whenever they arrive, and whenever they change (rename),
//   3. renders a nameplate that faces whoever is looking,
//   4. shows what the player is DOING: the active hotbar item rides in a
//      hand anchor (same procedural models as the first-person viewmodel),
//      and crouching squashes the body.
// Pose flows owner -> ServerRpc -> SyncVars -> everyone, so the server stays
// the single relay and late joiners get current values in the spawn payload.
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
        private const float CrouchFactor = 0.62f;   // 1.85 m -> ~1.15 m, matches the controller's crouch height

        private readonly SyncVar<string> _playerId = new SyncVar<string>();
        private readonly SyncVar<string> _playerName = new SyncVar<string>();
        private readonly SyncVar<string> _heldItemId = new SyncVar<string>();
        private readonly SyncVar<bool> _crouched = new SyncVar<bool>();

        [Tooltip("Nameplate above the head. Assigned by Setup Step 105.")]
        public TextMesh nameplate;

        [Header("Hand (third-person held item)")]
        public Vector3 handLocalPosition = new Vector3(0.45f, 1.25f, 0.35f);
        public Vector3 handLocalEuler = new Vector3(10f, -20f, 0f);

        /// <summary>The id this avatar registered into NetworkSession, so it
        /// always unregisters exactly what it registered.</summary>
        private string _registeredId;

        // visuals
        private Transform _hand;
        private GameObject _heldModel;
        private Transform _body;
        private Transform _visor;
        private Vector3 _bodyStandPos, _bodyStandScale, _visorStandPos;
        private bool _poseCached;

        // owner-side mirrors
        private VoxelEngine.Items.Inventory _inventory;
        private VoxelEngine.Player.PlayerController _controller;
        private string _sentHeldItemId;
        private bool _sentCrouched;

        public string PlayerId => _playerId.Value;
        public string PlayerName => _playerName.Value;

        private void Awake()
        {
            _playerId.OnChange += OnIdChanged;
            _playerName.OnChange += OnNameChanged;
            _heldItemId.OnChange += OnHeldItemChanged;
            _crouched.OnChange += OnCrouchedChanged;
        }

        private void OnDestroy()
        {
            _playerId.OnChange -= OnIdChanged;
            _playerName.OnChange -= OnNameChanged;
            _heldItemId.OnChange -= OnHeldItemChanged;
            _crouched.OnChange -= OnCrouchedChanged;
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
            else
            {
                // Late joiners: current pose arrived with the spawn payload.
                ApplyHeldItem(_heldItemId.Value);
                ApplyCrouch(_crouched.Value);
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

            MirrorPose(stats);
        }

        /// <summary>Owner-side: watch the local hotbar and stance, and tell
        /// the server only when something actually changes.</summary>
        private void MirrorPose(VoxelEngine.Player.PlayerStats stats)
        {
            if (_inventory == null) _inventory = stats.GetComponent<VoxelEngine.Items.Inventory>();
            if (_controller == null) _controller = stats.GetComponent<VoxelEngine.Player.PlayerController>();

            string held = "";
            if (_inventory != null)
            {
                var stack = _inventory.ActiveStack;
                if (stack != null && stack.item != null) held = stack.item.itemId ?? "";
            }
            bool crouched = _controller != null && (_controller.IsCrouched || _controller.IsSliding);

            if (held == _sentHeldItemId && crouched == _sentCrouched) return;
            _sentHeldItemId = held;
            _sentCrouched = crouched;
            RpcUpdatePose(held, crouched);
        }

        [ServerRpc]
        private void RpcUpdatePose(string heldItemId, bool crouched)
        {
            _heldItemId.Value = heldItemId ?? "";
            _crouched.Value = crouched;
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

        // ─────────────────────────── pose visuals ───────────────────────────

        private void OnHeldItemChanged(string previous, string next, bool asServer)
        {
            if (asServer || IsOwner) return;   // host applies on its client pass; owners are invisible to themselves
            ApplyHeldItem(next);
        }

        private void OnCrouchedChanged(bool previous, bool next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplyCrouch(next);
        }

        private void ApplyHeldItem(string itemId)
        {
            if (_heldModel != null) { Destroy(_heldModel); _heldModel = null; }
            if (string.IsNullOrEmpty(itemId)) return;

            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            var item = persistence != null ? persistence.FindItemById(itemId) : null;
            if (item == null) return;   // unknown on this side - show empty hands

            EnsureHand();
            _heldModel = VoxelEngine.Player.HeldToolView.BuildViewmodelFor(item);
            _heldModel.transform.SetParent(_hand, false);
            // Display only: held models must never collide with the world or
            // swallow interaction rays.
            foreach (var col in _heldModel.GetComponentsInChildren<Collider>(true))
                Destroy(col);
        }

        private void ApplyCrouch(bool crouched)
        {
            CachePose();
            float f = crouched ? CrouchFactor : 1f;
            if (_body != null)
            {
                _body.localScale = new Vector3(_bodyStandScale.x, _bodyStandScale.y * f, _bodyStandScale.z);
                _body.localPosition = new Vector3(_bodyStandPos.x, _bodyStandPos.y * f, _bodyStandPos.z);
            }
            if (_visor != null)
                _visor.localPosition = new Vector3(_visorStandPos.x, _visorStandPos.y * f, _visorStandPos.z);
            if (_hand != null)
                _hand.localPosition = new Vector3(handLocalPosition.x, handLocalPosition.y * f, handLocalPosition.z);
        }

        private void EnsureHand()
        {
            if (_hand != null) return;
            var go = new GameObject("HandAnchor");
            _hand = go.transform;
            _hand.SetParent(transform, false);
            _hand.localPosition = handLocalPosition;
            _hand.localRotation = Quaternion.Euler(handLocalEuler);
        }

        private void CachePose()
        {
            if (_poseCached) return;
            _poseCached = true;
            _body = transform.Find("Body");
            _visor = transform.Find("Visor");
            if (_body != null) { _bodyStandPos = _body.localPosition; _bodyStandScale = _body.localScale; }
            if (_visor != null) _visorStandPos = _visor.localPosition;
        }
    }
}
