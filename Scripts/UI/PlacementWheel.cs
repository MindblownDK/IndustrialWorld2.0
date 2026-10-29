// Assets/Scripts/VoxelEngine/UI/PlacementWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          PLACEMENT WHEEL — the placement-type dial (V)           ║
// ║                                                                  ║
// ║  18.9.1-dev: Block/shape variants live on B (BuildWheel), while  ║
// ║  placement type (SINGLE, SPAN, PILLAR, DECK, WALL + Ctrl+scroll  ║
// ║  for lattice step and run seating on static blocks) lives on V   ║
// ║  (PlacementWheel), so holding B on blocks with variants never    ║
// ║  opens placement type instead of the variants.                   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    /// <summary>The placement-pattern ring (SINGLE, SPAN, PILLAR, DECK, WALL) on the dedicated PlacementWheel key (V by default).</summary>
    public sealed class PlacementWheel : MonoBehaviour
    {
        private readonly RadialWheelController _wheel = new();
        private PlacementModeRing _ring;
        private bool _hasRingMode;
        private bool _ringIncludeStep;

        private Inventory _inventory;
        private VisualElement _host;

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.GroupLabel = "BUILD PATTERN";
            _wheel.IdleTitle = "KEEP PATTERN";
            _wheel.IdleDescription = "Release in the centre to leave the pattern as it is";
            _wheel.IdleTitleColor = UITheme.AccentCyan;
            // A tap here means "leave the pattern alone": it must close, never pin, or the
            // world stays blocked with no hologram and no obvious reason why.
            _wheel.AllowTapPin = false;
            EnsureRing(includeStep: true);
        }

        private void Start()
        {
            _inventory = GetComponentInParent<Inventory>();
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
        }

        private void OnDisable() => _wheel.Dispose();
        private void OnDestroy() => _wheel.Dispose();

        private void Update()
        {
            if (_inventory == null)
            {
                _inventory = FindAnyObjectByType<Inventory>();
                if (_inventory == null) { _wheel.HidePrompt(); return; }
            }

            bool holding = TryGetPlacementContext(out bool includeStep, out bool hasVariantWheelOnB);
            if (holding) EnsureRing(includeStep);

            _ring?.Tick();
            _wheel.Tick(InputAction.PlacementWheel, holding);

            if (holding && !hasVariantWheelOnB && !RadialWheelController.AnyOpen && !UIState.IsBlocking)
            {
                string prompt = includeStep
                    ? "[" + GameSettings.GetKey(InputAction.PlacementWheel) + "]  BUILD PATTERN  ·  "
                      + PlacementModeRing.PromptTail() + "   ·   " + PlacementModeRing.CtrlHint()
                    : "[" + GameSettings.GetKey(InputAction.PlacementWheel) + "]  BUILD PATTERN  ·  "
                      + PlacementOptions.PatternName(PlacementOptions.Pattern);
                _wheel.ShowPrompt(prompt);
            }
            else
            {
                _wheel.HidePrompt();
            }
        }

        private void EnsureRing(bool includeStep)
        {
            if (_hasRingMode && _ringIncludeStep == includeStep && _ring != null) return;
            _hasRingMode = true;
            _ringIncludeStep = includeStep;
            _ring = new PlacementModeRing(
                _wheel,
                "BUILD PATTERN",
                home: true,
                includeStep: includeStep,
                patternOnly: !includeStep);
            _wheel.Invalidate();
        }

        private VisualElement ResolveHost()
        {
            if (_host != null && _host.panel != null) return _host;
            var controller = GameUIController.Instance;
            var document = controller != null ? controller.GetComponent<UIDocument>() : FindAnyObjectByType<UIDocument>();
            _host = document != null ? document.rootVisualElement : null;
            return _host;
        }

        private void FillOptions(List<RadialOption> options)
        {
            if (options == null) return;
            if (!TryGetPlacementContext(out _, out _)) return;
            _ring?.TryFill(options);
        }

        private bool Confirm(int index) => _ring != null && _ring.TryConfirm(index);

        private bool TryGetPlacementContext(out bool includeStep, out bool hasVariantWheelOnB)
        {
            includeStep = true;
            hasVariantWheelOnB = false;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || stack.item == null) return false;

            if (stack.item is VoxelEngine.Building.Tiered.Hammer)
            {
                includeStep = false;
                hasVariantWheelOnB = true;
                return true;
            }

            if (stack.item is GridBlockItem gridBlock)
            {
                includeStep = false;
                hasVariantWheelOnB = gridBlock.SupportsShapeVariants
                    || (gridBlock.blockPrefab != null
                        && (gridBlock.blockPrefab.GetComponent<GridCockpit>() != null
                            || gridBlock.blockPrefab.GetComponent<GridWindowBlock>() != null));
                return true;
            }

            if (stack.item is BlockItem candidate && PlacementOptions.IsPatternEligible(candidate))
            {
                includeStep = true;
                hasVariantWheelOnB = HasStaticVariantWheelOnB(candidate);
                return true;
            }

            return false;
        }

        private static bool HasStaticVariantWheelOnB(BlockItem block)
        {
            var prefab = block != null ? block.placedPrefab : null;
            if (prefab == null) return false;

            var belt = prefab.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (belt != null && belt.autoShape && belt.shape == VoxelEngine.Simulation.ConveyorShape.Straight)
                return true;

            return false;
        }
    }
}
