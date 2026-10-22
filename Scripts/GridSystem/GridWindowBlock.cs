// Assets/Scripts/VoxelEngine/GridSystem/GridWindowBlock.cs
//
// One structural Grid Window item. Shape and pane finish are independent selections;
// long windows reserve their full rotated footprint while remaining one canonical block.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public enum GridWindowShape
    {
        Square = 0,
        Slope = 1,
        LongSlope = 2,
        LongSlopeTwoCell = 3,
        Half = 4,
        HalfSlope = 5,
        InvertedSlope = 6,
        Corner = 7
    }

    public sealed class GridWindowBlock : GridBlock
    {
        public const int PaneClear = 0;
        public const int PaneReinforced = 1;
        public const int PaneTinted = 2;

        private const string PanePreferenceKey = "grid.window.pane-style";
        private const string ShapePreferenceKey = "grid.window.shape";
        private const string ShapeRootName = "Generated_WindowShapes";
        private const string PaneVariantsName = "Generated_WindowPaneVariants";

        private static int _selectedPaneStyle = -1;
        private static int _selectedWindowShape = -1;
        private static readonly int[] s_paneStyles = { PaneClear, PaneReinforced, PaneTinted };
        private static readonly GridWindowShape[] s_windowShapes =
        {
            GridWindowShape.Square,
            GridWindowShape.Slope,
            GridWindowShape.LongSlope,
            GridWindowShape.LongSlopeTwoCell,
            GridWindowShape.Half,
            GridWindowShape.HalfSlope,
            GridWindowShape.InvertedSlope,
            GridWindowShape.Corner
        };
        private static readonly string[] s_paneVariantNames =
        {
            "Generated_WindowPane_Clear",
            "Generated_WindowPane_Reinforced",
            "Generated_WindowPane_Tinted",
        };

        [Header("Window")]
        [Tooltip("Selected pane finish, stored with this placed block.")]
        [Range(PaneClear, PaneTinted)] public int paneStyle = PaneClear;

        [Tooltip("Selected window geometry. The two-cell long slope occupies the adjacent cell along its local right axis.")]
        public GridWindowShape windowShape = GridWindowShape.Square;

        public static int[] PaneStyles => s_paneStyles;
        public static GridWindowShape[] WindowShapes => s_windowShapes;

        /// <summary>The pane finish armed in the contextual placement wheel.</summary>
        public static int SelectedPaneStyle
        {
            get
            {
                if (_selectedPaneStyle < 0)
                    _selectedPaneStyle = Normalize(PlayerPrefs.GetInt(PanePreferenceKey, PaneClear));
                return _selectedPaneStyle;
            }
            set
            {
                _selectedPaneStyle = Normalize(value);
                PlayerPrefs.SetInt(PanePreferenceKey, _selectedPaneStyle);
            }
        }

        /// <summary>The shape armed for the next Grid Window placed.</summary>
        public static GridWindowShape SelectedWindowShape
        {
            get
            {
                if (_selectedWindowShape < 0)
                    _selectedWindowShape = NormalizeShape(PlayerPrefs.GetInt(ShapePreferenceKey, (int)GridWindowShape.Square));
                return (GridWindowShape)_selectedWindowShape;
            }
            set
            {
                _selectedWindowShape = NormalizeShape((int)value);
                PlayerPrefs.SetInt(ShapePreferenceKey, _selectedWindowShape);
            }
        }

        public static int Normalize(int style) => Mathf.Clamp(style, PaneClear, PaneTinted);
        public static int NormalizeShape(int shape) => Mathf.Clamp(shape, (int)GridWindowShape.Square, (int)GridWindowShape.Corner);

        public static string NameOf(int style) => Normalize(style) switch
        {
            PaneReinforced => "Reinforced Glass",
            PaneTinted => "Tinted Glass",
            _ => "Clear Glass",
        };

        public static string BlurbOf(int style) => Normalize(style) switch
        {
            PaneReinforced => "Clear, high-visibility glass with a fine structural brace grid.",
            PaneTinted => "Deep blue-tinted glass that still keeps the view readable.",
            _ => "Low-opacity clear glazing for the widest view.",
        };

        public static string ShapeNameOf(int shape) => (GridWindowShape)NormalizeShape(shape) switch
        {
            GridWindowShape.Slope => "Slope",
            GridWindowShape.LongSlope => "Long Slope",
            GridWindowShape.LongSlopeTwoCell => "Two-Cell Long Slope",
            GridWindowShape.Half => "Half Window",
            GridWindowShape.HalfSlope => "Half Slope",
            GridWindowShape.InvertedSlope => "Inverted Slope",
            GridWindowShape.Corner => "Corner Wrap",
            _ => "Square Window",
        };

        public static string ShapeBlurbOf(int shape) => (GridWindowShape)NormalizeShape(shape) switch
        {
            GridWindowShape.Slope => "Clean 45-degree sloped window pane for continuous angled roofs and windscreens.",
            GridWindowShape.LongSlope => "Upper 1:2 shallow sloped window pane that aligns smoothly behind Half Slope.",
            GridWindowShape.LongSlopeTwoCell => "Continuous 2-cell raked canopy window pane; reserves both rotated grid cells.",
            GridWindowShape.Half => "Flush half-height industrial window with reinforced top sill.",
            GridWindowShape.HalfSlope => "Lower 1:2 sloped window pane that pairs smoothly ahead of Long Slope.",
            GridWindowShape.InvertedSlope => "Clean 45-degree inverted overhang window pane for observation noses.",
            GridWindowShape.Corner => "Connected 3-pane panoramic chamfer wrap sealing a 90-degree corner with zero gaps.",
            _ => "Flush full-cell architectural window frame.",
        };

        /// <summary>
        /// Returns the one extra occupied cell for the two-cell window. The footprint axis
        /// follows the placed block's grid-local rotation, so the second cell rotates with it.
        /// </summary>
        public static bool TryGetAdditionalFootprintOffset(int shape, Quaternion localRotation, out Vector3Int offset)
        {
            offset = Vector3Int.zero;
            if ((GridWindowShape)NormalizeShape(shape) != GridWindowShape.LongSlopeTwoCell)
                return false;

            Vector3 axis = localRotation * Vector3.right;
            float ax = Mathf.Abs(axis.x);
            float ay = Mathf.Abs(axis.y);
            float az = Mathf.Abs(axis.z);
            if (ax >= ay && ax >= az)
                offset = new Vector3Int(axis.x >= 0f ? 1 : -1, 0, 0);
            else if (ay >= ax && ay >= az)
                offset = new Vector3Int(0, axis.y >= 0f ? 1 : -1, 0);
            else
                offset = new Vector3Int(0, 0, axis.z >= 0f ? 1 : -1);
            return true;
        }

        /// <summary>Local placement bounds for the ghost/obstruction probe.</summary>
        public static Bounds LocalBoundsForShape(int shape, float cellSize)
        {
            float cell = Mathf.Max(0.01f, cellSize);
            if ((GridWindowShape)NormalizeShape(shape) == GridWindowShape.LongSlopeTwoCell)
                return new Bounds(new Vector3(cell * 0.5f, 0f, 0f), new Vector3(cell * 2f, cell, cell));
            return new Bounds(Vector3.zero, Vector3.one * cell);
        }

        private void OnEnable() => ApplyAppearance();

        /// <summary>Changes only the pane group; item identity and geometry remain unchanged.</summary>
        public void SetPaneStyle(int style)
        {
            paneStyle = Normalize(style);
            ApplyAppearance();
        }

        /// <summary>Changes the generated shape and its structural collider.</summary>
        public void SetWindowShape(int shape)
        {
            windowShape = (GridWindowShape)NormalizeShape(shape);
            ApplyAppearance();
        }

        public void SetWindowShape(GridWindowShape shape) => SetWindowShape((int)shape);

        private void ApplyAppearance()
        {
            paneStyle = Normalize(paneStyle);
            windowShape = (GridWindowShape)NormalizeShape((int)windowShape);
            ApplyShapeGroup();
            ApplyPaneGroup();
            ApplyShapeCollider();
        }

        private void ApplyShapeGroup()
        {
            Transform shapeRoot = transform.Find(ShapeRootName);
            if (shapeRoot == null) return;

            string activeName = "Generated_WindowShape_" + windowShape;
            for (int i = 0; i < shapeRoot.childCount; i++)
            {
                Transform shape = shapeRoot.GetChild(i);
                bool active = shape.name == activeName;
                if (shape.gameObject.activeSelf != active)
                    shape.gameObject.SetActive(active);
            }
        }

        private void ApplyPaneGroup()
        {
            Transform shapeRoot = transform.Find(ShapeRootName);
            if (shapeRoot == null) return;

            Transform[] descendants = shapeRoot.GetComponentsInChildren<Transform>(true);
            string activeName = s_paneVariantNames[paneStyle];
            for (int i = 0; i < descendants.Length; i++)
            {
                Transform variants = descendants[i];
                if (variants == null || variants.name != PaneVariantsName) continue;
                for (int j = 0; j < variants.childCount; j++)
                {
                    Transform variant = variants.GetChild(j);
                    bool active = variant.name == activeName;
                    if (variant.gameObject.activeSelf != active)
                        variant.gameObject.SetActive(active);
                }
            }
        }

        private void ApplyShapeCollider()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            float cell = Grid != null
                ? Grid.gridSize.CellSize()
                : SourceItem is GridBlockItem gridItem
                    ? gridItem.gridSize.CellSize()
                    : GridSize.Large.CellSize();

            Bounds bounds = LocalBoundsForShape((int)windowShape, cell);
            box.center = bounds.center;
            box.size = bounds.size;
        }
    }
}
