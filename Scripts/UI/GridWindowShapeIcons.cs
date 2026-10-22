// Assets/Scripts/VoxelEngine/UI/GridWindowShapeIcons.cs
// Clean architectural symbols for all 8 Grid Window shapes on the placement wheel.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.UI
{
    public static class GridWindowShapeIcons
    {
        public static Texture2D For(int shape) => IconAtlas.Get("grid-window-shape:" + shape, b =>
        {
            var selected = (GridWindowShape)GridWindowBlock.NormalizeShape(shape);
            switch (selected)
            {
                case GridWindowShape.Slope:
                    b.Closed(new Vector2(0.16f, 0.14f), new Vector2(0.84f, 0.14f), new Vector2(0.84f, 0.84f));
                    b.Line(new Vector2(0.34f, 0.24f), new Vector2(0.76f, 0.66f));
                    break;

                case GridWindowShape.InvertedSlope:
                    b.Closed(new Vector2(0.16f, 0.84f), new Vector2(0.84f, 0.84f), new Vector2(0.84f, 0.14f));
                    b.Line(new Vector2(0.34f, 0.74f), new Vector2(0.76f, 0.32f));
                    break;

                case GridWindowShape.Half:
                    b.Rect(0.16f, 0.14f, 0.84f, 0.50f);
                    b.Line(new Vector2(0.14f, 0.52f), new Vector2(0.86f, 0.52f));
                    b.Line(new Vector2(0.28f, 0.24f), new Vector2(0.56f, 0.42f));
                    break;

                case GridWindowShape.HalfSlope:
                    b.Closed(new Vector2(0.16f, 0.14f), new Vector2(0.84f, 0.14f), new Vector2(0.84f, 0.50f));
                    b.Line(new Vector2(0.36f, 0.20f), new Vector2(0.78f, 0.42f));
                    break;

                case GridWindowShape.LongSlope:
                    b.Closed(
                        new Vector2(0.16f, 0.14f),
                        new Vector2(0.84f, 0.14f),
                        new Vector2(0.84f, 0.84f),
                        new Vector2(0.16f, 0.50f));
                    b.Line(new Vector2(0.24f, 0.48f), new Vector2(0.78f, 0.75f));
                    break;

                case GridWindowShape.LongSlopeTwoCell:
                    b.Closed(new Vector2(0.06f, 0.18f), new Vector2(0.94f, 0.18f), new Vector2(0.94f, 0.80f));
                    b.Line(new Vector2(0.50f, 0.18f), new Vector2(0.50f, 0.49f));
                    b.Line(new Vector2(0.24f, 0.26f), new Vector2(0.86f, 0.69f));
                    break;

                case GridWindowShape.Corner:
                    // Connected 3-pane panoramic chamfer wrap icon (top-down + perspective read)
                    b.Polyline(
                        new Vector2(0.16f, 0.22f),
                        new Vector2(0.54f, 0.22f),
                        new Vector2(0.80f, 0.48f),
                        new Vector2(0.80f, 0.86f));
                    b.Polyline(
                        new Vector2(0.16f, 0.34f),
                        new Vector2(0.49f, 0.34f),
                        new Vector2(0.68f, 0.53f),
                        new Vector2(0.68f, 0.86f));
                    b.Line(new Vector2(0.16f, 0.22f), new Vector2(0.16f, 0.34f));
                    b.Line(new Vector2(0.54f, 0.22f), new Vector2(0.49f, 0.34f));
                    b.Line(new Vector2(0.80f, 0.48f), new Vector2(0.68f, 0.53f));
                    b.Line(new Vector2(0.80f, 0.86f), new Vector2(0.68f, 0.86f));
                    break;

                default:
                    b.Rect(0.16f, 0.14f, 0.84f, 0.86f);
                    b.Rect(0.22f, 0.20f, 0.78f, 0.80f);
                    b.Line(new Vector2(0.28f, 0.34f), new Vector2(0.62f, 0.68f));
                    b.Line(new Vector2(0.44f, 0.28f), new Vector2(0.70f, 0.54f));
                    break;
            }
        });
    }
}
