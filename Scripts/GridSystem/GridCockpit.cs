// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Cockpit with terminal + grid size switching.

using UnityEngine;
using VoxelEngine.Maritime;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        [Tooltip("Idle power draw while the cockpit is powered on.")]
        public float idleWatts = 50f;

        /// <summary>
        /// 17.39.0-dev — which shell this cockpit wears. There is ONE cockpit item now:
        /// the shell is chosen on the build wheel, stored here when the block is placed,
        /// and written to the save with the block. 1 is the crawler nose, 2 the boxy cab,
        /// 0 a legacy cockpit whose prefab carries no shell (it keeps the standing camera,
        /// so an old hull still works). The flight code is identical for every shell.
        /// </summary>
        [Tooltip("Shell: 0 = legacy, 1 = crawler nose, 2 = boxy cab")]
        public int shellStyle;

        public override float PowerDraw => Enabled ? idleWatts : 0f;

        /// <summary>The authored cabin basis. Its local forward matches the windscreen,
        /// unlike the grid prefab root which intentionally remains an item-placement frame.</summary>
        public Transform FlightControlFrame
        {
            get
            {
                Transform cabin = transform.Find(CockpitShellBuilder.ShellRootName + "/" + CockpitShellBuilder.InteriorName);
                return cabin != null ? cabin : transform;
            }
        }

        public const float WraparoundHealthMultiplier = 0.80f;
        public const float ArmoredCabHealthMultiplier = 1.85f;

        /// <summary>The reinforced heavy-machinery cab carries heavy armored integrity while
        /// the wraparound harvester cockpit trades durability for full-glass visibility.
        /// Derived from the authored item health so Setup never overwrites custom item values.</summary>
        public void ApplyShellDurability()
        {
            if (shellStyle != CockpitShellBuilder.StyleLegacy)
            {
                float cellSize = Grid != null
                    ? Grid.CellSize
                    : VoxelEngine.Items.GridSizeSpec.CellMetres(gridSize);
                CockpitShellBuilder.SyncColliderBounds(gameObject, cellSize, shellStyle);

                if (string.IsNullOrEmpty(blockName)
                    || blockName == "Cockpit"
                    || blockName == "Wraparound Cockpit"
                    || blockName == "Reinforced Cockpit")
                {
                    blockName = CockpitShellBuilder.NameOf(shellStyle);
                }
            }

            float itemHealth = SourceItem is GridBlockItem gridItem && gridItem.blockHP > 0f
                ? gridItem.blockHP
                : maxHP;
            if (itemHealth <= 0f) return;

            float previousMax = maxHP > 0f ? maxHP : itemHealth;
            float ratio = previousMax > 0f ? Mathf.Clamp01(currentHP / previousMax) : 1f;

            float multiplier = shellStyle == CockpitShellBuilder.StyleCab
                ? ArmoredCabHealthMultiplier
                : shellStyle == CockpitShellBuilder.StyleCrawler
                    ? WraparoundHealthMultiplier
                    : 1f;

            maxHP = Mathf.Round(itemHealth * multiplier);
            currentHP = currentHP > 0f
                ? Mathf.Clamp(Mathf.Round(maxHP * ratio), 1f, maxHP)
                : 0f;
        }

        public override void OnPlaced()
        {
            ApplyShellDurability();
            base.OnPlaced();
        }

        public Player.PlayerController Pilot { get; private set; }

        public bool IsFlightOnline => Enabled && Grid != null && Grid.HasPower;

        private bool _hasDefaultCameraPose;
        private Vector3 _defaultPivotLocalPosition;
        private Quaternion _defaultPivotLocalRotation;

        // 17.37.0-dev — the seated camera. The shell authors a SeatAnchor; the pilot's eye
        // is moved there by offsetting the camera pivot, so the player rig itself is never
        // touched and standing up is a single assignment back to the standing pose.
        private bool _hasSeatPose;
        private Vector3 _seatPivotOffset;
        private Quaternion _seatPivotRotation = Quaternion.identity;

        // ── Scroll camera zoom ───────────────────────────────────────
        [Header("Camera")]
        [Tooltip("Maximum distance the third-person camera can be scrolled out from the grid center.")]
        public float maxCameraDistance = 35f;
        [Tooltip("How fast the mouse wheel zooms in/out.")]
        public float zoomSpeed = 9.0f;
        [Tooltip("How fast the camera distance catches up to the target.")]
        public float zoomSmooth = 16f;
        [Tooltip("Mouse sensitivity for orbiting the third-person camera around the grid center.")]
        public float orbitSensitivity = 4.0f;

        private float _cameraDistance;
        private float _targetCameraDistance;
        private float _orbitYaw;
        private float _orbitPitch;
        private bool  _freeOrbiting;
        private float _lastAltPressAt = -999f;
        private const float AltDoublePressSeconds = 0.32f;
        private const float THIRD_PERSON_THRESHOLD = 0.35f;
        private const float TRAJECTORY_VIEW_DISTANCE = 8f;

        /// <summary>The cockpit the local player is currently seated in (null if on foot).</summary>
        public static GridCockpit ActivePilotSeat { get; private set; }

        /// <summary>Any cockpit-like control seat currently occupied by the local player.</summary>
        public static GridBlock ActiveControlSeat { get; private set; }

        /// <summary>The player currently seated in any cockpit-like control seat.</summary>
        public static Player.PlayerController ActiveControlPilot { get; private set; }

        /// <summary>The grid currently controlled by any cockpit-like control seat.</summary>
        public static GridEntity ActiveControlGrid => ActiveControlSeat != null ? ActiveControlSeat.Grid : null;

        /// <summary>True while the local player is seated in a cockpit, helm, or ship console.</summary>
        public static bool AnyPilotSeatActive => ActiveControlSeat != null && ActiveControlPilot != null;

        public static void RegisterAuxiliarySeat(GridBlock seat, Player.PlayerController pilot)
        {
            if (seat == null || pilot == null) return;
            ActiveControlSeat = seat;
            ActiveControlPilot = pilot;
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
        }

        public static void UnregisterAuxiliarySeat(GridBlock seat)
        {
            if (seat == null || ActiveControlSeat != seat) return;
            ActiveControlSeat = ActivePilotSeat;
            ActiveControlPilot = ActivePilotSeat != null ? ActivePilotSeat.Pilot : null;
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
        }

        private void OnEnable()
        {
            // Both shells ship inside the prefab; this picks the one the prefab (or a
            // designer) asked for. Nothing is generated - it only flips which hull is on.
            CockpitShellBuilder.ApplyShell(gameObject, shellStyle);
            if (Application.isPlaying && GetComponent<GridCockpitMfd>() == null)
                gameObject.AddComponent<GridCockpitMfd>();
            VoxelEngine.Networking.GridSync.SeatDenied += OnSeatDenied;
        }

        private void OnDisable()
        {
            VoxelEngine.Networking.GridSync.SeatDenied -= OnSeatDenied;
        }

        /// <summary>A cockpit can stop existing without anyone standing up - ground off,
        /// blown up, or rebuilt underneath a seated player when a record arrives. Every
        /// one of those paths has to release the seat, or the hull keeps believing it
        /// has a pilot: the chair is unreachable forever, and worse, a ship that thinks
        /// a pilot is holding station with dampeners stops applying gravity to itself
        /// and climbs away.</summary>
        private void OnDestroy()
        {
            if (Pilot != null) Exit();
            else VoxelEngine.Networking.GridSync.ReleaseSeatIfHeld(this);

        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Pilot != null) Exit();
        }

        /// <summary>The host gave this seat to somebody else. Only possible when two
        /// players reached for it in the same instant, but the loser has to stand back
        /// up or they are sitting in a cockpit the host does not think they own.</summary>
        private void OnSeatDenied(string netId, Vector3Int cell)
        {
            if (Pilot == null || Grid == null || cell != SeatCell) return;
            var tag = Grid.GetComponent<VoxelEngine.Networking.GridNetTag>();
            if (tag == null || tag.Id != netId) return;

            Exit();
            VoxelEngine.UI.BuildFeedbackHud.Show("Cockpit occupied",
                "Another player reached the seat first.");
        }

        private void Update()
        {
            if (Pilot == null) return;

            if (GameSettings.WasPressed(InputAction.ExitCockpit)
                && !VoxelEngine.UI.UIState.IsBlocking
                && !VoxelEngine.UI.UIState.TextInputActive
                && !VoxelEngine.UI.UIState.PauseConsumedThisFrame)
            { Exit(); return; }

            // Confirm wheel owns the cursor (Ctrl frees it for the drive slider).
            if (VoxelEngine.UI.ConfirmDialogHud.IsOpen)
            {
                Grid?.SetFlightInput(Vector3.zero, 0, 0, 0);
                return;
            }

            // While a full UI panel (terminal/inventory) is open, release control + cursor.
            if (VoxelEngine.UI.UIState.IsBlocking || VoxelEngine.UI.UIState.TextInputActive || Grid == null)
            {
                Grid?.SetFlightInput(Vector3.zero, 0, 0, 0);
                return;
            }

            // Offline cockpit or unpowered grid: player can still exit/open terminal,
            // but flight/gyro/tool controls are fully locked out.
            if (!IsFlightOnline)
            {
                Grid.SetFlightInput(Vector3.zero, 0, 0, 0);
                Grid.DrillVoidMode = false;
                return;
            }

            // No blocking UI → we're actively flying: lock the cursor so mouse-look works
            // (the player controller that normally does this is disabled while seated).
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Scroll zooms the cockpit camera between first-person and third-person.
            UpdateCameraZoom();

            // V toggles cockpit camera: first-person ⇄ exterior chase camera.
            if (VPressed) ToggleCameraMode();

            // Z toggles inertia dampeners (auto-brake to a stop when not thrusting).
            // Ctrl+Z (14.62.0) locks the grid under the crosshair as the RELATIVE
            // dampener target: the ship brakes toward matching ITS velocity instead
            // of world rest. Ctrl+Z again (or on empty space) clears the lock.
            if (GridInput.ZPressed)
            {
                if (GridInput.Ctrl) ToggleDampenerReference();
                else Grid.DampenersOn = !Grid.DampenersOn;
            }

            // Third-person Alt orbit is persistent. A quick second Alt press returns
            // the camera to the home/chase position without changing ship controls.
            HandleThirdPersonOrbitResetShortcut();

            // P toggles ALL landing gear on the grid (lock ⇆ unlock).
            if (GridInput.PPressed) ToggleAllLandingGear();

            // The warp key (U by default) engages / fires the Warp Drive (the ONLY warp
            // in the game — an expensive, chargeable grid block). First press begins
            // charging; second fires when ready.
            // The Jump Drive dial owns the warp key when one exists: holding it opens
            // the destination wheel and its centre carries the charge / fire control,
            // so the key must not also fire the legacy one-shot handler here.
            if (!JumpDriveWheel.OwnsWarpKey
                && VoxelEngine.Settings.GameSettings.WasPressed(VoxelEngine.Settings.InputAction.WarpDrive))
                HandleWarpDriveInput();

            ReadFlightInput();
        }

        private void LateUpdate()
        {
            if (Pilot == null || _freeLooking) return;
            ApplyCameraMode();
        }

        /// <summary>
        /// Warp Drive input: charge when idle, fire when ready. Only ONE drive per grid
        /// is engaged (the first enabled one). Requires vacuum — the drive refuses to
        /// charge or jump inside an atmosphere.
        /// </summary>
        private void HandleWarpDriveInput()
        {
            if (Grid == null) return;
            if (VoxelEngine.UI.ConfirmDialogHud.IsOpen) return;
            GridWarpDrive drive = null;
            foreach (var block in Grid.AllBlocks)
            {
                if (block is GridWarpDrive candidate && candidate.Enabled)
                {
                    drive = candidate;
                    break;
                }
            }
            if (drive == null)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Warp Drive", "No warp drive on this grid", null, new Color(1f, 0.7f, 0.25f));
                return;
            }

            if (drive.IsReady)
                drive.TryWarp();
            else if (!drive.IsCharging)
                drive.BeginCharge();
            else
                drive.TryWarp(); // shows the charge % toast while charging
        }

        /// <summary>Ctrl+Z — lock/unlock the grid in the pilot's sight as the ship's
        /// relative dampener target (match its velocity instead of braking to rest).</summary>
        private void ToggleDampenerReference()
        {
            if (Grid == null) return;
            var cam = Camera.main;
            Transform eye = cam != null ? cam.transform : transform;

            GridEntity target = null;
            float best = float.MaxValue;
            var hits = Physics.RaycastAll(eye.position, eye.forward, 3000f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                var g = hits[i].collider.GetComponentInParent<GridEntity>();
                if (g == null || g == Grid || g.Body == null) continue;
                if (hits[i].distance < best) { best = hits[i].distance; target = g; }
            }

            if (target != null && target != Grid.DampenerReferenceGrid)
            {
                Grid.DampenerReferenceGrid = target;
                VoxelEngine.UI.BuildFeedbackHud.Show("Relative Dampeners",
                    $"Matching velocity: {target.name}", null, new Color(0.35f, 0.90f, 0.80f));
            }
            else if (Grid.DampenerReferenceGrid != null)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Relative Dampeners",
                    $"Lock cleared: {Grid.DampenerReferenceGrid.name}", null, new Color(1f, 0.70f, 0.25f));
                Grid.DampenerReferenceGrid = null;
            }
            else
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Relative Dampeners",
                    "No grid under the crosshair", null, new Color(1f, 0.70f, 0.25f));
            }
        }

        private bool IsThirdPerson => _cameraDistance > THIRD_PERSON_THRESHOLD;

        /// <summary>How far the cockpit camera is currently scrolled out from the grid centre.</summary>
        public float CameraDistance => _cameraDistance;

        /// <summary>True once the pilot has scrolled out of first-person into the chase camera.</summary>
        public bool IsExteriorView => IsThirdPerson;

        /// <summary>
        /// The second zoom-out stage. The trajectory overlay only draws here, so the predicted
        /// path never covers the cockpit glass in first-person or the tight chase view.
        /// </summary>
        public bool IsWideExteriorView => _cameraDistance > TRAJECTORY_VIEW_DISTANCE;

        private void UpdateCameraZoom()
        {
            float scroll = GridInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetCameraDistance = Mathf.Clamp(
                    _targetCameraDistance - scroll * zoomSpeed,
                    0f, maxCameraDistance);
            }
            _cameraDistance = Mathf.MoveTowards(_cameraDistance, _targetCameraDistance, zoomSmooth * Time.deltaTime);

            // Alt is only required while changing the orbit. Once released, retain
            // the chosen exterior view until the pilot moves it again or double-presses
            // Alt to return home.
            ApplyCameraMode();
        }

        private void HandleThirdPersonOrbitResetShortcut()
        {
            if (!IsThirdPerson || !GridInput.AltPressed) return;
            float now = Time.unscaledTime;
            if (now - _lastAltPressAt <= AltDoublePressSeconds)
            {
                _orbitYaw = 0f;
                _orbitPitch = 0f;
                _freeOrbiting = false;
                _lastAltPressAt = -999f;
                ApplyCameraMode();
                return;
            }
            _lastAltPressAt = now;
        }

        private void ReadFlightInput()
        {
            // Translation — WASD + Space/C (relative to the cockpit's facing).
            float fwd   = (Held(InputAction.Forward) ? 1 : 0) - (Held(InputAction.Back) ? 1 : 0);
            float right = (Held(InputAction.Right) ? 1 : 0)   - (Held(InputAction.Left) ? 1 : 0);
            bool descendHeld = Held(InputAction.Crouch) || CKeyHeld();
            float up    = (Held(InputAction.Jump) ? 1 : 0)    - (descendHeld ? 1 : 0);
            Vector3 thrust = new Vector3(right, up, fwd);

            // Rotation — Q/E roll + mouse look (yaw/pitch) via gyroscopes.
            // Roll is a steady digital key (full 1.0) while yaw/pitch come from small mouse
            // deltas, so raw roll felt FAR too aggressive. Scale it down to match the feel
            // of mouse turning (tunable: lower = gentler roll).
            const float ROLL_SENS = 0.35f;
            float roll = ((GridInput.Q ? 1 : 0) - (GridInput.E ? 1 : 0)) * ROLL_SENS;

            Vector2 md = GridInput.MouseDelta;
            float mouseX = md.x, mouseY = md.y;

            // Tool cycle (T) — moved off the scroll wheel so scroll can control camera zoom.
            if (GameSettings.WasPressed(InputAction.ToolCycle))
            {
                int n = Grid.ToolGroupCount;
                if (n > 0) Grid.SelectedToolIndex = (Grid.SelectedToolIndex + 1) % n;
            }

            // Alt = free-look / free-orbit. Mouse movement otherwise controls the ship gyros.
            if (GridInput.Alt)
            {
                if (IsThirdPerson)
                {
                    // In third-person, Alt orbits the camera around the grid center while
                    // the gyros stay idle.
                    OrbitCamera(mouseX, mouseY);
                    _freeOrbiting = true;
                    ApplyCameraMode();
                }
                else
                {
                    FreeLook(mouseX, mouseY);
                }
                Grid.SetFlightInput(thrust, 0f, 0f, roll); // no gyro turn while free-looking
                return;
            }
            ResetFreeLook();

            // Track third-person mode so we can zero the orbit when returning to first-person.
            // Mouse look here always drives the ship gyros; camera orbit is Alt-only.
            if (IsThirdPerson)
            {
                _freeOrbiting = true;
            }
            else if (_freeOrbiting)
            {
                _orbitYaw = 0f;
                _orbitPitch = 0f;
                _freeOrbiting = false;
            }

            float sens = 0.06f;
            float yaw   = Mathf.Clamp(mouseX * sens, -1f, 1f);
            float pitch = Mathf.Clamp(-mouseY * sens, -1f, 1f);

            // Autopilot owns the nose while engaged: mouse gyro is parked so it can't
            // fight the flight plan, including warp aim — the gyros line the cone up.
            if (Grid != null && VoxelEngine.Navigation.NavFlightAutopilot.ActiveGrid == Grid)
            {
                yaw = 0f; pitch = 0f;
            }

            Grid.SetFlightInput(thrust, yaw, pitch, roll);

            // ── Maritime integration ───────────────────────────────────
            // If this ship has a MaritimePropulsionSystem, drive its throttle + steer
            // from the cockpit too (W = throttle, mouse yaw = rudder steer).
            DriveMaritime(fwd, yaw);

            // While the Drill group is selected: LMB = mine + collect, RMB = mine + VOID (faster).
            Grid.DrillVoidMode = GridInput.Mouse1 && !GridInput.Mouse0;
        }

        private bool PilotHoldingGridBlock()
        {
            if (GridBuilder.HoldingGridBlock) return true;
            if (Pilot == null) return false;

            var inventory = Pilot.GetComponentInParent<VoxelEngine.Items.Inventory>();
            if (inventory == null) return false;

            var stack = inventory.ActiveStack;
            return stack != null && !stack.IsEmpty && stack.item is GridBlockItem;
        }

        private static bool CKeyHeld()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.cKey.isPressed;
