// Assets/Scripts/VoxelEngine/Building/PlacementLattice.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║     PLACEMENT LATTICE — the grid you are actually building on     ║
// ║                                                                  ║
// ║  A hologram tells you what you are holding. It does not tell you  ║
// ║  which CELL, and that is the question every builder is silently  ║
// ║  asking. So while a block is in hand the surface under it grows  ║
// ║  a faint lattice: real lines on the real cell boundaries, in the  ║
// ║  real frame (see PlacementAlign), plus a slow band of light that ║
// ║  sweeps across it so the surface reads as live rather than as a  ║
// ║  decal.                                                          ║
// ║                                                                  ║
// ║  The lattice is not decoration pretending to be information - it  ║
// ║  is generated at exactly the step the placement quantises to, so  ║
// ║  a line is a cell and a cell is where the block will land. Switch  ║
// ║  the step to half a metre and the lattice halves with it.        ║
// ║                                                                  ║
// ║  One GameObject, one texture, both built on first use and reused  ║
// ║  forever. Every failure path is "no lattice", never "no build".   ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>The faint cell lattice drawn on the surface under a placement ghost.</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementLattice : MonoBehaviour
    {
        private const float Extent = 8f;        // metres of surface covered, each way
        private const float HeightOffset = 0.015f;
        private const float ScanPeriod = 2.6f;

        private static PlacementLattice _instance;

        private MeshRenderer _gridRenderer;
        private MeshRenderer _scanRenderer;
        private MaterialPropertyBlock _block;
        private Texture2D _gridTexture;
        private Texture2D _scanTexture;
        private Color _tint = new(0.35f, 0.82f, 0.95f, 0.20f);
        private PlacementFrame _frame;
        private float _step = 1f;
        private bool _built;

        /// <summary>Shows (or moves) the lattice under a pose. Safe to call every frame.</summary>
        public static void Show(Vector3 surfacePoint, in PlacementFrame frame, float step, Color tint)
        {
            if (step <= 0.01f) step = 1f;
            var lattice = Ensure();
            if (lattice == null) return;
            lattice.gameObject.SetActive(true);
            lattice.UpdatePose(surfacePoint, frame, step, tint);
        }

        public static void Hide()
        {
            if (_instance != null && _instance.gameObject.activeSelf)
                _instance.gameObject.SetActive(false);
        }

        private static PlacementLattice Ensure()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("~PlacementLattice");
            _instance = go.AddComponent<PlacementLattice>();
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_gridTexture != null) Destroy(_gridTexture);
            if (_scanTexture != null) Destroy(_scanTexture);
        }

        private void Update()
        {
            if (_scanRenderer == null || _scanTexture == null) return;
            // The band sweeps across the lattice; the grid itself never moves, because a
            // lattice that slides is a lattice that lies about where the cells are.
            float phase = Mathf.Repeat(Time.unscaledTime / ScanPeriod, 1f);
            float offset = Mathf.Lerp(-Extent * 0.5f, Extent * 0.5f, phase);
            _scanRenderer.transform.localPosition = new Vector3(offset, 0f, 0.012f);
        }

        private void UpdatePose(Vector3 surfacePoint, in PlacementFrame frame, float step, Color tint)
        {
            _frame = frame.valid ? frame : PlacementFrame.Ground(surfacePoint);
            _step = step;
            _tint = tint;

            EnsureBuilt();
            if (_gridRenderer == null) return;

            // Anchor the lattice on a real cell boundary in the frame, so a line always
            // lines up with a placeable cell even far from the world origin.
            Vector3 local = _frame.ToLocal(surfacePoint);
            local.x = Mathf.Round(local.x / step) * step;
            local.z = Mathf.Round(local.z / step) * step;
            local.y = 0f;
            Vector3 centre = _frame.ToWorld(local) + _frame.up * HeightOffset;

            // The root carries only the pose; the quads carry their own size, so the
            // sweep band can be a narrow strip without fighting a parent scale.
            transform.SetPositionAndRotation(
                centre,
                Quaternion.LookRotation(_frame.forward, _frame.up) * Quaternion.Euler(-90f, 0f, 0f));
            transform.localScale = Vector3.one;

            // One texture repeat per cell: the drawn lines ARE the cell edges.
            float repeats = Mathf.Max(1f, Mathf.Round(Extent / step));
            var material = _gridRenderer.sharedMaterial;
            if (_gridTexture != null && material != null)
            {
                _gridTexture.wrapMode = TextureWrapMode.Repeat;
                material.mainTextureScale = new Vector2(repeats, repeats);
            }

            _block ??= new MaterialPropertyBlock();
            _gridRenderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", _tint);
            _block.SetColor("_Color", _tint);
            _gridRenderer.SetPropertyBlock(_block);
        }

        private void EnsureBuilt()
        {
            if (_built || _gridRenderer != null) return;
            _built = true;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            if (shader == null) return;

            _gridTexture = BuildGridTexture();
            _scanTexture = BuildScanTexture();

            if (_gridTexture != null)
            {
                var gridGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                gridGo.name = "LatticeGrid";
                gridGo.transform.SetParent(transform, false);
                gridGo.transform.localPosition = Vector3.zero;
                gridGo.transform.localScale = new Vector3(Extent, Extent, 1f);
                var collider = gridGo.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                _gridRenderer = gridGo.GetComponent<MeshRenderer>();
                var material = new Material(shader);
                material.mainTexture = _gridTexture;
                VoxelEngine.Rendering.RuntimeMaterials.ApplyTranslucent(material, _tint);
                _gridRenderer.sharedMaterial = material;
                _gridRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _gridRenderer.receiveShadows = false;
            }

            if (_scanTexture != null)
            {
                var scanGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                scanGo.name = "LatticeScan";
                scanGo.transform.SetParent(transform, false);
                // Local X runs along the frame's right, local Y along its forward.
                scanGo.transform.localScale = new Vector3(1.4f, Extent, 1f);
                var collider = scanGo.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                _scanRenderer = scanGo.GetComponent<MeshRenderer>();
                var material = new Material(shader);
                material.mainTexture = _scanTexture;
                VoxelEngine.Rendering.RuntimeMaterials.ApplyTranslucent(material, new Color(0.55f, 0.90f, 1f, 0.10f));
                _scanRenderer.sharedMaterial = material;
                _scanRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _scanRenderer.receiveShadows = false;
            }
        }

        /// <summary>A cell with its two far edges drawn, so tiling it draws every boundary once.</summary>
        private static Texture2D BuildGridTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PlacementLatticeGrid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };

            var pixels = new Color32[size * size];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            var line = new Color32(255, 255, 255, 210);
            var soft = new Color32(255, 255, 255, 60);
            for (int i = 0; i < size; i++)
            {
                pixels[i] = line;                          // bottom edge (v = 0)
                pixels[size * (size - 1) + i] = soft;      // top edge is the next cell's seam
                pixels[i * size] = line;                   // left edge (u = 0)
                pixels[i * size + (size - 1)] = soft;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        /// <summary>A soft vertical band: the sweep that makes the lattice look alive.</summary>
        private static Texture2D BuildScanTexture()
        {
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "PlacementLatticeScan",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            var pixels = new Color32[width];
            for (int i = 0; i < width; i++)
            {
                float t = i / (float)(width - 1);
                float alpha = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(t * 2f - 1f));
                pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }
}
