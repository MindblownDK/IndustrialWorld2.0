// Assets/Scripts/VoxelEngine/Player/PlayerController.cs
//
// Smooth FPS character controller with:
//   • WASD + mouse look (sensitivity / FOV / invertY from GameSettings)
//   • Sprint, Jump, hold-to-Crouch
//   • momentum-based SLIDE: pressing crouch while sprinting on the ground
//     gives an instant boost + low-friction slide that decays back to walk speed.
//   • Optional FLY mode (toggleable via GameSettings.FlyMode or the ToggleFly key).
//
// Uses CharacterController for collision so it works on the voxel terrain meshes.

using UnityEngine;
using VoxelEngine.Settings;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;
using InputAction = VoxelEngine.Settings.InputAction;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Horizontal walk speed (m/s).")]
        public float walkSpeed = 5.5f;
        [Tooltip("Multiplier applied to walk speed while Sprint is held.")]
        public float sprintMultiplier = 1.6f;
        [Tooltip("Multiplier while crouched (and not sliding).")]
        public float crouchMultiplier = 0.45f;
        [Tooltip("How quickly horizontal velocity catches up to the input direction. Higher = snappier.")]
        public float groundAcceleration = 18f;
        [Tooltip("Acceleration while airborne (keep low for realistic feel).")]
        public float airAcceleration = 4f;
        [Tooltip("Friction applied to horizontal velocity on the ground when no input. Per second.")]
        public float groundFriction = 10f;

        [Header("Ice Movement")]
        [Tooltip("Ground friction used when standing on Ice voxels. Lower values keep momentum and create a slippery glide.")]
        public float iceGroundFriction = 0.85f;
        [Tooltip("Multiplier applied to ground acceleration on Ice voxels. Lower values make steering/braking less instant.")]
        [Range(0.05f, 1f)] public float iceAccelerationMultiplier = 0.28f;
        [Tooltip("Slide friction multiplier while sliding on Ice voxels.")]
        [Range(0.05f, 1f)] public float iceSlideFrictionMultiplier = 0.35f;

        [Header("Terrain Support")]
        [Tooltip("Small clearance kept between the controller feet and terrain to prevent uphill mesh penetration.")]
        [Range(0.01f, 0.20f)] public float terrainFootClearance = 0.06f;
        [Tooltip("Distance ahead of the player sampled for a walkable uphill surface.")]
        [Range(0.25f, 1.50f)] public float uphillProbeDistance = 0.80f;
        [Tooltip("Largest natural uphill rise the controller may assist in one movement frame.")]
        [Range(0.20f, 1.50f)] public float maxUphillAssistRise = 0.90f;
        [Tooltip("Minimum local-up component of a surface normal that counts as walkable terrain.")]
        [Range(0.05f, 0.95f)] public float walkableGroundNormalMin = 0.18f;

        [Header("Jump / Gravity")]
        public float jumpHeight = 1.4f;
        public float gravity = -22f;
        [Tooltip("Lets the player initiate a jump for a short window after walking off a ledge.")]
        public float coyoteTime = 0.12f;

        [Header("Fall Damage")]
        [Tooltip("Downward impact speed along local gravity before fall damage starts.")]
        public float fallDamageStartSpeed = 11f;
        [Tooltip("Downward impact speed that is roughly lethal at 100 HP.")]
        public float fallDamageLethalSpeed = 28f;
        [Tooltip("Damage curve exponent. Higher values make small falls gentler and big falls harsher.")]
        public float fallDamageExponent = 1.35f;

        [Header("Crouch / Slide (momentum-based)")]
        [Tooltip("Stand-up height of the controller.")]
        public float standHeight = 1.85f;
        [Tooltip("Crouched height of the controller (camera lowers proportionally).")]
        public float crouchHeight = 1.20f;
        [Tooltip("Initial slide boost added to current speed.")]
        public float slideBoost = 4.0f;
        [Tooltip("Friction during a slide (lower than walk friction for that 'glide' feel).")]
        public float slideFriction = 1.6f;
        [Tooltip("If horizontal speed drops below this during a slide, the slide ends.")]
        public float slideEndSpeed = 3.0f;
        [Tooltip("Minimum horizontal speed required to *start* a slide (keeps slides for sprint-only).")]
        public float slideMinSpeed = 6.0f;

        [Header("Fly Mode")]
        public float flySpeed = 14f;
        public float flySprintMultiplier = 3f;

        [Header("Camera")]
        [Tooltip("Local Y position of the camera while standing.")]
        public float standEyeHeight  = 1.65f;
        [Tooltip("Local Y position of the camera while crouching/sliding.")]
        public float crouchEyeHeight = 0.9f;
        public Transform cameraPivot;
        public Camera playerCamera;

        // ===== runtime state =====
        private CharacterController _cc;
        private float _yaw, _pitch;
        // Full orientation quaternion used ONLY in fly mode (6DOF: yaw/pitch/roll accumulate).
        // Walk mode ignores this and uses (_yaw, radial-up) as before.
        private Quaternion _flyRotation = Quaternion.identity;
        private Vector3 _velocity;          // includes Y in walk mode; in fly mode XYZ
        private bool   _grounded;
        private bool   _wasGrounded;
        private bool   _onIce;
        private float  _lastAirDownSpeed;
        private float  _lastGroundedTime;
        private bool   _crouched;
        private bool   _sliding;
        private bool   _sprinting;
        private float  _smoothedEyeHeight;
        private float  _jetpackBoostCharge;
        // Reused by terrain support probes to avoid per-frame raycast allocations.
        private readonly RaycastHit[] _terrainProbeHits = new RaycastHit[12];

        // ===== Editor inspector helpers (for the in-inspector toggle button) =====
        [HideInInspector] public bool inspectorFlyToggle;

        // ===== movement-state exposure (read by HeldToolView / animations) =====
        public bool IsGrounded => _grounded;
        public bool IsSliding => _sliding;
        public bool IsSprinting => _sprinting;
        public bool IsFlying => inspectorFlyToggle;
        /// <summary>True while riding a mount: locomotion is suspended, but mouse-look + camera stay live.</summary>
        public bool IsMounted;
        public Vector3 Velocity => _velocity;
        public float LastAirDownSpeed => _lastAirDownSpeed;

        /// <summary>Zero the internal velocity (used when mounting / dismounting a mount).</summary>
        public void ResetVelocity() => _velocity = Vector3.zero;

        /// <summary>Apply an external velocity impulse (e.g. a Karkadann charge knockback). Decays via normal friction.</summary>
        public void ApplyImpulse(Vector3 worldImpulse) => _velocity += worldImpulse;

        // Petrify (Basilisk gaze): temporary movement slow.
        private float _petrifySlow;
        private float _petrifyTimer;
        private float PetrifySpeedMul => _petrifyTimer > 0f ? Mathf.Max(0.1f, 1f - _petrifySlow) : 1f;
        /// <summary>Apply a temporary movement slow (e.g. a Basilisk's petrifying gaze). slowFraction 0=none, 1=frozen.</summary>
        public void ApplyPetrify(float slowFraction, float duration)
        {
            _petrifySlow   = Mathf.Max(_petrifySlow, Mathf.Clamp01(slowFraction));
            _petrifyTimer  = Mathf.Max(_petrifyTimer, duration);
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = standHeight;
            _cc.center = new Vector3(0, standHeight * 0.5f, 0);
            // On spherical bodies the terrain has hills/mountains in every direction. The default
            // slopeLimit (45°) blocks the player from walking up slopes → "stuck". Raise it so the
            // player can traverse any natural terrain angle on a planet surface.
            _cc.slopeLimit = 85f;
            _cc.stepOffset = 0.6f;
            // Ensure water/equipment trackers exist for movement-state checks.
            if (GetComponent<PlayerWaterState>() == null) gameObject.AddComponent<PlayerWaterState>();
            if (GetComponent<PlayerEquipment>() == null) gameObject.AddComponent<PlayerEquipment>();
            _smoothedEyeHeight = standEyeHeight;

            if (cameraPivot == null)
            {
                var pivotGo = new GameObject("CameraPivot");
                pivotGo.transform.SetParent(transform, false);
                pivotGo.transform.localPosition = new Vector3(0, standEyeHeight, 0);
                cameraPivot = pivotGo.transform;
            }
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
                if (playerCamera == null)
                {
                    var camGo = new GameObject("PlayerCamera");
                    camGo.transform.SetParent(cameraPivot, false);
                    playerCamera = camGo.AddComponent<Camera>();
                    playerCamera.tag = "MainCamera";
                    camGo.AddComponent<AudioListener>();
                    camGo.AddComponent<UnderwaterEffect>();

                }
                else if (playerCamera.transform.parent != cameraPivot)
                {
                    playerCamera.transform.SetParent(cameraPivot, false);
                    playerCamera.transform.localPosition = Vector3.zero;
                    playerCamera.transform.localRotation = Quaternion.identity;
                }
            }
            // Ensure underwater effect exists on the camera (ALWAYS runs).
            if (playerCamera != null && playerCamera.GetComponent<UnderwaterEffect>() == null)
                playerCamera.gameObject.AddComponent<UnderwaterEffect>();

            // Camera screenshake / FOV feedback for ship acceleration.
            if (playerCamera != null && playerCamera.GetComponent<CameraFeedback>() == null)
                playerCamera.gameObject.AddComponent<CameraFeedback>();

            _yaw = transform.eulerAngles.y;
            _pitch = 0f;
            ApplyFov();
            GameSettings.OnChanged += ApplyFov;
        }

        private void OnDestroy() => GameSettings.OnChanged -= ApplyFov;

        private void Start()
        {
            // Lock cursor immediately so the player can look around without first opening a menu.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }

        private void ApplyFov()
        {
            if (playerCamera != null) playerCamera.fieldOfView = GameSettings.Fov;
        }

        // ============================================================
        //                          MAIN LOOP
        // ============================================================
        private void Update()
        {
            // If the spawner hasn't finished placing us yet, freeze entirely (no input, no gravity).
            // Prevents falling-through-ungenerated-chunks AND prevents the player taking
            // control before the saved position has been restored.
            if (PlayerSpawner.Instance != null && !PlayerSpawner.Instance.ReadyForPlayerControl)
            {
                _velocity = Vector3.zero;
                return;
            }

            // UI gating (multiplayer-ready: the world never freezes — only a HARD pause
            // stops the player, and only because the pause menu zeroes timeScale):
            //   • HARD pause (pause menu / death screen) → old behaviour: full freeze.
            //   • Soft UI (inventory, machine panels, wheels, …) → KEEPS SIMULATING:
            //     gravity, inertial damping and jetpack flight/fuel drain continue
            //     while you browse; only INPUT is silenced (movement/thrust keys and
            //     mouse-look belong to the UI now).
            if (VoxelEngine.UI.UIState.IsHardPause)
            {
                _velocity = Vector3.zero;
                _sliding = false;
                return;
            }
            bool softUiOpen = VoxelEngine.UI.UIState.IsBlocking;

            if (!softUiOpen) UpdateLook();
            else { _sliding = false; }   // no slide tricks while browsing a menu

            // Petrify slow decay (Basilisk gaze, etc.).
            if (_petrifyTimer > 0f) { _petrifyTimer -= Time.deltaTime; if (_petrifyTimer <= 0f) _petrifySlow = 0f; }

            // While riding a mount, the mount drives movement — suspend our own locomotion
            // (but keep mouse-look + camera height so the rider can look around).
            // A soft UI (inventory, machine panel, …) no longer freezes ANY of this:
            // FlyUpdate/WalkUpdate keep running with input silenced (GetMoveInput and
            // the held-key gates return zero while a UI is blocking), so gravity,
            // inertial damping and jetpack fuel drain all continue behind the menu —
            // hovering mid-air with the inventory open burns H₂ exactly like it should.
            if (!IsMounted)
            {
                if (!VoxelEngine.UI.UIState.TextInputActive)
                    UpdateFlyToggle();   // no jetpack toggling mid-search-typing

                if (GameSettings.FlyMode && !HasFlightPermission())
                {
                    GameSettings.FlyMode = false;
                    _yaw = transform.rotation.eulerAngles.y;
                    var equipment = GetComponent<PlayerEquipment>();
                    var jets = equipment != null ? equipment.GetJetpackSummary() : PlayerEquipment.JetpackSummary.Empty;
                    string why = jets.anyPack && !string.IsNullOrEmpty(jets.offlineReason)
                        ? jets.offlineReason
                        : "No jetpack equipped";
                    VoxelEngine.UI.BuildFeedbackHud.Show("Flight Offline", why, null, Color.yellow);
                }

                if (GameSettings.FlyMode) FlyUpdate();
                else                      WalkUpdate();
            }

            UpdateCameraHeight();
        }

        // ----- shared: mouse look + fly-mode toggle key -----
        private void UpdateLook()
        {
            // Standard FPS look: the camera always follows the mouse while playing
            // (no button to hold). The cursor stays locked & hidden during play and
            // is only freed by UIState when a menu opens (handled above).
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            Vector2 d = GetMouseDelta();
            float sens = GameSettings.MouseSensitivity;
            float invert = GameSettings.InvertY ? -1f : 1f;

            if (GameSettings.FlyMode)
            {
                // ── 6DOF flight look ─────────────────────────────────────
                // Mouse X = yaw about LOCAL up, Mouse Y = pitch about LOCAL right.
                float yaw   = d.x * sens;
                float pitch = -d.y * sens * invert;
                // Q / E = roll about LOCAL forward (the classic jetpack roll).
                float roll  = 0f;
                if (GameSettings.IsHeld(InputAction.RollLeft))  roll += 1f;
                if (GameSettings.IsHeld(InputAction.RollRight)) roll -= 1f;
                roll *= sens * 1.5f; // a touch snappier than look so it feels deliberate
                // Apply around LOCAL axes of the current orientation → no gimbal weirdness.
                _flyRotation = _flyRotation * Quaternion.Euler(pitch, yaw, roll);
                transform.rotation        = _flyRotation;
                // Body now carries the full orientation (pitch + roll too), so the camera pivot
                // stays at identity — no double-applying of pitch.
                cameraPivot.localRotation = Quaternion.identity;
                return;
            }

            // ── Walk-mode look (unchanged): yaw + radial reorientation, camera pitch ──
            _yaw   += d.x * sens;
            _pitch -= d.y * sens * invert;
            _pitch = Mathf.Clamp(_pitch, -89f, 89f);

            // Radial reorientation: yaw stays, but the body's local +Y aligns to the
            // active gravity "up" (world-up on flat worlds → identity rotation, unchanged).
            transform.rotation       = Quaternion.FromToRotation(Vector3.up, UpVec) * Quaternion.Euler(0, _yaw, 0);
            cameraPivot.localRotation= Quaternion.Euler(_pitch, 0, 0);
        }

        private bool HasFlightPermission()
        {
            var equipment = GetComponent<PlayerEquipment>();
            return PlayerStats.Instance == null
                || PlayerStats.Instance.HasFlightUnlocked
                || (equipment != null && equipment.HasUsableJetpack);
        }

        private void UpdateFlyToggle()
        {
            if (GameSettings.WasPressed(InputAction.ToggleFly))
            {
                // Only allow toggling fly mode if research has unlocked it (OR if the user
                // is in dev/editor and just wants to enable it via Settings).
                var equipment = GetComponent<PlayerEquipment>();
                bool equippedNow = equipment != null && equipment.TryQuickEquipActiveJetpack();
                bool allowed = HasFlightPermission();
                if (allowed)
                {
                    bool turningOn = !GameSettings.FlyMode;
                    GameSettings.FlyMode = !GameSettings.FlyMode;
                    if (turningOn)
                        _flyRotation = transform.rotation;           // start 6DOF from current pose (no snap)
                    else
                        _yaw = transform.rotation.eulerAngles.y;     // carry heading back into walk mode
                }
                else
                {
                    var jets = equipment != null ? equipment.GetJetpackSummary() : PlayerEquipment.JetpackSummary.Empty;
                    string detail;
                    if (jets.anyPack && !string.IsNullOrEmpty(jets.offlineReason))
                        detail = jets.offlineReason; // e.g. "No atmosphere — engine can't ignite here"
                    else
                        detail = equippedNow ? "Jetpack equipped, but flight remains locked" : "Research flight or equip a jetpack in one of the two slots.";
                    VoxelEngine.UI.BuildFeedbackHud.Show("Flight Locked", detail, null, Color.yellow);
                    Debug.Log("[Player] Flight is locked. " + detail);
                }
            }
        }

        // ============================================================
        //                          WALK MODE
        // ============================================================
        private void WalkUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 up = UpVec;   // world-up on flat worlds; radial surface normal on spheres

            // -- ground check --
            // CRITICAL for spheres: CharacterController.isGrounded checks ground using WORLD-DOWN,
            // which only hits the sphere's surface near the "top" pole. Everywhere else it reports
            // not-grounded → the player slips toward the bottom pole (world-downhill). When radial
            // gravity is active we override the ground check with a RADIAL-DOWN raycast so the
            // player sticks to the surface anywhere on the planet.
            if (GravityProvider.IsRadial)
                _grounded = CheckRadialGround(up);
            else
                _grounded = _cc.isGrounded;

            float downSpeed = Mathf.Max(0f, -VerticalSpeed(up));
            if (!_grounded) _lastAirDownSpeed = Mathf.Max(_lastAirDownSpeed, downSpeed);
            if (_grounded && !_wasGrounded)
            {
                ApplyFallDamage(_lastAirDownSpeed);
                _lastAirDownSpeed = 0f;
            }
            _wasGrounded = _grounded;

            _onIce = _grounded && IceFrictionUtility.IsIceBelow(transform.position + up * 0.15f, up, 0.75f);
            if (_grounded) _lastGroundedTime = Time.time;
            bool canCoyote = (Time.time - _lastGroundedTime) <= coyoteTime;

            // -- input direction --
            Vector2 wish = GetMoveInput();
            // Build wish direction from local axes, then PROJECT onto the tangent plane
            // (perpendicular to radial up). Without this, transform.forward on a tilted player
            // has a small radial component that pushes the capsule INTO the terrain → "stuck".
            Vector3 wishDir = transform.right * wish.x + transform.forward * wish.y;
            wishDir = Vector3.ProjectOnPlane(wishDir, up);
            if (wishDir.sqrMagnitude > 0.001f) wishDir = wishDir.normalized;

            // -- crouch / slide state machine --
            bool uiLocked = VoxelEngine.UI.UIState.IsBlocking || VoxelEngine.UI.UIState.TextInputActive;
            bool crouchHeld = !uiLocked && GameSettings.IsHeld(InputAction.Crouch);
            bool sprintHeld = !uiLocked && GameSettings.IsHeld(InputAction.Sprint);
            UpdateCrouchSlide(crouchHeld, sprintHeld);

            // -- target horizontal speed --
            float speedMul = 1f;
            if (_sliding)            speedMul = 1f;                     // velocity decay handles it
            else if (_crouched)      speedMul = crouchMultiplier;
            // Use research-modified sprint multiplier if PlayerStats is available.
            float effSprint = PlayerStats.Instance != null ? PlayerStats.Instance.SprintMultiplier : sprintMultiplier;
            // Stamina removed — sprint is always available when held.
            bool canSprint = sprintHeld;
            _sprinting = canSprint && wish.y > 0.1f;
            if (canSprint && wish.y > 0.1f)
                speedMul = effSprint;

            float targetSpeed = walkSpeed * speedMul * PetrifySpeedMul;
            // Horizontal velocity lives on the local ground plane (perp to `up`).
            Vector3 horiz = Vector3.ProjectOnPlane(_velocity, up);

            if (_sliding)
            {
                // Apply slide friction -> exponential decay toward 0.
                float activeSlideFriction = _onIce ? slideFriction * iceSlideFrictionMultiplier : slideFriction;
                horiz = Vector3.Lerp(horiz, Vector3.zero, 1f - Mathf.Exp(-activeSlideFriction * dt));

                // End slide if speed too low or crouch released.
                if (horiz.magnitude < slideEndSpeed || !crouchHeld) _sliding = false;
            }
            else
            {
                Vector3 wishVel = wishDir * targetSpeed;
                float accel = _grounded ? groundAcceleration : airAcceleration;
                if (_grounded && _onIce) accel *= iceAccelerationMultiplier;

                if (_grounded && wishDir.sqrMagnitude < 0.01f)
                {
                    // Apply ground friction: glide toward 0 horizontal velocity.
                    float activeFriction = _onIce ? iceGroundFriction : groundFriction;
                    horiz = Vector3.Lerp(horiz, Vector3.zero, 1f - Mathf.Exp(-activeFriction * dt));
                }
                else
                {
                    horiz = Vector3.Lerp(horiz, wishVel, 1f - Mathf.Exp(-accel * dt));
                }
            }

            // Write horizontal back, preserving the vertical (along-up) component.
            _velocity = horiz + Vector3.Project(_velocity, up);

            // -- jump (allowed while sliding, while preserving slide momentum) --
            // Plain crouch with no slide = no jump (you'd just bonk your head).
            bool jumpAllowed = canCoyote && (!_crouched || _sliding);
            if (GameSettings.WasPressed(InputAction.Jump) && jumpAllowed)
            {
                float gravMag = GravVec.magnitude;
                _velocity = Vector3.ProjectOnPlane(_velocity, up) + up * Mathf.Sqrt(2f * gravMag * jumpHeight);
                _lastGroundedTime = -999f; // consume coyote

                // Slide-jump: keep horizontal momentum, end the slide. Pop up to standing height
                // so we don't keep our crouched collider mid-air (looks weird).
                if (_sliding)
                {
                    _sliding  = false;
                    _crouched = false;
                }
            }

            // -- voxel water swim (voxel-style) --
            // While submerged: WASD swims in the look direction, Space rises,
            // Crouch dives. With no swim input the player slowly sinks, so water/oil
            // feels physical instead of freezing the character in place.
            //
            // Old bug: only had gentle upward push + heavy Y-drag, so the player
            // could only sink with no way to swim forward. Now movement.input
            // drives a 3D swim velocity along the camera-forward, matching the
            // look angle (you can dive at a downward angle by looking down).
            var waterState = GetComponent<PlayerWaterState>();
            bool inWater = waterState != null && waterState.IsSwimming;
            if (inWater)
            {
                float surfY = waterState.WaterSurfaceY;
                float depth = GravityProvider.IsRadial ? (surfY - transform.position.magnitude) : (surfY - transform.position.y);

                // ── Build 3D swim direction using camera look ────────────────
                Vector3 camFwd = cameraPivot != null
                    ? cameraPivot.forward
                    : transform.forward;
                Vector3 camRight = cameraPivot != null
                    ? cameraPivot.right
                    : transform.right;

                Vector3 radialUp = GravityProvider.IsRadial ? UpVec : Vector3.up;
                bool wantsUp = GameSettings.IsHeld(InputAction.Jump);
                bool wantsDown = GameSettings.IsHeld(InputAction.Crouch);
                Vector3 swimDir = (camFwd * wish.y + camRight * wish.x);
                if (wantsUp)   swimDir += radialUp;
                if (wantsDown) swimDir -= radialUp;

                bool hasSwimInput = swimDir.sqrMagnitude > 0.001f;
                if (hasSwimInput) swimDir.Normalize();

                // ── Swim speed (slower than walking, doubled while sprinting) ─
                float swimSpeed = walkSpeed * 0.65f;
                if (sprintHeld) swimSpeed *= 1.4f;
                Vector3 wishVel = swimDir * swimSpeed;
                if (!hasSwimInput) wishVel = -radialUp * 0.85f;

                // Smoothly accelerate toward the wish velocity in all 3 axes.
                _velocity = Vector3.Lerp(_velocity, wishVel, 1f - Mathf.Exp(-6f * dt));

                // Natural buoyancy: if the player gives horizontal input but no
                // vertical input, add a small sinking bias instead of hovering frozen.
                if (hasSwimInput && !wantsUp && !wantsDown)
                    _velocity -= radialUp * (0.35f * dt);

                // Hard caps along vertical/radial axis so swimming is always controllable
                float vertSpd = Vector3.Dot(_velocity, radialUp);
                if (vertSpd < -3.8f) _velocity += radialUp * (-3.8f - vertSpd);
                if (vertSpd >  3.2f) _velocity += radialUp * ( 3.2f - vertSpd);

                // Jump-out: when the player presses Jump and is at/near the
                // surface AND moving up, give a small boost so they pop out.
                if (GameSettings.WasPressed(InputAction.Jump) && depth < 0.5f && vertSpd > 0)
                {
                    _velocity -= radialUp * vertSpd;
                    _velocity += radialUp * Mathf.Sqrt(-2f * gravity * 0.55f);
                }
            }
            else
            {
                // Radial gravity: apply along `up`; small downward stick when grounded.
                if (_grounded && VerticalSpeed(up) < 0f)
                    _velocity = Vector3.ProjectOnPlane(_velocity, up) + up * (-2f);
                else
                    _velocity += GravVec * dt;
            }

            // Probe slightly ahead before the horizontal move. This gives the
            // CharacterController a controlled lift onto natural mountain terrain
            // instead of allowing its downward stick velocity to push into a slope.
            if (!inWater && wishDir.sqrMagnitude > 0.001f)
                AssistTerrainAscent(up, wishDir, Mathf.Max(0.25f, horiz.magnitude * dt));

            // -- move --
            // Keep the small radial anti-stick lift, then run a post-move footing
            // recovery below for both flat and spherical terrain.
            Vector3 moveVec = _velocity * dt;
            if (_grounded && GravityProvider.IsRadial)
                moveVec += up * 0.015f;
            _cc.Move(moveVec);

            if (!inWater)
                RecoverTerrainFooting(up);
        }

        private void ApplyFallDamage(float impactDownSpeed)
        {
            if (impactDownSpeed <= fallDamageStartSpeed) return;
            var waterState = GetComponent<PlayerWaterState>();
            if (waterState != null && waterState.IsSwimming) return;
            var stats = PlayerStats.Instance != null ? PlayerStats.Instance : GetComponent<PlayerStats>();
            if (stats == null) return;

            float span = Mathf.Max(0.1f, fallDamageLethalSpeed - fallDamageStartSpeed);
            float severity = Mathf.Clamp01((impactDownSpeed - fallDamageStartSpeed) / span);
            float damage = Mathf.Pow(severity, Mathf.Max(0.1f, fallDamageExponent)) * stats.MaxHealth;
            if (damage <= 0.1f) return;

            var equipment = GetComponent<PlayerEquipment>();
            if (equipment != null) damage *= equipment.FallDamageMultiplier;

            stats.TakeDamage(damage);
            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Hard Landing",
                $"-{damage:0} HP · impact {impactDownSpeed:0.0} m/s",
                null,
                new Color(0.95f, 0.25f, 0.18f));
        }

        private void UpdateCrouchSlide(bool crouchHeld, bool sprintHeld)
        {
            // Initiate slide on the rising edge of crouch while sprinting on ground above slideMinSpeed.
            Vector3 horiz = new Vector3(_velocity.x, 0, _velocity.z);
            bool canStartSlide = crouchHeld && !_crouched && sprintHeld && _grounded
                                  && horiz.magnitude >= slideMinSpeed;
            if (canStartSlide)
            {
                _sliding = true;
                Vector3 dir = horiz.normalized;
                _velocity.x = (horiz + dir * slideBoost).x;
                _velocity.z = (horiz + dir * slideBoost).z;
            }

            _crouched = crouchHeld;
            float targetH = _crouched ? crouchHeight : standHeight;
            _cc.height = Mathf.MoveTowards(_cc.height, targetH, 6f * Time.deltaTime);
            _cc.center = new Vector3(0, _cc.height * 0.5f, 0);
        }

        // ============================================================
        //                          FLY MODE
        // ============================================================
        // Rate-limit for jetpack offline toasts so a dry pack doesn't spam every frame.
        private float _nextJetpackFeedbackTime;

        private void FlyUpdate()
        {
            float dt = Time.deltaTime;
            Vector2 wish = GetMoveInput();

            // 6DOF movement relative to the body's full orientation (includes pitch + roll).
            //   WASD   = forward/left/back/right
            //   Space = up, C = down (relative to where you're looking/rolled)
            //   Q / E = roll (handled in the fly-look branch)
            Vector3 wishDir = transform.right * wish.x + transform.forward * wish.y;
            bool uiLocked = VoxelEngine.UI.UIState.IsBlocking || VoxelEngine.UI.UIState.TextInputActive;
            if (!uiLocked && GameSettings.IsHeld(InputAction.Up)) wishDir += transform.up;
            if (!uiLocked && GameSettings.IsHeld(InputAction.Crouch)) wishDir -= transform.up;

            var equipment = GetComponent<PlayerEquipment>();
            bool researchFlight = PlayerStats.Instance != null && PlayerStats.Instance.HasFlightUnlocked;

            // Fuel/charge accounting: drain while flying; cut flight when no legal,
            // fueled pack remains (no atmosphere / dry tanks) and no research-only
            // flight permission remains.
            var jets = equipment != null ? equipment.GetJetpackSummary() : PlayerEquipment.JetpackSummary.Empty;
            // No equipment component at all (dev/research flight) keeps free boost; with
            // equipment, boost only engages when the drive pack reports afterburner fuel.
            bool boosting = (equipment == null || jets.canBoost)
                            && !uiLocked && GameSettings.IsHeld(InputAction.Sprint);
            if (equipment != null)
            {
                if (!researchFlight && !jets.canFly)
                {
                    CutJetpackFlight(jets.offlineReason);
                    return;
                }

                bool fueled = equipment.TryConsumeFlightFuel(dt, boosting);
                if (!fueled && !researchFlight)
                {
                    CutJetpackFlight("Out of fuel — fill Portable H₂ Tanks / Batteries / Cells");
                    return;
                }
                // If research flight is unlocked, allow unfueled flight without boost.
                if (!fueled) boosting = false;
            }

            float packSpeed = jets.speedMul > 0f ? jets.speedMul : 1f;
            float packBoost = jets.boostMul > 0f ? jets.boostMul : 1f;
            var pack = jets.drivePack;
            if (pack != null && pack.family == VoxelEngine.Items.JetpackFamily.HydrogenBoost)
            {
                // Hydrogen boost packs spool briefly instead of instantly hitting max
                // thrust. The ramp is deliberately short so controls stay responsive.
                float targetCharge = boosting ? 1f : 0f;
                float rate = boosting ? 3.2f : 5.5f;
                _jetpackBoostCharge = Mathf.MoveTowards(_jetpackBoostCharge, targetCharge, rate * dt);
            }
            else
            {
                _jetpackBoostCharge = boosting ? 1f : 0f;
            }
            float boostFactor = boosting ? Mathf.Lerp(1f, packBoost, _jetpackBoostCharge) : 1f;
            float spd = flySpeed * packSpeed * (boosting ? flySprintMultiplier * boostFactor : 1f);
            if (equipment != null) spd *= equipment.JetpackSpeedMultiplier;
            Vector3 wishVel = wishDir.sqrMagnitude > 0.0001f ? wishDir.normalized * spd : Vector3.zero;

            // Inertial-dampener feel: smooth toward target, no gravity in fly mode.
            _velocity = Vector3.Lerp(_velocity, wishVel, 1f - Mathf.Exp(-12f * dt));
            _cc.Move(_velocity * dt);
        }

        /// <summary>Kill fly-mode with a single, rate-limited explanation toast.</summary>
        private void CutJetpackFlight(string reason)
        {
            GameSettings.FlyMode = false;
            _velocity = Vector3.zero;
            _yaw = transform.rotation.eulerAngles.y;
            if (Time.unscaledTime < _nextJetpackFeedbackTime) return;
            _nextJetpackFeedbackTime = Time.unscaledTime + 1.5f;
            if (string.IsNullOrEmpty(reason)) reason = "Jetpack offline";
            VoxelEngine.UI.BuildFeedbackHud.Show("Jetpack Offline", reason, null, new Color(1f, 0.7f, 0.25f));
        }

        // ============================================================
        //                       CAMERA HEIGHT
        // ============================================================
        private void UpdateCameraHeight()
        {
            float target = (_crouched || _sliding) ? crouchEyeHeight : standEyeHeight;
            _smoothedEyeHeight = Mathf.MoveTowards(_smoothedEyeHeight, target, 4f * Time.deltaTime);
            cameraPivot.localPosition = new Vector3(0, _smoothedEyeHeight, 0);
        }

        // ============================================================
        //                       INPUT HELPERS
        // ============================================================
        private static Vector2 GetMoveInput()
        {
            // Any blocking UI owns the keyboard — physics keeps running, but the
            // player's movement keys are silenced (so typing/search never steers).
            if (VoxelEngine.UI.UIState.IsBlocking || VoxelEngine.UI.UIState.TextInputActive) return Vector2.zero;
            float x = (GameSettings.IsHeld(InputAction.Right) ? 1 : 0) - (GameSettings.IsHeld(InputAction.Left) ? 1 : 0);
            float y = (GameSettings.IsHeld(InputAction.Forward) ? 1 : 0) - (GameSettings.IsHeld(InputAction.Back) ? 1 : 0);
            return new Vector2(x, y);
        }


        private static Vector2 GetMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X") * 10f, Input.GetAxisRaw("Mouse Y") * 10f);
