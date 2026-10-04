// Assets/Scripts/VoxelEngine/Networking/PlayerAvatar.cs
//
// 14.1.0-dev - Multiplayer Foundation, part 2.
// 14.1.2-dev - identity applied after spawn, change-driven registration.
// 14.2.0-dev - avatars come alive: held item + crouch replication.
// 14.3.0-dev - vitals over the wire: replicated health bar; nameplate
//              billboards against the VIEWER's up (planets are spheres -
//              world up is meaningless away from the pole).
// 14.13.0-dev - Real Crusaders: the placeholder capsule gives way to the
//              procedural knight (CrusaderModel), built at runtime on the
//              same prefab - crouch squashes the knight, the held tool
//              rides in its right hand.
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

using System.Collections.Generic;
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
        // 14.14.0: worn armor tier (0 = none -> bare warrior; 1-6 tint the plate rig).
        private readonly SyncVar<int> _armorTier = new SyncVar<int>(0);
        // 14.15.0: chosen skin tone (multiplied over the body; picked in the menu).
        private readonly SyncVar<int> _skinTone = new SyncVar<int>(2);
        // 14.16.0: worn back gear (bit 0 jetpack, bit 1 oxygen tank).
        private readonly SyncVar<int> _equipFlags = new SyncVar<int>(0);
        // 14.16.0: the building preview, mirrored: item id ("" = none) + ghost pose.
        private readonly SyncVar<string> _ghostItemId = new SyncVar<string>();
        private readonly SyncVar<Vector3> _ghostPos = new SyncVar<Vector3>();
        private readonly SyncVar<Quaternion> _ghostRot = new SyncVar<Quaternion>(Quaternion.identity);
        // 14.17.0: motion flags for the locomotion driver (bit 0 = sliding).
        private readonly SyncVar<int> _motionFlags = new SyncVar<int>(0);
        // 14.18.0: attack counter - every increment is one visible swing.
        private readonly SyncVar<int> _attackCount = new SyncVar<int>(0);

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
        private int _sentArmorTier;
        private int _sentSkinTone = -1;   // sentinel: the first mirror pass always sends
        private int _sentEquipFlags = -1;
        private int _sentMotionFlags = -1;
        private int _sentSwingCount = -1;
        private VoxelEngine.Player.HeldToolView _heldToolView;
        private bool _attackPrimed;   // swallow the initial-state OnChange at join
        private VoxelEngine.Player.PlayerEquipment _equipment;
        private CrusaderAnimator _locomotion;

        /// <summary>The rig's locomotion driver, when the rigged body is in use.</summary>
        private CrusaderAnimator Locomotion()
        {
            if (_locomotion == null)
            {
                var rig = transform.Find(CrusaderModel.RootName + "/" + CrusaderModel.RigName);
                if (rig != null) _locomotion = rig.GetComponent<CrusaderAnimator>();
            }
            return _locomotion;
        }

        public string PlayerId => !string.IsNullOrEmpty(_playerId.Value) ? _playerId.Value : _announcedId;
        public string PlayerName => !string.IsNullOrEmpty(_playerName.Value) ? _playerName.Value : _announcedName;

        // ── identity announce fallback (14.46.0) ─────────────────────────
        //
        // The identity SyncVars are written ONCE, right after Spawn. Pose
        // SyncVars are written continuously, so a missed initial delivery
        // heals itself - identity never did, which is how a dedicated
        // session produced bodies without names: guests saw each other walk
        // but PlayerId stayed empty, so hits were skipped, presences never
        // registered and team members could not be named. The server now
        // ANNOUNCES every avatar's identity over a broadcast (at spawn, on
        // rename, and per-avatar to every joining client), keyed by
        // NetworkObject id. SyncVars remain the fast path; the announce is
        // the guarantee.

        private string _announcedId;
        private string _announcedName;

        /// <summary>Announces that raced ahead of their avatar's spawn wait
        /// here, keyed by object id, and are consumed in OnStartClient.</summary>
        private static readonly Dictionary<int, (string id, string name)> _pendingAnnounce = new();

        public static void CacheAnnounce(int objectId, string playerId, string playerName)
        {
            if (_pendingAnnounce.Count > 64) _pendingAnnounce.Clear();   // stale-proofing
            _pendingAnnounce[objectId] = (playerId, playerName);
        }

        /// <summary>Client-side: apply an announced identity. Idempotent and
        /// SyncVar-friendly - delivered SyncVars always win the properties.</summary>
        public void ApplyAnnouncedIdentity(string playerId, string playerName)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            _announcedId = playerId;
            if (!string.IsNullOrEmpty(playerName)) _announcedName = playerName;
            TryRegister();
            NetworkSession.UpdateDisplayName(PlayerId, PlayerName);
            ApplyNameplate();
        }

        // ── live avatar lookup (14.20.0) ──────────────────────────────────
        // Proximity voice needs "where does player X stand" every frame. A
        // dictionary kept by the existing register/unregister pair costs
        // nothing and replaces a per-frame scene scan.

        private static readonly Dictionary<string, PlayerAvatar> _byPlayerId = new();

        /// <summary>The spawned avatar for a player id, or null when that
        /// player has no body in this scene (yet).</summary>
        public static PlayerAvatar Find(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            if (!_byPlayerId.TryGetValue(playerId, out var avatar)) return null;
            if (avatar == null) { _byPlayerId.Remove(playerId); return null; }
            return avatar;
        }

        /// <summary>Every live avatar in the scene (14.34.0 - combat sweeps
        /// use this instead of physics, since avatar bodies carry no
        /// colliders by design). Destroyed entries are skipped, not yielded.</summary>
        public static IEnumerable<PlayerAvatar> All
        {
            get
            {
                foreach (var kv in _byPlayerId)
                    if (kv.Value != null) yield return kv.Value;
            }
        }

        /// <summary>This player's replicated health, 0-100. What the attacker
        /// side reads to skip a body that is already down.</summary>
        public int HealthPercent => _healthPct.Value;

        /// <summary>Where this player's voice comes out: the head bone when the
        /// rigged body is in use, the primitive head otherwise, and the avatar
        /// root as the last resort. Cached - the bone never moves in hierarchy.</summary>
        public Transform VoiceAnchor()
        {
            if (_voiceAnchor != null) return _voiceAnchor;
            _voiceAnchor = CrusaderModel.FindHeadAnchor(transform);
            if (_voiceAnchor == null) _voiceAnchor = transform;
            return _voiceAnchor;
        }

        private Transform _voiceAnchor;

        private void Awake()
        {
            // The crusader body replaces the placeholder capsule (14.13.0). Built
            // here so it exists before ANY SyncVar callback lands - held item and
            // crouch callbacks can fire before OnStartClient on late joins.
            CrusaderModel.EnsureBuilt(transform);
            _playerId.OnChange += OnIdChanged;
            _playerName.OnChange += OnNameChanged;
            _heldItemId.OnChange += OnHeldItemChanged;
            _crouched.OnChange += OnCrouchedChanged;
            _healthPct.OnChange += OnHealthChanged;
            _armorTier.OnChange += OnArmorChanged;
            _skinTone.OnChange += OnSkinToneChanged;
            _equipFlags.OnChange += OnEquipChanged;
            _ghostItemId.OnChange += OnGhostItemChanged;
            _ghostPos.OnChange += OnGhostPosChanged;
            _ghostRot.OnChange += OnGhostRotChanged;
            _motionFlags.OnChange += OnMotionChanged;
            _attackCount.OnChange += OnAttackChanged;
        }

        private void OnDestroy()
        {
            _playerId.OnChange -= OnIdChanged;
            _playerName.OnChange -= OnNameChanged;
            _heldItemId.OnChange -= OnHeldItemChanged;
            _crouched.OnChange -= OnCrouchedChanged;
            _healthPct.OnChange -= OnHealthChanged;
            _armorTier.OnChange -= OnArmorChanged;
            _skinTone.OnChange -= OnSkinToneChanged;
            _equipFlags.OnChange -= OnEquipChanged;
            _ghostItemId.OnChange -= OnGhostItemChanged;
            _ghostPos.OnChange -= OnGhostPosChanged;
            _ghostRot.OnChange -= OnGhostRotChanged;
            _motionFlags.OnChange -= OnMotionChanged;
            _attackCount.OnChange -= OnAttackChanged;
            if (_ghostReplica != null) { Destroy(_ghostReplica); _ghostReplica = null; }
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
            // An identity announce can land before the spawn it describes -
            // consume the cached one now that the object id resolves.
            if (_pendingAnnounce.TryGetValue(ObjectId, out var announced))
            {
                _pendingAnnounce.Remove(ObjectId);
                ApplyAnnouncedIdentity(announced.id, announced.name);
            }
            TryRegister();      // late joiners get identity in the spawn payload
            ApplyNameplate();

            if (IsOwner)
            {
                // This is ME - my real first-person rig is my body. Hide the
                // puppet locally so it never pokes into my camera.
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
                var ownLocomotion = Locomotion();
                if (ownLocomotion != null) ownLocomotion.StopForOwner();
            }
            else
            {
                // Late joiners: current pose arrived with the spawn payload.
                ApplyHeldItem(_heldItemId.Value);
                ApplyCrouch(_crouched.Value);
                ApplyHealth(_healthPct.Value);
                ApplyArmor(_armorTier.Value);
                ApplySkinTone(_skinTone.Value);
                ApplyBackGear(_equipFlags.Value);
                ApplyGhost(_ghostItemId.Value);
                ApplyMotion(_motionFlags.Value);
                _attackPrimed = true;   // initial state consumed - every change from here is a real swing
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
            // 14.15.3: deadband the mirror. The controller's ground snap makes a
            // standing player's Y micro-oscillate every frame; copying it raw
            // broadcast that shake to everyone else's screen. Only real movement
            // (> ~1.6 cm or > 0.4 deg) moves the networked avatar.
            if ((rig.position - transform.position).sqrMagnitude > 0.00025f
                || Quaternion.Angle(rig.rotation, transform.rotation) > 0.4f)
                transform.SetPositionAndRotation(rig.position, rig.rotation);

            MirrorPose(stats);
            MirrorGhost();
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
            // 14.17.0: crouch and slide are separate now - the slide plays its
            // own animation and must not also squash the model.
            bool sliding = _controller != null && _controller.IsSliding;
            bool crouched = _controller != null && _controller.IsCrouched && !sliding;
            // bit 0 = sliding, bit 1 = shield raised (14.37.0)
            int motionFlags = (sliding ? 1 : 0)
                | (VoxelEngine.Combat.ShieldBlock.Active ? 2 : 0);
            var wornArmor = stats.equippedArmor;
            int armorTier = wornArmor != null ? Mathf.Clamp(wornArmor.tier, 1, 6) : 0;
            int skinTone = PlayerIdentity.LocalSkinTone;
            // 14.18.5: HeldToolView lives on the CAMERA object (setup wizard),
            // not on the player root - GetComponent on the stats object always
            // returned null and the swing counter was never read. Search the
            // children (the camera hangs under the player), with the local
            // main camera as fallback - this is owner-only code.
            if (_heldToolView == null) _heldToolView = stats.GetComponentInChildren<VoxelEngine.Player.HeldToolView>(true);
            if (_heldToolView == null && Camera.main != null)
                _heldToolView = Camera.main.GetComponent<VoxelEngine.Player.HeldToolView>();
            if (_heldToolView != null)
            {
                int swings = _heldToolView.SwingCount;
                if (_sentSwingCount < 0) _sentSwingCount = swings;   // adopt, never replay history
                else if (swings != _sentSwingCount)
                {
                    _sentSwingCount = swings;
                    RpcSwing(swings);
                }
            }
            if (_equipment == null) _equipment = stats.GetComponent<VoxelEngine.Player.PlayerEquipment>();
            int equipFlags = 0;
            if (_equipment != null)
            {
                if (_equipment.GetBestJetpack() != null) equipFlags |= 1;
                if (_equipment.EquippedOxygenTank != null) equipFlags |= 2;
            }
            int healthPct = stats.MaxHealth > 0f
                ? Mathf.Clamp(Mathf.RoundToInt(stats.Health / stats.MaxHealth * 100f), 0, 100)
                : 100;

            if (held == _sentHeldItemId && crouched == _sentCrouched
                && healthPct == _sentHealthPct && armorTier == _sentArmorTier
                && skinTone == _sentSkinTone && equipFlags == _sentEquipFlags
                && motionFlags == _sentMotionFlags) return;
            _sentHeldItemId = held;
            _sentCrouched = crouched;
            _sentHealthPct = healthPct;
            _sentArmorTier = armorTier;
            _sentSkinTone = skinTone;
            _sentEquipFlags = equipFlags;
            _sentMotionFlags = motionFlags;
            RpcUpdatePose(held, crouched, healthPct, armorTier, skinTone, equipFlags, motionFlags);
        }

        [ServerRpc]
        private void RpcUpdatePose(string heldItemId, bool crouched, int healthPct, int armorTier, int skinTone, int equipFlags, int motionFlags)
        {
            _heldItemId.Value = heldItemId ?? "";
            _crouched.Value = crouched;
            _healthPct.Value = Mathf.Clamp(healthPct, 0, 100);
            _armorTier.Value = Mathf.Clamp(armorTier, 0, 6);
            _skinTone.Value = Mathf.Clamp(skinTone, 0, CrusaderModel.SkinToneCount - 1);
            _equipFlags.Value = equipFlags & 3;
            _motionFlags.Value = motionFlags & 3;
        }

        [ServerRpc]
        private void RpcSwing(int count)
        {
            _attackCount.Value = count;
        }

        // ── building-ghost mirror (14.16.0) ──────────────────────────────
        // The preview follows the aim, so it moves nearly every frame; the mirror
        // is throttled to 10 Hz with a 5 cm / 2 deg deadband. Showing or clearing
        // the ghost always sends immediately.

        private float _nextGhostSend;
        private string _sentGhostId = "";
        private Vector3 _sentGhostPos;
        private Quaternion _sentGhostRot = Quaternion.identity;

        private void MirrorGhost()
        {
            var bs = VoxelEngine.Building.BuildSystem.Instance;
            string id = "";
            Vector3 pos = default;
            Quaternion rot = Quaternion.identity;
            if (bs != null && bs.TryGetGhostState(out var gid, out pos, out rot)) id = gid ?? "";

            bool idChanged = id != _sentGhostId;
            if (!idChanged && id.Length == 0) return;
            if (!idChanged && Time.unscaledTime < _nextGhostSend) return;
            if (!idChanged
                && (pos - _sentGhostPos).sqrMagnitude < 0.0025f
                && Quaternion.Angle(rot, _sentGhostRot) < 2f) return;

            _sentGhostId = id;
            _sentGhostPos = pos;
            _sentGhostRot = rot;
            _nextGhostSend = Time.unscaledTime + 0.1f;
            RpcUpdateGhost(id, pos, rot);
        }

        [ServerRpc]
        private void RpcUpdateGhost(string itemId, Vector3 pos, Quaternion rot)
        {
            _ghostItemId.Value = itemId ?? "";
            _ghostPos.Value = pos;
            _ghostRot.Value = rot;
        }

        // ── replicated-motion estimate (14.52.0) ─────────────────────
        // Collision damage needs "how fast is that crusader moving" for
        // avatars, whose transforms are driven by replication, not physics.
        private Vector3 _velTrackPos;
        private Vector3 _estimatedVelocity;

        /// <summary>Frame-delta velocity of this avatar's replicated body.</summary>
        public Vector3 EstimatedVelocity => _estimatedVelocity;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                Vector3 raw = (transform.position - _velTrackPos) / dt;
                // Light smoothing: replication arrives in bursts, and a
                // single teleport-sized frame must not read as lethal speed.
                _estimatedVelocity = raw.sqrMagnitude > 10000f
                    ? Vector3.zero   // >100 m/s in one frame = teleport/snap, not motion
                    : Vector3.Lerp(_estimatedVelocity, raw, 0.5f);
                _velTrackPos = transform.position;
            }

            PollCrest();
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

        // ─────────────────────── personal crest (14.49.0) ───────────────────────

        private int _crestVersion = -1;
        private string _crestId = "";

        /// <summary>Wears the player's chest text and icon from the cosmetics
        /// registry. Registry-version polling, the cheap pattern every panel
        /// uses: two field compares a frame until something actually changes.
        /// Runs on every machine - owners wear their own crest too, that is
        /// what everyone else is looking at.</summary>
        private void PollCrest()
        {
            if (NetworkSession.IsDedicated) return;   // headless wears nothing
            string id = PlayerId;
            if (string.IsNullOrEmpty(id)) return;
            if (_crestVersion == PlayerCosmeticsRegistry.Version && _crestId == id) return;
            _crestVersion = PlayerCosmeticsRegistry.Version;
            _crestId = id;
            CrusaderModel.SetCrest(transform,
                PlayerCosmeticsRegistry.TextureOf(id),
                PlayerCosmeticsRegistry.ChestTextOf(id));
        }

        // ─────────────────────────── presence ───────────────────────────

        /// <summary>Registers once the player id is known - at spawn if the
        /// value already arrived, otherwise the moment the SyncVar lands.</summary>
        private void TryRegister()
        {
            if (_registeredId != null) return;
            string id = PlayerId;   // SyncVar when delivered, announce fallback otherwise
            if (string.IsNullOrEmpty(id)) return;   // identity not delivered yet - OnIdChanged/announce retries
            _registeredId = id;
            _byPlayerId[id] = this;
            NetworkSession.RegisterPlayer(id, PlayerName);
            NetworkSession.UpdateDisplayName(id, PlayerName);
        }

        private void Unregister()
        {
            if (_registeredId == null) return;
            if (_byPlayerId.TryGetValue(_registeredId, out var held) && held == this)
                _byPlayerId.Remove(_registeredId);
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
            // 14.52.0 - the plate is GUARANTEED, and guaranteed ABOVE THE
            // HEAD. A prefab that lost the reference (meta churn strips the
            // component, re-adding it blanks the field) used to mean no name
            // at all - the only name left visible was whatever was printed on
            // the chest. Build one at runtime when missing and always enforce
            // the above-head anchor, so the name never rides anywhere else.
            if (nameplate == null)
            {
                var found = transform.Find("Nameplate");
                if (found != null) nameplate = found.GetComponent<TextMesh>();
            }
            if (nameplate == null)
            {
                var plateGo = new GameObject("Nameplate");
                plateGo.transform.SetParent(transform, false);
                nameplate = plateGo.AddComponent<TextMesh>();
                nameplate.characterSize = 0.12f;
                nameplate.fontSize = 64;
                nameplate.anchor = TextAnchor.LowerCenter;
                nameplate.alignment = TextAlignment.Center;
                nameplate.color = new Color(0.92f, 0.94f, 0.97f);
                // A runtime TextMesh starts with no font - and no font means
                // no glyphs at all. The built-in always exists.
                try
                {
                    var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (font != null)
                    {
                        nameplate.font = font;
                        var r = plateGo.GetComponent<MeshRenderer>();
                        if (r != null) r.material = font.material;
                    }
                }
                catch { /* glyphless beats crashing */ }
            }
            var plate = nameplate.transform;
            if (plate.parent != transform) plate.SetParent(transform, false);
            plate.localPosition = new Vector3(0f, 2.25f, 0f);   // above the head, above the bar
            plate.localScale = Vector3.one;

            string name = PlayerName;   // SyncVar or announce fallback
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

        private void OnArmorChanged(int previous, int next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplyArmor(next);
        }

        /// <summary>14.14.0: armor is display, not identity - plates only show
        /// while a suit is actually worn, tinted by tier.</summary>
        private void ApplyArmor(int tier)
        {
            CrusaderModel.SetArmor(transform, tier);
        }

        private void OnSkinToneChanged(int previous, int next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplySkinTone(next);
        }

        private void ApplySkinTone(int tone)
        {
            CrusaderModel.SetSkinTone(transform, tone);
        }

        private void OnEquipChanged(int previous, int next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplyBackGear(next);
        }

        private void ApplyBackGear(int flags)
        {
            CrusaderModel.SetBackGear(transform, (flags & 1) != 0, (flags & 2) != 0);
        }

        private void OnMotionChanged(int previous, int next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplyMotion(next);
        }

        private void ApplyMotion(int flags)
        {
            var driver = Locomotion();
            if (driver == null) return;
            driver.Sliding = (flags & 1) != 0;
            driver.Blocking = (flags & 2) != 0;
        }

        private void OnAttackChanged(int previous, int next, bool asServer)
        {
            if (asServer || IsOwner) return;
            // Deliveries before OnStartClient primes are pre-join history, not swings.
            if (!_attackPrimed) return;
            var driver = Locomotion();
            if (driver != null) driver.PlayAttack();
        }

        // ── remote building-ghost replica ──
        private GameObject _ghostReplica;
        private string _ghostReplicaId = "";

        private void OnGhostItemChanged(string previous, string next, bool asServer)
        {
            if (asServer || IsOwner) return;
            ApplyGhost(next);
        }

        private void OnGhostPosChanged(Vector3 previous, Vector3 next, bool asServer)
        {
            if (asServer || IsOwner) return;
            if (_ghostReplica != null) _ghostReplica.transform.position = next;
        }

        private void OnGhostRotChanged(Quaternion previous, Quaternion next, bool asServer)
        {
            if (asServer || IsOwner) return;
            if (_ghostReplica != null) _ghostReplica.transform.rotation = next;
        }

        /// <summary>Mirror of the other player's building preview, plus the arm-out
        /// building pose whenever a preview is showing.</summary>
        private void ApplyGhost(string itemId)
        {
            if (itemId == null) itemId = "";
            CrusaderModel.SetBuildPose(transform, itemId.Length > 0);
            if (itemId == _ghostReplicaId && (_ghostReplica != null || itemId.Length == 0)) return;
            if (_ghostReplica != null) { Destroy(_ghostReplica); _ghostReplica = null; }
            _ghostReplicaId = itemId;
            if (itemId.Length == 0) return;
            _ghostReplica = VoxelEngine.Building.BuildSystem.CreateRemoteGhost(itemId);
            if (_ghostReplica == null) return;
            _ghostReplica.transform.SetPositionAndRotation(_ghostPos.Value, _ghostRot.Value);
        }

        private void ApplyHeldItem(string itemId)
        {
            if (_heldModel != null) { Destroy(_heldModel); _heldModel = null; }
            if (string.IsNullOrEmpty(itemId))
            {
                var bareDriver = Locomotion();
                if (bareDriver != null) bareDriver.Stance = 0;   // empty hands, no stance
                return;
            }

            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            var item = persistence != null ? persistence.FindItemById(itemId) : null;

            // 14.18.0: weapon stance follows the held item - no extra wire data.
            // 14.18.1: classify by CLASS first (any melee WeaponItem is a sword
            // stance) - asset toolType values can be stale on assets created
            // before the setup wizard learned to assign them.
            var driver = Locomotion();
            if (driver != null)
            {
                var weapon = item as VoxelEngine.Combat.WeaponItem;
                var tool = item as VoxelEngine.Items.ToolItem;
                bool swordStance =
                    (weapon != null && weapon.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Melee)
                    || (tool != null && tool.toolType == VoxelEngine.Items.ToolType.Sword);
                driver.Stance = swordStance ? 1 : 0;
            }

            if (item == null) return;   // unknown on this side - show empty hands

            EnsureHand();
            _heldModel = VoxelEngine.Player.HeldToolView.BuildViewmodelFor(item);
            _heldModel.transform.SetParent(_hand, false);
            // Display only: held models must never collide with the world or
            // swallow interaction rays.
            foreach (var col in _heldModel.GetComponentsInChildren<Collider>(true))
                Destroy(col);
            AlignHeldModel(item);
        }

        // ---------- Held-model grip alignment (14.18.4) ----------
        // The viewmodels are authored for the first-person camera anchor; in a
        // skeleton hand they need a real grip. The hand frame is derived from
        // the actual finger and thumb bones, so it is valid in any animated
        // pose - and because the model is parented to the bone-riding anchor,
        // one world-space alignment at build time holds forever after.

        private const int GripPalm = 0, GripBlade = 1, GripGun = 2;

        private static int GripArchetypeFor(VoxelEngine.Items.ItemDefinition item, out float shift)
        {
            shift = 0f;
            var weapon = item as VoxelEngine.Combat.WeaponItem;
            if (weapon != null)
            {
                if (weapon.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Ranged) { shift = 0.05f; return GripGun; }
                if (weapon.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Melee) { shift = 0.10f; return GripBlade; }
                return GripPalm;   // thrown: sits in the palm
            }
            var tool = item as VoxelEngine.Items.ToolItem;
            if (tool != null)
            {
                switch (tool.toolType)
                {
                    case VoxelEngine.Items.ToolType.Pickaxe:
                    case VoxelEngine.Items.ToolType.Axe:
                    case VoxelEngine.Items.ToolType.Shovel:
                        shift = 0.08f;   // fist below the middle of the shaft, head above the hand
                        return GripBlade;
                    case VoxelEngine.Items.ToolType.Sword:
                        shift = 0.10f;   // fist on the grip, crossguard above, pommel below
                        return GripBlade;
                }
            }
            return GripPalm;
        }

        private void AlignHeldModel(VoxelEngine.Items.ItemDefinition item)
        {
            if (_heldModel == null || _hand == null) return;
            var handBone = _hand.parent;
            // Primitive fallback body: no skeleton - keep the legacy placement.
            if (handBone == null || !handBone.name.EndsWith(CrusaderModel.RightHandName)
                || handBone.name == CrusaderModel.RightHandName) return;

            // Finger and thumb bones give the hand frame.
            Transform fingers = null, thumb = null;
            foreach (Transform child in handBone)
            {
                if (child == _hand) continue;
                if (child.name.Contains("Thumb"))
                {
                    if (thumb == null || child.name.Length < thumb.name.Length) thumb = child;
                }
                else if (fingers == null || (child.name.Contains("Middle") && !fingers.name.Contains("Middle")))
                {
                    fingers = child;
                }
            }
            Vector3 fingerDir = fingers != null
                ? fingers.position - handBone.position
                : (handBone.parent != null ? handBone.position - handBone.parent.position : transform.forward);
            if (fingerDir.sqrMagnitude < 1e-8f) return;
            fingerDir.Normalize();

            // The blade side of a fist is the thumb side: the grip axis is the
            // thumb direction with its along-the-fingers part removed.
            Vector3 grip = thumb != null ? thumb.position - handBone.position : Vector3.Cross(fingerDir, transform.forward);
            grip -= fingerDir * Vector3.Dot(grip, fingerDir);
            if (grip.sqrMagnitude < 1e-6f) grip = Vector3.Cross(fingerDir, transform.forward);
            grip.Normalize();

            float shift;
            int archetype = GripArchetypeFor(item, out shift);
            var m = _heldModel.transform;
            Vector3 palm = handBone.position + fingerDir * 0.07f;

            if (archetype == GripBlade)
            {
                // Blade/shaft (+Y of the model) along the grip axis, head/edge
                // rolled toward the fingers' forward.
                m.rotation = Quaternion.FromToRotation(m.up, grip) * m.rotation;
                RollAround(m, grip, fingerDir, false);
                m.position = palm + grip * shift;
            }
            else if (archetype == GripGun)
            {
                // Barrel (+Z of the model) perpendicular to the grip axis, top
                // of the weapon rolled to the thumb side.
                Vector3 barrel = fingerDir - grip * Vector3.Dot(fingerDir, grip);
                if (barrel.sqrMagnitude < 1e-6f) barrel = fingerDir;
                barrel.Normalize();
                m.rotation = Quaternion.FromToRotation(m.forward, barrel) * m.rotation;
                RollAround(m, barrel, grip, true);
                m.position = palm + grip * shift;
            }
            else
            {
                m.position = palm;   // palm items: centered in the hand
            }
        }

        /// <summary>Roll the model around an axis so its projected up/forward
        /// lines up with the projected target direction.</summary>
        private static void RollAround(Transform m, Vector3 axis, Vector3 target, bool useUp)
        {
            Vector3 current = Vector3.ProjectOnPlane(useUp ? m.up : m.forward, axis);
            Vector3 desired = Vector3.ProjectOnPlane(target, axis);
            if (current.sqrMagnitude < 1e-6f || desired.sqrMagnitude < 1e-6f) return;
            m.rotation = Quaternion.FromToRotation(current.normalized, desired.normalized) * m.rotation;
        }

        private void ApplyCrouch(bool crouched)
        {
            // 14.22.0: with crouch clips present the skeleton does the work and
            // the model is never squashed. The squash below is the fallback for
            // a project whose Resources/PlayerAnimations has no crouch clip -
            // exactly the behaviour that shipped in 14.13.0.
            var driver = Locomotion();
            if (driver != null)
            {
                driver.Crouched = crouched;
                if (driver.HasCrouchClips) { ClearCrouchSquash(); return; }
            }

            CachePose();
            float f = crouched ? CrouchFactor : 1f;
            if (_body != null)
            {
                _body.localScale = new Vector3(_bodyStandScale.x, _bodyStandScale.y * f, _bodyStandScale.z);
                _body.localPosition = new Vector3(_bodyStandPos.x, _bodyStandPos.y * f, _bodyStandPos.z);
            }
            if (_visor != null)
                _visor.localPosition = new Vector3(_visorStandPos.x, _visorStandPos.y * f, _visorStandPos.z);
            // Only the legacy root-level anchor needs manual crouch tracking; the
            // crusader hand lives inside the model and squashes with it (14.13.0).
            if (_hand != null && _hand.parent == transform)
                _hand.localPosition = new Vector3(handLocalPosition.x, handLocalPosition.y * f, handLocalPosition.z);
        }

        /// <summary>Undo any squash left over from before the crouch clips were
        /// available (or from a session that ran without them).</summary>
        private void ClearCrouchSquash()
        {
            CachePose();
            if (_body != null)
            {
                _body.localScale = _bodyStandScale;
                _body.localPosition = _bodyStandPos;
            }
            if (_visor != null) _visor.localPosition = _visorStandPos;
            if (_hand != null && _hand.parent == transform) _hand.localPosition = handLocalPosition;
        }

        /// <summary>Rust-style honesty: the bar only appears when hurt.</summary>
        private void ApplyHealth(int pct)
        {
            // 14.17.0: a badly hurt crusader stands differently (sad idle).
            // 14.36.0: a dead one falls - the mirror hitting 0 plays the death
            // clip on every machine, and the respawn (health back up) clears it.
            var driver = Locomotion();
            if (driver != null)
            {
                driver.LowHealth = pct <= 35;
                driver.Dead = pct <= 0;
            }

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
            // Preferred anchor: the crusader's right hand, under the arm pivot -
            // the tool follows every arm pose (14.13.0).
            var crusader = transform.Find(CrusaderModel.RootName);
            if (crusader != null)
            {
                var hand = crusader.Find(CrusaderModel.RightArmPivotName + "/" + CrusaderModel.RightHandName);
                // 14.15.0: on the rigged body the anchor hangs off the right-hand
                // BONE, wherever the skeleton put it - search by name instead.
                if (hand == null) hand = CrusaderModel.FindDeep(crusader, CrusaderModel.RightHandName);
                if (hand != null) { _hand = hand; return; }
            }
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
            _body = transform.Find(CrusaderModel.RootName);
            if (_body == null) _body = transform.Find("Body");
            _visor = transform.Find("Visor");
            if (_body != null) { _bodyStandPos = _body.localPosition; _bodyStandScale = _body.localScale; }
            if (_visor != null) _visorStandPos = _visor.localPosition;
        }
    }
}