#else
            return Input.GetKey(KeyCode.C);
#endif
        }

        private static bool VPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.V);
#endif
            }
        }

        private static bool Held(InputAction a) => GameSettings.IsHeld(a);

        private void OrbitCamera(float mouseX, float mouseY)
        {
            float orbitSens = orbitSensitivity;
            _orbitYaw   += mouseX * orbitSens;
            _orbitPitch += -mouseY * orbitSens;

            // Yaw is a full, seamless 360° ring; keep it in a clean range so the
            // return-to-follow direction is predictable when Alt is released. Pitch is
            // clamped against the poles in ApplyCameraMode — letting it wrap across
            // ±90° flipped the camera's up basis and snapped the view 180°.
            _orbitYaw = Mathf.Repeat(_orbitYaw + 180f, 360f) - 180f;
        }

        /// <summary>P key — if ANY landing gear is locked, unlock them all; otherwise lock them all.</summary>
        private void ToggleAllLandingGear()
        {
            if (Grid == null) return;
            bool anyLocked = false;
            foreach (var block in Grid.AllBlocks)
                if (block is GridLandingGear lg && lg.IsLocked) { anyLocked = true; break; }

            foreach (var block in Grid.AllBlocks)
            {
                if (!(block is GridLandingGear lg)) continue;
                if (anyLocked) lg.Unlock();
                else           lg.TryLock();
            }
        }

        // ── Free-look (hold Alt) ───────────────────────────────────────────────
        private float _lookYaw, _lookPitch;
        private bool  _freeLooking;

        private void FreeLook(float mouseX, float mouseY)
        {
            var pivot = Pilot != null ? Pilot.cameraPivot : null;
            if (pivot == null) return;

            float sens = 2.0f;
            _lookYaw   = Mathf.Clamp(_lookYaw   + mouseX * sens, -120f, 120f);
            _lookPitch = Mathf.Clamp(_lookPitch - mouseY * sens, -80f, 80f);
            // Apply look input in the seated camera's basis. Replacing the local rotation
            // with a bare Euler angle discarded the SeatAnchor's 180-degree facing, so Alt
            // pointed the pilot through the back of the cabin. Legacy cockpits keep their
            // captured standing basis; generated shells keep their authored forward view.
            Quaternion baseRotation = _hasSeatPose ? _seatPivotRotation : _defaultPivotLocalRotation;
            pivot.localRotation = baseRotation * Quaternion.Euler(_lookPitch, _lookYaw, 0f);
            _freeLooking = true;
        }

        private void ResetFreeLook()
        {
            if (!_freeLooking) return;
            _freeLooking = false;
            _lookYaw = 0f; _lookPitch = 0f;
            ApplyCameraMode();
        }

        private void ToggleCameraMode()
        {
            if (_cameraDistance > THIRD_PERSON_THRESHOLD)
            {
                _targetCameraDistance = 0f;
                _orbitYaw = 0f;
                _orbitPitch = 0f;
            }
            else
            {
                _targetCameraDistance = maxCameraDistance * 0.6f;
            }
            _freeLooking = false;
            _lookYaw = 0f;
            _lookPitch = 0f;
            _lastAltPressAt = -999f;
        }

        private void CaptureDefaultCameraPose(Player.PlayerController player)
        {
            var pivot = player != null ? player.cameraPivot : null;
            if (pivot == null || _hasDefaultCameraPose) return;
            _defaultPivotLocalPosition = pivot.localPosition;
            _defaultPivotLocalRotation = pivot.localRotation;
            _hasDefaultCameraPose = true;
        }

        /// <summary>
        /// Work out where this cockpit wants the pilot's eyes. A shell that has been through
        /// Setup carries a SeatAnchor; one that has not keeps the standing pose, so an old
        /// cockpit degrades to what it always was instead of dropping the camera into a wall.
        /// </summary>
        private void CaptureSeatPose()
        {
            _hasSeatPose = false;
            _seatPivotOffset = Vector3.zero;
            _seatPivotRotation = _defaultPivotLocalRotation;

            if (!_hasDefaultCameraPose) return;
            if (!CockpitShellBuilder.TryGetSeatAnchor(gameObject, out Vector3 seatPosition, out Quaternion seatRotation))
                return;

            // The player rig is parented to the cockpit with identity rotation, so cockpit
            // local and rig local are the same frame: the pivot only has to move by the
            // difference between where the eye is and where the seat is.
            _seatPivotOffset = seatPosition - _defaultPivotLocalPosition;
            _seatPivotRotation = _defaultPivotLocalRotation * seatRotation;
            _hasSeatPose = true;
        }

        private void ApplyCameraMode()
        {
            var pivot = Pilot != null ? Pilot.cameraPivot : null;
            if (pivot == null || !_hasDefaultCameraPose) return;

            if (IsThirdPerson)
            {
                Vector3 gridCenter = Grid != null ? Grid.GetGridCenter() : transform.position;

                // Default camera offset: from grid center, behind the cockpit, at the current distance.
                Vector3 toCockpit = transform.position - gridCenter;
                float baseRadius = toCockpit.magnitude;
                Vector3 baseDir = baseRadius > 0.001f ? toCockpit / baseRadius : transform.forward;
                Vector3 offset = baseDir * (baseRadius + _cameraDistance);

                // Full 360° spherical orbit around the grid center (full-orbit camera style).
                // Yaw rotates around the grid's up axis, pitch rotates around the camera's right axis.
                if (Mathf.Abs(_orbitYaw) > 0.001f)
                    offset = Quaternion.AngleAxis(_orbitYaw, transform.up) * offset;

                // Pitch: clamp the TOTAL elevation short of the poles. Crossing
                // straight over the top/bottom flipped the derived up vector — the
                // old "camera snaps to the other side" when orbiting ~180° around.
                float baseElevation = 90f - Vector3.Angle(transform.up, offset);
                _orbitPitch = Mathf.Clamp(_orbitPitch, -85f - baseElevation, 85f - baseElevation);
                Vector3 right = Vector3.Cross(transform.up, offset);
                if (Mathf.Abs(_orbitPitch) > 0.001f && right.sqrMagnitude > 0.0001f)
                {
                    right.Normalize();
                    offset = Quaternion.AngleAxis(_orbitPitch, right) * offset;
                }

                // Camera position is exactly on the sphere around the grid center.
                Vector3 targetPos = gridCenter + offset;

                // Look at the grid center so the ship is always framed by its pivot point.
                Vector3 lookDir = gridCenter - targetPos;
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    // Derive the camera's up vector from the ship's up so the view stays upright
                    // even when the camera is directly above or below the grid center.
                    Vector3 camRight = Vector3.Cross(transform.up, lookDir);
                    if (camRight.sqrMagnitude < 0.0001f)
                        camRight = transform.right; // fallback at the poles so the camera doesn't roll
                    camRight.Normalize();
                    Vector3 camUp = Vector3.Cross(lookDir, camRight).normalized;
                    Quaternion targetRot = Quaternion.LookRotation(lookDir, camUp);
                    pivot.SetPositionAndRotation(targetPos, targetRot);
                }
            }
            else
            {
                // First person: either the seated pose the shell authored, or - for a
                // cockpit that predates the shells - the standing pose exactly as before.
                pivot.localPosition = _defaultPivotLocalPosition + (_hasSeatPose ? _seatPivotOffset : Vector3.zero);
                pivot.localRotation = _hasSeatPose ? _seatPivotRotation : _defaultPivotLocalRotation;
            }
        }

        /// <summary>The cell this seat occupies on its hull. A seat is addressed the
        /// same way a block is - (grid, integer cell) - so the address survives the
        /// ship moving and a hull with several cockpits has several seats.</summary>
        private Vector3Int SeatCell => GridPos;   // GridCockpit IS a GridBlock

        public void Enter(Player.PlayerController player)
        {
            // Pilot is only this MACHINE's answer. Before 14.26.1 that was the whole
            // check, so two players on two machines each saw an empty seat and both
            // sat in it. Occupancy is replicated now and asked first.
            if (Pilot != null || VoxelEngine.Networking.GridSync.IsSeatOccupied(Grid, SeatCell))
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Cockpit occupied",
                    "Another player is flying from this seat.");
                return;
            }

            Pilot = player;
            CaptureDefaultCameraPose(player);
            CaptureSeatPose();
            _cameraDistance = 0f;
            _targetCameraDistance = 0f;
            _orbitYaw = 0f;
            _orbitPitch = 0f;
            _freeOrbiting = false;
            _lastAltPressAt = -999f;
            ApplyCameraMode();
            player.enabled = false;
            // The player uses a CharacterController (not a Rigidbody) — disable it
            // so the seated player doesn't collide while piloting. Guard both so a
            // different player rig never throws.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                _pilotRbWasKinematic = rb.isKinematic;
                rb.isKinematic = true;
            }

            // Parent the player to the cockpit so they ride along with the grid as
            // it moves/rolls/rotates (fixes "ship rolls away, player stays put").
            _originalParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            player.transform.position = transform.position;
            player.transform.localRotation = Quaternion.identity;

            // Take control of the grid for flight + mark this as the active seat so
            // the "I" key opens the master terminal instead of the player inventory.
            if (Grid != null) Grid.ActiveCockpit = this;
            ActivePilotSeat = this;
            ActiveControlSeat = this;
            ActiveControlPilot = player;
            // 14.26.0: ask the host for control of this hull. On the host and
            // offline this only records the claim - that machine already flies it.
            VoxelEngine.Networking.GridSync.ClaimControl(Grid, SeatCell);

            // Rebuild the HUD now so the on-foot hotbar is hidden immediately on entry
            // (BuildHotbar skips while ActivePilotSeat != null) — the ship toolbar replaces it.
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();

            // Enable camera FOV feedback only while actually piloting this grid, starting clean at rest.
            VoxelEngine.Player.CameraFeedback.ResetTransient();
            VoxelEngine.Player.CameraFeedback.IsPiloting = true;
        }

        private Transform _originalParent;
        // 14.64.2 — the pre-seat kinematic state of the rig's helper rigidbody,
        // captured on Enter and RESTORED on Exit (never forced dynamic).
        private bool _pilotRbWasKinematic = true;

        // ── Maritime integration ──────────────────────────────────────
        // When the ship has a MaritimePropulsionSystem, the cockpit doubles as the
        // helm: W = throttle up, S = throttle down, mouse-yaw = rudder steer.
        private float _maritimeThrottle;

        private void DriveMaritime(float fwdAxis, float yawAxis)
        {
            var maritime = Grid?.Maritime;
            if (maritime == null) return;

            const float ramp = 1.5f;
            if (fwdAxis > 0.01f)
                _maritimeThrottle = Mathf.MoveTowards(_maritimeThrottle, 1f, ramp * Time.deltaTime);
            else if (fwdAxis < -0.01f)
                _maritimeThrottle = Mathf.MoveTowards(_maritimeThrottle, 0f, ramp * Time.deltaTime);

            maritime.Throttle = _maritimeThrottle;
            maritime.Steer = yawAxis;
            maritime.HelmActive = true;
        }

        private void ZeroMaritime()
        {
            _maritimeThrottle = 0f;
            var maritime = Grid?.Maritime;
            if (maritime != null)
            {
                maritime.Throttle = 0f;
                maritime.Steer = 0f;
                maritime.HelmActive = false;
            }
        }

        /// <summary>Open the grid-terminal master terminal for this grid.</summary>
        public void OpenTerminal()
        {
            VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(Grid);
        }

        /// <summary>14.64.2 / 17.43.0 — pick a drop spot outside the full cockpit collider
        /// (including 2x2x2 cockpits) so the player capsule never starts inside the hull.</summary>
        private Vector3 FindClearExitPosition()
        {
            var box = GetComponent<BoxCollider>();
            Vector3 localCenter = box != null ? box.center : Vector3.zero;
            Vector3 halfExtents = box != null ? box.size * 0.5f : Vector3.one;
            Vector3 worldCenter = transform.TransformPoint(localCenter);

            var cc = Pilot != null ? Pilot.GetComponent<CharacterController>() : null;
            float radius = cc != null ? Mathf.Max(0.25f, cc.radius * 0.95f) : 0.38f;
            float height = cc != null ? Mathf.Max(radius * 2f + 0.1f, cc.height * 0.95f) : 1.7f;

            float clearance = radius + 0.45f;
            float sideDist = halfExtents.x + clearance;
            float foreAftDist = halfExtents.z + clearance;
            float floorY = -halfExtents.y + 0.15f;
            float roofY = halfExtents.y + 0.25f;

            Vector3 fallback = worldCenter + transform.up * floorY + transform.right * sideDist;
            if (Pilot == null) return fallback;

            Vector3[] offsets =
            {
                transform.up * floorY + transform.right * sideDist,
                transform.up * floorY - transform.right * sideDist,
                transform.up * floorY + transform.forward * foreAftDist,
                transform.up * roofY,
                transform.up * floorY - transform.forward * foreAftDist,
            };
            var scratch = new Collider[8];
            for (int o = 0; o < offsets.Length; o++)
            {
                Vector3 pos = worldCenter + offsets[o];
                Vector3 foot = pos + transform.up * radius;
                Vector3 head = pos + transform.up * (height - radius);
                int n = Physics.OverlapCapsuleNonAlloc(foot, head, radius, scratch,
                    ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < n; i++)
                {
                    var col = scratch[i];
                    if (col == null) continue;
                    if (col.transform.IsChildOf(Pilot.transform)) continue;
                    blocked = true;
                    break;
                }
                if (!blocked) return pos;
            }
            return fallback;
        }

        public void Exit()
        {
            if (Pilot == null) return;

            // Release maritime controls (stop engines + rudder).
            ZeroMaritime();

            // Restore first-person camera before unparenting/leaving the cockpit. The seat
            // pose is cleared first so the eye is back at standing height before the rig is
            // moved out of the cockpit.
            _hasSeatPose = false;
            _cameraDistance = 0f;
            _targetCameraDistance = 0f;
            _orbitYaw = 0f;
            _orbitPitch = 0f;
            _freeOrbiting = false;
            _freeLooking = false;
            _lastAltPressAt = -999f;
            ApplyCameraMode();

            // Unparent the player from the grid and drop them beside the cockpit.
            Pilot.transform.SetParent(_originalParent, worldPositionStays: true);
            Pilot.transform.position = FindClearExitPosition();

            // Prevent the shared 'F' key press from also toggling jetpack flight on this frame.
            Pilot.SuppressFlyToggleThisFrame();
            Pilot.enabled = true;
            var cc = Pilot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            var rb = Pilot.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 14.64.2 — RESTORE the pre-seat state instead of forcing the body
                // dynamic. Exit used to flip the helper rigidbody live, and from
                // then on PhysX depenetration fought the CharacterController every
                // frame: the exit "ship bounce" plus impact damage, blocks smashed
                // at walking speed and the residual deck jitter all traced back to
                // that one permanently dynamic body.
                rb.isKinematic = _pilotRbWasKinematic;
                if (!rb.isKinematic)
                {
                    var hull = Grid != null ? Grid.Body : null;
                    rb.linearVelocity = hull != null
                        ? hull.GetPointVelocity(Pilot.transform.position)
                        : Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            // 14.61.0 - leaving the seat INHERITS the ship's velocity: stepping out
            // of a cruising ship used to zero the player against the world, so the
            // hull instantly sailed away (or slammed into you). The player now
            // leaves the seat co-moving with the hull; the magnetic boots grab on
            // touch, and the jetpack dampeners null velocity relative to the grid.
            var hullBody = Grid != null ? Grid.Body : null;
            if (hullBody != null)
                Pilot.SetVelocity(hullBody.GetPointVelocity(Pilot.transform.position));

            // 14.64.1 - leaving ANY control seat locks the relative dampeners to
            // this grid: you step out already station-keeping with your own ship
            // (the 200 m leash or Ctrl+Z releases it later).
            Pilot.ForceDampenersOn();
            var exitBoots = Pilot.GetComponent<VoxelEngine.Player.MagneticBoots>();
            if (exitBoots != null && Grid != null)
                exitBoots.LockReference(Grid);

            VoxelEngine.Networking.GridSync.ReleaseControl();
            Pilot = null;
            if (Grid != null && Grid.ActiveCockpit == this) Grid.ActiveCockpit = null;
            if (ActivePilotSeat == this) ActivePilotSeat = null;
            if (ActiveControlSeat == this)
            {
                ActiveControlSeat = null;
                ActiveControlPilot = null;
            }
            VoxelEngine.UI.GameUIController.Instance?.CloseAll();

            // Clear camera transient offsets cleanly on seat exit.
            VoxelEngine.Player.CameraFeedback.IsPiloting = false;
            VoxelEngine.Player.CameraFeedback.ResetTransient();
        }

        public void SwitchToSmallGrid()
        {
            GridSizeSwitcher.Instance?.SwitchGrid(Grid, GridSize.Small);
        }

        public void SwitchToLargeGrid()
        {
            GridSizeSwitcher.Instance?.SwitchGrid(Grid, GridSize.Large);
        }
    }
}