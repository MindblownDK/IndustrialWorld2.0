// Assets/Scripts/VoxelEngine/UI/PlacementPatternIcons.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          PLACEMENT PATTERN ICONS — drawn, not imported           ║
// ║                                                                  ║
// ║  Five patterns and three steps, drawn as isometric solids through ║
// ║  the shared line-art rasteriser. A run of three cells reads as a  ║
// ║  run at wheel size; a single cell reads as a single cell. That is ║
// ║  the entire requirement, and it needs no art.                     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.UI
{
    /// <summary>Line-art masks for the placement ring, keyed and cached by IconAtlas.</summary>
    public static class PlacementPatternIcons
    {
        private const float Cell = 0.62f;   // drawn cell size in icon units
        private const float Gap = 0.10f;

        public static Texture2D For(PlacementPattern pattern) => IconAtlas.Get(
            "placement:" + pattern, b => DrawPattern(b, pattern), 128, 4.6f, 16f);

        public static Texture2D ForStep(PlacementStep step) => IconAtlas.Get(
            "placement:step:" + step, b => DrawStep(b, step), 128, 4.2f, 14f);

        private static void DrawPattern(LineArtBuilder b, PlacementPattern pattern)
        {
            switch (pattern)
            {
                case PlacementPattern.Span:
                    for (int i = 0; i < 3; i++)
                        b.IsoBox(i * (Cell + Gap), 0f, 0f, Cell, Cell, Cell);
                    break;

                case PlacementPattern.Pillar:
                    for (int i = 0; i < 3; i++)
                        b.IsoBox(0f, i * (Cell + Gap), 0f, Cell, Cell, Cell);
                    break;

                case PlacementPattern.Deck:
                    for (int x = 0; x < 3; x++)
                        for (int z = 0; z < 2; z++)
                            b.IsoBox(x * (Cell + Gap), 0f, z * (Cell + Gap), Cell, Cell, Cell);
                    break;

                // Cells rise on Y and advance on X, so a wall reads as a SHEET standing up
                // rather than as another floor patch (17.49.0-dev).
                case PlacementPattern.Wall:
                    for (int x = 0; x < 3; x++)
                        for (int y = 0; y < 2; y++)
                            b.IsoBox(x * (Cell + Gap), y * (Cell + Gap), 0f, Cell, Cell, Cell);
                    break;

                default:
                    b.IsoBox(0f, 0f, 0f, Cell, Cell, Cell);
                    // A single cell gets a centre pip so it never reads as a tiny crate.
                    b.Line(LineArtBuilder.Iso(0.3f, Cell + 0.16f, 0.3f),
                           LineArtBuilder.Iso(0.32f, Cell + 0.16f, 0.32f));
                    break;
            }
        }

        private static void DrawStep(LineArtBuilder b, PlacementStep step)
        {
            // A ruler with a marked cell: one cell wide, wider, or open.
            b.Rect(-0.30f, -0.10f, 1.30f, 0.34f);
            switch (step)
            {
                case PlacementStep.Half:
                    b.Line(new Vector2(0.10f, -0.10f), new Vector2(0.10f, 0.34f));
                    b.Line(new Vector2(0.70f, -0.10f), new Vector2(0.70f, 0.34f));
                    b.Line(new Vector2(1.30f, -0.10f), new Vector2(1.30f, 0.34f));
                    break;
                case PlacementStep.Free:
                    b.Line(new Vector2(0.20f, -0.34f), new Vector2(0.66f, 0.58f));
                    break;
                default:
                    b.Line(new Vector2(0.50f, -0.10f), new Vector2(0.50f, 0.34f));
                    b.Line(new Vector2(1.30f, -0.10f), new Vector2(1.30f, 0.34f));
                    break;
            }
        }
    }
}
