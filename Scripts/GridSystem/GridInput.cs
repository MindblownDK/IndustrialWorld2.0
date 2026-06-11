// Assets/Scripts/VoxelEngine/GridSystem/GridInput.cs
//
// Tiny input wrapper so grid scripts work under the new Input System (the project
// has legacy Input disabled). Falls back to the old Input class if the new system
// isn't active.

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.GridSystem
{
    public static class GridInput
    {
        public static bool Mouse0
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            get => Input.GetMouseButton(0);
#endif
        }

        public static bool Mouse1
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            get => Input.GetMouseButton(1);
#endif
        }

        public static float Scroll
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            get => Input.mouseScrollDelta.y;
#endif
        }

        public static Vector2 MouseDelta
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            get => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        public static bool Ctrl
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);
#else
            get => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
        }

        public static bool Shift
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
            get => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        public static bool Alt
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);
#else
            get => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
#endif
        }

        public static bool Q
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && Keyboard.current.qKey.isPressed;
#else
            get => Input.GetKey(KeyCode.Q);
#endif
        }

        public static bool E
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && Keyboard.current.eKey.isPressed;
#else
            get => Input.GetKey(KeyCode.E);
#endif
        }

        /// <summary>True only on the frame Z is pressed (dampener toggle).</summary>
        public static bool ZPressed
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && Keyboard.current.zKey.wasPressedThisFrame;
#else
            get => Input.GetKeyDown(KeyCode.Z);
#endif
        }

        /// <summary>True only on the frame P is pressed (landing-gear lock/unlock toggle).</summary>
        public static bool PPressed
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;
#else
            get => Input.GetKeyDown(KeyCode.P);
#endif
        }
    }
}
