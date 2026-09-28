// Assets/Scripts/VoxelEngine/Building/Tiered/HammerBuildWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — HAMMER BUILD WHEEL                 ║
// ║                                                                  ║
// ║  Hold the build key, flick, release. The wheel never asks for a  ║
// ║  second click and never asks the hand to land on a target.       ║
// ║                                                                  ║
// ║  The feel, the geometry and the palette all live in the shared   ║
// ║  RadialWheelController / RadialWheelView, which every radial     ║
// ║  selector in the game runs through. What is left here is the     ║
// ║  build-specific part and nothing else: which families exist,     ║
// ║  what they cost, and what selecting one means.                   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.FX;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Building.Tiered
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class HammerBuildWheel : MonoBehaviour
    {
        public static HammerBuildWheel Instance { get; private set; }
        public BuildFamily? ActiveFamily { get; private set; }
        public bool IsUpgradeMode { get; private set; }
        public bool IsOpen => _wheel.IsOpen;

        public Inventory inventory;
        public TieredBlockRegistry registry;

        private static readonly BuildFamily[] StructuralFamilies =
        {
            BuildFamily.Foundation, BuildFamily.Floor, BuildFamily.Wall,
            BuildFamily.HalfWall, BuildFamily.Pillar,
            BuildFamily.Stairs, BuildFamily.Railing,
            BuildFamily.Doorway, BuildFamily.Door,
            BuildFamily.Window, BuildFamily.WindowPane,
            BuildFamily.WallFrame, BuildFamily.GarageDoor,
            BuildFamily.FloorHatch, BuildFamily.HatchLid
        };

        // 13.15.0: menu two. The flat Roof moved here from the structural page
        // so every roofing piece lives on one wheel.
        private static readonly BuildFamily[] RoofsAndGatesFamilies =
        {
            BuildFamily.Roof, BuildFamily.SlantedRoof,
            BuildFamily.TriangularRoof, BuildFamily.SlantedTriangularRoof,
            BuildFamily.CornerRoof, BuildFamily.SlantedCornerRoofInverted,
            BuildFamily.PyramidRoof,
            BuildFamily.TriangularWall, BuildFamily.TriangularWallInverted,
            BuildFamily.GateFrame, BuildFamily.Gate,
            BuildFamily.BigGateFrame, BuildFamily.BigGate,
            BuildFamily.CompoundWall
        };

        private static readonly BuildFamily[] StationFamilies =
        {
            BuildFamily.StationHull, BuildFamily.StationFloor, BuildFamily.StationCorridor,
            BuildFamily.StationJunction, BuildFamily.StationWindow, BuildFamily.StationAirlock,
            BuildFamily.StationDock, BuildFamily.StationDome
        };

        /// <summary>
        /// Static so the wheel reopens on whichever menu the player used last,
        /// surviving wheel closes, respawns and scene reloads within the session.
        /// Starting from the first page on every open forced roof and station
        /// builders to scroll past the everyday pieces again and again.
        /// </summary>
        private static BuildFamilyGroup _group = BuildFamilyGroup.Structural;

        private BuildFamily[] Families => _group switch
        {
            BuildFamilyGroup.OrbitalStation => StationFamilies,
            BuildFamilyGroup.RoofsAndGates => RoofsAndGatesFamilies,
            _ => StructuralFamilies,
        };

        private static string GroupLabelOf(BuildFamilyGroup group) => group switch
        {
            BuildFamilyGroup.OrbitalStation => "ORBITAL STATION",
            BuildFamilyGroup.RoofsAndGates => "ROOFS & GATES",
            _ => "STRUCTURAL",
        };

        /// <summary>Menu cycle: structural, roofs and gates, then station when unlocked.</summary>
        private BuildFamilyGroup NextGroup() => _group switch
        {
            BuildFamilyGroup.Structural => BuildFamilyGroup.RoofsAndGates,
            BuildFamilyGroup.RoofsAndGates => StationGroupUnlocked
                ? BuildFamilyGroup.OrbitalStation : BuildFamilyGroup.Structural,
            _ => BuildFamilyGroup.Structural,
        };

        private readonly RadialWheelController _wheel = new();
        private readonly List<string> _detailScratch = new(4);
        private readonly List<bool> _detailOkScratch = new(4);
        private UIDocument _document;

        /// <summary>
        /// Checked live rather than cached, so finishing the research makes the group
        /// available without reopening the wheel.
        /// </summary>
        private static bool StationGroupUnlocked
        {
            get
            {
                string nodeId = BuildFamilyInfo.RequiredResearchId(BuildFamilyGroup.OrbitalStation);
                if (string.IsNullOrEmpty(nodeId)) return true;
                var rm = VoxelEngine.Research.ResearchManager.Instance;
                return rm != null && rm.IsUnlocked(nodeId);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _document = GetComponent<UIDocument>();
            if (_document.panelSettings == null)
                _document.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            if (_document.panelSettings != null)
            {
                // Same fit-to-screen scaling as the main HUD — the dial must never be
                // anchored off-screen on a smaller window.
                GameSettings.ApplyUiScaleAndFit(_document.panelSettings);
            }
            if (_document.rootVisualElement != null)
            {
                _document.rootVisualElement.style.flexGrow = 1;
                _document.rootVisualElement.pickingMode = PickingMode.Ignore;
            }

            _wheel.ResolveHost = () => _document != null ? _document.rootVisualElement : null;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = ConfirmFamily;
            _wheel.ConfirmCentre = EnterUpgradeMode;
            _wheel.SwapGroup = ToggleGroup;
            _wheel.IdleTitle = "UPGRADE MODE";
            _wheel.IdleDescription = "Left click to arm the hammer for tier upgrades";
            _wheel.IdleTitleColor = T.AccentGold;
            _wheel.IdleIcon = MachineShapeIcons.Upgrade;
        }

        private void Start() => ResolveDependencies();

        private void OnDisable() => _wheel.Close(false);

        private void OnDestroy()
        {
            _wheel.Dispose();
            if (Instance == this) Instance = null;
        }

        private void ResolveDependencies()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (registry == null && BuildSystemV2.Instance != null) registry = BuildSystemV2.Instance.registry;
            if (registry == null) registry = Resources.Load<TieredBlockRegistry>("TieredBlockRegistry");
        }

        private void Update()
        {
            ResolveDependencies();

            var stack = inventory != null ? inventory.ActiveStack : null;
            bool holdingHammer = stack != null && !stack.IsEmpty && stack.item is Hammer;
            if (!holdingHammer) ActiveFamily = null;

            // The remembered menu may be a locked group after a new save or a
            // research rollback; never show locked content.
            if (_group == BuildFamilyGroup.OrbitalStation && !StationGroupUnlocked)
                _group = BuildFamilyGroup.Structural;

            // Escape is NOT handled here. InGamePauseMenu already exits build mode when
            // the hammer has a family armed or the dial is up, and opens the pause menu
            // otherwise — owning the key in two places is what made Escape report
            // "build mode closed" while standing idle with a hammer.

            _wheel.GroupLabel = GroupLabelOf(_group);
            _wheel.SwapHint = "TAB / SCROLL  ·  " + GroupLabelOf(NextGroup());

            _wheel.Tick(InputAction.BuildWheel, holdingHammer);
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPTIONS
        // ══════════════════════════════════════════════════════════════════

        private void FillOptions(List<RadialOption> options)
        {
            var families = Families;
            for (int i = 0; i < families.Length; i++)
            {
                var family = families[i];
                bool available = IsAvailable(family);
                BuildDetails(family);

                options.Add(new RadialOption
                {
                    Title = BuildFamilyInfo.DisplayName(family),
                    Description = available
                        ? BuildFamilyInfo.Description(family)
                        : "No prefab registered — run the Voxel Engine Setup tool",
                    Icon = BuildPieceIcons.Get(family),
                    Details = _detailScratch.ToArray(),
                    DetailsOk = _detailOkScratch.ToArray(),
                    Available = available,
                    Affordable = CanAffordFamily(family),
                    Selected = ActiveFamily.HasValue && ActiveFamily.Value == family
                });
            }
        }

        /// <summary>
        /// One line per ingredient reading "50 x Wood (985,703)": the requirement,
        /// then what is actually carried.
        /// </summary>
        private void BuildDetails(BuildFamily family)
        {
            _detailScratch.Clear();
            _detailOkScratch.Clear();

            var def = registry != null ? registry.Get(family) : null;
            if (def?.placeCost?.items == null || def.placeCost.items.Length == 0)
            {
                _detailScratch.Add("NO COST");
                _detailOkScratch.Add(true);
                return;
            }

            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (_detailScratch.Count >= 4) break;
                int stock = inventory != null ? inventory.container.CountOf(ingredient.item) : 0;
                _detailScratch.Add($"{ingredient.count} x {ingredient.item.displayName}  ({stock:N0})");
                _detailOkScratch.Add(stock >= ingredient.count);
            }

            if (_detailScratch.Count == 0)
            {
                _detailScratch.Add("NO COST");
                _detailOkScratch.Add(true);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  ACTIONS
        // ══════════════════════════════════════════════════════════════════

        private bool ConfirmFamily(int index)
        {
            var families = Families;
            if (index < 0 || index >= families.Length) return true;
            var family = families[index];

            if (!IsAvailable(family))
            {
                BuildFeedbackHud.Show($"{BuildFamilyInfo.DisplayName(family)} unavailable",
                    "No prefab is registered for this piece yet.", null, T.AccentRed);
                AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.5f, 0.72f);
                return true;
            }

            ActiveFamily = family;
            IsUpgradeMode = false;
            bool affordable = CanAffordFamily(family);
            BuildFeedbackHud.Show($"Build: {BuildFamilyInfo.DisplayName(family)}",
                CostSummary(family), null, affordable ? T.AccentCyan : T.AccentRed);
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.7f, affordable ? 1.06f : 0.8f);
            return true;
        }

        private void EnterUpgradeMode()
        {
            ActiveFamily = null;
            IsUpgradeMode = true;
            BuildFeedbackHud.Show("Building Hammer", "Upgrade mode — strike a piece to raise its tier",
                null, T.AccentGold);
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.6f, 0.94f);
        }

        /// <summary>
        /// Flips between the structural and station sets. Refuses while the station
        /// research is undone, so the toggle can never reveal locked content.
        /// </summary>
        private void ToggleGroup()
        {
            _group = NextGroup();
            _wheel.GroupLabel = GroupLabelOf(_group);
            _wheel.Invalidate();
        }

        /// <summary>Closes the dial and disarms the hammer. Called by the pause menu.</summary>
        public void ExitBuildMode()
        {
            ActiveFamily = null;
            IsUpgradeMode = false;
            _wheel.Close(false);
            BuildFeedbackHud.Show("Building Hammer", "Build mode closed", null, T.TextMuted);
        }

        public void Open() => _wheel.Open();
        public void Close(bool selectHovered = false) => _wheel.Close(selectHovered);

        // ══════════════════════════════════════════════════════════════════
        //  QUERIES
        // ══════════════════════════════════════════════════════════════════

        private bool IsAvailable(BuildFamily family)
        {
            if (registry == null) return false;
            var def = registry.Get(family);
            return def != null && def.GetPrefab(BuildTier.Wood) != null;
        }

        private bool CanAffordFamily(BuildFamily family)
        {
            if (registry == null || inventory == null) return false;
            var def = registry.Get(family);
            if (def?.placeCost?.items == null) return true;
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (inventory.container.CountOf(ingredient.item) < ingredient.count) return false;
            }
            return true;
        }

        private string CostSummary(BuildFamily family)
        {
            var def = registry != null ? registry.Get(family) : null;
            if (def?.placeCost?.items == null) return "Free";
            var builder = new System.Text.StringBuilder();
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (builder.Length > 0) builder.Append(", ");
                builder.Append($"{ingredient.count} {ingredient.item.displayName}");
            }
            return builder.Length == 0 ? "Free" : builder.ToString();
        }
    }
}
