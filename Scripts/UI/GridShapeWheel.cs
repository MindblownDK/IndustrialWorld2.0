// Assets/Scripts/VoxelEngine/UI/GridShapeWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — GRID SHAPE WHEEL                   ║
// ║                                                                  ║
// ║  Armour shape variants for grid blocks, on the shared radial     ║
// ║  dial. Each variant is a drawn isometric solid rather than a     ║
// ║  Unicode glyph, so a slope actually looks like a slope and a     ║
// ║  corner cannot be mistaken for a half block.                     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    public enum GridShapeVariant
    {
        Cube,
        Slope,
        HalfBlock,
        HalfSlope,
        Corner,
        InvertedSlope
    }

    public sealed class GridShapeWheel : MonoBehaviour
    {
        private static readonly GridShapeVariant[] Variants =
        {
            GridShapeVariant.Cube,
            GridShapeVariant.Slope,
            GridShapeVariant.HalfBlock,
            GridShapeVariant.HalfSlope,
            GridShapeVariant.Corner,
            GridShapeVariant.InvertedSlope
        };

        private static readonly string[] Titles =
        {
            "FULL BLOCK", "SLOPE", "HALF BLOCK", "HALF SLOPE", "CORNER", "INVERTED SLOPE"
        };

        private static readonly string[] Blurbs =
        {
            "The full cell. Maximum armour and mass.",
            "Rises a full cell over its run. Sheds fire and drag.",
            "Half height. Decking, sills and low walls.",
            "Half-height ramp. Pairs with the half block.",
            "Tetrahedral corner. Closes the gap where two slopes meet.",
            "A full block with the underside cut away — the ceiling ramp."
        };

        private static GridShapeVariant _current = GridShapeVariant.Cube;
        public static GridShapeVariant CurrentShape => _current;

        private readonly RadialWheelController _wheel = new();
        private Inventory _inventory;
        private VisualElement _host;

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.GroupLabel = "ARMOUR SHAPE";
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
            bool holding = TryGetHeldGridArmor(out _);

            _wheel.Tick(InputAction.BuildWheel, holding);

            if (holding && !_wheel.IsOpen && !UIState.IsBlocking)
                _wheel.ShowPrompt($"[{GameSettings.GetKey(InputAction.BuildWheel)}]  ARMOUR SHAPE  ·  " +
                                  Titles[Mathf.Clamp(System.Array.IndexOf(Variants, _current), 0, Titles.Length - 1)]);
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
            for (int i = 0; i < Variants.Length; i++)
            {
                options.Add(new RadialOption
                {
                    Title = Titles[i],
                    Description = Blurbs[i],
                    Icon = MachineShapeIcons.GridShape(i),
                    Available = true,
                    Affordable = true,
                    Selected = Variants[i] == _current
                });
            }
        }

        private bool Confirm(int index)
        {
            if (index < 0 || index >= Variants.Length) return true;
            _current = Variants[index];
            BuildFeedbackHud.Show("Grid Shape", Titles[index], null, UITheme.AccentCyan);
            return true;
        }

        private bool TryGetHeldGridArmor(out GridBlockItem item)
        {
            item = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is GridBlockItem gbi)) return false;
            if (!gbi.SupportsShapeVariants) return false;
            item = gbi;
            return true;
        }
    }
}
