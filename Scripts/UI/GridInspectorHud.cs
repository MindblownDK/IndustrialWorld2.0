// Assets/Scripts/VoxelEngine/UI/GridInspectorHud.cs
//
// GRID INSPECTOR OVERLAY — heat, damage and centre-of-mass reads on the construct the
// player is looking at (roadmap 5.1 item 18, 9.37.0-dev). A single rebindable hotkey
// cycles OFF → HEAT → DAMAGE → CENTRE OF MASS, and every mode is also selectable from
// the compact pill that appears while the overlay is on.
//
// Design rules this file honours:
//   • It is a VIEWING MODE, not a HUD and not a gameplay change: it re-colours what the
//     existing block renderers mean for as long as it is on, and restores them the
//     moment it is off. Nothing about the ship, its damage or its heat is altered.
//   • One shared pass driven by per-renderer MaterialPropertyBlock data: no per-block
//     GameObjects, no material instances, no scene objects while the overlay is off.
//     Blocks keep their own BlockDamageVisual cracks/soot underneath — this pass only
//     tints their base colour, so the two read as one picture (the damage shells are
//     generated renderers and are explicitly skipped here).
//   • The three modes are research unlocks, not a settings toggle. Damage first
//     (structural awareness), heat second (engine-room awareness), centre of mass last
//     (ship design). A press with nothing researched says why, in one line, and does
//     nothing else.
//   • Budget discipline: a construct above 240 blocks degrades to the 24 blocks
//     nearest the camera rather than dropping frames, and per-renderer writes are
//     skipped while the tint has not changed.
//
// Targets: any grid (boarded or not — the interesting read is a ship you are NOT
// standing in), any placed static block, and any tiered building piece. While the
// player sits in a cockpit the subject is the grid under the seat. A target that was
// aimed at keeps its read for a short grace so a glance at the sky does not blank the
// view, then clears itself.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;
using VoxelEngine.GridSystem;
using VoxelEngine.Research;
using VoxelEngine.Thermal;
using InputAction = VoxelEngine.Settings.InputAction;
using GameSettings = VoxelEngine.Settings.GameSettings;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>The states of the inspector overlay. The hotkey ring runs
    /// OFF → HEAT → DAMAGE → CENTRE OF MASS → OFF (see GridInspectorHud.Ring).</summary>
    public enum GridInspectorMode
    {
        Off = 0,
        Heat = 1,
        Damage = 2,
        CentreOfMass = 3,
    }

    public static class GridInspectorHud
    {
        // ── Research node ids (authored non-destructively by Setup Step 68) ─────
        public const string NodeDamage = "res_grid_inspector_damage";
        public const string NodeHeat = "res_grid_inspector_heat";
        public const string NodeCom = "res_grid_inspector_com";

        // ── Cadence / budgets ───────────────────────────────────────────────────
        private const float ProbeInterval = 0.20f;          // re-aim at the world
        private const float ScanInterval = 0.125f;          // re-read block data (8 Hz)
        private const float ProbeDistance = 160f;           // a ship can be far away
        private const float TargetGraceSeconds = 1.5f;      // keep last subject after a miss
        private const float MessageCooldown = 1.6f;
        private const int MaxFullBlocks = 240;              // above this, degrade
        private const int DegradedNearest = 24;             // …to the 24 nearest blocks
        private const float ComDataInterval = 0.5f;

        // The damage ramp keys: scratch → lost. Integrity is read per block from the
        // block's own maxHP, so a shielded plate and a glass canopy share one ramp.
        private static readonly Color RampHealthy = new(0.30f, 0.78f, 0.68f);
        private static readonly Color RampAmber = new(0.95f, 0.66f, 0.22f);
        private static readonly Color RampRed = new(1.00f, 0.22f, 0.13f);
        // The heat ramp: warm (120 °C floor) → hot (60 % of tolerance) → critical
        // (tolerance) → white steel past the burn span.
        private static readonly Color RampWarm = new(0.95f, 0.66f, 0.22f);
        private static readonly Color RampHot = new(1.00f, 0.42f, 0.12f);
        private static readonly Color RampCritical = new(1.00f, 0.20f, 0.12f);
        private static readonly Color RampWhiteHot = new(1.00f, 0.92f, 0.82f);

        // ── Mounted UI ──────────────────────────────────────────────────────────
        private static VisualElement _root;
        private static VisualElement _card;
        private static Label _titleLabel;
        private static Label _statusLabel;
        private static VisualElement _chipsRow;
        private static readonly List<Button> _chips = new();
        private static VisualElement _worstTag;             // floating worst-block read
        private static Label _worstLabel;
        private static bool _uiReady;
        private static float _uiOpacity = 1f;

        // ── State ──────────────────────────────────────────────────────────────
        private static GridInspectorMode _mode;
        private static float _nextProbe;
        private static float _nextScan;
        private static float _nextComData;
        private static float _nextMessageAt;
        private static float _aimMissSince = -999f;   // when the crosshair last found nothing

        private static MonoBehaviour _target;               // GridEntity | PlacedBlock | PlacedTieredBlock
        private static string _targetName = string.Empty;
        private static string _lastStatusText = string.Empty;

        // Worst readouts found by the last scan (HEAT marker + DAMAGE summary).
        private static GridBlock _worstBlock;
        private static float _worstTemperatureC;
        private static float _worstSeverity = float.NegativeInfinity;
        private static float _worstDamage01;

        // COM geometry (computed at ComDataInterval, markers follow every frame).
        private static Vector3 _comPosition;
        private static float _comRadius;
        private static float _comOffsetFromThrustLine;
        private static float _comMassKg;
        private static bool _comHasDrive;
        private static Vector3 _thrustCentre;
        private static Vector3 _thrustAxis;
        private static float _thrustHalfLength;

        // Tint bookkeeping. `saved` is the pre-overlay property block that must be put
        // back on exit; `working` is a scratch block re-filled from the renderer each
        // write so other MPB writers (paint, pipe flow) keep their own properties.
        private sealed class TintEntry
        {
            public MaterialPropertyBlock saved;
            public MaterialPropertyBlock working;
            public int baseColorId = -1;
            public Color32 last;
            public bool applied;
        }
        private static readonly Dictionary<Renderer, TintEntry> _tints = new();
        private static readonly List<Renderer> _tintScrub = new();

        // COM markers — the only scene objects this feature ever creates, and they are
        // invisible until CENTRE OF MASS is on.
        private static GameObject _comBall;
        private static GameObject _comLine;
        private static Renderer _comBallRenderer;
        private static LineRenderer _comLineRenderer;
        private static Material _markerMaterial;
        private static int _markerColorId = -1;
        private static readonly MaterialPropertyBlock _markerBlock = new();
        private static Color32 _lastBallColor;
        private static Color32 _lastLineColor;
        private static float _lastLineWidth = -1f;

        // Per-block renderer cache: scanning a hull at 8 Hz must not re-walk every
        // transform tree each tick. Entries are rebuilt when the block's child count
        // changes (shape variants) and evicted when the block dies.
        private sealed class BlockVisuals
        {
            public readonly List<Renderer> renderers = new();
            public int childCount = -1;
            public float builtAt = -99f;
        }
        private static readonly Dictionary<Component, BlockVisuals> _visualCache = new();
        private static readonly List<Component> _cacheScrub = new();

        private static readonly List<GridBlock> _blockScratch = new();
        private static readonly List<float> _distScratch = new();

        // ── Public API ──────────────────────────────────────────────────────────
        public static GridInspectorMode Mode => _mode;
        public static bool IsActive => _mode != GridInspectorMode.Off;

        /// <summary>Mount the pill into the persistent HUD layer. No-op once mounted.</summary>
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _card != null && _card.parent == uiRoot) return;
            _root = uiRoot;
            if (_card != null) _card.RemoveFromHierarchy();
            if (_worstTag != null) _worstTag.RemoveFromHierarchy();

            _card = new VisualElement { name = "GridInspectorHud" };
            _card.style.position = Position.Absolute;
            // Top-centre anchor: the bottom-centre band is crowded (hotbar name at 88,
            // build cost at 90, canister strip at 96, bombs at 84, grinder at 190), and
            // the world-inspection card owns the top-left. 170 px clears the cockpit
            // alert banner at 16 % height and the persistent readouts alike.
            _card.style.top = 170;
            _card.style.left = Length.Percent(50f);
            _card.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f, 0f);
            _card.style.flexDirection = FlexDirection.Row;
            _card.style.alignItems = Align.Center;
            _card.style.paddingLeft = 12;
            _card.style.paddingRight = 12;
            _card.style.paddingTop = 5;
            _card.style.paddingBottom = 5;
            _card.style.backgroundColor = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.94f));
            T.Radius(_card, 6f);
            T.Border(_card, 1, new Color(T.BorderBright.r, T.BorderBright.g, T.BorderBright.b, 0.5f));
            _card.style.opacity = 0f;
            _card.style.display = DisplayStyle.None;
            _card.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_card);

            _titleLabel = new Label("INSPECTOR");
            _titleLabel.style.color = new StyleColor(T.TextPrimary);
            _titleLabel.style.fontSize = 10;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.letterSpacing = 1.2f;
            _titleLabel.style.marginRight = 10;
            _titleLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_titleLabel);

            _statusLabel = new Label();
            _statusLabel.style.color = new StyleColor(T.TextMuted);
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.letterSpacing = 0.4f;
            _statusLabel.style.marginRight = 10;
            _statusLabel.style.maxWidth = 340;
            _statusLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_statusLabel);

            // Mode chips — the overlay itself is the selector, per the design.
            _chipsRow = new VisualElement { name = "InspectorChips" };
            _chipsRow.style.flexDirection = FlexDirection.Row;
            _chipsRow.pickingMode = PickingMode.Ignore;
            _card.Add(_chipsRow);

            _chips.Clear();
            AddChip("DAMAGE", GridInspectorMode.Damage);
            AddChip("HEAT", GridInspectorMode.Heat);
            AddChip("C.O.M", GridInspectorMode.CentreOfMass);

            // The floating worst-block tag (HEAT only) sits above everything.
            _worstTag = new VisualElement { name = "InspectorWorstTag" };
            _worstTag.style.position = Position.Absolute;
            _worstTag.style.paddingLeft = 8;
            _worstTag.style.paddingRight = 8;
            _worstTag.style.paddingTop = 3;
            _worstTag.style.paddingBottom = 3;
            _worstTag.style.backgroundColor = new StyleColor(new Color(0.055f, 0.045f, 0.045f, 0.90f));
            T.Radius(_worstTag, 4f);
            T.Border(_worstTag, 1, new Color(T.AccentRed.r, T.AccentRed.g, T.AccentRed.b, 0.7f));
            _worstTag.style.display = DisplayStyle.None;
            _worstTag.pickingMode = PickingMode.Ignore;
            _worstLabel = new Label();
            _worstLabel.style.color = new StyleColor(new Color(1f, 0.55f, 0.45f));
            _worstLabel.style.fontSize = 10;
            _worstLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _worstLabel.style.letterSpacing = 1f;
            _worstLabel.pickingMode = PickingMode.Ignore;
            _worstTag.Add(_worstLabel);
            uiRoot.Add(_worstTag);

            _uiReady = true;
            _uiOpacity = 0f;
            RefreshPill();
        }

        private static void AddChip(string label, GridInspectorMode mode)
        {
            var chip = new Button(() => RequestMode(mode)) { text = label };
            chip.style.fontSize = 9;
            chip.style.unityFontStyleAndWeight = FontStyle.Bold;
            chip.style.letterSpacing = 0.8f;
            chip.style.marginLeft = 3;
            chip.style.paddingLeft = 9;
            chip.style.paddingRight = 9;
            chip.style.paddingTop = 3;
            chip.style.paddingBottom = 3;
            chip.style.backgroundColor = new StyleColor(T.BgSlot);
            chip.style.color = new StyleColor(T.TextSecondary);
            chip.style.borderTopWidth = 0; chip.style.borderBottomWidth = 0;
            chip.style.borderLeftWidth = 0; chip.style.borderRightWidth = 0;
            T.Radius(chip, 4f);
            chip.RegisterCallback<PointerEnterEvent>(_ => chip.style.backgroundColor = new StyleColor(T.BgHover));
            chip.RegisterCallback<PointerLeaveEvent>(_ => chip.style.backgroundColor = new StyleColor(T.BgSlot));
            _chipsRow.Add(chip);
            _chips.Add(chip);
        }

        // ── Per-frame entry (called from GameUIController.Update) ───────────────
        public static void Tick()
        {
            if (!_uiReady || _card == null) return;
            SelfHeal();

            bool blocked = UIState.IsBlocking || UIState.IsHardPause || UIState.TextInputActive;

            // The hotkey: one key, walks the ring OFF → HEAT → DAMAGE → CENTRE OF MASS.
            if (!blocked && GameSettings.WasPressed(InputAction.GridInspector))
                CycleMode();

            if (_mode == GridInspectorMode.Off)
            {
                HideUi();
                return;
            }

            float now = Time.unscaledTime;

            // Aim at the world (or take the grid under the seat while piloting).
            if (!blocked && now >= _nextProbe)
            {
                _nextProbe = now + ProbeInterval;
                ResolveTarget();
            }

            // A subject that has been out of the crosshair for the grace period is
            // released, so a ship you glanced at once is not tinted from two hills away.
            if (_target != null && _aimMissSince > 0f && now - _aimMissSince > TargetGraceSeconds)
                DropTarget();

            if (now >= _nextScan)
            {
                _nextScan = now + ScanInterval;
                ScanTick();
            }

            if (_mode == GridInspectorMode.CentreOfMass)
            {
                HideWorstTag();
                TickCom(now);
            }
            else if (_mode == GridInspectorMode.Heat) TickWorstTag();
            else HideWorstTag();

            // The pill yields to blocking panels: fade out fast while one is open so the
            // chips can never eat a click meant for a machine panel, and fade back after.
            float targetOpacity = blocked ? 0f : 1f;
            _uiOpacity = Mathf.MoveTowards(_uiOpacity, targetOpacity, Time.unscaledDeltaTime * (blocked ? 14f : 7f));
            bool hidden = _uiOpacity < 0.02f;
            if (_card.style.display == DisplayStyle.Flex)
            {
                _card.style.opacity = _uiOpacity;
                // A faded pill must never intercept the cursor.
                for (int i = 0; i < _chips.Count; i++)
                    _chips[i].pickingMode = hidden ? PickingMode.Ignore : PickingMode.Position;
            }
            if (_worstTag.style.display == DisplayStyle.Flex)
                _worstTag.style.opacity = _uiOpacity;
        }

        private static void SelfHeal()
        {
            if (_card.parent == null && _root != null && _root.panel != null)
            {
                _root.Add(_card);
                if (_worstTag != null && _worstTag.parent == null) _root.Add(_worstTag);
                _uiOpacity = 0f;
            }
        }

        // ── Mode control ────────────────────────────────────────────────────────
        /// <summary>The canonical hotkey ring: OFF → HEAT → DAMAGE → CENTRE OF MASS → OFF.</summary>
        private static readonly GridInspectorMode[] Ring =
        {
            GridInspectorMode.Off,
            GridInspectorMode.Heat,
            GridInspectorMode.Damage,
            GridInspectorMode.CentreOfMass,
        };

        private static void CycleMode()
        {
            // Walk the ring forward from the current state. A locked mode is skipped
            // silently — the press lands on the next mode the player has earned — and
            // the ring's OFF state is the normal way out of the last earned mode. Only
            // when nothing at all is unlocked does the press say why, in one line, and
            // do nothing else.
            if (!IsUnlocked(GridInspectorMode.Heat)
                && !IsUnlocked(GridInspectorMode.Damage)
                && !IsUnlocked(GridInspectorMode.CentreOfMass))
            {
                ResolveGate(GridInspectorMode.Damage, out string reason);
                Notify(reason);
                return;
            }

            int pos = 0;
            for (int i = 0; i < Ring.Length; i++)
                if (Ring[i] == _mode) { pos = i; break; }

            for (int step = 1; step <= Ring.Length; step++)
            {
                GridInspectorMode candidate = Ring[(pos + step) % Ring.Length];
                if (candidate == GridInspectorMode.Off)
                {
                    SetMode(GridInspectorMode.Off);
                    return;
                }
                if (IsUnlocked(candidate))
                {
                    SetMode(candidate);
                    return;
                }
            }
        }

        /// <summary>Whether a mode's research node is unlocked (true with no research
        /// system present, so the overlay never hard-locks on a stripped scene).</summary>
        private static bool IsUnlocked(GridInspectorMode mode)
        {
            var rm = ResearchManager.Instance;
            if (rm == null) return true;
            var node = FindNode(rm, NodeIdOf(mode));
            return node != null && rm.IsUnlocked(node);
        }

        private static string NodeIdOf(GridInspectorMode mode) => mode switch
        {
            GridInspectorMode.Damage => NodeDamage,
            GridInspectorMode.Heat => NodeHeat,
            _ => NodeCom,
        };

        private static void RequestMode(GridInspectorMode requested)
        {
            if (requested == GridInspectorMode.Off)
            {
                SetMode(GridInspectorMode.Off);
                return;
            }
            if (!ResolveGate(requested, out string reason))
            {
                Notify(reason);
                return;   // says why, in one line, and does nothing else
            }
            SetMode(requested);
        }

        /// <summary>The progression gate: damage → heat → centre of mass, each its own node.</summary>
        private static bool ResolveGate(GridInspectorMode requested, out string reason)
        {
            reason = string.Empty;
            var rm = ResearchManager.Instance;
            if (rm == null) return true;   // no research system in this scene: never hard-lock

            var node = FindNode(rm, NodeIdOf(requested));
            if (node == null)
            {
                reason = "Run Voxel Engine Setup Step 68 to author the Grid Inspector research nodes.";
                return false;
            }
            if (rm.IsUnlocked(node))
            {
                reason = string.Empty;
                return true;
            }
            string verb = requested switch
            {
                GridInspectorMode.Damage => "unlocks the integrity scan",
                GridInspectorMode.Heat => "unlocks the thermal scan",
                _ => "unlocks the centre-of-mass read",
            };
            reason = "Research \u201C" + node.displayName + "\u201D first \u2014 it " + verb + ".";
            return false;
        }

        private static ResearchNode FindNode(ResearchManager rm, string nodeId)
        {
            if (rm == null || rm.tree == null || rm.tree.nodes == null) return null;
            var nodes = rm.tree.nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n != null && n.nodeId == nodeId) return n;
            }
            return null;
        }

        private static void SetMode(GridInspectorMode next)
        {
            if (_mode == next) return;
            ClearTints();
            HideMarkers();
            HideWorstTag();
            _mode = next;
            _worstBlock = null;
            _worstSeverity = float.NegativeInfinity;
            _worstDamage01 = 0f;
            _worstTemperatureC = 0f;
            _lastStatusText = string.Empty;
            _nextScan = 0f;
            _nextComData = 0f;
            _aimMissSince = -999f;

            if (_mode == GridInspectorMode.Off)
            {
                _card.style.display = DisplayStyle.None;
                _target = null;
                _targetName = string.Empty;
                // The overlay's own rule: no scene objects while it is off. The two
                // markers are destroyed here and re-created the next time CENTRE OF
                // MASS turns on, so an idle scene never carries inspector leftovers.
                DestroyMarkers();
                return;
            }

            _card.style.display = DisplayStyle.Flex;
            Notify(PillCaption(_mode) + " ON \u2014 point it at a ship, base block or wreck");
            RefreshPill();
        }

        private static void Notify(string detail)
        {
            if (Time.unscaledTime < _nextMessageAt) return;
            _nextMessageAt = Time.unscaledTime + MessageCooldown;
            BuildFeedbackHud.Show("GRID INSPECTOR", detail, null, ModeAccent(_mode));
        }

        private static string PillCaption(GridInspectorMode m) => m switch
        {
            GridInspectorMode.Damage => "INTEGRITY SCAN",
            GridInspectorMode.Heat => "THERMAL SCAN",
            GridInspectorMode.CentreOfMass => "MASS BALANCE",
            _ => "INSPECTOR",
        };

        private static Color ModeAccent(GridInspectorMode m) => m switch
        {
            GridInspectorMode.Damage => T.AccentGreen,
            GridInspectorMode.Heat => T.AccentOrange,
            GridInspectorMode.CentreOfMass => T.AccentCyan,
            _ => T.AccentDim,
        };

        private static void SetStatus(string text)
        {
            if (_statusLabel == null) return;
            if (text == _lastStatusText) return;
            _lastStatusText = text;
            _statusLabel.text = text;
        }

        private static void RefreshPill()
        {
            if (!_uiReady) return;
            _titleLabel.text = PillCaption(_mode);
            _titleLabel.style.color = new StyleColor(ModeAccent(_mode));
            for (int i = 0; i < _chips.Count; i++)
            {
                var chip = _chips[i];
                bool active = (i == 0 && _mode == GridInspectorMode.Damage)
                              || (i == 1 && _mode == GridInspectorMode.Heat)
                              || (i == 2 && _mode == GridInspectorMode.CentreOfMass);
                chip.style.color = new StyleColor(active ? Color.white : T.TextSecondary);
                chip.style.backgroundColor = new StyleColor(active ? ModeAccent(_mode) * 0.45f : T.BgSlot);
            }
        }

        private static void HideUi()
        {
            if (_card.style.display != DisplayStyle.None) _card.style.display = DisplayStyle.None;
            if (_worstTag.style.display != DisplayStyle.None) _worstTag.style.display = DisplayStyle.None;
        }

        // ── Target resolution ──────────────────────────────────────────────────
        private static void ResolveTarget()
        {
            bool found = false;

            // A seated pilot reads the grid under the seat; everyone else probes the
            // crosshair so the interesting case works — inspecting a construct the
            // player is NOT standing in.
            var seatGrid = GridCockpit.ActiveControlGrid;
            if (seatGrid != null)
            {
                SetTarget(seatGrid, GridName(seatGrid));
                _aimMissSince = -999f;
                return;
            }

            var camera = Camera.main;
            if (camera == null) return;
            Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            var player = camera.GetComponentInParent<VoxelEngine.Player.PlayerController>();
            Transform playerRoot = player != null ? player.transform : null;

            var hits = Physics.RaycastAll(ray, ProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (collider == null) continue;
                if (playerRoot != null && collider.transform.IsChildOf(playerRoot)) continue;
                string rootName = collider.transform.root != null ? collider.transform.root.name : string.Empty;
                if (IsTransientRigName(rootName)) continue;
                if (IsSystemCollider(collider, rootName)) continue;

                var grid = collider.GetComponentInParent<GridEntity>();
                if (grid != null)
                {
                    SetTarget(grid, GridName(grid));
                    found = true;
                    break;
                }
                var tiered = collider.GetComponentInParent<PlacedTieredBlock>();
                if (tiered != null && tiered.definition != null)
                {
                    SetTarget(tiered, tiered.definition.displayName);
                    found = true;
                    break;
                }
                var placed = collider.GetComponentInParent<PlacedBlock>();
                if (placed != null)
                {
                    SetTarget(placed, placed.Item != null ? placed.Item.displayName : placed.name);
                    found = true;
                    break;
                }
            }
            // Remember when the crosshair last found a construct, so a subject that
            // stopped being looked at can be released after the grace period.
            _aimMissSince = found ? -999f : Time.unscaledTime;
        }

        private static bool IsTransientRigName(string rootName)
        {
            if (string.IsNullOrEmpty(rootName)) return false;
            return rootName.StartsWith("GridGhost", System.StringComparison.Ordinal)
                || rootName.StartsWith("BuildGhost", System.StringComparison.Ordinal)
                || rootName.StartsWith("Viewmodel", System.StringComparison.Ordinal)
                || rootName.StartsWith("PipePrecisionLatticePreview", System.StringComparison.Ordinal)
                || rootName.StartsWith("LedStretchGhost", System.StringComparison.Ordinal);
        }

        private static bool IsSystemCollider(Collider collider, string rootName)
        {
            if (collider == null) return true;
            if (collider.GetComponentInParent<VoxelEngine.Cosmos.PlanetSafetyCollider>() != null) return true;
            string ownName = collider.gameObject.name;
            return ownName.IndexOf("Bootstrap", System.StringComparison.OrdinalIgnoreCase) >= 0
                || ownName.IndexOf("OceanLOD", System.StringComparison.OrdinalIgnoreCase) >= 0
                || ownName.IndexOf("PlanetLOD", System.StringComparison.OrdinalIgnoreCase) >= 0
                || ownName.IndexOf("NativeSphericalWater", System.StringComparison.OrdinalIgnoreCase) >= 0
                || rootName.IndexOf("Bootstrap", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GridName(GridEntity grid)
        {
            if (grid == null) return string.Empty;
            string n = grid.name;
            return string.IsNullOrWhiteSpace(n) ? "SHIP" : n.Replace("(Clone)", string.Empty).Trim();
        }

        private static void SetTarget(MonoBehaviour next, string name)
        {
            if (ReferenceEquals(_target, next)) return;
            ClearTints();
            HideMarkers();
            _target = next;
            _targetName = string.IsNullOrWhiteSpace(name) ? "TARGET" : name.ToUpperInvariant();
            _worstBlock = null;
            _worstSeverity = float.NegativeInfinity;
            _worstDamage01 = 0f;
            _lastStatusText = string.Empty;
            _nextScan = 0f;
            _nextComData = 0f;
            _aimMissSince = -999f;
        }

        private static void DropTarget()
        {
            if (_target == null) return;
            ClearTints();
            HideMarkers();
            HideWorstTag();
            _target = null;
            _targetName = string.Empty;
            _worstBlock = null;
            _lastStatusText = string.Empty;
            _aimMissSince = -999f;
            SetStatus("NO TARGET \u2014 AIM AT A CONSTRUCT");
        }

        // ── Scan: read the subject and tint it ─────────────────────────────────
        private static void ScanTick()
        {
            PruneCache();
            if (_target == null) return;

            if (_mode == GridInspectorMode.CentreOfMass)
                return;

            _worstBlock = null;
            _worstSeverity = float.NegativeInfinity;
            _worstDamage01 = 0f;

            if (_target is GridEntity grid) ScanGrid(grid);
            else
            {
                ScanSingleBlock(_target);
                // Single static blocks compose their own status line inside the scan
                // (a base block has a temperature readout, not a worst-block search).
                return;
            }

            // Live status line: what the eye should look for on this subject.
            if (_mode == GridInspectorMode.Heat)
            {
                if (_worstBlock != null)
                {
                    var band = ThermalRules.Band(_worstBlock, _worstTemperatureC);
                    SetStatus(_targetName + "  \u00B7  WORST "
                        + _worstBlock.blockName.ToUpperInvariant() + " "
                        + _worstTemperatureC.ToString("0") + "\u00B0C "
                        + ThermalRules.BandLabel(band));
                }
                else SetStatus(_targetName + "  \u00B7  ALL COOL");
            }
            else
            {
                SetStatus(_targetName + "  \u00B7  WORST DAMAGE "
                    + (_worstDamage01 * 100f).ToString("0") + "%");
            }
        }

        private static void ScanGrid(GridEntity grid)
        {
            if (grid == null) return;

            _blockScratch.Clear();
            _distScratch.Clear();
            Vector3 camPos = Camera.main != null ? Camera.main.transform.position : grid.transform.position;
            foreach (var block in grid.AllBlocks)
            {
                if (block == null) continue;
                _blockScratch.Add(block);
                _distScratch.Add((block.transform.position - camPos).sqrMagnitude);
            }

            bool degrade = _blockScratch.Count > MaxFullBlocks;
            if (degrade)
            {
                // Budget guard: the 24 nearest blocks, cheap selection sort.
                for (int i = 0; i < _blockScratch.Count - 1; i++)
                {
                    for (int j = i + 1; j < _blockScratch.Count; j++)
                    {
                        if (_distScratch[j] < _distScratch[i])
                        {
                            (_blockScratch[i], _blockScratch[j]) = (_blockScratch[j], _blockScratch[i]);
                            (_distScratch[i], _distScratch[j]) = (_distScratch[j], _distScratch[i]);
                        }
                    }
                }
                _blockScratch.RemoveRange(DegradedNearest, _blockScratch.Count - DegradedNearest);
            }

            var thermal = grid.GetComponent<VoxelEngine.Thermal.GridThermalSystem>();

            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var block = _blockScratch[i];
                if (block == null || !block.gameObject.activeInHierarchy) continue;

                if (_mode == GridInspectorMode.Heat)
                {
                    float temperature = thermal != null ? thermal.TemperatureOf(block) : ThermalRules.FallbackAmbientC;
                    Color? tint = HeatTint(block, temperature, out float severity, out float severityTemperature);
                    if (severity > _worstSeverity)
                    {
                        _worstSeverity = severity;
                        _worstBlock = block;
                        _worstTemperatureC = severityTemperature;
                    }
                    ApplyBlockTint(block, tint);
                }
                else
                {
                    float damage = block.Damage01;
                    if (damage > _worstDamage01) _worstDamage01 = damage;
                    ApplyBlockTint(block, DamageTint(damage));
                }
            }
        }

        private static void ScanSingleBlock(MonoBehaviour blockRoot)
        {
            float damage01;
            float temperature;
            string displayName = _targetName;

            if (blockRoot is PlacedBlock placed)
            {
                damage01 = placed.Damage01;
                var heat = placed.GetComponent<VoxelEngine.Thermal.PlacedBlockHeat>();
                // A block that has never been heated sits at its world's ambient, the
                // same figure PlacedBlockHeat would report once it exists.
                temperature = heat != null
                    ? heat.TemperatureC
                    : ThermalRules.AmbientTemperatureC(blockRoot.transform.position);
                if (placed.Item != null) displayName = placed.Item.displayName.ToUpperInvariant();
            }
            else if (blockRoot is PlacedTieredBlock tiered && tiered.definition != null)
            {
                int maximum = Mathf.Max(1, tiered.definition.GetStats(tiered.tier).hp);
                damage01 = Mathf.Clamp01(1f - Mathf.Max(0, tiered.hp) / (float)maximum);
                var heat = tiered.GetComponent<VoxelEngine.Thermal.PlacedBlockHeat>();
                temperature = heat != null
                    ? heat.TemperatureC
                    : ThermalRules.AmbientTemperatureC(blockRoot.transform.position);
                displayName = tiered.definition.displayName.ToUpperInvariant();
            }
            else return;

            // A single block is its own worst block, and its status line is composed
            // here: the grid path up in ScanTick does not run for static blocks.
            if (_mode == GridInspectorMode.Heat)
            {
                // Static blocks burn like hull plate: the block system's own threshold.
                float tol = ThermalRules.BlockDamageThresholdC;
                float severity = temperature - tol;
                if (severity > _worstSeverity)
                {
                    _worstSeverity = severity;
                    _worstTemperatureC = temperature;
                }
                ApplyRootTint(blockRoot, HeatRamp(temperature, tol, severity));
                SetStatus(displayName + "  \u00B7  " + temperature.ToString("0") + "\u00B0C  "
                    + ThermalRules.BandLabel(ThermalRules.Band(temperature, tol)));
            }
            else
            {
                if (damage01 > _worstDamage01) _worstDamage01 = damage01;
                ApplyRootTint(blockRoot, DamageTint(damage01));
                SetStatus(displayName + "  \u00B7  WORST DAMAGE "
                    + (damage01 * 100f).ToString("0") + "%");
            }
        }

        private static void ApplyBlockTint(GridBlock block, Color? tint)
        {
            if (block == null) return;
            if (tint.HasValue) ApplyRootTint(block, tint.Value);
            else RemoveTintFromRoot(block);
        }

        private static List<Renderer> VisualsOf(Component root)
        {
            if (_visualCache.TryGetValue(root, out var cached))
            {
                // Rebuild when the block changed shape, when a cached renderer died, or
                // when the entry is older than a couple of seconds (renderers toggle).
                bool stale = cached.childCount != root.transform.childCount
                             || Time.unscaledTime - cached.builtAt > 2.5f;
                if (!stale)
                {
                    for (int i = cached.renderers.Count - 1; i >= 0; i--)
                        if (cached.renderers[i] == null) { stale = true; break; }
                }
                if (!stale) return cached.renderers;
            }

            cached ??= new BlockVisuals();
            cached.renderers.Clear();
            root.GetComponentsInChildren(true, cached.renderers);
            for (int i = cached.renderers.Count - 1; i >= 0; i--)
                if (!IsTintable(cached.renderers[i])) cached.renderers.RemoveAt(i);
            cached.childCount = root.transform.childCount;
            cached.builtAt = Time.unscaledTime;
            _visualCache[root] = cached;

            if (_visualCache.Count > 512)
            {
                // Age the whole cache out rather than bookkeeping per construct.
                _visualCache.Clear();
                _visualCache[root] = cached;
            }
            return cached.renderers;
        }

        private static void PruneCache()
        {
            if (_visualCache.Count == 0) return;
            _cacheScrub.Clear();
            _cacheScrub.AddRange(_visualCache.Keys);
            for (int i = 0; i < _cacheScrub.Count; i++)
                if (_cacheScrub[i] == null) _visualCache.Remove(_cacheScrub[i]);
        }

        private static void ApplyRootTint(Component root, Color? tint)
        {
            if (root == null) return;
            var renderers = VisualsOf(root);
            if (!tint.HasValue)
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    var r = renderers[i];
                    if (r != null && _tints.TryGetValue(r, out var entry) && entry.applied)
                        RestoreEntry(r, entry);
                }
                return;
            }

            Color32 c = tint.Value;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var entry = BeginTint(r);
                if (entry == null) continue;
                if (entry.applied && c.Equals(entry.last)) continue;
                // Re-fill from the renderer so concurrent MPB writers keep their props.
                r.GetPropertyBlock(entry.working);
                entry.working.SetColor(entry.baseColorId, tint.Value);
                r.SetPropertyBlock(entry.working);
                entry.last = c;
                entry.applied = true;
            }
        }

        private static void RemoveTintFromRoot(Component root)
        {
            if (root == null) return;
            if (!_visualCache.TryGetValue(root, out var cached)) return;
            for (int i = 0; i < cached.renderers.Count; i++)
            {
                var r = cached.renderers[i];
                if (r == null) continue;
                if (_tints.TryGetValue(r, out var entry) && entry.applied)
                    RestoreEntry(r, entry);
            }
        }

        private static bool IsTintable(Renderer r)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
            if (!(r is MeshRenderer)) return false;
            if (!r.enabled || !r.gameObject.activeInHierarchy) return false;
            // The damage shells stay as they are: this pass tints, it never replaces
            // BlockDamageVisual — the two read as one picture by design.
            if (r.name == "Generated_DamageOverlay") return false;
            var mat = r.sharedMaterial;
            if (mat == null) return false;
            if (mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) return false;
            return mat.HasProperty("_BaseColor") || mat.HasProperty("_Color");
        }

        private static TintEntry BeginTint(Renderer r)
        {
            if (_tints.TryGetValue(r, out var existing)) return existing;

            var mat = r.sharedMaterial;
            int colorId = mat != null && mat.HasProperty("_BaseColor")
                ? Shader.PropertyToID("_BaseColor")
                : Shader.PropertyToID("_Color");

            var entry = new TintEntry
            {
                saved = new MaterialPropertyBlock(),
                working = new MaterialPropertyBlock(),
                baseColorId = colorId,
            };
            r.GetPropertyBlock(entry.saved);   // the exact block to restore on exit
            _tints[r] = entry;
            return entry;
        }

        private static void RestoreEntry(Renderer r, TintEntry entry)
        {
            if (r == null || entry == null) return;
            if (entry.saved.isEmpty)
                r.SetPropertyBlock(null);
            else
                r.SetPropertyBlock(entry.saved);
            entry.applied = false;
            entry.last = default;
        }

        /// <summary>Put every tinted renderer back the way it was.</summary>
        private static void ClearTints()
        {
            if (_tints.Count == 0) return;
            _tintScrub.Clear();
            _tintScrub.AddRange(_tints.Keys);
            for (int i = 0; i < _tintScrub.Count; i++)
            {
                var r = _tintScrub[i];
                if (r == null) continue;
                if (_tints.TryGetValue(r, out var entry) && entry.applied)
                    RestoreEntry(r, entry);
            }
            _tints.Clear();
        }

        // ── Colour ramps ────────────────────────────────────────────────────────
        /// <summary>
        /// Integrity ramp: pristine blocks keep their own look — a scan should make the
        /// wounded plates jump out, not re-paint a healthy hull. The moment a block has
        /// taken any real hit it walks teal → amber → red by its own damage fraction,
        /// exactly as the design settled: the fraction of the block's maxHP is the only
        /// input, so shielded plate and glass read on the same ramp.
        /// </summary>
        private static Color? DamageTint(float damage01)
        {
            if (damage01 < 0.02f) return null;
            float t = Mathf.Clamp01((damage01 - 0.02f) / 0.98f);
            Color c = Color.Lerp(RampHealthy, RampAmber, Mathf.Clamp01(t * 1.6f));
            c = Color.Lerp(c, RampRed, Mathf.Clamp01((t - 0.45f) / 0.55f));
            return c;
        }

        /// <summary>
        /// HEAT ramp for a grid block, judged per block against its own family
        /// tolerance: glass fails long before machinery, so each block's red line is
        /// its own. Warm starts at the 120 °C floor the HUD band labels already use;
        /// hot crosses at 60 % of tolerance; past tolerance the ramp burns white.
        /// </summary>
        private static Color? HeatTint(GridBlock block, float temperatureC, out float severity, out float severityTemperature)
        {
            float tol = ThermalRules.ToleranceC(block);
            severityTemperature = temperatureC;
            severity = temperatureC - tol;
            return HeatRamp(temperatureC, tol, severity);
        }

        private static Color? HeatRamp(float temperatureC, float toleranceC, float severity)
        {
            const float warmFloorC = 120f;
            float tol = Mathf.Max(1f, toleranceC);
            if (temperatureC < warmFloorC) return null;

            float p1 = Mathf.Clamp01((temperatureC - warmFloorC) / Mathf.Max(1f, tol * 0.6f - warmFloorC));
            float p2 = Mathf.Clamp01((temperatureC - tol * 0.6f) / Mathf.Max(1f, tol * 0.4f));
            float p3 = severity > 0f ? Mathf.Clamp01(severity / ThermalRules.BlockDamageSpanC) : 0f;

            Color c = Color.Lerp(RampWarm, RampHot, p1 * p1);
            c = Color.Lerp(c, RampCritical, p2);
            if (p3 > 0f) c = Color.Lerp(c, RampWhiteHot, p3 * 0.85f);
            return c;
        }

        // ── Worst-block tag & COM markers ───────────────────────────────────────
        private static void TickWorstTag()
        {
            if (_worstTag == null) return;
            if (_worstBlock == null)
            {
                HideWorstTag();
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                HideWorstTag();
                return;
            }
            Vector3 sp = cam.WorldToScreenPoint(_worstBlock.transform.position);
            if (sp.z <= 0.05f)
            {
                HideWorstTag();
                return;
            }
            // Pixel space → panel space (the panel scales with a shrink factor).
            float panelScale = Mathf.Max(0.01f, Mathf.Min(Screen.width / 1920f, Screen.height / 1080f));
            _worstTag.style.left = sp.x / panelScale + 14;
            _worstTag.style.top = (Screen.height - sp.y) / panelScale - 34;
            _worstTag.style.display = DisplayStyle.Flex;

            string text = "\u25B2 " + _worstBlock.blockName.ToUpperInvariant() + "  "
                + _worstTemperatureC.ToString("0") + "\u00B0C  "
                + ThermalRules.BandLabel(ThermalRules.Band(_worstBlock, _worstTemperatureC));
            if (_worstLabel.text != text) _worstLabel.text = text;
        }

        private static void HideWorstTag()
        {
            if (_worstTag != null && _worstTag.style.display != DisplayStyle.None)
                _worstTag.style.display = DisplayStyle.None;
        }

        private static void TickCom(float now)
        {
            if (_target is not GridEntity grid || grid == null)
            {
                HideMarkers();
                SetStatus("POINT AT A SHIP \u2014 A BASE BLOCK HAS NO MASS CENTRE");
                return;
            }

            if (now >= _nextComData)
            {
                _nextComData = now + ComDataInterval;
                ComputeCom(grid);
            }

            EnsureMarkers();
            if (_comBall == null) return;

            // The ball rides the grid's solved centre of mass every frame.
            _comBall.transform.position = _comPosition;

            float diag = Mathf.Max(2f, GridRadiusEstimate(grid) * 2f);
            float ratio = Mathf.Clamp01(_comOffsetFromThrustLine / Mathf.Max(1.5f, diag * 0.45f));
            Color ballColor = _comHasDrive
                ? Color.Lerp(RampHealthy, RampRed, ratio * ratio)
                : new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b);

            Color32 ball32 = ballColor;
            if (!ball32.Equals(_lastBallColor))
            {
                SetMarkerColor(_comBallRenderer, ballColor);
                _lastBallColor = ball32;
            }

            if (_comLineRenderer != null)
            {
                _comLine.SetActive(_comHasDrive);
                if (_comHasDrive)
                {
                    Vector3 a = _thrustCentre - _thrustAxis * _thrustHalfLength;
                    Vector3 b = _thrustCentre + _thrustAxis * _thrustHalfLength;
                    _comLineRenderer.positionCount = 2;
                    _comLineRenderer.SetPosition(0, a);
                    _comLineRenderer.SetPosition(1, b);
                    float width = Mathf.Lerp(0.05f, 0.18f, _comRadius / 2.2f);
                    if (Mathf.Abs(width - _lastLineWidth) > 0.001f)
                    {
                        _comLineRenderer.startWidth = width;
                        _comLineRenderer.endWidth = width;
                        _lastLineWidth = width;
                    }
                    Color lineColor = new Color(0.72f, 0.84f, 0.96f, 1f);
                    Color32 line32 = lineColor;
                    if (!line32.Equals(_lastLineColor))
                    {
                        SetMarkerColor(_comLineRenderer, lineColor);
                        _lastLineColor = line32;
                    }
                }
            }

            string driveText = _comHasDrive
                ? "OFFSET " + _comOffsetFromThrustLine.ToString("0.00") + " M FROM THRUST LINE"
                : "NO DRIVE \u2014 MASS CENTRE ONLY";
            SetStatus(_targetName + "  \u00B7  " + (_comMassKg / 1000f).ToString("0.0") + " T  \u00B7  " + driveText);
        }

        private static void ComputeCom(GridEntity grid)
        {
            _comMassKg = grid.TotalMass;

            if (grid.Body != null)
                _comPosition = grid.Body.worldCenterOfMass;
            else
            {
                // Pre-physics fallback: plain average of block positions.
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (var block in grid.AllBlocks)
                {
                    if (block == null) continue;
                    sum += block.transform.position;
                    count++;
                }
                _comPosition = count > 0 ? sum / count : grid.transform.position;
            }

            _comRadius = Mathf.Clamp(0.22f + Mathf.Pow(Mathf.Max(0f, _comMassKg), 0.3333f) * 0.045f, 0.22f, 2.2f);

            // The thrust line: the weighted centre of thrust and the net burn
            // direction. A drive whose vectors cancel (manoeuvring quads) falls back
            // to the grid's own forward axis through that centre.
            float sumF = 0f;
            Vector3 sumP = Vector3.zero;
            Vector3 sumD = Vector3.zero;
            foreach (var block in grid.AllBlocks)
            {
                if (block is not GridThruster t) continue;
                float f = Mathf.Max(0f, t.maxThrustN);
                if (f <= 0f) continue;
                sumF += f;
                sumP += t.transform.position * f;
                sumD += t.PushDirection * f;
            }
            _comHasDrive = sumF > 1f;
            _thrustCentre = _comHasDrive ? sumP / sumF : grid.transform.position;
            Vector3 dir = _comHasDrive ? sumD.normalized : grid.transform.forward;
            if (dir.sqrMagnitude < 0.01f) dir = grid.transform.forward;
            _thrustAxis = dir;
            _thrustHalfLength = Mathf.Max(2f, GridRadiusEstimate(grid) + 3f);

            // The number that makes ship design teachable: how far the mass centre
            // sits from the line the drive actually pushes along.
            _comOffsetFromThrustLine = DistanceToLine(_comPosition, _thrustCentre, _thrustAxis);
        }

        private static float GridRadiusEstimate(GridEntity grid)
        {
            float best = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (block == null) continue;
                float d = (block.transform.position - grid.transform.position).magnitude;
                if (d > best) best = d;
            }
            return best;
        }

        private static float DistanceToLine(Vector3 point, Vector3 linePoint, Vector3 direction)
        {
            Vector3 d = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            return Vector3.Cross(point - linePoint, d).magnitude;
        }

        // ── COM markers (created only when CENTRE OF MASS first turns on) ───────
        private static void EnsureMarkers()
        {
            if (_markerMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Sprites/Default");
                _markerMaterial = new Material(sh) { name = "Mat_GridInspectorMarkers" };
                _markerColorId = _markerMaterial.HasProperty("_BaseColor")
                    ? Shader.PropertyToID("_BaseColor")
                    : Shader.PropertyToID("_Color");
            }

            if (_comBall == null)
            {
                _comBall = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _comBall.name = "Generated_InspectorComBall";
                var col = _comBall.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                var mr = _comBall.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _markerMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                _comBallRenderer = mr;
                _lastBallColor = default;
                _comBall.SetActive(false);
            }
            float diameter = Mathf.Max(0.01f, _comRadius * 2f);
            if (_comBall != null && _comBall.activeSelf)
            {
                Vector3 s = _comBall.transform.localScale;
                if (Mathf.Abs(s.x - diameter) > 0.001f)
                    _comBall.transform.localScale = Vector3.one * diameter;
            }
            else if (_comBall != null)
            {
                _comBall.transform.localScale = Vector3.one * diameter;
                _comBall.SetActive(true);
            }

            if (_comLine == null)
            {
                _comLine = new GameObject("Generated_InspectorThrustLine");
                _comLineRenderer = _comLine.AddComponent<LineRenderer>();
                _comLineRenderer.sharedMaterial = _markerMaterial;
                _comLineRenderer.useWorldSpace = true;
                _comLineRenderer.positionCount = 2;
                _comLineRenderer.startWidth = 0.08f;
                _comLineRenderer.endWidth = 0.08f;
                _comLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _comLineRenderer.receiveShadows = false;
                _comLineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                _comLineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                _comLineRenderer.allowOcclusionWhenDynamic = false;
                _lastLineColor = default;
                _lastLineWidth = -1f;
                _comLine.SetActive(false);
            }
            if (_comLine != null && !_comLine.activeSelf) _comLine.SetActive(true);
        }

        private static void SetMarkerColor(Renderer r, Color color)
        {
            if (r == null || _markerMaterial == null || _markerColorId < 0) return;
            _markerBlock.Clear();
            _markerBlock.SetColor(_markerColorId, color);
            r.SetPropertyBlock(_markerBlock);
        }

        private static void HideMarkers()
        {
            if (_comBall != null && _comBall.activeSelf) _comBall.SetActive(false);
            if (_comLine != null && _comLine.activeSelf) _comLine.SetActive(false);
        }

        /// <summary>Destroy the marker objects (overlay rule: nothing in the scene
        /// while the overlay is off). Destruction is deferred to end of frame by
        /// Object.Destroy, but the objects are hidden first, so nothing shows.</summary>
        private static void DestroyMarkers()
        {
            HideMarkers();
            if (_comBall != null)
            {
                Object.Destroy(_comBall);
                _comBall = null;
                _comBallRenderer = null;
            }
            if (_comLine != null)
            {
                Object.Destroy(_comLine);
                _comLine = null;
                _comLineRenderer = null;
            }
        }
    }
}
