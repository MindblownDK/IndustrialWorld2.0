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

        /// <summary>Bumped every time the font engine rebuilds an atlas. A text mesh's
        /// baked quads describe the atlas that existed when it was built, and a mesh built
        /// before a rebuild samples the wrong slice for every glyph - the "alien script"
        /// that appears on a caption that was set a moment too early. Comparing the
        /// MATERIAL's texture cannot detect this: the rebuild hook below has already
        /// re-pointed every material at the new atlas, so the texture matches while the
        /// mesh is still stale. Only a version, taken at bake time, can.</summary>
        private static int _atlasVersion;

        /// <summary>The version a given text mesh was last baked against.</summary>
        private static readonly Dictionary<TextMesh, int> _bakedAtVersion = new();

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

        /// <summary>
        /// Re-bake a text mesh's quads against the atlas its font has now.
        ///
        /// Two things invalidate a mesh: the font's material texture changing, and the
        /// engine rebuilding the atlas (which also changes every UV). The first is visible
        /// on the material; the second is only knowable from the version counter, because
        /// the rebuild hook has already fixed the material's texture by the time anything
        /// looks at it.
        /// </summary>
        public static void SyncAtlas(TextMesh tm)
        {
            if (tm == null) return;
            var font = tm.font;
            if (font == null || font.material == null) return;
            var renderer = tm.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null) return;

            Texture atlas = font.material.mainTexture;
            bool textureMoved = atlas != null && renderer.sharedMaterial.mainTexture != atlas;
            if (textureMoved) renderer.sharedMaterial.mainTexture = atlas;

            bool stale = textureMoved
                || !_bakedAtVersion.TryGetValue(tm, out int baked)
                || baked != _atlasVersion;
            if (!stale) return;

            // Text meshes are created and destroyed constantly in a big base (every ghost,
            // every rebuilt page); the map is trimmed rather than allowed to grow for ever.
            if (_bakedAtVersion.Count > 512) _bakedAtVersion.Clear();

            _bakedAtVersion[tm] = _atlasVersion;
            // Clearing first is deliberate: assigning the same string back is not
            // guaranteed to rebuild the quads, and a rebuild that does not happen is
            // exactly the state this method exists to leave behind.
            string text = tm.text;
            tm.text = string.Empty;
            tm.text = text;
        }

        /// <summary>Depth-test every TextMesh in a hierarchy.
        ///
        /// Prefabs authored by the editor tools cannot carry the swapped-in material:
        /// it is created at runtime, so a prefab that referenced it either lost the
        /// reference or kept the stock font shader - which draws straight through
        /// terrain and through the machine the text is bolted to. Anything whose text
        /// is authored in the editor asks for this on wake instead, and every label it
        /// owns becomes a real, occluded, readable sign.</summary>
        public static void ApplyToHierarchy(GameObject root)
        {
            if (root == null) return;
            var texts = root.GetComponentsInChildren<TextMesh>(true);
            for (int i = 0; i < texts.Length; i++) Apply(texts[i]);
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            // A dynamic font that adds glyphs REPLACES its atlas texture; every
            // swapped material must follow or its text turns to squares.
            Font.textureRebuilt += _ =>
            {
                _atlasVersion++;
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
