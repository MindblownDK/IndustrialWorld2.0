// Assets/Scripts/VoxelEngine/UI/GridBuildWheel.cs
//
// Contextual selector for grid-block placement variants. The window keeps one item;
// its radial wheel can switch between geometry and glass finish without creating item variants.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    /// <summary>Shows cockpit-shell or Grid Window options only while the matching item is held.</summary>
    public sealed class GridBuildWheel : MonoBehaviour
    {
        private enum VariantKind { None, Cockpit, Window }
        private enum WindowWheelMode { Shape, Pane }

        private readonly RadialWheelController _wheel = new();
        private Inventory _inventory;
        private VisualElement _host;
        private VariantKind _variantKind;
        private WindowWheelMode _windowMode = WindowWheelMode.Shape;

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = ConfirmVariant;
            _wheel.SwapGroup = null;
            _wheel.SwapHint = string.Empty;
            _wheel.IdleTitle = "KEEP CURRENT";
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

            _variantKind = TryGetHeldCockpit(out _)
                ? VariantKind.Cockpit
                : TryGetHeldWindow(out _) ? VariantKind.Window : VariantKind.None;
            bool context = _variantKind != VariantKind.None;

            if (_variantKind == VariantKind.Cockpit)
            {
                int style = CockpitShellBuilder.SelectedStyle;
                _wheel.GroupLabel = "COCKPIT";
                _wheel.IdleIcon = CockpitShellIcons.For(style);
                _wheel.IdleTitle = CockpitShellBuilder.NameOf(style).ToUpperInvariant();
                _wheel.IdleDescription = "Armed. Release here to keep this cockpit shell.";
                _wheel.SwapGroup = null;
                _wheel.SwapHint = string.Empty;
            }
            else if (_variantKind == VariantKind.Window)
            {
                _wheel.SwapGroup = SwapWindowWheelMode;
                _wheel.SwapHint = "TAB / SCROLL · SWITCH SHAPE / GLASS";
                RefreshWindowCaptions();
            }
            else
            {
                _wheel.SwapGroup = null;
                _wheel.SwapHint = string.Empty;
            }

            _wheel.Tick(InputAction.BuildWheel, context);

            if (context && !RadialWheelController.AnyOpen && !UIState.IsBlocking)
            {
                string label = _variantKind == VariantKind.Cockpit
                    ? "COCKPIT SHELL"
                    : _windowMode == WindowWheelMode.Shape ? "WINDOW SHAPE" : "WINDOW GLASS";
                string patternTail = $"   ·   [{GameSettings.GetKey(InputAction.PlacementWheel)}]  BUILD PATTERN  ·  "
                    + VoxelEngine.Building.PlacementOptions.PatternName(VoxelEngine.Building.PlacementOptions.Pattern);
                _wheel.ShowPrompt("[" + GameSettings.GetKey(InputAction.BuildWheel) + "]  " + label + patternTail);
            }
            else _wheel.HidePrompt();
        }

        private void RefreshWindowCaptions()
        {
            if (_windowMode == WindowWheelMode.Shape)
            {
                GridWindowShape shape = GridWindowBlock.SelectedWindowShape;
                _wheel.GroupLabel = "WINDOW SHAPE";
                _wheel.IdleIcon = GridWindowShapeIcons.For((int)shape);
                _wheel.IdleTitle = GridWindowBlock.ShapeNameOf((int)shape).ToUpperInvariant();
                _wheel.IdleDescription = "Armed. Tab or scroll to choose the glass finish.";
            }
            else
            {
                int pane = GridWindowBlock.SelectedPaneStyle;
                _wheel.GroupLabel = "WINDOW GLASS";
                _wheel.IdleIcon = GridWindowPaneIcons.For(pane);
                _wheel.IdleTitle = GridWindowBlock.NameOf(pane).ToUpperInvariant();
                _wheel.IdleDescription = "Armed. Tab or scroll to choose the window shape.";
            }
        }

        private void SwapWindowWheelMode()
        {
            _windowMode = _windowMode == WindowWheelMode.Shape
                ? WindowWheelMode.Pane
                : WindowWheelMode.Shape;
            RefreshWindowCaptions();
            _wheel.Invalidate();
        }

        private VisualElement ResolveHost()
        {
            if (_host != null && _host.panel != null) return _host;
            var controller = GameUIController.Instance;
            var document = controller != null
                ? controller.GetComponent<UIDocument>()
                : FindAnyObjectByType<UIDocument>();
            _host = document != null ? document.rootVisualElement : null;
            return _host;
        }

        private void FillOptions(List<RadialOption> options)
        {
            if (_variantKind == VariantKind.Window)
            {
                if (_windowMode == WindowWheelMode.Shape)
                {
                    GridWindowShape[] shapes = GridWindowBlock.WindowShapes;
                    for (int i = 0; i < shapes.Length; i++)
                    {
                        GridWindowShape shape = shapes[i];
                        options.Add(new RadialOption
                        {
                            Title = GridWindowBlock.ShapeNameOf((int)shape).ToUpperInvariant(),
                            Description = GridWindowBlock.ShapeBlurbOf((int)shape),
                            Icon = GridWindowShapeIcons.For((int)shape),
                            Available = true,
                            Affordable = true,
                            Selected = shape == GridWindowBlock.SelectedWindowShape,
                        });
                    }
                }
                else
                {
                    int[] styles = GridWindowBlock.PaneStyles;
                    for (int i = 0; i < styles.Length; i++)
                    {
                        int style = styles[i];
                        options.Add(new RadialOption
                        {
                            Title = GridWindowBlock.NameOf(style).ToUpperInvariant(),
                            Description = GridWindowBlock.BlurbOf(style),
                            Icon = GridWindowPaneIcons.For(style),
                            Available = true,
                            Affordable = true,
                            Selected = style == GridWindowBlock.SelectedPaneStyle,
                        });
                    }
                }
                return;
            }

            if (_variantKind != VariantKind.Cockpit) return;
            int[] shellStyles = CockpitShellBuilder.Styles;
            for (int i = 0; i < shellStyles.Length; i++)
            {
                int style = shellStyles[i];
                options.Add(new RadialOption
                {
                    Title = CockpitShellBuilder.NameOf(style).ToUpperInvariant(),
                    Description = CockpitShellBuilder.BlurbOf(style),
                    Icon = CockpitShellIcons.For(style),
                    Available = true,
                    Affordable = true,
                    Selected = style == CockpitShellBuilder.SelectedStyle,
                });
            }
        }

        private bool ConfirmVariant(int index)
        {
            if (_variantKind == VariantKind.Window)
            {
                if (_windowMode == WindowWheelMode.Shape)
                {
                    GridWindowShape[] shapes = GridWindowBlock.WindowShapes;
                    if (index < 0 || index >= shapes.Length) return true;
                    GridWindowShape shape = shapes[index];
                    GridWindowBlock.SelectedWindowShape = shape;
                    BuildFeedbackHud.Show(GridWindowBlock.ShapeNameOf((int)shape),
                        "Armed — the next Grid Window uses this shape and its rotated footprint",
                        null, UITheme.AccentCyan);
                }
                else
                {
                    int[] styles = GridWindowBlock.PaneStyles;
                    if (index < 0 || index >= styles.Length) return true;
                    int style = styles[index];
                    GridWindowBlock.SelectedPaneStyle = style;
                    BuildFeedbackHud.Show(GridWindowBlock.NameOf(style),
                        "Armed — the next Grid Window uses this glass finish",
                        null, UITheme.AccentCyan);
                }
                return true;
            }

            int[] shellStyles = CockpitShellBuilder.Styles;
            if (index < 0 || index >= shellStyles.Length) return true;
            int shell = shellStyles[index];
            CockpitShellBuilder.SelectedStyle = shell;
            BuildFeedbackHud.Show(CockpitShellBuilder.NameOf(shell),
                "Armed — the next cockpit you place wears this shell",
                null, UITheme.AccentCyan);
            return true;
        }

        private bool TryGetHeldCockpit(out GridBlockItem block)
        {
            block = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is GridBlockItem gridBlock)) return false;
            if (gridBlock.SupportsShapeVariants || gridBlock.blockPrefab == null) return false;
            if (gridBlock.blockPrefab.GetComponent<GridCockpit>() == null) return false;
            block = gridBlock;
            return true;
        }

        private bool TryGetHeldWindow(out GridBlockItem block)
        {
            block = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is GridBlockItem gridBlock)) return false;
            if (gridBlock.SupportsShapeVariants || gridBlock.blockPrefab == null) return false;
            if (gridBlock.blockPrefab.GetComponent<GridWindowBlock>() == null) return false;
            block = gridBlock;
            return true;
        }
    }
}
