// Assets/Scripts/VoxelEngine/Maritime/GridHelm.cs
//
// Helm (Ship's Wheel) — the dedicated maritime control station.
//
//   • Right-click to ENTER — a third-person camera positions above the helm
//     so you can see the wheel sticks and the water ahead.
//   • W/S = throttle up/down, A/D = steer left/right.
//   • Scroll wheel = zoom in/out (ship-size-aware default distance).
//   • Right-click again or press F to EXIT.
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

            // Disable player movement.
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // Lock cursor.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // Cache + position the camera pivot.
            _pilotCamPivot = player.cameraPivot;
            if (_pilotCamPivot != null && !_camCached)
            {
                _origPivotPos = _pilotCamPivot.localPosition;
                _origPivotRot = _pilotCamPivot.localRotation;
                _camCached = true;
            }

            // Compute ship-size-aware camera distance.
            int blockCount = Grid != null ? Grid.BlockCount : 1;
            _cameraDist = Mathf.Clamp(blockCount * shipSizeCameraFactor + 8f, minCameraDist, maxCameraDist);

            ApplyCamera();

            // Parent the player to the helm so they ride with the ship.
            _origParent = player.transform.parent;
            player.transform.SetParent(transform, true);

            if (Grid != null) _maritime = Grid.Maritime;
            if (Grid != null) Grid.ActiveCockpit = null; // not a flight cockpit
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
                Pilot.enabled = true;
                var cc = Pilot.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

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

            // Exit on F or right-click.
            bool exitPressed = ExitPressed;
            bool rightClick = GridInput.Mouse1 && !GridInput.Mouse0;
            if (exitPressed || rightClick)
            {
                Exit();
                return;
            }

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
                ApplyCamera();
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

        /// <summary>Position the camera pivot above + behind the helm.</summary>
        private void ApplyCamera()
        {
            if (_pilotCamPivot == null) return;

            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
            // Camera sits above the helm, looking forward over the ship.
            Vector3 helmLocal = transform.localPosition;
            Vector3 camLocal = new Vector3(
                helmLocal.x,
                helmLocal.y + _cameraDist * 0.45f + cs,  // above the wheel sticks
                helmLocal.z - _cameraDist);               // behind the helm

            _pilotCamPivot.localPosition = camLocal;
            // Tilt to look forward + down at the water.
            float pitch = 15f + (_cameraDist / maxCameraDist) * 20f;
            _pilotCamPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private static bool ExitPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.F);
#endif
            }
        }
    }
}
