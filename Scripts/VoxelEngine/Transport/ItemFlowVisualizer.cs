// Assets/Scripts/VoxelEngine/Transport/ItemFlowVisualizer.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   ITEM FLOW VISUALIZER — animated pellets inside glass pipes     ║
// ║                                                                  ║
// ║   Driven by ItemPipe. Each time the pipe physically moves an     ║
// ║   item across a tick, it calls Emit() with the entry + exit      ║
// ║   directions. A small emissive pellet (tinted to the item) then  ║
// ║   slides from the entry arm tip → hub → exit arm tip over one    ║
// ║   tick, selling the "stream flowing through the glass".          ║
// ║                                                                  ║
// ║   • Object-pooled — zero per-frame allocation after warm-up.     ║
// ║   • Self-contained: no edits to the mesh rebuild pipeline.       ║
// ║   • Only renders for GLASS pipes (opaque pipes hide the core).   ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    [DisallowMultipleComponent]
    public class ItemFlowVisualizer : MonoBehaviour
    {
        [Tooltip("Diameter of a flowing pellet relative to the pipe core.")]
        public float pelletSize = 0.16f;

        [Tooltip("How far down each arm the pellet travels before/after the hub.")]
        public float armReach = 0.5f;

        // ── Pooling ─────────────────────────────────────────────────────────
        private readonly List<Pellet> _active = new();
        private readonly Stack<Pellet> _pool = new();
        private Transform _root;
        private Material _sharedMat;
        private static Mesh _sphereMesh;

        private class Pellet
        {
            public Transform tf;
            public MeshRenderer renderer;
            public MaterialPropertyBlock mpb;
            public Vector3 from;     // local entry point
            public Vector3 mid;      // local hub point
            public Vector3 to;       // local exit point
            public float duration;
            public float elapsed;
            public Color color;
        }

        private void Awake()
        {
            var go = new GameObject("ItemFlow");
            _root = go.transform;
            _root.SetParent(transform, worldPositionStays: false);
            EnsureSphereMesh();
        }

        private void OnDisable()
        {
            // Recycle everything so re-enabling starts clean.
            for (int i = _active.Count - 1; i >= 0; i--) Recycle(_active[i]);
            _active.Clear();
        }

        /// <summary>
        /// Spawn an animated pellet flowing from <paramref name="fromDir"/> (the
        /// cardinal direction the item entered from, world space) toward
        /// <paramref name="toDir"/> (where it's heading). Pass Vector3.zero for a
        /// missing side (e.g. injected at this pipe, or delivered into a sink).
        /// </summary>
        public void Emit(ItemDefinition item, Vector3 fromDir, Vector3 toDir, float duration)
        {
            if (item == null) return;
            duration = Mathf.Max(0.08f, duration);

            var p = _pool.Count > 0 ? _pool.Pop() : CreatePellet();

            // Convert world cardinal directions to local space so the pellet
            // path follows the pipe even if the pipe is rotated.
            Vector3 inLocal  = fromDir.sqrMagnitude > 0.01f
                ? transform.InverseTransformDirection(fromDir).normalized * armReach
                : Vector3.zero;
            Vector3 outLocal = toDir.sqrMagnitude > 0.01f
                ? transform.InverseTransformDirection(toDir).normalized * armReach
                : Vector3.zero;

            p.from = inLocal;
            p.mid  = Vector3.zero;
            p.to   = outLocal;
            p.duration = duration;
            p.elapsed  = 0f;

            // Tint to the item: prefer the icon tint, fall back to a warm amber.
            Color c = item.iconTint;
            if (c.maxColorComponent < 0.05f) c = new Color(0.95f, 0.7f, 0.3f);
            c.a = 1f;
            p.color = c;

            p.tf.gameObject.SetActive(true);
            p.tf.localScale = Vector3.one * pelletSize;
            p.tf.localPosition = p.from;
            ApplyColor(p);

            _active.Add(p);
        }

        private void Update()
        {
            if (_active.Count == 0) return;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var p = _active[i];
                p.elapsed += Time.deltaTime;
                float t = p.elapsed / p.duration;

                if (t >= 1f)
                {
                    _active.RemoveAt(i);
                    Recycle(p);
                    continue;
                }

                // Two-leg path: entry→hub for first half, hub→exit for second.
                Vector3 pos;
                if (t < 0.5f)
                    pos = Vector3.Lerp(p.from, p.mid, t * 2f);
                else
                    pos = Vector3.Lerp(p.mid, p.to, (t - 0.5f) * 2f);

                p.tf.localPosition = pos;

                // Gentle fade-in / fade-out so pellets don't pop at the ends.
                float fade = Mathf.Clamp01(Mathf.Min(t, 1f - t) * 6f);
                p.tf.localScale = Vector3.one * (pelletSize * Mathf.Lerp(0.4f, 1f, fade));
            }
        }

        // ── Pool helpers ────────────────────────────────────────────────────
        private Pellet CreatePellet()
        {
            var go = new GameObject("Pellet");
            go.transform.SetParent(_root, worldPositionStays: false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _sphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = EnsureSharedMaterial();

            return new Pellet
            {
                tf = go.transform,
                renderer = mr,
                mpb = new MaterialPropertyBlock()
            };
        }

        private void Recycle(Pellet p)
        {
            if (p?.tf == null) return;
            p.tf.gameObject.SetActive(false);
            _pool.Push(p);
        }

        private void ApplyColor(Pellet p)
        {
            p.mpb.SetColor("_BaseColor", p.color);
            p.mpb.SetColor("_Color", p.color);
            p.mpb.SetColor("_EmissionColor", p.color * 1.4f);
            p.renderer.SetPropertyBlock(p.mpb);
        }

        private Material EnsureSharedMaterial()
        {
            if (_sharedMat != null) return _sharedMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _sharedMat = new Material(sh) { name = "ItemFlowPellet" };
            if (_sharedMat.HasProperty("_Smoothness")) _sharedMat.SetFloat("_Smoothness", 0.2f);
            if (_sharedMat.HasProperty("_Metallic"))   _sharedMat.SetFloat("_Metallic", 0.0f);
            _sharedMat.EnableKeyword("_EMISSION");
            return _sharedMat;
        }

        private static void EnsureSphereMesh()
        {
            if (_sphereMesh != null) return;
            // Borrow Unity's built-in sphere primitive mesh once, then discard
            // the temporary GameObject — cheaper than authoring a mesh by hand.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(temp); else DestroyImmediate(temp);
        }
    }
}
