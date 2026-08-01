// Assets/Scripts/VoxelEngine/Items/PaintToolItem.cs
//
// Hand tool that applies cosmetic finishes to placed static blocks and grid blocks.
// LMB paints the looked-at block with the selected finish.
// RMB cycles the selected finish. Scroll also cycles while the tool is held.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Paint Tool", fileName = "Tool_Paint")]
    public class PaintToolItem : ToolItem
    {
        [Header("Paint")]
        [Tooltip("Default finish selected when the tool is first equipped.")]
        public PaintFinishId defaultFinish = PaintFinishId.IndustrialGrey;

        /// <summary>Session-wide selected finish (shared across all paint tool stacks).</summary>
        public static PaintFinishId SelectedFinish { get; private set; } = PaintFinishId.IndustrialGrey;

        public PaintToolItem()
        {
            toolType = ToolType.Other;
            fireRate = 6f;
        }

        public static void EnsureSelected()
        {
            if (SelectedFinish == PaintFinishId.None)
                SelectedFinish = PaintFinishId.IndustrialGrey;
        }

        public static void Cycle(int delta)
        {
            EnsureSelected();
            // Skip None while cycling forward/back so paint always applies a finish;
            // hold Shift+RMB (handled by interaction) to clear.
            var next = PaintFinishCatalog.Next(SelectedFinish, delta == 0 ? 1 : delta);
            if (next == PaintFinishId.None)
                next = PaintFinishCatalog.Next(next, delta >= 0 ? 1 : -1);
            SelectedFinish = next;
        }

        public static void SetFinish(PaintFinishId id)
        {
            SelectedFinish = id;
        }

        public static string SelectedName
        {
            get
            {
                EnsureSelected();
                return PaintFinishCatalog.DisplayName(SelectedFinish);
            }
        }

        public static Color SelectedColor
        {
            get
            {
                EnsureSelected();
                return PaintFinishCatalog.Get(SelectedFinish).color;
            }
        }
    }
}
