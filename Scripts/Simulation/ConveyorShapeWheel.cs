// Assets/Scripts/VoxelEngine/Simulation/ConveyorShapeWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║            INDUSTRIAL WORLD — CONVEYOR SHAPE WHEEL                ║
// ║                                                                  ║
// ║  The three conveyor build modes on the shared radial dial: same  ║
// ║  hold-flick-release, same cursor lock, same drawn icons as the   ║
// ║  build wheel. The held conveyor supplies the speed tier, so one  ║
// ║  item and one recipe own every shape available at that tier.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Simulation
{
    public enum ConveyorBuildMode
    {
        Straight,
        Ramp,
        Vertical
    }

    public sealed class ConveyorShapeWheel : MonoBehaviour
    {
        /// <summary>
        /// The choice is per speed tier and lives here rather than on the tool, so it
        /// survives switching hotbar slots.
        /// </summary>
        private static readonly ConveyorBuildMode[] SelectedByTier =
        {
            ConveyorBuildMode.Straight,
            ConveyorBuildMode.Straight,
            ConveyorBuildMode.Straight
        };

        private static readonly ConveyorBuildMode[] Modes =
        {
            ConveyorBuildMode.Straight,
            ConveyorBuildMode.Ramp,
            ConveyorBuildMode.Vertical
        };

        private static readonly string[] Blurbs =
        {
            "Flat run. Feeds the next belt or machine straight ahead.",
            "Climbs one level over its run. Chain them for a long lift.",
            "Vertical lift. Takes items straight up through a floor."
        };

        private readonly RadialWheelController _wheel = new();
        private Inventory _inventory;
        private VisualElement _host;
        private ConveyorSpeed _activeTier;

        public static ConveyorBuildMode GetMode(ConveyorSpeed tier)
            => SelectedByTier[Mathf.Clamp((int)tier, 0, SelectedByTier.Length - 1)];

        private static void SetMode(ConveyorSpeed tier, ConveyorBuildMode mode)
            => SelectedByTier[Mathf.Clamp((int)tier, 0, SelectedByTier.Length - 1)] = mode;

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.GroupLabel = "CONVEYOR SHAPE";
            _wheel.IdleTitle = "KEEP CURRENT";
            _wheel.IdleDescription = "Release in the centre to leave the shape as it is";
            _wheel.IdleIcon = MachineShapeIcons.Cancel;
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
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            bool holding = TryGetHeldConveyor(out var belt);
            if (holding) _activeTier = belt.speed;

            _wheel.Tick(InputAction.BuildWheel, holding);

            if (holding && !_wheel.IsOpen && !UIState.IsBlocking)
                _wheel.ShowPrompt($"[{GameSettings.GetKey(InputAction.BuildWheel)}]  CONVEYOR SHAPE  ·  " +
                                  GetMode(_activeTier).ToString().ToUpperInvariant());
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
            var current = GetMode(_activeTier);
            for (int i = 0; i < Modes.Length; i++)
            {
                options.Add(new RadialOption
                {
                    Title = Modes[i].ToString().ToUpperInvariant(),
                    Description = Blurbs[i],
                    Icon = MachineShapeIcons.Conveyor(i),
                    Available = true,
                    Affordable = true,
                    Selected = Modes[i] == current
                });
            }
        }

        private bool Confirm(int index)
        {
            if (index < 0 || index >= Modes.Length) return true;
            SetMode(_activeTier, Modes[index]);
            BuildFeedbackHud.Show("Conveyor Shape", Modes[index].ToString(), null, UITheme.AccentCyan);
            return true;
        }

        private bool TryGetHeldConveyor(out ConveyorBelt belt)
        {
            belt = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is BlockItem block) || block.placedPrefab == null)
                return false;

            belt = block.placedPrefab.GetComponentInChildren<ConveyorBelt>(true);
            return belt != null && belt.autoShape && belt.shape == ConveyorShape.Straight;
        }
    }
}
