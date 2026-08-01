// Assets/Scripts/VoxelEngine/Building/BlockPaint.cs
//
// Runtime cosmetic finish on a placed static block or grid block. Stores the
// finish id for saves and reapplies URP material tints to child renderers.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    [DisallowMultipleComponent]
    public class BlockPaint : MonoBehaviour
    {
        [SerializeField] private PaintFinishId _finish = PaintFinishId.None;

        // Cached prefab/shared materials so clear can restore the original look.
        private readonly List<Renderer> _cachedRenderers = new List<Renderer>();
        private readonly List<Material[]> _cachedShared = new List<Material[]>();
        private bool _hasCache;

        public PaintFinishId Finish
        {
            get => _finish;
            set
            {
                _finish = value;
                Apply();
            }
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private void Start()
        {
            if (_finish != PaintFinishId.None)
                Apply();
        }

        private void EnsureCache()
        {
            if (_hasCache) return;
            _cachedRenderers.Clear();
            _cachedShared.Clear();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var ren = renderers[r];
                if (ren == null) continue;
                if (ren is ParticleSystemRenderer || ren is TrailRenderer || ren is LineRenderer) continue;
                var shared = ren.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;
                _cachedRenderers.Add(ren);
                var copy = new Material[shared.Length];
                for (int i = 0; i < shared.Length; i++) copy[i] = shared[i];
                _cachedShared.Add(copy);
            }
            _hasCache = true;
        }

        public void Apply()
        {
            EnsureCache();

            if (_finish == PaintFinishId.None)
            {
                // Restore original shared materials.
                for (int r = 0; r < _cachedRenderers.Count; r++)
                {
                    var ren = _cachedRenderers[r];
                    if (ren == null) continue;
                    ren.sharedMaterials = _cachedShared[r];
                }
                return;
            }

            var def = PaintFinishCatalog.Get(_finish);
            for (int r = 0; r < _cachedRenderers.Count; r++)
            {
                var ren = _cachedRenderers[r];
                if (ren == null) continue;
                var srcMats = _cachedShared[r];
                var instanced = new Material[srcMats.Length];
                for (int i = 0; i < srcMats.Length; i++)
                {
                    var src = srcMats[i];
                    if (src == null) { instanced[i] = null; continue; }
                    var mat = new Material(src);
                    ApplyDef(mat, def);
                    instanced[i] = mat;
                }
                ren.materials = instanced;
            }
        }

        private static void ApplyDef(Material mat, PaintFinishCatalog.Def def)
        {
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, def.color);
            else if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, def.color);
            else mat.color = def.color;

            if (mat.HasProperty(MetallicId)) mat.SetFloat(MetallicId, def.metallic);
            if (mat.HasProperty(SmoothnessId)) mat.SetFloat(SmoothnessId, def.smoothness);
        }

        /// <summary>Apply a finish to the nearest paintable host under a collider/transform.</summary>
        public static bool TryPaint(Component host, PaintFinishId finish)
        {
            if (host == null) return false;
            var root = ResolvePaintRoot(host);
            if (root == null) return false;

            var paint = root.GetComponent<BlockPaint>();
            if (paint == null) paint = root.AddComponent<BlockPaint>();
            paint.Finish = finish;
            return true;
        }

        public static PaintFinishId GetFinish(Component host)
        {
            var root = ResolvePaintRoot(host);
            if (root == null) return PaintFinishId.None;
            var paint = root.GetComponent<BlockPaint>();
            return paint != null ? paint.Finish : PaintFinishId.None;
        }

        private static GameObject ResolvePaintRoot(Component host)
        {
            // Prefer PlacedBlock / GridBlock / PlacedTieredBlock roots so we don't
            // attach paint to a child mesh piece.
            var pb = host.GetComponentInParent<PlacedBlock>();
            if (pb != null) return pb.gameObject;
            var gb = host.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            if (gb != null) return gb.gameObject;
            var tb = host.GetComponentInParent<Tiered.PlacedTieredBlock>();
            if (tb != null) return tb.gameObject;
            return null;
        }
    }
}
