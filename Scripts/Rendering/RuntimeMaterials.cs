// Assets/Scripts/VoxelEngine/Rendering/RuntimeMaterials.cs
//
// ONE place that knows how to make a URP Lit material translucent at runtime.
// Every hand-built preview surface - the static build ghost, the grid ghost,
// the LED stretch ghost - used to assemble its own transparent material by
// writing _Surface/_SrcBlend/_ZWrite floats and calling it a day. That works
// on older URP releases, but modern URP treats transparency as a KEYWORD
// state, not just a float state: without _SURFACE_TYPE_TRANSPARENT the
// material can end up in a half-switched state that newer render paths are
// free to reject outright - a preview that simply does not draw, with no
// error anywhere. This helper applies the full canonical set (keyword,
// override tags, blend modes, ZWrite, render queue) the same way URP's own
// material editor does, so a ghost looks identical no matter which URP
// version renders it.

using UnityEngine;

namespace VoxelEngine.Rendering
{
    public static class RuntimeMaterials
    {
        /// <summary>Creates a translucent, alpha-blended URP Lit material.
        /// Falls back to the built-in Standard shader only when URP is absent,
        /// so the helper also works in minimal test scenes.</summary>
        public static Material MakeTranslucentLit(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader);
            ApplyTranslucent(m, color);
            return m;
        }

        /// <summary>Switches an existing material to the canonical URP
        /// transparent state and sets its colour. Safe on materials whose
        /// shader is missing the properties - every write is guarded.</summary>
        public static void ApplyTranslucent(Material m, Color color)
        {
            if (m == null) return;

            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);

            // The keyword is the piece the old hand-rolled ghosts never set.
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // transparent
            if (m.HasProperty("_Blend"))   m.SetFloat("_Blend", 0f);     // alpha blend
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetOverrideTag("Queue", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // A preview must never leave depth or shadows behind it.
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
        }
    }
}
