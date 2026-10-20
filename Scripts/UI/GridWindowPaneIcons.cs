// Assets/Scripts/VoxelEngine/UI/GridWindowPaneIcons.cs
// Simple pane symbols for the contextual Grid Window wheel.

using UnityEngine;

namespace VoxelEngine.UI
{
    public static class GridWindowPaneIcons
    {
        public static Texture2D For(int style) => IconAtlas.Get("grid-window-pane:" + style, b =>
        {
            b.Rect(0.18f, 0.12f, 0.82f, 0.88f);
            b.Line(new Vector2(0.26f, 0.76f), new Vector2(0.74f, 0.76f));

            if (style == VoxelEngine.GridSystem.GridWindowBlock.PaneReinforced)
            {
                b.Line(new Vector2(0.50f, 0.16f), new Vector2(0.50f, 0.72f));
                b.Line(new Vector2(0.22f, 0.46f), new Vector2(0.78f, 0.46f));
            }
            else if (style == VoxelEngine.GridSystem.GridWindowBlock.PaneTinted)
            {
                b.Line(new Vector2(0.27f, 0.35f), new Vector2(0.73f, 0.35f));
                b.Line(new Vector2(0.32f, 0.25f), new Vector2(0.68f, 0.25f));
            }
        });
    }
}
