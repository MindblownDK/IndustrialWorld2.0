// Assets/Scripts/VoxelEngine/UI/GridWindowShapeIcons.cs
// Simple glass-frame symbols for the Grid Window shape selector.

using UnityEngine;

namespace VoxelEngine.UI
{
    public static class GridWindowShapeIcons
    {
        public static Texture2D For(int shape) => IconAtlas.Get("grid-window-shape:" + shape, b =>
        {
            var selected = (VoxelEngine.GridSystem.GridWindowShape)VoxelEngine.GridSystem.GridWindowBlock.NormalizeShape(shape);
            if (selected == VoxelEngine.GridSystem.GridWindowShape.LongSlopeTwoCell)
                b.Rect(0.06f, 0.12f, 0.94f, 0.88f);
            else
                b.Rect(0.18f, 0.12f, 0.82f, 0.88f);
            switch (selected)
            {
                case VoxelEngine.GridSystem.GridWindowShape.Slope:
                    b.Line(new Vector2(0.22f, 0.18f), new Vector2(0.78f, 0.80f));
                    break;
                case VoxelEngine.GridSystem.GridWindowShape.LongSlope:
                    b.Line(new Vector2(0.22f, 0.32f), new Vector2(0.78f, 0.68f));
                    break;
                case VoxelEngine.GridSystem.GridWindowShape.LongSlopeTwoCell:
                    b.Line(new Vector2(0.50f, 0.14f), new Vector2(0.50f, 0.86f));
                    b.Line(new Vector2(0.22f, 0.34f), new Vector2(0.78f, 0.66f));
                    break;
                case VoxelEngine.GridSystem.GridWindowShape.Half:
                    b.Line(new Vector2(0.20f, 0.48f), new Vector2(0.80f, 0.48f));
                    break;
                case VoxelEngine.GridSystem.GridWindowShape.HalfSlope:
                    b.Line(new Vector2(0.22f, 0.42f), new Vector2(0.78f, 0.72f));
                    break;
                case VoxelEngine.GridSystem.GridWindowShape.InvertedSlope:
                    b.Line(new Vector2(0.22f, 0.78f), new Vector2(0.78f, 0.18f));
                    break;
                case VoxelEngine.GridSystem.GridWindowShape.Corner:
                    b.Line(new Vector2(0.18f, 0.12f), new Vector2(0.50f, 0.12f));
                    b.Line(new Vector2(0.50f, 0.12f), new Vector2(0.50f, 0.88f));
                    b.Line(new Vector2(0.50f, 0.88f), new Vector2(0.82f, 0.88f));
                    break;
                default:
                    b.Line(new Vector2(0.50f, 0.18f), new Vector2(0.50f, 0.82f));
                    b.Line(new Vector2(0.22f, 0.50f), new Vector2(0.78f, 0.50f));
                    break;
            }
        });
    }
}
