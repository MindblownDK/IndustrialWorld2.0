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
        /// so the caller can refresh the ghost. <paramref name="view"/> is the camera:
        /// nudges are measured in SCREEN axes projected onto the ground, so left is always
        /// left of the screen and up is always away from the player, whatever the camera is
        /// doing - looking level, looking down on the machine, or anything between.
        /// </summary>
        public static bool Tick(BlockItem held, float step, Transform view)
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

            // ── 17.44.3-dev: SCREEN AXES, NOT A TANGENT ──────────────────────────
            // Two earlier versions both failed here. Flattening the camera's FORWARD is
            // unstable the moment the player looks down at a machine - a builder's normal
            // view - because that projection shrinks to nearly nothing and its direction
            // stops meaning anything, which is how left/right could move a block "up".
            // The camera's RIGHT vector projected onto the ground plane never degenerates
            // while the player is looking at the ground, so that is the reference: right is
            // screen right, and away is the ground direction that reads as screen up.
            Vector3 right;
            if (view != null)
            {
                right = Vector3.ProjectOnPlane(view.right, up);
                if (right.sqrMagnitude < 0.04f)
                    right = Vector3.ProjectOnPlane(view.up, up);
            }
            else right = Vector3.ProjectOnPlane(Vector3.right, up);

            if (right.sqrMagnitude < 0.0001f)
                right = PlacementFrame.FromUp(Position, up, "LOCK", false).right;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();

            Vector3 away = Vector3.Cross(right, up);
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            away.Normalize();

            Vector3 direction;
            if (ctrl)
            {
                direction = up * Mathf.Sign(axis.y);
            }
            else
            {
                // One axis per press. Pressing two arrows at once used to blend them into a
                // diagonal that the repeat logic then measured against - which is the rest
                // of the "zigzag" report. The stronger axis wins.
                if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y)) direction = right * Mathf.Sign(axis.x);
                else direction = away * Mathf.Sign(axis.y);
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
