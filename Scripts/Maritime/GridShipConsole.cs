// Assets/Scripts/VoxelEngine/Maritime/GridShipConsole.cs
//
// Ship Control Console — a modern alternative to the Helm. Acts identically
// (enter via right-click, W/S throttle, A/D steer, scroll zoom) but has a
// sleek console/throttle-lever aesthetic instead of a ship's wheel.

using UnityEngine;
using VoxelEngine.GridSystem;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Maritime
{
    public class GridShipConsole : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Hull;

        [Header("Ship Console")]
        public float interactionRadius = 3f;
        public float throttleRampSpeed = 1.5f;
        public float steerReturnSpeed = 4f;

        [Header("Camera")]
        public float minCameraDist = 5f;
        public float maxCameraDist = 40f;
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
        private Transform _origParent;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Ship Control Console";
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            Exit();
        }

        public void Enter(Player.PlayerController player)
        {
            if (IsActive || player == null) return;
            IsActive = true;
            Pilot = player;
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
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
            ApplyCamera();

            _origParent = player.transform.parent;
            player.transform.SetParent(transform, true);
            if (Grid != null) _maritime = Grid.Maritime;
        }

        public void Exit()
        {
            if (!IsActive) { IsActive = false; return; }
            if (_maritime != null)
            {
                _maritime.Throttle = 0f;
                _maritime.Steer = 0f;
                _maritime.HelmActive = false;
            }
            ThrottleSetting = 0f;
            _currentSteer = 0f;

            if (_pilotCamPivot != null && _camCached)
            {
                _pilotCamPivot.localPosition = _origPivotPos;
                _pilotCamPivot.localRotation = _origPivotRot;
            }
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
            if (!IsActive || Pilot == null) return;

            bool exitPressed = ExitPressed;
            bool rightClick = GridInput.Mouse1 && !GridInput.Mouse0;
            if (exitPressed || rightClick) { Exit(); return; }

            float dt = Time.deltaTime;
            float fwd = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Forward) ? 1 : 0;
            float back = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Back) ? 1 : 0;
            if (fwd > 0) ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 1f, throttleRampSpeed * dt);
            else if (back > 0) ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 0f, throttleRampSpeed * dt);

            bool left = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Left);
            bool right = VoxelEngine.Settings.GameSettings.IsHeld(InputAction.Right);
            float steerTarget = (right ? 1f : 0f) - (left ? 1f : 0f);
            _currentSteer = Mathf.MoveTowards(_currentSteer, steerTarget, steerReturnSpeed * dt);

            float scroll = GridInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _cameraDist = Mathf.Clamp(_cameraDist - scroll * 0.02f, minCameraDist, maxCameraDist);
                ApplyCamera();
            }

            if (_maritime == null && Grid != null) _maritime = Grid.Maritime;
            if (_maritime != null)
            {
                _maritime.Throttle = ThrottleSetting;
                _maritime.Steer = _currentSteer;
                _maritime.HelmActive = true;
            }
        }

        private void ApplyCamera()
        {
            if (_pilotCamPivot == null) return;
            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
            Vector3 helmLocal = transform.localPosition;
            Vector3 camLocal = new Vector3(helmLocal.x, helmLocal.y + _cameraDist * 0.45f + cs, helmLocal.z - _cameraDist);
            _pilotCamPivot.localPosition = camLocal;
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
