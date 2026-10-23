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
// ║  arrow keys nudge it ONE LATTICE STEP at a time (Shift = one     ║
// ║  whole cell of the block's own row, Ctrl + up/down = lift and    ║
// ║  drop 5 cm). The next build click confirms exactly what is on    ║
// ║  screen, and the lock survives the click so a second copy can    ║
// ║  be stamped down without re-aiming.                              ║
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
        // and read as the block flying away.
        private const float NudgeRepeatDelay = 0.32f;
        private const float NudgeRepeatRate = 0.14f;
        private const float NudgeFineStep = 0.05f;

        /// <summary>
        /// How far the camera may turn before the four nudge directions are re-picked from
        /// the new view. Below this the pair is rigid, so a burst of presses is one straight
        /// line on the lattice and any number of steps is exactly reversible.
        /// </summary>
        private const float NudgeRepinTurn = 40f;

        public static bool IsLocked { get; private set; }
        public static Vector3 Position { get; private set; }
        public static Quaternion Rotation { get; private set; } = Quaternion.identity;
        public static BlockItem Item { get; private set; }
        /// <summary>The lattice frame the pose was frozen in: the axes nudges step along.</summary>
        public static PlacementFrame Frame { get; private set; }

        private static float _nextNudge;

        // ── The nudge basis ────────────────────────────────────────────────────
        // 17.45.0-dev: THE FOUR DIRECTIONS ARE LATTICE AXES.
        //
        // Every earlier version moved the frozen block along a direction derived from the
        // camera, which is exact arithmetic on axes that are NOT the axes the world is built
        // on. Press right and the block moved screen-right; press up and it moved
        // screen-away; those two vectors are not perpendicular in world space unless the
        // player happens to be looking straight down a grid line, so the path between them
        // reads as a zigzag - and pressing right N times then left N times did not come
        // back to where it started, because the pair had been re-derived in between.
        //
        // The pair is now chosen once per view from the frozen pose's own two ground axes:
        // whichever of them points most nearly at screen-right IS "right", whichever points
        // most nearly away IS "away". Both are real lattice axes, so every press is a whole
        // number of cells along one axis, the path is a straight line on the grid, and the
        // second press cancels the first exactly. The pair is re-picked only when the player
        // has actually walked around the block and is looking at it from a new side.
        private static bool _nudgeBasisValid;
        private static Vector3 _nudgeRight = Vector3.right;
        private static Vector3 _nudgeAway = Vector3.forward;
        // Which of the pose frame's two ground axes each arrow direction landed on, so
        // Shift can step by that block's own extent along the very axis it is moving on.
        private static bool _nudgeRightUsesRightAxis = true;
        private static bool _nudgeAwayUsesRightAxis = true;
        private static Vector3 _nudgeBasisCamera = Vector3.forward;

        /// <summary>Which arrow is being held: 0 none, +/-1 left-right, +/-2 back-away, +/-3 down-up.</summary>
        private static int _nudgeIntent;

        /// <summary>Freezes the pose that is currently on screen.</summary>
        public static void Lock(BlockItem item, Vector3 position, Quaternion rotation,
            PlacementFrame frame = default(PlacementFrame))
        {
            if (item == null) return;
            IsLocked = true;
            Item = item;
            Position = position;
            Rotation = rotation;
            Frame = frame.valid ? frame : PlacementFrame.FromUp(position, GravityProvider.GetUp(position),
                "LOCK", false);
            _nextNudge = 0f;
            _nudgeIntent = 0;
            _nudgeBasisValid = false;
            VoxelEngine.UI.BuildFeedbackHud.Show("Hologram locked",
                "It stays put while you walk around it. Arrow keys step one cell along the lattice, Shift + arrow steps a whole machine, Ctrl + up or down lifts.",
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
        /// frame; does nothing unless a lock is held. Returns true when the pose moved, so
        /// the caller can refresh the ghost.
        /// </summary>
        /// <param name="step">The lattice step in metres (the same one placement quantises to).</param>
        /// <param name="pitch">The block's own measured footprint, per frame axis - what Shift steps by.</param>
        /// <param name="view">The camera, used only to decide WHICH lattice axis each arrow means.</param>
        public static bool Tick(BlockItem held, float step, Vector3 pitch, Transform view)
        {
            if (!IsLocked) return false;
            if (held == null || held != Item) { Unlock(); return false; }

            if (step <= 0.0001f) step = 1f;
            if (pitch.x <= 0.0001f) pitch.x = step;
            if (pitch.y <= 0.0001f) pitch.y = step;
            if (pitch.z <= 0.0001f) pitch.z = step;

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
            float amount;

            if (ctrl)
            {
                // Ctrl means LIFT and DROP, nothing else. A horizontal arrow with Ctrl held
                // is not a fine sideways nudge - it does nothing at all.
                if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y)) return false;
                intent = axis.y > 0f ? 3 : -3;
                direction = upNow * Mathf.Sign(axis.y);
                amount = NudgeFineStep;
            }
            else
            {
                // A FRESH PRESS is the only moment the pair may be re-picked. Mid-burst it
                // cannot be allowed at all: a camera that drifts while an arrow is held would
                // hand the same key a new axis halfway through the line, which is the zigzag
                // this whole rewrite exists to remove.
                int rawKey = Mathf.Abs(axis.x) >= Mathf.Abs(axis.y)
                    ? (axis.x > 0f ? 1 : -1)
                    : (axis.y > 0f ? 2 : -2);
                bool freshKey = rawKey != _nudgeIntent;
                if (!_nudgeBasisValid || (freshKey && CameraTurnedAway(view)))
                    RepinNudgeBasis(view, upNow);

                // The basis axis already points at screen-right (or screen-away), so the
                // ARROW's own sign is the only thing that decides which way the block goes.
                if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y))
                {
                    intent = axis.x > 0f ? 1 : -1;
                    direction = _nudgeRight * Mathf.Sign(axis.x);
                    amount = shift ? (_nudgeRightUsesRightAxis ? pitch.x : pitch.z) : step;
                }
                else
                {
                    intent = axis.y > 0f ? 2 : -2;
                    direction = _nudgeAway * Mathf.Sign(axis.y);
                    amount = shift ? (_nudgeAwayUsesRightAxis ? pitch.x : pitch.z) : step;
                }
                if (amount <= 0.0001f) amount = step;
            }

            Vector3 offset = direction * amount;

            // Repeat is measured on the ARROW, never on the resulting vector, so a basis that
            // moves for any reason cannot turn a held key into sixty nudges a second.
            bool freshPress = intent != _nudgeIntent;
            _nudgeIntent = intent;
            if (freshPress)
            {
                _nextNudge = Time.unscaledTime + NudgeRepeatDelay;
            }
            else
            {
                // Shift and Ctrl move once per press: a whole cell and 5 cm are decisions.
                if (shift || ctrl) return false;
                if (Time.unscaledTime < _nextNudge) return false;
                _nextNudge = Time.unscaledTime + NudgeRepeatRate;
            }

            Nudge(offset, freshPress);
            return true;
        }

        /// <summary>
        /// Picks the two lattice axes the arrow keys will move along.
        ///
        /// The pose's own frame supplies a perpendicular pair of ground axes (from a
        /// neighbouring foundation, a deck edge, or local gravity when the block sits in the
        /// open). Screen-right and screen-away are then matched to whichever axis they point
        /// most nearly along, sign included - so the arrows still follow the view, but they
        /// only ever move the block along axes the world is built on.
        ///
        /// The caller decides WHEN that is allowed: once on the first press after a lock, and
        /// afterwards only on a fresh press from a view that has really changed (see
        /// <see cref="CameraTurnedAway"/>).
        /// </summary>
        private static void RepinNudgeBasis(Transform view, Vector3 up)
        {
            var frame = Frame.valid ? Frame : PlacementFrame.FromUp(Position, up, "LOCK", false);
            Vector3 axisA = Vector3.ProjectOnPlane(frame.right, up).normalized;
            if (axisA.sqrMagnitude < 0.0001f) axisA = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            if (axisA.sqrMagnitude < 0.0001f) axisA = Vector3.ProjectOnPlane(Vector3.up, up).normalized;
            if (axisA.sqrMagnitude < 0.0001f) axisA = Vector3.forward;

            // Axis B must be SQUARE to axis A, not merely close: a pair that is twenty
            // degrees off would move the block sideways as well as forward, which is the
            // diagonal the player reports as a zigzag, and pressing one arrow then the other
            // would not close the rectangle. Orthogonalise, then re-derive if that collapses.
            Vector3 axisB = Vector3.ProjectOnPlane(frame.forward, up);
            if (axisB.sqrMagnitude < 0.0001f) axisB = Vector3.ProjectOnPlane(Vector3.right, up);
            float skew = Vector3.Dot(axisA, axisB);
            if (Mathf.Abs(skew) > 0.001f) axisB -= axisA * skew;
            if (axisB.sqrMagnitude < 0.0001f) axisB = Vector3.Cross(up, axisA);
            axisB.Normalize();

            Vector3 camRight = view != null ? Vector3.ProjectOnPlane(view.right, up) : Vector3.zero;
            Vector3 camAway = view != null ? Vector3.ProjectOnPlane(view.forward, up) : Vector3.zero;
            if (camRight.sqrMagnitude < 0.02f && view != null)
                camRight = Vector3.ProjectOnPlane(view.up, up);   // looking straight down
            if (camAway.sqrMagnitude < 0.02f && view != null)
                camAway = Vector3.ProjectOnPlane(view.up, up);
            if (camRight.sqrMagnitude < 0.0001f) camRight = axisA;
            if (camAway.sqrMagnitude < 0.0001f) camAway = axisB;
            camRight.Normalize();
            camAway.Normalize();

            _nudgeRight = PickLatticeAxis(camRight, axisA, axisB, 1f);
            _nudgeAway = PickLatticeAxis(camAway, axisA, axisB, -1f);

            // Screen-right and screen-away must be two DIFFERENT axes. Standing with the
            // camera square down an axis, both requests can land on it; the away request
            // then takes the other axis instead of duplicating the first.
            if (Mathf.Abs(Vector3.Dot(_nudgeRight, _nudgeAway)) > 0.5f)
            {
                Vector3 other = Mathf.Abs(Vector3.Dot(_nudgeRight, axisA)) > 0.5f ? axisB : axisA;
                float sign = Vector3.Dot(other, camAway);
                _nudgeAway = other * (sign < 0f ? -1f : 1f);
            }

            _nudgeRightUsesRightAxis = Mathf.Abs(Vector3.Dot(_nudgeRight, axisA)) > 0.5f;
            _nudgeAwayUsesRightAxis = Mathf.Abs(Vector3.Dot(_nudgeAway, axisA)) > 0.5f;
            _nudgeBasisCamera = view != null ? view.forward : Vector3.forward;
            _nudgeBasisValid = true;
        }

        /// <summary>True when the player has walked far enough around the block that the
        /// arrows should re-aim: a different side of the machine is a different view.</summary>
        private static bool CameraTurnedAway(Transform view)
        {
            if (view == null || !_nudgeBasisValid) return false;
            return Vector3.Angle(_nudgeBasisCamera, view.forward) >= NudgeRepinTurn;
        }

        /// <summary>
        /// The lattice axis (either of the two, in either direction) that points most nearly
        /// along <paramref name="screen"/> - with <paramref name="fallbackSign"/> deciding the
        /// rare exactly-sideways tie.
        /// </summary>
        private static Vector3 PickLatticeAxis(Vector3 screen, Vector3 axisA, Vector3 axisB, float fallbackSign)
        {
            float dotA = Vector3.Dot(screen, axisA);
            float dotB = Vector3.Dot(screen, axisB);
            float best = Mathf.Max(Mathf.Abs(dotA), Mathf.Abs(dotB));
            if (best < 0.0001f)
                return (fallbackSign >= 0f ? axisB : axisA).normalized;

            return Mathf.Abs(dotA) >= Mathf.Abs(dotB)
                ? axisA * (dotA < 0f ? -1f : 1f)
                : axisB * (dotB < 0f ? -1f : 1f);
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
