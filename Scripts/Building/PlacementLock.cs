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

        // ── The nudge basis ────────────────────────────────────────────────────
        // Screen axes, captured ONCE per lock and rebuilt only when the player really
        // turns their view. Rebuilding them every frame sounds more accurate and is the
        // opposite: the basis is derived from a gravity up that moves with the ghost and a
        // camera that is never perfectly still, so it drifts a fraction of a degree per
        // step. Hold an arrow and those fractions add up in one direction - the block
        // walks away in a slow zigzag instead of stepping sideways.
        private static bool _nudgeBasisValid;
        private static Vector3 _nudgeRight = Vector3.right;
        private static Vector3 _nudgeAway = Vector3.forward;
        private static Vector3 _nudgeUp = Vector3.up;
        private static Vector3 _nudgeBasisAt;

        /// <summary>Which arrow is being held: 0 none, +/-1 left-right, +/-2 back-away, +/-3 down-up.</summary>
        private static int _nudgeIntent;

        /// <summary>Freezes the pose that is currently on screen.</summary>
        public static void Lock(BlockItem item, Vector3 position, Quaternion rotation)
        {
            if (item == null) return;
            IsLocked = true;
            Item = item;
            Position = position;
            Rotation = rotation;
            _nextNudge = 0f;
            _nudgeIntent = 0;
            _nudgeBasisValid = false;
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
            _nudgeIntent = 0;
            _nudgeBasisValid = false;
        }

        /// <summary>True while the lock is held for this item and it is still in hand.</summary>
        public static bool Matches(BlockItem item) => IsLocked && item != null && Item == item;

        public static void Nudge(Vector3 worldOffset, bool sound = true)
        {
            if (!IsLocked) return;
            Position += worldOffset;

            // One tick per press. A held arrow repeats several times a second, and a click
            // on every repeat turns a nudge into a rattle.
            if (sound)
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
                _nudgeIntent = 0;
                return false;
            }

            bool ctrl = VoxelEngine.GridSystem.GridInput.Ctrl;
            bool shift = VoxelEngine.GridSystem.GridInput.Shift;
            Vector3 upNow = GravityProvider.GetUp(Position);
            if (upNow.sqrMagnitude < 0.0001f) upNow = Vector3.up;
            upNow.Normalize();

            int intent;
            Vector3 direction;
            if (ctrl)
            {
                // Lift / drop: straight along the local surface normal.
                intent = axis.y >= 0f ? 3 : -3;
                direction = upNow * Mathf.Sign(axis.y);
            }
            else
            {
                EnsureNudgeBasis(view, upNow, Position);

                // 17.44.5-dev: one arrow, one axis, always - the light that moved was the
                // axis-blend, not the basis. Left and right are screen right; up and down
                // are the ground direction that reads as screen up.
                if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y))
                {
                    intent = axis.x > 0f ? 1 : -1;
                    direction = _nudgeRight * Mathf.Sign(axis.x);
                }
                else
                {
                    intent = axis.y > 0f ? 2 : -2;
                    direction = _nudgeAway * Mathf.Sign(axis.y);
                }
            }

            float amount = ctrl ? NudgeFineStep : (shift ? step * 5f : step);
            Vector3 offset = direction * amount;

            // 17.44.5-dev: repeat is measured on the ARROW, never on the resulting vector.
            // Comparing directions meant a single drifting basis or a gravity up that
            // changed with the ghost counted as a brand-new press every frame - so a held
            // key nudged sixty times a second and crawled the block away.
            bool freshPress = intent != _nudgeIntent;
            _nudgeIntent = intent;
            if (freshPress)
            {
                _nextNudge = Time.unscaledTime + NudgeRepeatDelay;
            }
            else
            {
                // Shift and Ctrl move once per press: five cells and 0.05 m are decisions.
                if (shift || ctrl) return false;
                if (Time.unscaledTime < _nextNudge) return false;
                _nextNudge = Time.unscaledTime + NudgeRepeatRate;
            }

            Nudge(offset, freshPress);
            return true;
        }

        /// <summary>
        /// Locks the screen basis for nudging. It is rebuilt only when the player has turned
        /// their view by more than about 20 degrees, or when the frozen pose has travelled
        /// far enough that the local surface is a different place.
        /// </summary>
        private static void EnsureNudgeBasis(Transform view, Vector3 up, Vector3 position)
        {
            bool stale = !_nudgeBasisValid || (position - _nudgeBasisAt).sqrMagnitude > 16f;

            Vector3 candidate = view != null ? Vector3.ProjectOnPlane(view.right, up) : Vector3.zero;
            if (!stale && candidate.sqrMagnitude > 0.04f
                && Vector3.Dot(candidate.normalized, _nudgeRight) < 0.94f)
                stale = true;   // the view has turned: sideways means something else now

            if (!stale) return;

            // The camera's RIGHT, flattened onto the ground, never its forward: a forward
            // projection collapses toward zero whenever the player looks down at a machine.
            Vector3 right = candidate;
            if (right.sqrMagnitude < 0.04f && view != null)
                right = Vector3.ProjectOnPlane(view.up, up);
            if (right.sqrMagnitude < 0.0001f)
                right = PlacementFrame.FromUp(position, up, "LOCK", false).right;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;

            _nudgeRight = right.normalized;
            Vector3 away = Vector3.Cross(_nudgeRight, up);
            if (away.sqrMagnitude < 0.0001f) away = Vector3.Cross(_nudgeRight, Vector3.up);
            _nudgeAway = away.normalized;
            _nudgeUp = up;
            _nudgeBasisAt = position;
            _nudgeBasisValid = true;
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
