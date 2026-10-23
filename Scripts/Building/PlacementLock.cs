// Assets/Scripts/VoxelEngine/Building/PlacementLock.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║       PLACEMENT LOCK — freeze the hologram, then nudge it        ║
// ║                                                                  ║
// ║  The hardest part of building with a hologram is that it moves   ║
// ║  whenever you look. You aim a machine, lean in to check the      ║
// ║  clearance behind it, and the pose you had is gone.              ║
// ║                                                                  ║
// ║  Lock (V) takes the pose out of the crosshair's hands: the       ║
// ║  ghost stays exactly where it was, the mouse is free to walk     ║
// ║  around the machine and look at it from the far side, and the    ║
// ║  arrow keys nudge it one lattice step along the view            ║
// ║  (Shift + arrow = five steps, Ctrl + arrow = lift / drop on the  ║
// ║  surface normal). The next build click confirms exactly what is  ║
// ║  on screen, and the lock survives the click so a second copy     ║
// ║  can be stamped down without re-aiming.                          ║
// ║                                                                  ║
// ║  Nothing here places anything: it holds a pose, and the builder  ║
// ║  asks for it before it asks the crosshair.                       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>The frozen hologram: one pose, one item, nudged by hand.</summary>
    public static class PlacementLock
    {
        // A held arrow key repeats, but at a walking pace: the first version repeated
        // every seventh of a second, which walked a frozen hologram a dozen cells a second
        // and read as the block flying away. Shift never repeats at all - five cells is a
        // deliberate move, not a rate.
        private const float NudgeRepeatDelay = 0.32f;
        private const float NudgeRepeatRate = 0.14f;
        private const float NudgeFineStep = 0.05f;

        public static bool IsLocked { get; private set; }
        public static Vector3 Position { get; private set; }
        public static Quaternion Rotation { get; private set; } = Quaternion.identity;
        public static BlockItem Item { get; private set; }

        private static float _nextNudge;
        private static Vector3 _lastNudgeDirection;

        /// <summary>Freezes the pose that is currently on screen.</summary>
        public static void Lock(BlockItem item, Vector3 position, Quaternion rotation)
        {
            if (item == null) return;
            IsLocked = true;
            Item = item;
            Position = position;
            Rotation = rotation;
            _nextNudge = 0f;
            _lastNudgeDirection = Vector3.zero;
            VoxelEngine.UI.BuildFeedbackHud.Show("Hologram locked",
                "It stays put while you walk around it. Arrow keys nudge, Shift + arrow moves five, Ctrl + arrow lifts.",
                null, VoxelEngine.UI.UITheme.AccentGold);
            VoxelEngine.FX.AudioManager.PlayUI(VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.UiClick), 0.5f, 0.88f);
        }

        public static void Unlock()
        {
            if (!IsLocked) return;
            IsLocked = false;
            Item = null;
            _lastNudgeDirection = Vector3.zero;
        }

        /// <summary>True while the lock is held for this item and it is still in hand.</summary>
        public static bool Matches(BlockItem item) => IsLocked && item != null && Item == item;

        public static void Nudge(Vector3 worldOffset)
        {
            if (!IsLocked) return;
            Position += worldOffset;
            VoxelEngine.FX.AudioManager.PlayUI(
                VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.UiHover), 0.35f, 1.24f);
        }

        /// <summary>Moves the frozen pose by a rotation step, keeping it where it is.</summary>
        public static void Rotate(Quaternion delta)
        {
            if (!IsLocked) return;
            Rotation = delta * Rotation;
        }

        /// <summary>
        /// Reads the arrow keys and applies them to the frozen pose. Safe to call every
        /// frame; does nothing unless a lock is held. Returns true when the pose moved,
        /// so the caller can refresh the ghost. <paramref name="viewForward"/> is the
        /// camera's forward: nudges are screen-relative, so left is always left.
        /// </summary>
        public static bool Tick(BlockItem held, float step, Vector3 viewForward)
        {
            if (!IsLocked) return false;
            if (held == null || held != Item) { Unlock(); return false; }
            if (step <= 0.0001f) step = 1f;

            Vector2 axis = ReadArrowAxis();
            if (axis.sqrMagnitude < 0.01f)
            {
                _lastNudgeDirection = Vector3.zero;
                return false;
            }

            bool ctrl = VoxelEngine.GridSystem.GridInput.Ctrl;
            bool shift = VoxelEngine.GridSystem.GridInput.Shift;
            Vector3 up = GravityProvider.GetUp(Position);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            // Nudge along what the PLAYER is looking at, never along a world-derived
            // tangent. The old basis was rebuilt at the ghost's own position on every
            // step, so on a curved planet each step rotated the basis slightly: a held
            // arrow key walked the hologram away in a curve, and the axes did not agree
            // with the screen. The view forward, flattened onto the local gravity plane,
            // is the only reference a player can aim with.
            Vector3 forward = Vector3.ProjectOnPlane(viewForward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = PlacementFrame.FromUp(Position, up, "LOCK", false).forward;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();

            Vector3 direction;
            if (ctrl)
            {
                direction = up * Mathf.Sign(axis.y);
            }
            else
            {
                direction = right * Mathf.Sign(axis.x) + forward * Mathf.Sign(axis.y);
                if (direction.sqrMagnitude > 1f) direction.Normalize();
            }

            float amount = ctrl ? NudgeFineStep : (shift ? step * 5f : step);
            Vector3 offset = direction * amount;

            // A tap moves once, a held plain arrow repeats at a walking pace, and a Shift
            // nudge is one deliberate five-cell move with no repeat at all.
            bool freshPress = direction != _lastNudgeDirection;
            _lastNudgeDirection = direction;
            if (freshPress)
            {
                _nextNudge = Time.unscaledTime + NudgeRepeatDelay;
            }
            else
            {
                if (shift || ctrl) return false;
                if (Time.unscaledTime < _nextNudge) return false;
                _nextNudge = Time.unscaledTime + NudgeRepeatRate;
            }

            Nudge(offset);
            return true;
        }

        private static Vector2 ReadArrowAxis()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return Vector2.zero;
            float x = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
            float y = (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            float x = (Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.UpArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
            return new Vector2(x, y);
#endif
        }
    }
}
