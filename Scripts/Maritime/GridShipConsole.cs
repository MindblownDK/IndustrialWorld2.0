// Assets/Scripts/VoxelEngine/Maritime/GridShipConsole.cs
//
// Ship Control Console — a modern cockpit-style control station.
// Right-click enters, the cockpit exit key leaves, and the console can fly
// thruster/gyro spaceships while also driving maritime throttle/rudder on boats.

using UnityEngine;
using VoxelEngine.GridSystem;
using InputAction = VoxelEngine.Settings.InputAction;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
        public bool IsFlightOnline => Enabled && Grid != null;

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

            _origParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            player.transform.position = transform.position;
            player.transform.localRotation = Quaternion.identity;

            ApplyCamera();

            if (Grid != null)
            {
                _maritime = Grid.Maritime;
                Grid.BeginExternalControl(transform, player);
                Grid.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                Grid.DrillVoidMode = false;
            }

            GridCockpit.RegisterAuxiliarySeat(this, player);
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

            if (Grid != null)
            {
                Grid.EndExternalControl(transform);
                Grid.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                Grid.DrillVoidMode = false;
            }

            if (_pilotCamPivot != null && _camCached)
            {
                _pilotCamPivot.localPosition = _origPivotPos;
                _pilotCamPivot.localRotation = _origPivotRot;
            }
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
            GridCockpit.UnregisterAuxiliarySeat(this);
            IsActive = false;
            Pilot = null;
            _pilotCamPivot = null;
        }

        private void Update()
        {
            if (!IsActive || Pilot == null) return;

            if (ExitPressed)
            {
                Exit();
                return;
            }

            if (VoxelEngine.UI.UIState.IsBlocking || Grid == null)
            {
                Grid?.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (!IsFlightOnline)
            {
                Grid.SetFlightInput(Vector3.zero, 0f, 0f, 0f);
                Grid.DrillVoidMode = false;
                DriveMaritime(0f, 0f);
                return;
            }

            if (GridInput.ZPressed) Grid.DampenersOn = !Grid.DampenersOn;
            if (GridInput.PPressed) ToggleAllLandingGear();

            ReadFlightInput();
        }

        private void ReadFlightInput()
        {
            float fwd = (Held(InputAction.Forward) ? 1f : 0f) - (Held(InputAction.Back) ? 1f : 0f);
            float right = (Held(InputAction.Right) ? 1f : 0f) - (Held(InputAction.Left) ? 1f : 0f);
            float up = (Held(InputAction.Jump) ? 1f : 0f) - (Held(InputAction.Down) ? 1f : 0f);
            Vector3 thrust = new Vector3(right, up, fwd);

            const float ROLL_SENS = 0.35f;
            float roll = ((GridInput.Q ? 1f : 0f) - (GridInput.E ? 1f : 0f)) * ROLL_SENS;

            Vector2 md = GridInput.MouseDelta;
            const float mouseSensitivity = 0.06f;
            float yaw = Mathf.Clamp(md.x * mouseSensitivity, -1f, 1f);
            float pitch = Mathf.Clamp(-md.y * mouseSensitivity, -1f, 1f);

            Grid.SetFlightInput(thrust, yaw, pitch, roll);
            DriveMaritime(fwd, yaw);

            float scroll = GridInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int n = Grid.ToolGroupCount;
                if (n > 0) Grid.SelectedToolIndex = ((Grid.SelectedToolIndex + (scroll > 0 ? 1 : -1)) % n + n) % n;
            }

            Grid.DrillVoidMode = GridInput.Mouse1 && !GridInput.Mouse0;
        }

        private void DriveMaritime(float fwdAxis, float yawAxis)
        {
            if (_maritime == null && Grid != null) _maritime = Grid.Maritime;
            if (_maritime == null) return;

            float dt = Time.deltaTime;
            if (fwdAxis > 0.01f)
                ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 1f, throttleRampSpeed * dt);
            else if (fwdAxis < -0.01f)
                ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 0f, throttleRampSpeed * dt);

            _currentSteer = Mathf.MoveTowards(_currentSteer, yawAxis, steerReturnSpeed * dt);
            _maritime.Throttle = ThrottleSetting;
            _maritime.Steer = _currentSteer;
            _maritime.HelmActive = true;
        }

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

        private void ApplyCamera()
        {
            if (_pilotCamPivot == null) return;
            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
            _pilotCamPivot.localPosition = new Vector3(0f, _cameraDist * 0.45f + cs, -_cameraDist);
            float pitch = 15f + (_cameraDist / maxCameraDist) * 20f;
            _pilotCamPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private static bool Held(InputAction action) => VoxelEngine.Settings.GameSettings.IsHeld(action);

        private static bool ExitPressed => VoxelEngine.Settings.GameSettings.WasPressed(InputAction.ExitCockpit);

    }
}
