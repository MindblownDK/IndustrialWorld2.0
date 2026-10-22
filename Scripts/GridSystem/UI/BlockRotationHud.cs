// Assets/Scripts/VoxelEngine/GridSystem/UI/BlockRotationHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        PLACEMENT ASSIST — the card in the top-right corner        ║
// ║                                                                  ║
// ║  The old card was a cube, three arrows and four lines of text.   ║
// ║  It told you which keys rotate which axis. It could not tell you  ║
// ║  WHICH WAY THE BLOCK WAS ACTUALLY POINTING - so a player with a   ║
// ║  block in hand was left guessing, and the card that was supposed  ║
// ║  to help became the thing they ignored.                          ║
// ║                                                                  ║
// ║  This one is a live instrument:                                          ║
// ║    • the body is the player's own block, drawn as a projected     ║
// ║      wireframe in the SAME pose as the hologram in the world,     ║
// ║      with its front face filled so "which way is forward" is a    ║
// ║      glance, not a calculation                                    ║
// ║    • three orbital rings around it are the three rotation axes;   ║
// ║      an axis you have actually stepped lights up and stays lit    ║
// ║    • a compass line names the heading relative to the structure   ║
// ║      being lined up against (AHEAD / RIGHT / BACK / LEFT)         ║
// ║    • chips report the only three things that change a click:     ║
// ║      the build pattern, the lattice, and whether the hologram     ║
// ║      is frozen                                                ║
// ║    • the key rows are adaptive - a locked hologram talks about    ║
// ║      nudging, a run being dragged talks about releasing          ║
// ║                                                                  ║
// ║  It is a read-only view of state owned by BuildSystem,            ║
// ║  PlacementOptions and PlacementLock. It never writes anything and  ║
// ║  repaints only when the pose it is drawing actually changes.      ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Settings;
using T = VoxelEngine.UI.UITheme;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem.UI
{
    public static class BlockRotationHud
    {
        // ── Mounted UI ────────────────────────────────────────────────
        private static VisualElement _root, _box;
        private static Label _nameLabel;
        private static PlacementBodyElement _body;
        private static Label _headingLabel, _headingValue;
        private static VisualElement _chipRow;
        private static Label _patternChip, _latticeChip, _lockChip;
        private static VisualElement _legend;
        private static Label _legendPrimary, _legendSecondary;
        private static Label _runLabel;
        private static bool _visible;

        // ── Change detection (repaint only on real change) ────────────
        private static Quaternion _lastRotation = Quaternion.identity;
        private static Vector3 _lastUp = Vector3.up;
        private static Vector3 _lastSteps = new(-1f, -1f, -1f);
        private static Vector3 _lastFrameForward = new(float.PositiveInfinity, 0f, 0f);
        private static bool _lastValid, _lastAligned;
        private static string _lastLegendPrimary, _lastLegendSecondary;
        private static string _lastPatternText, _lastLatticeText, _lastLockText, _lastHeadingText;
        private static int _lastRunCount = -1;

        // Field round holdovers (kept from the original card: a one-frame resolution
        // flicker while mining must not blink the card off and on).
        private static string _lastName;
        private static float _lastShownAt;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _box != null && _box.parent == uiRoot) return;
            _root = uiRoot;
            if (_box != null) _box.RemoveFromHierarchy();

            // A remount builds a FRESH box; reset the visibility state so the rebuilt box
            // is shown by the next tick (the stale-state bug class from 9.16.0).
            _visible = false;

            _box = new VisualElement { name = "BlockRotationHud" };
            _box.style.position = Position.Absolute;
            _box.style.top = 18;
            _box.style.right = 18;
            _box.style.width = 274;
            _box.style.backgroundColor = new StyleColor(new Color(0.030f, 0.038f, 0.052f, 0.93f));
            T.Border(_box, 1, new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.55f));
            T.Radius(_box, 12);
            _box.style.paddingTop = 12;
            _box.style.paddingBottom = 12;
            _box.style.paddingLeft = 12;
            _box.style.paddingRight = 12;
            _box.pickingMode = PickingMode.Ignore;
            _box.style.display = DisplayStyle.None;

            _nameLabel = new Label(string.Empty);
            _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _nameLabel.style.fontSize = 13;
            _nameLabel.style.color = new StyleColor(T.TextPrimary);
            _nameLabel.style.marginBottom = 2;
            _nameLabel.pickingMode = PickingMode.Ignore;
            _box.Add(_nameLabel);

            var title = new Label("PLACEMENT ASSIST");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 9;
            title.style.letterSpacing = 1.5f;
            title.style.color = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.85f));
            title.style.marginBottom = 6;
            title.pickingMode = PickingMode.Ignore;
            _box.Add(title);

            // ── The live body ─────────────────────────────────────────
            _body = new PlacementBodyElement();
            _body.style.height = 112;
            _body.style.marginBottom = 6;
            _box.Add(_body);

            // ── Heading read-out (which way is forward, right now) ────
            var headingRow = new VisualElement();
            headingRow.style.flexDirection = FlexDirection.Row;
            headingRow.style.justifyContent = Justify.SpaceBetween;
            headingRow.style.alignItems = Align.Center;
            headingRow.style.marginBottom = 8;
            headingRow.pickingMode = PickingMode.Ignore;

            _headingLabel = new Label("FACING");
            _headingLabel.style.fontSize = 9;
            _headingLabel.style.letterSpacing = 1.2f;
            _headingLabel.style.color = new StyleColor(T.TextMuted);
            _headingLabel.pickingMode = PickingMode.Ignore;
            headingRow.Add(_headingLabel);

            _headingValue = new Label("--");
            _headingValue.style.fontSize = 10;
            _headingValue.style.unityFontStyleAndWeight = FontStyle.Bold;
            _headingValue.style.letterSpacing = 1.2f;
            _headingValue.style.color = new StyleColor(T.AccentCyan);
            _headingValue.pickingMode = PickingMode.Ignore;
            headingRow.Add(_headingValue);

            _box.Add(headingRow);

            // ── Chips: the three things that change a click ───────────
            _chipRow = new VisualElement();
            _chipRow.style.flexDirection = FlexDirection.Row;
            _chipRow.style.marginBottom = 6;
            _chipRow.pickingMode = PickingMode.Ignore;
            _patternChip = Chip(_chipRow);
            _latticeChip = Chip(_chipRow);
            _lockChip = Chip(_chipRow);
            _box.Add(_chipRow);

            // ── Run read-out (only while a pattern is laying) ─────────
            _runLabel = new Label(string.Empty);
            _runLabel.style.fontSize = 10;
            _runLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _runLabel.style.letterSpacing = 0.6f;
            _runLabel.style.color = new StyleColor(T.AccentGold);
            _runLabel.style.marginBottom = 6;
            _runLabel.style.display = DisplayStyle.None;
            _runLabel.pickingMode = PickingMode.Ignore;
            _box.Add(_runLabel);

            // ── Adaptive key rows ─────────────────────────────────────
            _legend = new VisualElement();
            _legend.pickingMode = PickingMode.Ignore;

            _legendPrimary = LegendRow(_legend, T.AccentGreen);
            _legendSecondary = LegendRow(_legend, new Color(0.62f, 0.66f, 0.72f));
            _box.Add(_legend);

            uiRoot.Add(_box);
        }

        private static Label Chip(VisualElement row)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = 9;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 0.8f;
            label.style.color = new StyleColor(T.TextPrimary);
            label.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.06f));
            label.style.paddingLeft = 6;
            label.style.paddingRight = 6;
            label.style.paddingTop = 2;
            label.style.paddingBottom = 2;
            label.style.marginRight = 4;
            label.style.borderTopLeftRadius = 4;
            label.style.borderTopRightRadius = 4;
            label.style.borderBottomLeftRadius = 4;
            label.style.borderBottomRightRadius = 4;
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            return label;
        }

        private static Label LegendRow(VisualElement parent, Color color)
        {
            var label = new Label(string.Empty);
            label.style.fontSize = 10;
            label.style.color = new StyleColor(color);
            label.style.marginBottom = 3;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;
            parent.Add(label);
            return label;
        }

        // ══════════════════════════════════════════════════════════════
        //  FRAME
        // ══════════════════════════════════════════════════════════════

        public static void Tick()
        {
            if (_box == null) return;

            // Self-heal an orphaned box after a UI-layer rebuild.
            if (_box.parent == null && _root != null && _root.panel != null)
            {
                _root.Add(_box);
                _visible = false;
            }

            bool gridBlock = GridBuilder.HoldingGridBlock;
            bool staticBlock = VoxelEngine.Building.BuildSystem.HoldingBlock;
            Vector3Int steps = gridBlock ? GridBuilder.RotationSteps
                : VoxelEngine.Building.BuildSystem.RotationSteps;

            // 17.44.1-dev: the card belongs to the HOLOGRAM, so only a builder may show it.
            // It used to be shown whenever any Inventory in the scene happened to hold a
            // block - including a machine's, a loot bag's or another player's - and stayed
            // on screen with an empty hand. The direct inventory read is kept only as a name
            // source, and only for an inventory that belongs to a player rig.
            string heldName = null;
            if (staticBlock && !string.IsNullOrEmpty(VoxelEngine.Building.BuildSystem.HeldBlockName))
            {
                heldName = VoxelEngine.Building.BuildSystem.HeldBlockName;
                steps = VoxelEngine.Building.BuildSystem.RotationSteps;
            }
            else if (gridBlock && !string.IsNullOrEmpty(GridBuilder.HeldBlockName))
            {
                heldName = GridBuilder.HeldBlockName;
                steps = GridBuilder.RotationSteps;
            }
            else if ((staticBlock || gridBlock) && TryResolveHeldDirect(out string directName))
            {
                heldName = directName;
            }

            bool show = !string.IsNullOrEmpty(heldName) && !VoxelEngine.UI.UIState.IsBlocking;

            // Bridge one-frame resolution flickers rather than blinking the card.
            if (!show && _lastName != null && heldName == null
                && !VoxelEngine.UI.UIState.IsBlocking
                && Time.unscaledTime - _lastShownAt < 0.75f)
            {
                heldName = _lastName;
                show = true;
            }

            _box.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                if (_visible) _visible = false;
                return;
            }

            _lastName = heldName;
            _lastShownAt = Time.unscaledTime;
            _visible = true;

            var block = VoxelEngine.Building.BuildSystem.HeldBlockItem;
            Quaternion rotation = VoxelEngine.Building.BuildSystem.GhostRotation;
            Vector3 up = VoxelEngine.Building.BuildSystem.GhostUp;
            bool valid = VoxelEngine.Building.BuildSystem.GhostValid;
            bool aligned = VoxelEngine.Building.BuildSystem.GhostAligned;
            string frameLabel = VoxelEngine.Building.BuildSystem.GhostFrameLabel;
            int runCells = VoxelEngine.Building.BuildSystem.PatternPreviewCount;

            // ── Name ──────────────────────────────────────────────────
            if (_nameLabel.text != "\u25A3 " + heldName)
                _nameLabel.text = "\u25A3 " + heldName;

            // ── The body: pose, axes, validity ────────────────────────
            var cardFrame = VoxelEngine.Building.BuildSystem.GhostFrame;
            Vector3 frameForward = cardFrame.valid ? cardFrame.forward : Vector3.zero;
            Vector3 stepVector = new(steps.x * 90f, steps.y * 90f, steps.z * 90f);
            if (_lastRotation != rotation || _lastUp != up || _lastSteps != stepVector
                || _lastFrameForward != frameForward
                || _lastValid != valid || _lastAligned != aligned)
            {
                _lastRotation = rotation;
                _lastUp = up;
                _lastFrameForward = frameForward;
                _lastSteps = stepVector;
                _lastValid = valid;
                _lastAligned = aligned;

                _body.SetPose(rotation, up, frameForward, steps, valid, aligned);
                _body.MarkDirtyRepaint();
            }

            // ── Compass read-out ─────────────────────────────────────
            string heading = PlacementAlign.Heading(rotation, VoxelEngine.Building.BuildSystem.GhostFrame, "--");
            string headingText = aligned ? heading + "  ·  LINED UP" : heading;
            if (_lastHeadingText != headingText)
            {
                _lastHeadingText = headingText;
                _headingValue.text = headingText;
                _headingValue.style.color = new StyleColor(aligned ? T.AccentGreen : T.AccentCyan);
                _headingLabel.text = string.IsNullOrEmpty(frameLabel) ? "FACING" : "FACING  ·  " + frameLabel;
            }

            // ── Chips ────────────────────────────────────────────────
            var pattern = PlacementOptions.Pattern;
            string patternText = PlacementOptions.PatternName(pattern);
            if (_lastPatternText != patternText)
            {
                _lastPatternText = patternText;
                _patternChip.text = patternText;
                _patternChip.style.color = new StyleColor(
                    pattern == PlacementPattern.Single ? T.TextMuted : T.AccentCyan);
            }

            bool gridOn = VoxelEngine.Building.BuildSystem.GridSnapActive;
            string latticeText = PlacementOptions.StepName(PlacementOptions.Step)
                                 + (gridOn ? string.Empty : " · OFF");
            if (_lastLatticeText != latticeText)
            {
                _lastLatticeText = latticeText;
                _latticeChip.text = latticeText;
                _latticeChip.style.color = new StyleColor(
                    !gridOn ? T.TextMuted
                    : PlacementOptions.Step == PlacementStep.Metre ? T.TextSecondary : T.AccentGold);
            }

            bool locked = PlacementLock.IsLocked && block != null && PlacementLock.Item == block;
            string lockText = locked ? "LOCKED" : "FREE";
            if (_lastLockText != lockText)
            {
                _lastLockText = lockText;
                _lockChip.text = lockText;
                _lockChip.style.color = new StyleColor(locked ? T.AccentAmber : T.TextMuted);
            }

            // ── Run read-out ─────────────────────────────────────────
            if (runCells != _lastRunCount)
            {
                _lastRunCount = runCells;
                if (runCells > 1)
                {
                    _runLabel.style.display = DisplayStyle.Flex;
                    _runLabel.text = "LAYING  " + runCells + " CELLS";
                }
                else if (pattern != PlacementPattern.Single)
                {
                    _runLabel.style.display = DisplayStyle.Flex;
                    _runLabel.text = "DRAG TO LAY A RUN";
                }
                else
                {
                    _runLabel.style.display = DisplayStyle.None;
                }
            }

            // ── Adaptive legend ──────────────────────────────────────
            string turnKey = GameSettings.GetKey(InputAction.BuildRotate);
            string lockKey = GameSettings.GetKey(InputAction.BuildLock);
            string wheelKey = GameSettings.GetKey(InputAction.BuildWheel);

            string gridKey = GameSettings.GetKey(InputAction.BuildToggleGrid);

            string primary, secondary;
            if (locked)
            {
                primary = "ARROWS  nudge one step  ·  SHIFT+ARROWS  five";
                secondary = "CTRL+ARROWS  lift / drop  ·  " + lockKey + "  release lock";
            }
            else if (runCells > 1)
            {
                primary = "RELEASE MOUSE  build the run";
                secondary = "CTRL  line up  ·  " + lockKey + "  freeze  ·  " + gridKey + "  lattice";
            }
            else
            {
                primary = turnKey + "  turn 90°   ·   SHIFT+" + turnKey + "  turn back   ·   CTRL+" + turnKey + "  reset";
                secondary = "CTRL  line up  ·  " + wheelKey + "  pattern  ·  " + gridKey + "  lattice  ·  "
                            + lockKey + "  freeze";
            }

            if (_lastLegendPrimary != primary)
            {
                _lastLegendPrimary = primary;
                _legendPrimary.text = primary;
            }
            if (_lastLegendSecondary != secondary)
            {
                _lastLegendSecondary = secondary;
                _legendSecondary.text = secondary;
            }

            _body.SetValid(valid);
        }

        // ══════════════════════════════════════════════════════════════
        //  GROUND-TRUTH "is the player holding a placeable block?"
        //  (reads the live inventory, independent of builder update order)
        // ══════════════════════════════════════════════════════════════

        private static VoxelEngine.Items.Inventory _inventoryCache;

        private static bool TryResolveHeldDirect(out string name)
        {
            name = null;
            if (_inventoryCache == null)
                _inventoryCache = Object.FindAnyObjectByType<VoxelEngine.Items.Inventory>();
            if (_inventoryCache == null || _inventoryCache.ActiveStack.IsEmpty
                || _inventoryCache.ActiveStack.item == null) return false;

            // Only a rig that can actually place blocks: a container, a rack or a loot bag
            // has an Inventory too, and reading one of those kept the card on screen.
            if (_inventoryCache.GetComponentInParent<VoxelEngine.Building.BuildSystem>() == null
                && _inventoryCache.GetComponentInParent<VoxelEngine.GridSystem.GridBuilder>() == null)
                return false;
            var item = _inventoryCache.ActiveStack.item;
            if (item is VoxelEngine.GridSystem.GridBlockItem gbi)
            {
                name = !string.IsNullOrEmpty(gbi.displayName) ? gbi.displayName : gbi.name;
                return true;
            }
            if (item is VoxelEngine.Items.BlockItem bi && bi.placedPrefab != null)
            {
                name = !string.IsNullOrEmpty(bi.displayName) ? bi.displayName : bi.name;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The projected wireframe body: the player's block in the hologram's own pose, with
    /// the three rotation axes drawn as orbital rings around it.
    /// </summary>
    public sealed class PlacementBodyElement : VisualElement
    {
        private static readonly Color EdgeColour = new(0.72f, 0.86f, 0.94f, 0.85f);
        private static readonly Color FarEdgeColour = new(0.55f, 0.70f, 0.80f, 0.32f);
        private static readonly Color FaceColour = new(0.20f, 0.66f, 0.84f, 0.30f);
        private static readonly Color GroundColour = new(0.55f, 0.72f, 0.85f, 0.20f);
        private static readonly Color YawColour = new(0.25f, 0.84f, 0.46f);
        private static readonly Color PitchColour = new(0.92f, 0.76f, 0.24f);
        private static readonly Color RollColour = new(0.16f, 0.70f, 0.78f);
        private static readonly Color DimAxis = new(0.55f, 0.60f, 0.68f, 0.28f);

        private Quaternion _rotation = Quaternion.identity;
        private Vector3 _up = Vector3.up;
        private Vector3 _referenceForward;
        private Vector3 _steps;
        private bool _valid = true;
        private bool _aligned;

        public PlacementBodyElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void SetPose(Quaternion rotation, Vector3 up, Vector3 referenceForward,
            Vector3Int steps, bool valid, bool aligned)
        {
            _rotation = rotation;
            _up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            _referenceForward = referenceForward;
            _steps = new Vector3(steps.x, steps.y, steps.z);
            _valid = valid;
            _aligned = aligned;
        }

        public void SetValid(bool valid)
        {
            if (_valid == valid) return;
            _valid = valid;
            MarkDirtyRepaint();
        }

        private static Vector2 Project(Vector3 local)
        {
            // A fixed three-quarter view: the same projection the line-art icons use, so the
            // card and the wheel agree about what "up and to the right" means.
            float x = (local.x - local.z) * 0.866f;
            float y = local.y + (local.x + local.z) * 0.5f;
            return new Vector2(x, -y);
        }

        private void Draw(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width <= 4f || rect.height <= 4f) return;

            var painter = ctx.painter2D;
            Vector2 centre = new(rect.center.x, rect.center.y + 6f);
            float scale = Mathf.Min(rect.width, rect.height) * 0.30f;

            // World → screen. The card's basis is FIXED against gravity and a stable world
            // tangent - it must never follow the block's own heading. It used to take its
            // forward from the block, which cancelled the yaw out: turning a block with R
            // changed nothing on the card, and the one thing the card exists to show was
            // invisible. Against a fixed basis the body turns exactly as the hologram does,
            // and the ground plane underneath stays put so a turn can be read.
            Vector3 up = _up;
            // The card's forward is the frame the block is being placed IN - the structure it
            // is lining up with - not the block's own heading, which cancelled the yaw out.
            Vector3 forward = Vector3.ProjectOnPlane(_referenceForward, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = PlacementFrame.FromUp(Vector3.zero, up, "CARD", false).forward;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.right;
            forward.Normalize();
            Vector3 right = Vector3.Cross(up, forward).normalized;

            // The body: the hologram's own rotation, expressed in that basis.
            Quaternion body = Quaternion.LookRotation(
                new Vector3(Vector3.Dot(_rotation * Vector3.forward, right),
                            Vector3.Dot(_rotation * Vector3.forward, up),
                            Vector3.Dot(_rotation * Vector3.forward, forward)),
                new Vector3(Vector3.Dot(_rotation * Vector3.up, right),
                            Vector3.Dot(_rotation * Vector3.up, up),
                            Vector3.Dot(_rotation * Vector3.up, forward)));

            DrawGround(painter, centre, scale);
            DrawAxes(painter, centre, scale, body);
            DrawBody(painter, centre, scale, body);
        }

        private void DrawGround(Painter2D painter, Vector2 centre, float scale)
        {
            // A faint plane under the block: it is what makes a pitch and a roll visible at
            // all, because without a horizon every orientation looks like every other one.
            float radius = scale * 1.32f;
            painter.strokeColor = GroundColour;
            painter.lineWidth = 1f;
            for (int i = -1; i <= 1; i++)
            {
                painter.BeginPath();
                Vector2 a = centre + Project(new Vector3(i * 0.55f, -0.72f, -1f)) * radius;
                Vector2 b = centre + Project(new Vector3(i * 0.55f, -0.72f, 1f)) * radius;
                painter.MoveTo(a);
                painter.LineTo(b);
                painter.Stroke();

                painter.BeginPath();
                a = centre + Project(new Vector3(-1f, -0.72f, i * 0.55f)) * radius;
                b = centre + Project(new Vector3(1f, -0.72f, i * 0.55f)) * radius;
                painter.MoveTo(a);
                painter.LineTo(b);
                painter.Stroke();
            }
        }

        private void DrawAxes(Painter2D painter, Vector2 centre, float scale, Quaternion body)
        {
            // The rings orbit the BLOCK's own axes, not the screen's: on a wall the yaw ring
            // has to point out of the wall, or the card would be lying about which way a
            // wheel turn will spin the piece.
            float r = scale * 1.16f;
            DrawOrbit(painter, centre, r, body * Vector3.up, YawColour, _steps.y != 0f);
            DrawOrbit(painter, centre, r * 1.04f, body * Vector3.right, PitchColour, _steps.x != 0f);
            DrawOrbit(painter, centre, r * 1.10f, body * Vector3.forward, RollColour, _steps.z != 0f);
        }

        /// <summary>An orbital ring around one axis, with an arrowhead at the leading edge.</summary>
        private void DrawOrbit(Painter2D painter, Vector2 centre, float radius, Vector3 axis,
                               Color colour, bool active)
        {
            const int segments = 30;
            // Build two tangents perpendicular to the axis in the card's own view space.
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > 0.9f
                ? Vector3.right : Vector3.forward;
            Vector3 u = Vector3.Cross(axis, reference).normalized;
            Vector3 v = Vector3.Cross(axis, u).normalized;

            painter.strokeColor = active ? colour : DimAxis;
            painter.lineWidth = active ? 1.8f : 1f;

            Vector2 previous = default;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 1.72f;   // open arc, not a full ring
                Vector3 point = (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius;
                Vector2 screen = centre + Project(point);
                if (i == 0) painter.BeginPath();
                if (i == 0) painter.MoveTo(screen);
                else painter.LineTo(screen);
                previous = screen;
                if (i == segments) painter.Stroke();
            }

            if (!active) return;

            // Arrowhead: two short strokes back from the arc's end.
            float endAngle = Mathf.PI * 1.72f;
            Vector3 endPoint = (u * Mathf.Cos(endAngle) + v * Mathf.Sin(endAngle)) * radius;
            Vector3 tangent = (-u * Mathf.Sin(endAngle) + v * Mathf.Cos(endAngle));
            Vector2 tip = centre + Project(endPoint);
            Vector2 back = centre + Project(endPoint - tangent * (radius * 0.22f));
            Vector2 side = new Vector2(-(tip - back).y, (tip - back).x) * 0.32f;
            painter.strokeColor = colour;
            painter.lineWidth = 1.8f;
            painter.BeginPath();
            painter.MoveTo(back + side);
            painter.LineTo(tip);
            painter.LineTo(back - side);
            painter.Stroke();
        }

        private void DrawBody(Painter2D painter, Vector2 centre, float scale, Quaternion rotation)
        {
            const float half = 0.46f;
            // Local corners of the block, before the hologram's rotation is applied.
            Vector3[] corners =
            {
                new(-half, -half, -half), new(half, -half, -half),
                new(half, half, -half),   new(-half, half, -half),
                new(-half, -half, half),  new(half, -half, half),
                new(half, half, half),    new(-half, half, half),
            };

            Vector2[] screen = new Vector2[8];
            Vector3[] view = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                view[i] = rotation * corners[i];
                screen[i] = centre + Project(view[i]) * (scale * 1.30f);
            }

            // The front face (local +Z) is filled so "forward" is unmistakable.
            painter.fillColor = _valid ? FaceColour : new Color(0.85f, 0.28f, 0.20f, 0.30f);
            painter.BeginPath();
            painter.MoveTo(screen[4]);
            painter.LineTo(screen[5]);
            painter.LineTo(screen[6]);
            painter.LineTo(screen[7]);
            painter.ClosePath();
            painter.Fill(FillRule.NonZero);

            // Edges nearest the viewer read solid; the three behind them read faint.
            int[,] edges =
            {
                { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
                { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
            };
            Vector3 viewDirection = new Vector3(-0.45f, -0.62f, -0.64f).normalized;
            for (int e = 0; e < 12; e++)
            {
                int a = edges[e, 0], b = edges[e, 1];
                // An edge is "near" when either endpoint faces the viewer.
                bool near = Vector3.Dot(view[a], viewDirection) < 0.05f
                            || Vector3.Dot(view[b], viewDirection) < 0.05f;
                painter.strokeColor = near ? EdgeColour : FarEdgeColour;
                painter.lineWidth = near ? 1.9f : 1f;
                painter.BeginPath();
                painter.MoveTo(screen[a]);
                painter.LineTo(screen[b]);
                painter.Stroke();
            }

            // A pip on the front face marks the exact face centre - the thing a machine's
            // ports and doors are referenced from.
            painter.fillColor = _aligned ? new Color(0.25f, 0.86f, 0.47f, 0.95f) : new Color(0.85f, 0.92f, 1f, 0.85f);
            painter.BeginPath();
            painter.Arc(centre + Project(rotation * new Vector3(0f, 0f, half)) * (scale * 1.30f), 2.6f,
                Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Fill(FillRule.NonZero);
        }
    }
}
