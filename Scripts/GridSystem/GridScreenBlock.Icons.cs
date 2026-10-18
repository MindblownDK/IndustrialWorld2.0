// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.Icons.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        ITEM PICTURES ON A TEXT PANEL (17.36.0-dev)               ║
// ║                                                                  ║
// ║  A page is characters. A storage monitor is pictures. A screen   ║
// ║  that lists "Cobalt 27.00 t" is a readout; a screen that shows   ║
// ║  the cobalt icon above the number is a container you recognise   ║
// ║  from across the room, which is what the reference did and what  ║
// ║  a player actually wants when they walk past a wall.             ║
// ║                                                                  ║
// ║  So the canvas reserves cells and this half fills them: one quad  ║
// ║  per requested icon, textured from the item's own sprite, laid   ║
// ║  out in the same character grid the text was fitted to. Because  ║
// ║  the grid comes from the fitted mesh, an icon cannot drift away  ║
// ║  from its number when the panel changes size - both are placed   ║
// ║  from the same measurement.                                      ║
// ║                                                                  ║
// ║  Nothing here runs per frame unless the page is one of the two   ║
// ║  that use it, and even then only when the icon list changes.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem.UI;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public partial class GridScreenBlock
    {
        /// <summary>Quads currently rented for item pictures, in request order.</summary>
        private readonly List<Renderer> _iconQuads = new(32);
        private readonly List<ItemDefinition> _iconOwners = new(32);
        private readonly List<Material> _iconMaterials = new(32);
        private Transform _iconRoot;
        private IReadOnlyList<ScreenIconRequest> _pageIcons;
        private int _iconColumns;
        private int _iconRows;

        /// <summary>Called by a page builder: "these cells hold these items".</summary>
        private void SetPageIcons(IReadOnlyList<ScreenIconRequest> icons, int columns, int rows)
        {
            _pageIcons = icons != null && icons.Count > 0 ? icons : null;
            _iconColumns = Mathf.Max(1, columns);
            _iconRows = Mathf.Max(1, rows);
            _iconsDirty = true;
        }

        private bool _iconsDirty;

        /// <summary>
        /// Lay the requested pictures onto the glass. Positions come from the body text's
        /// measured rectangle divided into the canvas grid, so a picture is always exactly
        /// where its count was printed.
        /// </summary>
        private void RefreshPageIcons()
        {
            if (!_iconsDirty) return;
            _iconsDirty = false;

            var requests = _pageIcons;
            bool wanted = requests != null && requests.Count > 0 && Enabled && IsPowered
                          && dataMode != ScreenDataMode.Camera;

            if (!wanted)
            {
                for (int i = 0; i < _iconQuads.Count; i++)
                    if (_iconQuads[i] != null) _iconQuads[i].enabled = false;
                return;
            }

            // The body's rendered rectangle: the same measurement the fit pass used.
            if (_screenText == null) return;
            var filter = _screenText.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            if (mesh.bounds.size.x <= 0.0001f || mesh.bounds.size.y <= 0.0001f) return;

            Vector3 localScale = _screenText.transform.localScale;
            float bodyHeight = mesh.bounds.size.y * localScale.y;
            if (bodyHeight <= 0.001f) return;

            // 17.36.0-dev — measured from the LINE, not from the whole block.
            //
            // The body is left-anchored, so its transform is the left edge of the text and
            // its mesh height covers the rows that were actually printed. One row is that
            // height divided by the printed rows, and one column is a row times the glyph
            // aspect the page layout was computed with. Both numbers come from the same
            // measurement the fit pass used, so a picture lands on its own column even when
            // the page is shorter than the panel or the font is stretched.
            int rows = Mathf.Max(1, _iconRows);
            float lineHeight = bodyHeight / rows;
            // The font style may stretch the glyphs horizontally; a column is as wide as the
            // stretched advance, so the pictures follow the same stretch the text got.
            float charWidth = Mathf.Max(0.0001f, lineHeight * 0.52f * StyleStretch(fontStyle));

            Vector3 origin = _screenText.transform.localPosition;

            EnsureIconRoot();
            EnsureIconPool(requests.Count);

            for (int i = 0; i < _iconQuads.Count; i++)
            {
                var quad = _iconQuads[i];
                if (quad == null) continue;

                if (i >= requests.Count)
                {
                    quad.enabled = false;
                    continue;
                }

                var request = requests[i];
                if (request.Item == null) { quad.enabled = false; continue; }

                BindIconMaterial(i, request.Item);
                if (_iconMaterials[i] != null) quad.sharedMaterial = _iconMaterials[i];

                float width = request.Width * charWidth * 0.84f;
                float height = request.Height * lineHeight * 0.74f;

                // Cell centre, in the body's own frame: columns run left to right from the
                // text's left edge, rows run top to bottom from the centre the anchor puts
                // them at. Both come from measurements, so neither drifts.
                float cx = origin.x + (request.Column + request.Width * 0.5f) * charWidth;
                float cy = origin.y + (rows * 0.5f - (request.Row + request.Height * 0.5f)) * lineHeight;

                quad.transform.localPosition = new Vector3(cx, cy, 0.004f);
                quad.transform.localScale = new Vector3(width, height, 1f);
                quad.enabled = true;
            }
        }

        private void EnsureIconRoot()
        {
            if (_iconRoot != null) return;
            Transform displayRoot = transform.Find("DisplayRoot");
            if (displayRoot == null) return;

            Transform existing = displayRoot.Find("PageIcons");
            if (existing != null) { _iconRoot = existing; return; }

            var root = new GameObject("PageIcons");
            root.transform.SetParent(displayRoot, false);
            _iconRoot = root.transform;
        }

        private void EnsureIconPool(int count)
        {
            while (_iconQuads.Count < count)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "PageIcon";
                go.transform.SetParent(_iconRoot, false);
                var collider = go.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.enabled = true;
                }
                go.SetActive(true);
                _iconQuads.Add(renderer);
                _iconOwners.Add(null);
                _iconMaterials.Add(null);
            }
        }

        /// <summary>
        /// One material per item, built once and kept: a sprite atlas is a big texture and
        /// a monitor can show thirty of them, so the material is reused and only the UV
        /// window into the atlas changes when the sprite does.
        /// </summary>
        private void BindIconMaterial(int index, ItemDefinition item)
        {
            Sprite sprite = item.icon;
            if (sprite == null || sprite.texture == null)
            {
                // No art authored: the text side already printed the count, so an empty cell
                // is honest. A guessed shape would be a lie about what is in the box.
                if (_iconMaterials[index] != null) _iconOwners[index] = null;
                return;
            }

            if (_iconOwners[index] == item && _iconMaterials[index] != null) return;

            Material material = _iconMaterials[index];
            if (material == null)
            {
                material = MakeUnlitSpriteMaterial();
                _iconMaterials[index] = material;
            }

            Texture texture = sprite.texture;
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);

            // A sprite is a window into a shared atlas; render only that window.
            Rect rect = sprite.textureRect;
            float texW = Mathf.Max(1f, texture.width);
            float texH = Mathf.Max(1f, texture.height);
            var scale = new Vector2(rect.width / texW, rect.height / texH);
            var offset = new Vector2(rect.x / texW, rect.y / texH);
            if (material.HasProperty("_BaseMap")) material.SetTextureScale("_BaseMap", scale);
            if (material.HasProperty("_BaseMap")) material.SetTextureOffset("_BaseMap", offset);
            if (material.HasProperty("_MainTex")) material.SetTextureScale("_MainTex", scale);
            if (material.HasProperty("_MainTex")) material.SetTextureOffset("_MainTex", offset);

            material.color = Color.white;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

            _iconOwners[index] = item;
        }

        private static Material MakeUnlitSpriteMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "ScreenPageIcon_Runtime" };

            // Transparent, so the sprite's own alpha cuts the icon out instead of drawing a
            // black tile around it. Same canonical keyword set the ghost materials use.
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = (int)UnityEngine.RenderQueue.Transparent;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("ShadowCaster", false);
            return material;
        }

        /// <summary>Drop the pictures when the block goes away; the materials are runtime
        /// objects and Unity will not collect them on its own.</summary>
        private void ReleaseIconResources()
        {
            for (int i = 0; i < _iconMaterials.Count; i++)
                if (_iconMaterials[i] != null) Destroy(_iconMaterials[i]);
            _iconMaterials.Clear();
            _iconQuads.Clear();
            _iconOwners.Clear();
        }
    }
}