#endif
        }

        // ── Radial-gravity helpers ──────────────────────────────────
        // When no spherical body is active, GravityProvider reports world-up, so every
        // expression below reduces EXACTLY to the original flat-world math (up == +Y).
        // When a CelestialBody is active, "up" becomes the radial surface normal and the
        // player reorients to stand upright on the sphere — gravity, jump and horizontal
        // movement all operate on the local ground plane (perpendicular to `up`).
        private Vector3 UpVec => GravityProvider.GetUp(transform.position);
        private Vector3 GravVec => GravityProvider.IsRadial
            ? GravityProvider.GetGravity(transform.position)
            : (Vector3.up * gravity);   // gravity is negative → Vector3.up * gravity points down

        /// <summary>The vertical (along-up) component of velocity, as a signed scalar.</summary>
        private float VerticalSpeed(Vector3 up) => Vector3.Dot(_velocity, up);

        /// <summary>
        /// Raises the capsule just enough to meet a walkable mountain surface sampled
        /// ahead of the player. This is deliberately capped so cliffs remain cliffs.
        /// </summary>
        private void AssistTerrainAscent(Vector3 up, Vector3 wishDirection, float travelDistance)
        {
            if (_cc == null || wishDirection.sqrMagnitude < 0.0001f) return;

            float footClearance = Mathf.Clamp(terrainFootClearance, 0.01f, 0.20f);
            float maxRise = Mathf.Max(0.20f, maxUphillAssistRise);
            float maxProbe = Mathf.Max(0.25f, uphillProbeDistance);
            float probeDistance = Mathf.Clamp(
                Mathf.Max(travelDistance * 1.5f, _cc.radius * 0.85f),
                0.25f,
                maxProbe);
            Vector3 ahead = transform.position + wishDirection.normalized * probeDistance;
            if (!TryGetWalkableGround(ahead, up, maxRise + footClearance, out var hit)) return;

            float rise = Vector3.Dot(hit.point - transform.position, up) + footClearance;
            if (rise <= footClearance * 0.5f || rise > maxRise) return;

            _cc.Move(up * rise);
            if (VerticalSpeed(up) < 0f)
                _velocity = Vector3.ProjectOnPlane(_velocity, up);
        }

        /// <summary>
        /// Corrects a controller that has been nudged fractionally into a terrain mesh
        /// after a move. This runs after collision resolution so uphill traversal stays
        /// smooth without snapping the player upward while airborne.
        /// </summary>
        private void RecoverTerrainFooting(Vector3 up)
        {
            if (_cc == null) return;
            float footClearance = Mathf.Clamp(terrainFootClearance, 0.01f, 0.20f);
            float maxRise = Mathf.Max(0.20f, maxUphillAssistRise);
            float recoveryBelow = Mathf.Max(0.35f, maxRise);
            if (!TryGetWalkableGround(transform.position, up, recoveryBelow, out var hit)) return;

            float feetAboveGround = Vector3.Dot(transform.position - hit.point, up);
            float correction = footClearance - feetAboveGround;
            if (correction <= 0.001f) return;

            // Recover a bad overlap over a few frames rather than teleporting up
            // a cliff in one frame.
            correction = Mathf.Min(correction, maxRise);
            _cc.Move(up * correction);
            if (VerticalSpeed(up) < 0f)
                _velocity = Vector3.ProjectOnPlane(_velocity, up);
            _grounded = true;
        }

        private bool TryGetWalkableGround(Vector3 feetPosition, Vector3 up, float belowFeet, out RaycastHit bestHit)
        {
            bestHit = default;
            if (_cc == null || up.sqrMagnitude < 0.0001f) return false;

            up.Normalize();
            float probeHeight = Mathf.Max(_cc.height + 0.35f, 2.25f);
            Vector3 origin = feetPosition + up * probeHeight;
            float distance = probeHeight + Mathf.Max(0.1f, belowFeet);
            int count = Physics.RaycastNonAlloc(origin, -up, _terrainProbeHits, distance, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                var hit = _terrainProbeHits[i];
                var collider = hit.collider;
                if (collider == null) continue;
                if (collider.transform == transform || collider.transform.IsChildOf(transform) || transform.IsChildOf(collider.transform)) continue;
                if (IsLiquidSurfaceCollider(collider)) continue;
                if (Vector3.Dot(hit.normal, up) < Mathf.Clamp(walkableGroundNormalMin, 0.05f, 0.95f)) continue;
                if (hit.distance >= bestDistance) continue;

                bestDistance = hit.distance;
                bestHit = hit;
            }

            return bestDistance < float.PositiveInfinity;
        }

        private static bool IsLiquidSurfaceCollider(Collider collider)
        {
            if (collider == null) return false;
            string name = collider.gameObject.name;
            return name.IndexOf("LiquidSurface", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("WaterSurface", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Ocean", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Radial ground check for spherical bodies. Casts a ray RADIAL-DOWN (along -up) from the
        /// player's base; if it hits terrain within a small distance, the player is grounded.
        /// This replaces CharacterController.isGrounded (which only works world-down) so the
        /// player can walk on ANY part of a planet — top, side, or bottom — without sliding off.
        /// </summary>
        private bool CheckRadialGround(Vector3 up)
        {
            if (_cc == null) return false;
            // Start the ray slightly above the capsule base (along up) to avoid self-collision.
            Vector3 origin = transform.position + up * 0.05f;
            float halfHeight = _cc.height * 0.5f;
            // A little extra so we register ground even when floating a hair above it.
            float checkDist = halfHeight + 0.25f;
            return Physics.Raycast(origin, -up, checkDist);
        }
    }
}
