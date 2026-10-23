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
        /// <summary>The lattice frame the pose was frozen in: the axes nudges step along.</summary>
        public static PlacementFrame Frame { get; private set; }

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
                "It stays put while you walk around it. Arrow keys nudge, Shift + arrow moves five, Ctrl + up or down lifts.",
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
                // 17.44.7-dev: Ctrl means LIFT and DROP, nothing else. A horizontal arrow
                // with Ctrl held is not a fine sideways nudge - it does nothing at all.
                if (Mathf.Abs(axis.x) >= Mathf.Abs(axis.y)) return false;

                intent = axis.y > 0f ? 3 : -3;
                direction = FrameAxis(upNow, vertical: true) * Mathf.Sign(axis.y);
            }
            else
            {
                EnsureNudgeBasis(view, upNow, Position);

                // 17.44.7-dev: the press is read as a SCREEN direction, then committed to
                // the nearest lattice axis and stepped along that. Two things fall out of
                // it. A step is always a whole cell on the block's own lattice, never a
                // fraction of one. And the axis cannot drift between steps: it is chosen
                // from the frozen pose's frame, so a wobbling camera basis can change WHICH
                // cardinal a press means, but it can never make two consecutive steps fight
                // each other - which is the zigzag.
                // 17.44.8-dev: SCREEN AXES, flat out. The previous release committed each
                // press to the frozen pose's own lattice axes, which is stable but wrong in
                // the hand: standing edge-on to a machine, screen-right IS the machine's
                // forward, so pressing right walked the block forwards and backwards instead
                // of sideways. Left must always be left of the screen. The basis is the
                // cached one, captured once per lock, so this stays just as steady - the
                // stability was never the lattice's job, it was the caching.
                bool horizontal = Mathf.Abs(axis.x) >= Mathf.Abs(axis.y);
                if (horizontal)
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

            // One line per press, not per repeat: the basis this step was taken in, what it
            // did, and where the pose ended up. If a nudge ever reads as a zigzag again, this
            // line says whether the basis moved, the step shrank, or nothing moved at all.
            if (freshPress)
                UnityEngine.Debug.Log(
                    $"[nudge] key={(intent > 0 ? "+" : "")}{intent} step={amount:0.###} " +
                    $"right={_nudgeRight} away={_nudgeAway} up={upNow} " +
                    $"offset={offset} -> {Position}");

            Nudge(offset, freshPress);
            return true;
        }

        /// <summary>The frozen pose's vertical: the surface normal, or the frame's up.</summary>
        private static Vector3 FrameAxis(Vector3 up, bool vertical)
        {
            if (!vertical) return up;
            if (Frame.valid && Vector3.Dot(Frame.up, up) > 0.9f) return Frame.up.normalized;
            return up;
        }

        /// <summary>
        /// Locks the screen basis for nudging. It is rebuilt only when the player has turned
        /// their view by more than about 20 degrees, or when the frozen pose has travelled
        /// far enough that the local surface is a different place.
        /// </summary>
        private static void EnsureNudgeBasis(Transform view, Vector3 up, Vector3 position)
        {
            // 17.44.9-dev: PINNED for the life of the lock. The basis used to be rebuilt
            // whenever the view turned about twenty degrees or the pose had travelled four
            // metres - and since the pose travels a metre per press, a held arrow rebuilt it
            // every handful of steps. Each rebuild could re-map left onto a different world
            // direction, which is exactly the zigzag: the steps were individually clean and
            // collectively crooked. Turn the camera as much as you like now; the axes stay
            // where the freeze put them. Only a genuine walk-around (past sixty degrees)
            // re-derives them, and re-locking always starts clean.
            bool stale = !_nudgeBasisValid;

            Vector3 candidate = view != null ? Vector3.ProjectOnPlane(view.right, up) : Vector3.zero;
            if (!stale && candidate.sqrMagnitude > 0.04f
                && Vector3.Dot(candidate.normalized, _nudgeRight) < 0.5f)
                stale = true;   // more than sixty degrees round: sideways means something else

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
