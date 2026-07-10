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

        public override float PowerDraw => Enabled ? idleWatts : 0f;

        public Player.PlayerController Pilot { get; private set; }

        public bool IsFlightOnline => Enabled && Grid != null && Grid.HasPower;

        private bool _hasDefaultCameraPose;
        private Vector3 _defaultPivotLocalPosition;
        private Quaternion _defaultPivotLocalRotation;

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
        private const float THIRD_PERSON_THRESHOLD = 0.35f;

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

        private void Update()
        {
            if (Pilot == null) return;

            if (GameSettings.WasPressed(InputAction.ExitCockpit)) { Exit(); return; }

            // While a full UI panel (terminal/inventory) is open, release control + cursor.
            if (VoxelEngine.UI.UIState.IsBlocking || Grid == null)
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
            if (GridInput.ZPressed) Grid.DampenersOn = !Grid.DampenersOn;

            // P toggles ALL landing gear on the grid (lock ⇆ unlock).
            if (GridInput.PPressed) ToggleAllLandingGear();

            ReadFlightInput();
        }

        private bool IsThirdPerson => _cameraDistance > THIRD_PERSON_THRESHOLD;

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

            // Camera orbit is only active while Alt is held. When Alt is released, smoothly
            // return the orbit offset to zero so the camera follows the ship from its default
            // position while the mouse controls the gyros.
            if (!GridInput.Alt)
            {
                const float ORBIT_RETURN_SPEED = 120f; // degrees per second
                _orbitYaw   = Mathf.MoveTowardsAngle(_orbitYaw,   0f, ORBIT_RETURN_SPEED * Time.deltaTime);
                _orbitPitch = Mathf.MoveTowardsAngle(_orbitPitch, 0f, ORBIT_RETURN_SPEED * Time.deltaTime);
            }

            ApplyCameraMode();
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

            // Full 360° sphere around the grid center; keep angles in a clean range so the
            // return-to-follow direction is predictable when Alt is released.
            _orbitYaw   = Mathf.Repeat(_orbitYaw   + 180f, 360f) - 180f;
            _orbitPitch = Mathf.Repeat(_orbitPitch + 180f, 360f) - 180f;
        }

        /// <summary>P key — if ANY landing gear is locked, unlock them all; otherwise lock them all.</summary>
        private void ToggleAllLandingGear()
        {
            if (Grid == null) return;
            bool anyLocked = false;
            foreach (var kv in Grid.Blocks)
                if (kv.Value is GridLandingGear lg && lg.IsLocked) { anyLocked = true; break; }

            foreach (var kv in Grid.Blocks)
            {
                if (!(kv.Value is GridLandingGear lg)) continue;
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
            // Offset the camera relative to the cockpit's facing (player is parented to it).
            pivot.localRotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
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
        }

        private void CaptureDefaultCameraPose(Player.PlayerController player)
        {
            var pivot = player != null ? player.cameraPivot : null;
            if (pivot == null || _hasDefaultCameraPose) return;
            _defaultPivotLocalPosition = pivot.localPosition;
            _defaultPivotLocalRotation = pivot.localRotation;
            _hasDefaultCameraPose = true;
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

                Vector3 right = Vector3.Cross(transform.up, offset);
                if (right.sqrMagnitude > 0.0001f)
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
                // Restore the first-person cockpit pose.
                pivot.localPosition = _defaultPivotLocalPosition;
                pivot.localRotation = _defaultPivotLocalRotation;
            }
        }

        public void Enter(Player.PlayerController player)
        {
            if (Pilot != null) return;

            Pilot = player;
            CaptureDefaultCameraPose(player);
            _cameraDistance = 0f;
            _targetCameraDistance = 0f;
            _orbitYaw = 0f;
            _orbitPitch = 0f;
            _freeOrbiting = false;
            ApplyCameraMode();
            player.enabled = false;
            // The player uses a CharacterController (not a Rigidbody) — disable it
            // so the seated player doesn't collide while piloting. Guard both so a
            // different player rig never throws.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

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

            // Rebuild the HUD now so the on-foot hotbar is hidden immediately on entry
            // (BuildHotbar skips while ActivePilotSeat != null) — the ship toolbar replaces it.
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();

            // Enable camera screenshake/FOV feedback only while actually piloting this grid.
            VoxelEngine.Player.CameraFeedback.IsPiloting = true;
        }

        private Transform _originalParent;

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

        public void Exit()
        {
            if (Pilot == null) return;

            // Release maritime controls (stop engines + rudder).
            ZeroMaritime();

            // Restore first-person camera before unparenting/leaving the cockpit.
            _cameraDistance = 0f;
            _targetCameraDistance = 0f;
            _orbitYaw = 0f;
            _orbitPitch = 0f;
            _freeOrbiting = false;
            _freeLooking = false;
            ApplyCameraMode();

            // Unparent the player from the grid and drop them beside the cockpit.
            Pilot.transform.SetParent(_originalParent, worldPositionStays: true);
            Pilot.transform.position = transform.position + transform.up * 1.2f + transform.right * 1.5f;

            Pilot.enabled = true;
            var cc = Pilot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            var rb = Pilot.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            Pilot = null;
            if (Grid != null && Grid.ActiveCockpit == this) Grid.ActiveCockpit = null;
            if (ActivePilotSeat == this) ActivePilotSeat = null;
            if (ActiveControlSeat == this)
            {
                ActiveControlSeat = null;
                ActiveControlPilot = null;
            }
            VoxelEngine.UI.GameUIController.Instance?.CloseAll();

            // Camera shake/FOV warp is for piloting only — turn it off when leaving the seat.
            VoxelEngine.Player.CameraFeedback.IsPiloting = false;
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