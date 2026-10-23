// Assets/Scripts/VoxelEngine/UI/PlacementWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          PLACEMENT WHEEL — the build wheel for blocks            ║
// ║                                                                  ║
// ║  Grid blocks, pipes, conveyors and the building hammer each own  ║
// ║  a wheel that answers "which variant". A crate, a foundation, a  ║
// ║  furnace and a chest have no variants - so before this wheel     ║
// ║  existed, holding the build key did NOTHING for them.            ║
// ║                                                                  ║
// ║  Now it opens the placement wheel: pattern first (SINGLE, SPAN,  ║
// ║  PILLAR, DECK), and Ctrl + scroll turns the same dial to the     ║
// ║  lattice step. It only ever appears for items that can actually  ║
// ║  use a run, and it yields the key to any wheel with a stronger   ║
// ║  claim (a conveyor's shape wheel, a pipe's variant wheel).       ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    /// <summary>The placement ring for plain static blocks, on the shared build key.</summary>
    public sealed class PlacementWheel : MonoBehaviour
    {
        private readonly RadialWheelController _wheel = new();
        private PlacementModeRing _ring;

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
            _ring = new PlacementModeRing(_wheel, "BUILD PATTERN", home: true);
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

            bool holding = TryGetHeldPlainBlock(out _);

            _ring.Tick();
            _wheel.Tick(InputAction.BuildWheel, holding);

            if (holding && !_wheel.IsOpen && !UIState.IsBlocking)
                _wheel.ShowPrompt(
                    "[" + GameSettings.GetKey(InputAction.BuildWheel) + "]  BUILD PATTERN  ·  " +
                    PlacementModeRing.PromptTail() + "   ·   " + PlacementModeRing.CtrlHint());
            else
                _wheel.HidePrompt();
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
            if (!TryGetHeldPlainBlock(out _)) return;
            _ring.TryFill(options);
        }

        private bool Confirm(int index) => _ring.TryConfirm(index);

        /// <summary>
        /// The item this wheel answers for: a placeable block that can lay runs AND that no
        /// other wheel already claims. A conveyor keeps its shape wheel; a pipe keeps its
        /// variant wheel; everything else comes here.
        /// </summary>
        private bool TryGetHeldPlainBlock(out BlockItem block)
        {
            block = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty) return false;
            if (stack.item is not BlockItem candidate) return false;
            if (!PlacementOptions.IsPatternEligible(candidate)) return false;
            if (ClaimedByAnotherWheel(candidate)) return false;
            block = candidate;
            return true;
        }

        private static bool ClaimedByAnotherWheel(BlockItem block)
        {
            var prefab = block != null ? block.placedPrefab : null;
            if (prefab == null) return true;

            // The conveyor wheel owns a belt that can auto-shape.
            var belt = prefab.GetComponentInChildren<VoxelEngine.Simulation.ConveyorBelt>(true);
            if (belt != null && belt.autoShape && belt.shape == VoxelEngine.Simulation.ConveyorShape.Straight)
                return true;

            // Energy and data pipes have their own wheels and their own snap contracts.
            if (prefab.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true) != null) return true;
            if (prefab.GetComponentInChildren<VoxelEngine.Networks.DataCable>(true) != null) return true;
            return false;
        }
    }
}
