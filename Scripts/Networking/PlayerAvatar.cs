// Assets/Scripts/VoxelEngine/Networking/PlayerAvatar.cs
//
// 14.1.0-dev - Multiplayer Foundation, part 2.
// 14.1.2-dev - identity applied after spawn, change-driven registration.
// 14.2.0-dev - avatars come alive: held item + crouch replication.
// 14.3.0-dev - vitals over the wire: replicated health bar; nameplate
//              billboards against the VIEWER's up (planets are spheres -
//              world up is meaningless away from the pole).
//
// The networked body of one player. The server spawns one per connection,
// FishNet's NetworkTransform replicates its movement, and this class:
//   1. mirrors the OWNING player's local rig into the avatar every frame,
//   2. keeps NetworkSession's presence registry in step with the identity
//      SyncVars - whenever they arrive, and whenever they change (rename),
//   3. renders a nameplate + health bar that face whoever is looking,
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
        private const float BarWidth = 0.90f;

        private readonly SyncVar<string> _playerId = new SyncVar<string>();
        private readonly SyncVar<string> _playerName = new SyncVar<string>();
        private readonly SyncVar<string> _heldItemId = new SyncVar<string>();
        private readonly SyncVar<bool> _crouched = new SyncVar<bool>();
        private readonly SyncVar<int> _healthPct = new SyncVar<int>(100);

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
        private GameObject _healthBar;
        private Transform _healthFill;
        private Renderer _healthFillRenderer;

        // owner-side mirrors
        private VoxelEngine.Items.Inventory _inventory;
        private VoxelEngine.Player.PlayerController _controller;
        private string _sentHeldItemId;
        private bool _sentCrouched;
        private int _sentHealthPct = 100;

        public string PlayerId => _playerId.Value;
        public string PlayerName => _playerName.Value;

        private void Awake()
        {
            _playerId.OnChange += OnIdChanged;
            _playerName.OnChange += OnNameChanged;
            _heldItemId.OnChange += OnHeldItemChanged;
            _crouched.OnChange += OnCrouchedChanged;
            _healthPct.OnChange += OnHealthChanged;
        }

        private void OnDestroy()
        {
            _playerId.OnChange -= OnIdChanged;
            _playerName.OnChange -= OnNameChanged;
            _heldItemId.OnChange -= OnHeldItemChanged;
            _crouched.OnChange -= OnCrouchedChanged;
            _healthPct.OnChange -= OnHealthChanged;
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
                ApplyHealth(_healthPct.Value);
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

        /// <summary>Owner-side: watch the local hotbar, stance and health, and
        /// tell the server only when something actually changes.</summary>
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
            int healthPct = stats.MaxHealth > 0f
                ? Mathf.Clamp(Mathf.RoundToInt(stats.Health / stats.MaxHealth * 100f), 0, 100)
                : 100;

            if (held == _sentHeldItemId && crouched == _sentCrouched && healthPct == _sentHealthPct) return;
            _sentHeldItemId = held;
            _sentCrouched = crouched;
            _sentHealthPct = healthPct;
            RpcUpdatePose(held, crouched, healthPct);
        }

        [ServerRpc]
        private void RpcUpdatePose(string heldItemId, bool crouched, int healthPct)
        {
            _heldItemId.Value = heldItemId ?? "";
            _crouched.Value = crouched;
            _healthPct.Value = Mathf.Clamp(healthPct, 0, 100);
        }

        private void LateUpdate()
        {
            if (IsOwner || nameplate == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var plate = nameplate.transform;
            Vector3 toPlate = plate.position - cam.transform.position;
            if (toPlate.sqrMagnitude < 0.0001f) return;
            // Level against the VIEWER's up: on spherical worlds neither
            // player stands along world up, so only the camera's own up
            // keeps the text horizontal on screen. (Health bar is a child
            // of the plate and inherits this.)
            plate.rotation = Quaternion.LookRotation(toPlate, cam.transform.up);
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

        private void OnHealthChanged(int previous, int next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplyHealth(next);
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

        /// <summary>Rust-style honesty: the bar only appears when hurt.</summary>
        private void ApplyHealth(int pct)
        {
            if (pct >= 100)
            {
                if (_healthBar != null) _healthBar.SetActive(false);
                return;
            }

            EnsureHealthBar();
            _healthBar.SetActive(true);
            float t = Mathf.Clamp01(pct / 100f);
            _healthFill.localScale = new Vector3(BarWidth * t, 0.075f, 1f);
            _healthFill.localPosition = new Vector3(-BarWidth * 0.5f + BarWidth * 0.5f * t, 0f, -0.004f);
            if (_healthFillRenderer != null && _healthFillRenderer.material != null)
                _healthFillRenderer.material.color = Color.Lerp(
                    new Color(0.82f, 0.20f, 0.14f), new Color(0.30f, 0.78f, 0.32f), t);
        }

        // ─────────────────────────── visual helpers ───────────────────────────

        private void EnsureHand()
        {
            if (_hand != null) return;
            var go = new GameObject("HandAnchor");
            _hand = go.transform;
            _hand.SetParent(transform, false);
            _hand.localPosition = handLocalPosition;
            _hand.localRotation = Quaternion.Euler(handLocalEuler);
        }

        /// <summary>Two unlit quads under the nameplate; they inherit its
        /// billboard rotation, so they always face the viewer too.</summary>
        private void EnsureHealthBar()
        {
            if (_healthBar != null) return;
            var parent = nameplate != null ? nameplate.transform : transform;

            _healthBar = new GameObject("HealthBar");
            _healthBar.transform.SetParent(parent, false);
            _healthBar.transform.localPosition = new Vector3(0f, -0.14f, 0f);

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            Transform MakeQuad(string name, Vector3 pos, Vector3 scale, Color color, out Renderer rendererOut)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = name;
                var quadCollider = quad.GetComponent<Collider>();
                if (quadCollider != null) Destroy(quadCollider);
                quad.transform.SetParent(_healthBar.transform, false);
                quad.transform.localPosition = pos;
                quad.transform.localScale = scale;
                rendererOut = quad.GetComponent<Renderer>();
                if (shader != null)
                {
                    var mat = new Material(shader) { color = color };
                    rendererOut.material = mat;
                }
                return quad.transform;
            }

            MakeQuad("Back", Vector3.zero, new Vector3(BarWidth + 0.04f, 0.11f, 1f),
                new Color(0.05f, 0.05f, 0.06f, 1f), out _);
            _healthFill = MakeQuad("Fill", new Vector3(0f, 0f, -0.004f), new Vector3(BarWidth, 0.075f, 1f),
                new Color(0.30f, 0.78f, 0.32f), out _healthFillRenderer);
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
