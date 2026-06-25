// Assets/Scripts/VoxelEngine/GridSystem/GridBlockVisuals.cs
//
// Improves visual appearance of grid blocks.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class GridBlockVisuals
    {
        public static void ApplyVisuals(GridBlock block, Color color, float metallic = 0.6f, float smoothness = 0.4f)
        {
            var renderer = block.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);

            renderer.material = mat;
        }

        public static void ApplyDefaultVisuals(GridBlock block)
        {
            Color color = block switch
            {
                GridArmorBlock => new Color(0.4f, 0.4f, 0.45f),
                GridGlassBlock => new Color(0.6f, 0.8f, 1f, 0.4f),
                GridCockpit => new Color(0.2f, 0.3f, 0.5f),
                GridThruster => new Color(0.3f, 0.3f, 0.35f),
                _ => new Color(0.5f, 0.5f, 0.55f)
            };

            ApplyVisuals(block, color);
        }
    }
}