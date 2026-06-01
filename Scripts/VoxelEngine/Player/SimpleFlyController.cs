// Assets/Scripts/VoxelEngine/Player/SimpleFlyController.cs
//
// Reads keybinds, mouse sensitivity, and FOV from GameSettings.
// Works with EITHER legacy Input Manager OR new Input System (auto-detected).

using UnityEngine;
using VoxelEngine.Settings;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Player
{
    public class SimpleFlyController : MonoBehaviour
    {
        public float moveSpeed = 12f;
        public float sprintMultiplier = 3f;

        private float _yaw, _pitch;
        private bool  _looking;
        private Camera _cachedCam;

        private void Start()
        {
            _yaw   = transform.eulerAngles.y;
            _pitch = transform.eulerAngles.x;
            _cachedCam = GetComponentInChildren<Camera>();
            ApplyFov();
            GameSettings.OnChanged += ApplyFov;
        }

        private void OnDestroy() => GameSettings.OnChanged -= ApplyFov;

        private void ApplyFov()
        {
            if (_cachedCam == null) _cachedCam = GetComponentInChildren<Camera>();
            if (_cachedCam != null) _cachedCam.fieldOfView = GameSettings.Fov;
        }

        private void Update()
        {
            // ---------- look toggle (right mouse) ----------
            if (GetRightMouseDown())
            {
                _looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (GetRightMouseUp())
            {
                _looking = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (_looking)
            {
                Vector2 delta = GetMouseDelta();
                float sens = GameSettings.MouseSensitivity;
                float invert = GameSettings.InvertY ? -1f : 1f;
                _yaw   += delta.x * sens;
                _pitch -= delta.y * sens * invert;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);
            }

            // ---------- movement ----------
            float speed = moveSpeed * (GameSettings.IsHeld(InputAction.Sprint) ? sprintMultiplier : 1f);
            Vector3 dir = Vector3.zero;
            if (GameSettings.IsHeld(InputAction.Forward)) dir += transform.forward;
            if (GameSettings.IsHeld(InputAction.Back))    dir -= transform.forward;
            if (GameSettings.IsHeld(InputAction.Right))   dir += transform.right;
            if (GameSettings.IsHeld(InputAction.Left))    dir -= transform.right;
            if (GameSettings.IsHeld(InputAction.Up))      dir += Vector3.up;
            if (GameSettings.IsHeld(InputAction.Down))    dir -= Vector3.up;
            transform.position += dir.normalized * speed * Time.deltaTime;
        }

        // Right mouse always toggles mouse-look — kept independent of rebinds for sanity.
        private static bool GetRightMouseDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }
        private static bool GetRightMouseUp()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp(1);
#endif
        }
        private static Vector2 GetMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X") * 10f, Input.GetAxisRaw("Mouse Y") * 10f);
#endif
        }
    }
}
