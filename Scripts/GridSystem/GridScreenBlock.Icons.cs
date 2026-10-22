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

            // 17.42.0-dev — THE FIT PASS'S OWN MEASUREMENT.
            //
            // The fit stores the fitted cell size after sizing the ink layers; the pictures
            // are laid out on that same cell, so they cannot drift away from their counts.
            // Quads are also clamped inside the glass: an icon that would hang outside the
            // panel is dropped instead of painted over the frame (which is how a stray bar
            // used to appear at the bottom of a big screen).
            if (_fittedLineHeight <= 0.0001f || _fittedCharWidth <= 0.0001f) return;
            if (_bodyAnchor == null) return;

            float lineHeight = _fittedLineHeight;
            float charWidth = _fittedCharWidth;

            EnsureIconRoot();
            EnsureIconPool(requests.Count);

            var (glassW, glassH) = GlassSize();

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

                // THE PICTURE GOES ON ITS CELL, MEASURED FROM THE PRINTED BLOCK.
                //
                // Not from the anchor and not from a per-character guess: the fit reports
                // the block's own left edge, top edge and size, and the canvas reports how
                // many rows and columns that block is. A cell's centre is therefore a
                // fraction of the block - exact even when the page does not start at the
                // block's own origin, and immune to any error in the per-cell metrics
                // (which accumulated across columns and is why pictures drifted off their
                // counts on a wide page).
                int columns = Mathf.Max(1, _iconColumns);
                int rows = Mathf.Max(1, _iconRows);
                float fx = (request.Column + request.Width * 0.5f) / columns;
                float fy = (request.Row + request.Height * 0.5f) / rows;

                float width = request.Width / columns * _blockWidth * 0.84f;
                float height = request.Height / rows * _blockHeight * 0.74f;

                float cx = _blockLeft + fx * _blockWidth;
                float cy = _blockTop - fy * _blockHeight;

                // Inside the glass or nowhere at all. Positions live in the body
                // anchor's frame - the page's top-left corner - so the glass is to the
                // right and BELOW; the old centred-box clamp silently dropped every
                // picture past the middle of the panel.
                if (cx < -glassW * 0.02f || cx > glassW * 1.02f
                    || cy > glassH * 0.12f || cy < -glassH * 1.05f)
                {
                    quad.enabled = false;
                    continue;
                }
                width = Mathf.Min(width, glassW);
                height = Mathf.Min(height, glassH);

                quad.transform.localPosition = new Vector3(cx, cy, 0.004f);
                quad.transform.localScale = new Vector3(width, height, 1f);
                quad.enabled = true;
            }
        }

        private void EnsureIconRoot()
        {
            if (_iconRoot != null) return;
            // Pictures ride the body anchor, so they inherit the page's corner and can
            // never sit somewhere the text grid is not.
            Transform parent = _bodyAnchor != null ? _bodyAnchor : transform.Find("DisplayRoot");
            if (parent == null) return;

            Transform existing = parent.Find("PageIcons");
            if (existing != null) { _iconRoot = existing; return; }

            var root = new GameObject("PageIcons");
            root.transform.SetParent(parent, false);
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
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
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
