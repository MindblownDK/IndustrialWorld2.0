// Assets/Scripts/VoxelEngine/Maritime/GridHelm.cs
//
// Helm (Ship's Wheel) — the dedicated maritime control station.
//
//   • Right-click to ENTER — a third-person camera positions above the helm
//     so you can see the wheel sticks and the water ahead.
//   • W/S = throttle up/down, A/D = steer left/right.
//   • Scroll wheel = zoom in/out (ship-size-aware default distance).
//   • Press the cockpit exit key to EXIT (same flow as GridCockpit).
//
// The Helm drives the parent grid's MaritimePropulsionSystem:
//   system.Throttle, system.Steer, system.HelmActive

using UnityEngine;
using VoxelEngine.GridSystem;
using InputAction = VoxelEngine.Settings.InputAction;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.Maritime
{
    public class GridHelm : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Hull;

        [Header("Helm")]
        public float interactionRadius = 3f;
        public float throttleRampSpeed = 1.5f;
        public float steerReturnSpeed = 4f;

        [Header("Camera")]
        [Tooltip("Minimum camera distance behind the helm (metres).")]
        public float minCameraDist = 5f;
        [Tooltip("Maximum camera distance behind the helm (metres).")]
        public float maxCameraDist = 40f;
        [Tooltip("How much the camera height scales with ship block count.")]
        public float shipSizeCameraFactor = 0.15f;

        public bool IsActive { get; private set; }
        public Player.PlayerController Pilot { get; private set; }
        public float ThrottleSetting { get; private set; }

        private MaritimePropulsionSystem _maritime;
        private float _currentSteer;
        private float _cameraDist;
        private Transform _pilotCamPivot;
        private Vector3 _origPivotPos;
        private Quaternion _origPivotRot;
        private bool _camCached;
        private bool _thirdPersonCamera = true;
        private float _lookYaw;
        private float _lookPitch;
        private bool _freeLooking;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Helm";
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            Exit();
        }

        /// <summary>Enter the helm — set up the third-person camera + lock controls.</summary>
        public void Enter(Player.PlayerController player)
        {
            if (IsActive || player == null) return;

            IsActive = true;
            Pilot = player;

            // Disable player movement exactly like a cockpit seat.
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _pilotCamPivot = player.cameraPivot;
            if (_pilotCamPivot != null && !_camCached)
            {
                _origPivotPos = _pilotCamPivot.localPosition;
                _origPivotRot = _pilotCamPivot.localRotation;
                _camCached = true;
            }

            int blockCount = Grid != null ? Grid.BlockCount : 1;
            _cameraDist = Mathf.Clamp(blockCount * shipSizeCameraFactor + 8f, minCameraDist, maxCameraDist);
            _thirdPersonCamera = true;
            _freeLooking = false;
            _lookYaw = 0f;
            _lookPitch = 0f;

            // Parent/seat the player before applying local camera offsets.
            _origParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            player.transform.position = transform.position;
            player.transform.localRotation = Quaternion.identity;

            ApplyCameraMode();

            if (Grid != null)
            {
                _maritime = Grid.Maritime;
                Grid.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                Grid.DrillVoidMode = false;
            }

            GridCockpit.RegisterAuxiliarySeat(this, player);
        }

        private Transform _origParent;

        /// <summary>Exit the helm — restore camera + player.</summary>
        public void Exit()
        {
            if (!IsActive) { IsActive = false; return; }

            // Zero the maritime controls.
            if (_maritime != null)
            {
                _maritime.Throttle = 0f;
                _maritime.Steer = 0f;
                _maritime.HelmActive = false;
            }
            ThrottleSetting = 0f;
            _currentSteer = 0f;

            _thirdPersonCamera = false;
            _freeLooking = false;
            _lookYaw = 0f;
            _lookPitch = 0f;

            // Restore camera.
            if (_pilotCamPivot != null && _camCached)
            {
                _pilotCamPivot.localPosition = _origPivotPos;
                _pilotCamPivot.localRotation = _origPivotRot;
            }

            // Re-enable player.
            if (Pilot != null)
            {
                Pilot.transform.SetParent(_origParent, true);
                Pilot.transform.position = transform.position + transform.up * 1.2f + transform.right * 1.5f;
                Pilot.enabled = true;
                var cc = Pilot.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = true;
                var rb = Pilot.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (Grid != null)
            {
                Grid.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                Grid.DrillVoidMode = false;
            }
            GridCockpit.UnregisterAuxiliarySeat(this);

            IsActive = false;
            Pilot = null;
            _pilotCamPivot = null;
        }

        private void Update()
        {
            if (!IsActive || Pilot == null)
            {
                // Check for enter via proximity (fallback if interaction tool doesn't fire).
                return;
            }

            if (ExitPressed)
            {
                Exit();
                return;
            }

            if (VoxelEngine.UI.UIState.IsBlocking)
            {
                Grid?.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (VPressed) ToggleCameraMode();
            if (GridInput.Alt) FreeLook(GridInput.MouseDelta);
            else ResetFreeLook();

            // Helm never drives space-flight thrusters directly; clear any stale flight input.
            Grid?.SetFlightInput(Vector3.zero, 0f, 0f, 0f);

            // ── Read helm input ────────────────────────────────────────
            float dt = Time.deltaTime;

            // Throttle.
            float fwd = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Forward) ? 1 : 0;
            float back = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Back) ? 1 : 0;
            if (fwd > 0)
                ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 1f, throttleRampSpeed * dt);
            else if (back > 0)
                ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 0f, throttleRampSpeed * dt);

            // Steer.
            bool left = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Left);
            bool right = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Right);
            float steerTarget = (right ? 1f : 0f) - (left ? 1f : 0f);
            _currentSteer = Mathf.MoveTowards(_currentSteer, steerTarget, steerReturnSpeed * dt);

            // ── Scroll zoom ────────────────────────────────────────────
            float scroll = GridInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _cameraDist = Mathf.Clamp(_cameraDist - scroll * 0.02f, minCameraDist, maxCameraDist);
                ApplyCameraMode();
            }

            // ── Push to maritime system ───────────────────────────────
            if (_maritime == null && Grid != null) _maritime = Grid.Maritime;
            if (_maritime != null)
            {
                _maritime.Throttle = ThrottleSetting;
                _maritime.Steer = _currentSteer;
                _maritime.HelmActive = true;
            }
        }

        /// <summary>Position the camera pivot in first/third person around the helm.</summary>
        private void ApplyCameraMode()
        {
            if (_pilotCamPivot == null) return;

            if (_thirdPersonCamera)
            {
                float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
                _pilotCamPivot.localPosition = new Vector3(0f, _cameraDist * 0.45f + cs, -_cameraDist);
                float pitch = 15f + (_cameraDist / maxCameraDist) * 20f;
                _pilotCamPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            else if (_camCached)
            {
                _pilotCamPivot.localPosition = _origPivotPos;
                _pilotCamPivot.localRotation = _origPivotRot;
            }
        }

        private void ToggleCameraMode()
        {
            _thirdPersonCamera = !_thirdPersonCamera;
            _freeLooking = false;
            _lookYaw = 0f;
            _lookPitch = 0f;
            ApplyCameraMode();
        }

        private void FreeLook(Vector2 mouseDelta)
        {
            if (_pilotCamPivot == null) return;

            const float sensitivity = 2.0f;
            _lookYaw = Mathf.Clamp(_lookYaw + mouseDelta.x * sensitivity, -140f, 140f);
            _lookPitch = Mathf.Clamp(_lookPitch - mouseDelta.y * sensitivity, -80f, 80f);
            _freeLooking = true;

            if (_thirdPersonCamera)
            {
                float basePitch = 15f + (_cameraDist / maxCameraDist) * 20f;
                _pilotCamPivot.localRotation = Quaternion.Euler(
                    Mathf.Clamp(basePitch + _lookPitch, -20f, 85f),
                    _lookYaw,
                    0f);
            }
            else
            {
                _pilotCamPivot.localRotation = _origPivotRot * Quaternion.Euler(_lookPitch, _lookYaw, 0f);
            }
        }

        private void ResetFreeLook()
        {
            if (!_freeLooking) return;
            _freeLooking = false;
            _lookYaw = 0f;
            _lookPitch = 0f;
            ApplyCameraMode();
        }

        private static bool VPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.V);
#endif
            }
        }

        private static bool ExitPressed => VoxelEngine.Settings.GameSettings.WasPressed(InputAction.ExitCockpit);
    }
}
