// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Player enters this block to control the ship/vehicle. F to exit.
// All input uses the Input System (no UnityEngine.Input).

using UnityEngine;
using VoxelEngine.Player;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        public Vector3 cameraOffset = new Vector3(0, 1.5f, -0.5f);

        public PlayerController Pilot { get; private set; }

        private Camera _pilotCam;
        private float _yaw, _pitch;
        private CharacterController _playerCC;

        public void Enter(PlayerController player)
        {
            if (Pilot != null || Grid == null) return;

            Pilot = player;
            Grid.ActiveCockpit = this;

            _playerCC = player.GetComponent<CharacterController>();
            if (_playerCC != null) _playerCC.enabled = false;
            player.enabled = false;

            var renderers = player.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in renderers) r.enabled = false;

            player.transform.SetParent(transform, false);
            player.transform.localPosition = Vector3.zero;

            _pilotCam = player.GetComponentInChildren<Camera>();
            if (_pilotCam != null)
            {
                _pilotCam.transform.SetParent(transform, false);
                _pilotCam.transform.localPosition = cameraOffset;
            }

            _yaw = transform.eulerAngles.y;
            _pitch = 0;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            VoxelEngine.UI.BuildFeedbackHud.Show("Entered Cockpit",
                "WASD=Move  Space=Up  Shift=Down  Z=Dampeners  F=Exit",
                null, VoxelEngine.UI.UITheme.AccentCyan);
        }

        public void Exit()
        {
            if (Pilot == null) return;
            var player = Pilot;
            Pilot = null;
            if (Grid != null) Grid.ActiveCockpit = null;
            Grid.ThrustInput = Vector3.zero;
            Grid.RotationYaw = Grid.RotationPitch = Grid.RotationRoll = 0;

            player.transform.SetParent(null, true);
            player.transform.position = transform.position + transform.up * 2f + transform.forward * 2f;

            if (_playerCC != null) _playerCC.enabled = true;
            player.enabled = true;

            var renderers = player.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in renderers) r.enabled = true;

            if (_pilotCam != null)
            {
                var pivot = player.cameraPivot;
                if (pivot != null)
                {
                    _pilotCam.transform.SetParent(pivot, false);
                    _pilotCam.transform.localPosition = Vector3.zero;
                    _pilotCam.transform.localRotation = Quaternion.identity;
                }
            }

            VoxelEngine.UI.BuildFeedbackHud.Show("Exited Cockpit", "", null,
                VoxelEngine.UI.UITheme.AccentCyan);
        }

        private void Update()
        {
            if (Pilot == null) return;

            // ── EXIT: F key ──
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame)
            {
                Exit();
                return;
            }
#else
            if (Input.GetKeyDown(KeyCode.F)) { Exit(); return; }
#endif

            // ── MOUSE LOOK ──
            float sens = GameSettings.MouseSensitivity;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw   += delta.x * sens;
                _pitch -= delta.y * sens;
            }
#else
            _yaw   += Input.GetAxis("Mouse X") * sens;
            _pitch -= Input.GetAxis("Mouse Y") * sens;
#endif
            _pitch = Mathf.Clamp(_pitch, -80, 80);

            if (_pilotCam != null)
                _pilotCam.transform.localRotation = Quaternion.Euler(_pitch, _yaw - transform.eulerAngles.y, 0);

            // ── MOVEMENT INPUT ──
            float h = 0, v = 0, up = 0;
#if ENABLE_INPUT_SYSTEM
            if (kb != null)
            {
                if (kb.dKey.isPressed) h += 1;
                if (kb.aKey.isPressed) h -= 1;
                if (kb.wKey.isPressed) v += 1;
                if (kb.sKey.isPressed) v -= 1;
                if (kb.spaceKey.isPressed) up += 1;
                if (kb.leftShiftKey.isPressed) up -= 1;
            }
#else
            h = Input.GetAxisRaw("Horizontal");
            v = Input.GetAxisRaw("Vertical");
            if (Input.GetKey(KeyCode.Space)) up = 1;
            if (Input.GetKey(KeyCode.LeftShift)) up = -1;
#endif

            Grid.ThrustInput = new Vector3(h, up, v);

            // ── ROTATION ──
            float ryaw = 0, rpitch = 0, rroll = 0;
#if ENABLE_INPUT_SYSTEM
            if (kb != null)
            {
                if (kb.eKey.isPressed) ryaw += 1;
                if (kb.qKey.isPressed) ryaw -= 1;
                if (kb.upArrowKey.isPressed) rpitch += 1;
                if (kb.downArrowKey.isPressed) rpitch -= 1;
                if (kb.leftArrowKey.isPressed) rroll += 1;
                if (kb.rightArrowKey.isPressed) rroll -= 1;
            }
#else
            ryaw = Input.GetKey(KeyCode.E) ? 1 : (Input.GetKey(KeyCode.Q) ? -1 : 0);
            rpitch = Input.GetKey(KeyCode.UpArrow) ? 1 : (Input.GetKey(KeyCode.DownArrow) ? -1 : 0);
            rroll = Input.GetKey(KeyCode.LeftArrow) ? 1 : (Input.GetKey(KeyCode.RightArrow) ? -1 : 0);
#endif
            Grid.RotationYaw = ryaw;
            Grid.RotationPitch = rpitch;
            Grid.RotationRoll = rroll;

            // ── DAMPENERS TOGGLE (Z) ──
#if ENABLE_INPUT_SYSTEM
            if (kb != null && kb.zKey.wasPressedThisFrame)
#else
            if (Input.GetKeyDown(KeyCode.Z))
#endif
            {
                Grid.DampenersOn = !Grid.DampenersOn;
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"Dampeners {(Grid.DampenersOn ? "ON" : "OFF")}",
                    "", null, Grid.DampenersOn ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.AccentRed);
            }

            // ── DOCK/UNDOCK (X) ──
#if ENABLE_INPUT_SYSTEM
            if (kb != null && kb.xKey.wasPressedThisFrame)
#else
            if (Input.GetKeyDown(KeyCode.X))
#endif
            {
                foreach (var kv in Grid.Blocks)
                {
                    if (kv.Value is GridDockingPort dp)
                    {
                        if (dp.IsDocked) dp.Undock();
                        else { /* auto-dock is handled by the port itself */ }
                        break;
                    }
                }
            }

            // ── TOGGLE AUTO-EXPORT (C) ──
#if ENABLE_INPUT_SYSTEM
            if (kb != null && kb.cKey.wasPressedThisFrame)
#else
            if (Input.GetKeyDown(KeyCode.C))
#endif
            {
                foreach (var kv in Grid.Blocks)
                {
                    if (kv.Value is GridDockingPort dp)
                    {
                        dp.autoExport = !dp.autoExport;
                        VoxelEngine.UI.BuildFeedbackHud.Show(
                            $"Auto-Export {(dp.autoExport ? "ON" : "OFF")}",
                            "", null, dp.autoExport ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.AccentOrange);
                        break;
                    }
                }
            }

            // ── LANDING GEAR TOGGLE (P) ──
#if ENABLE_INPUT_SYSTEM
            if (kb != null && kb.pKey.wasPressedThisFrame)
#else
            if (Input.GetKeyDown(KeyCode.P))
#endif
            {
                foreach (var kv in Grid.Blocks)
                {
                    if (kv.Value is GridLandingGear lg)
                    {
                        lg.Toggle();
                        break;
                    }
                }
            }
        }
    }
}
