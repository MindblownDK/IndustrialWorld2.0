// Assets/Scripts/VoxelEngine/UI/RadialWheelInput.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — RADIAL WHEEL POINTER MODEL            ║
// ║                                                                  ║
// ║  Direction-only pointer for hold-to-open radial menus.            ║
// ║                                                                  ║
// ║  The OS cursor is LOCKED while a wheel is up, so the pointer is   ║
// ║  virtual: raw mouse delta integrates into a unit disc that is     ║
// ║  clamped in length but never in angle. A flick past the edge      ║
// ║  keeps selecting — the wedge under the direction wins no matter   ║
// ║  how far the hand travels. Angle comes straight out of Atan2      ║
// ║  with zero smoothing, so the selection changes on the exact       ║
// ║  frame the hand does.                                            ║
// ║                                                                  ║
// ║  Re-centred on every open, which is what makes the muscle memory  ║
// ║  work: "up-left is always stairs" regardless of where the cursor  ║
// ║  happened to be sitting.                                         ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Virtual pointer for a radial selector. Pure data — no UI, no scene
    /// dependency — so every wheel in the game can share the same feel.
    /// </summary>
    public sealed class RadialWheelInput
    {
        /// <summary>
        /// Screen fraction the hand must travel for full deflection. Small by
        /// design: the wheel should answer a flick, not a drag.
        /// </summary>
        public float FlickScreenFraction = 0.13f;

        /// <summary>Below this deflection nothing is selected (the centre hub owns it).</summary>
        public float Deadzone = 0.30f;

        /// <summary>Extra pull applied to the very first sample so a fast flick registers instantly.</summary>
        public float Sensitivity = 1f;

        /// <summary>Pointer position on the unit disc. Length is clamped to 1, angle is free.</summary>
        public Vector2 Normalized { get; private set; }

        /// <summary>0 at 12 o'clock, increasing clockwise, wrapped to [0, 360).</summary>
        public float AngleDegrees { get; private set; }

        public float Deflection => Normalized.magnitude;
        public bool InDeadzone => Deflection < Deadzone;

        /// <summary>Re-centres the pointer. Call every time the wheel opens.</summary>
        public void Begin()
        {
            Normalized = Vector2.zero;
            AngleDegrees = 0f;
        }

        /// <summary>Integrates this frame's mouse delta. Call once per frame while open.</summary>
        public void Sample()
        {
            float reach = Mathf.Max(64f, Mathf.Min(Screen.width, Screen.height) * FlickScreenFraction);
            Vector2 delta = ReadMouseDelta() * (Sensitivity / reach);
            if (delta.sqrMagnitude > 0f)
                Normalized = Vector2.ClampMagnitude(Normalized + delta, 1f);

            if (Normalized.sqrMagnitude > 1e-8f)
                AngleDegrees = Mathf.Repeat(Mathf.Atan2(Normalized.x, Normalized.y) * Mathf.Rad2Deg, 360f);
        }

        /// <summary>
        /// Wedge under the pointer for a ring of <paramref name="segmentCount"/>
        /// wedges with wedge 0 centred at 12 o'clock, or -1 inside the deadzone.
        /// </summary>
        public int SegmentAt(int segmentCount)
        {
            if (segmentCount <= 0 || InDeadzone) return -1;
            float step = 360f / segmentCount;
            int segment = Mathf.FloorToInt(Mathf.Repeat(AngleDegrees + step * 0.5f, 360f) / step);
            return Mathf.Clamp(segment, 0, segmentCount - 1);
        }

        /// <summary>Centre angle of a wedge, in the same 12-o'clock-clockwise space.</summary>
        public static float SegmentAngle(int segment, int segmentCount)
            => segmentCount <= 0 ? 0f : segment * (360f / segmentCount);

        /// <summary>
        /// Keeps the hardware cursor pinned and hidden. A blocking UI normally
        /// releases the cursor, so this is re-asserted every frame the wheel is up.
        /// </summary>
        public static void HoldCursorCentred()
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.visible) Cursor.visible = false;
        }

        private static Vector2 ReadMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X") * 10f, Input.GetAxisRaw("Mouse Y") * 10f);
#endif
        }
    }
}
