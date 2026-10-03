// Assets/Scripts/VoxelEngine/Rendering/WorldTextMaterial.cs
//
// 14.37.2 - one fix for every 3D TextMesh in the game: banner lines, grid
// screen readouts, rail displays. The stock font shader draws with ZTest
// Always (it is a screen-GUI shader), so world text shone through terrain
// and blocks. Apply() swaps a TextMesh onto the depth-tested WorldText
// shader while keeping the font's own glyph atlas.
//
// Dynamic fonts rebuild their atlas texture when new characters appear;
// the swapped materials are cached per source font material and re-pointed
// at the fresh atlas on every Font.textureRebuilt, so glyphs never vanish
// after a rebuild.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Rendering
{
    public static class WorldTextMaterial
    {
        // Depth-tested material per source font material (the atlas lives on
        // the source; our copy mirrors it). TextMesh bakes its color into the
        // mesh vertices, so one shared material serves any number of texts.
        private static readonly Dictionary<Material, Material> _byFontMaterial = new();
        private static Shader _shader;
        private static bool _hooked;

        /// <summary>Swap this TextMesh onto the depth-tested world-text
        /// shader. Safe to call for any TextMesh; if the shader is missing
        /// the text simply keeps the stock (x-ray) font material.</summary>
        public static void Apply(TextMesh tm)
        {
            if (tm == null) return;
            var renderer = tm.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var source = renderer.sharedMaterial;
            if (source == null && tm.font != null) source = tm.font.material;
            if (source == null || source.mainTexture == null) return;
            if (_byFontMaterial.ContainsValue(source)) return;   // already one of ours

            if (_shader == null) _shader = Shader.Find("VoxelEngine/WorldText");
            if (_shader == null) return;   // shader not in project yet - keep stock

            if (!_byFontMaterial.TryGetValue(source, out var depthTested) || depthTested == null)
            {
                depthTested = new Material(_shader)
                {
                    name = "WorldText_DepthTested",
                    mainTexture = source.mainTexture,
                };
                _byFontMaterial[source] = depthTested;
                Hook();
            }
            else if (depthTested.mainTexture != source.mainTexture)
            {
                depthTested.mainTexture = source.mainTexture;   // atlas moved since caching
            }

            renderer.sharedMaterial = depthTested;
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            // A dynamic font that adds glyphs REPLACES its atlas texture; every
            // swapped material must follow or its text turns to squares.
            Font.textureRebuilt += _ =>
            {
                foreach (var pair in _byFontMaterial)
                {
                    if (pair.Key == null || pair.Value == null) continue;
                    if (pair.Value.mainTexture != pair.Key.mainTexture)
                        pair.Value.mainTexture = pair.Key.mainTexture;
                }
            };
        }
    }
}
