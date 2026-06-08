// Assets/Scripts/VoxelEngine/Transport/ItemFlowVisualizer.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   ITEM FLOW VISUALIZER — directed pellet stream in glass pipe    ║
// ║                                                                  ║
// ║   Driven by ItemPipe. The pipe calls SetFlow() with one or more  ║
// ║   DIRECTED segments (entryDir → exitDir, world space). Each       ║
// ║   pellet rides the full one-way path  entryTip → hub → exitTip,   ║
// ║   so flow reads as a real stream travelling THROUGH the pipe      ║
// ║   instead of pulsing outward from the middle. A missing entry/exit║
// ║   side simply starts/ends the pellet at the hub centre.           ║
// ║                                                                  ║
// ║   • Object-pooled, zero steady-state allocation.                 ║
// ║   • Self-contained: no edits to the mesh rebuild pipeline.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    [DisallowMultipleComponent]
    public class ItemFlowVisualizer : MonoBehaviour
    {
        [Tooltip("Diameter of a flowing pellet.")]
        public float pelletSize = 0.18f;

        [Tooltip("How far from the hub a pellet travels along each arm.")]
        public float armReach = 0.5f;

        [Tooltip("Pellets shown simultaneously per active direction.")]
        public int pelletsPerStream = 3;

        [Tooltip("Seconds for one pellet to travel a full arm.")]
        public float travelTime = 0.6f;

        [Tooltip("Seconds the stream keeps flowing after the last item moved.")]
        public float flowLinger = 0.9f;

        // ── Stream state ────────────────────────────────────────────────────
        // Local-space directed segments (entry → exit) the pipe is feeding.
        // A zero entry/exit means "start/end at the hub centre".
        private readonly List<(Vector3 from, Vector3 to)> _streams = new(6);
        private float _hotUntil;          // stream renders while Time.time < this
        private float _intensity;         // 0..1 eased fade for scale/brightness

        // ── Pellet pool ─────────────────────────────────────────────────────
        private readonly List<Transform> _pellets = new();
        private readonly List<MeshRenderer> _renderers = new();
        private MaterialPropertyBlock _mpb;
        private Transform _root;
        private Material _sharedMat;
        private static Mesh _sphereMesh;
        private Color _color = new(0.95f, 0.7f, 0.3f);

        private void Awake()
        {
            var go = new GameObject("ItemFlow");
            _root = go.transform;
            _root.SetParent(transform, worldPositionStays: false);
            _mpb = new MaterialPropertyBlock();
            EnsureSphereMesh();
        }

        private void OnDisable()
        {
            _streams.Clear();
            _hotUntil = 0f;
            _intensity = 0f;
            SetActivePelletCount(0);
        }

        /// <summary>
        /// Tell the visualizer the pipe is actively moving <paramref name="item"/>
        /// along the given DIRECTED segments (entryDir → exitDir, world space).
        /// Pellets ride entry → hub → exit so the stream reads one-way. A zero
        /// entry or exit vector means that end sits at the hub centre. Call every
        /// tick the pipe moves something; the stream lingers briefly afterwards.
        /// </summary>
        public void SetFlow(ItemDefinition item, List<(Vector3 from, Vector3 to)> worldSegments)
        {
            if (item == null || worldSegments == null || worldSegments.Count == 0) return;

            Color c = item.iconTint;
            if (c.maxColorComponent < 0.05f) c = new Color(0.95f, 0.7f, 0.3f);
            c.a = 1f;
            _color = c;

            // Rebuild stream descriptors in LOCAL space (cheap — at most 6).
            _streams.Clear();
            for (int i = 0; i < worldSegments.Count; i++)
            {
                var seg = worldSegments[i];
                if (seg.to.sqrMagnitude < 0.01f && seg.from.sqrMagnitude < 0.01f) continue;
                Vector3 fromL = seg.from.sqrMagnitude > 0.01f
                    ? transform.InverseTransformDirection(seg.from).normalized : Vector3.zero;
                Vector3 toL = seg.to.sqrMagnitude > 0.01f
                    ? transform.InverseTransformDirection(seg.to).normalized : Vector3.zero;
                _streams.Add((fromL, toL));
            }

            _hotUntil = Time.time + flowLinger;
        }

        private void Update()
        {
            bool hot = Time.time < _hotUntil && _streams.Count > 0;

            // Ease intensity toward 1 while hot, toward 0 once cold.
            float target = hot ? 1f : 0f;
            _intensity = Mathf.MoveTowards(_intensity, target, Time.deltaTime / 0.25f);

            int need = (_intensity > 0.001f) ? _streams.Count * pelletsPerStream : 0;
            SetActivePelletCount(need);
            if (need == 0) return;

            float tt = Mathf.Max(0.1f, travelTime);
            float globalPhase = (Time.time / tt) % 1f;

            int idx = 0;
            for (int s = 0; s < _streams.Count; s++)
            {
                Vector3 fromTip = _streams[s].from * armReach; // entry side (or hub if zero)
                Vector3 toTip   = _streams[s].to   * armReach; // exit side  (or hub if zero)

                for (int k = 0; k < pelletsPerStream; k++)
                {
                    // Evenly spaced pellets marching from entry → exit, looping
                    // 0→1 so the stream looks continuous and ONE-WAY.
                    float p = (globalPhase + (float)k / pelletsPerStream) % 1f;

                    // Two-leg path: entryTip → hub for first half, hub → exitTip
                    // for the second. Half collapses to a point when a side is
                    // the hub, so single-sided flow still moves cleanly one way.
                    Vector3 pos = (p < 0.5f)
                        ? Vector3.Lerp(fromTip, Vector3.zero, p * 2f)
                        : Vector3.Lerp(Vector3.zero, toTip, (p - 0.5f) * 2f);

                    var tf = _pellets[idx];
                    tf.localPosition = pos;

                    // Fade the head/tail of each pellet's life + global intensity.
                    float edge = Mathf.Clamp01(Mathf.Min(p, 1f - p) * 5f);
                    float sc = pelletSize * Mathf.Lerp(0.45f, 1f, edge) * _intensity;
                    tf.localScale = Vector3.one * sc;
                    idx++;
                }
            }

            ApplyColor();
        }

        // ── Pool management ─────────────────────────────────────────────────
        private void SetActivePelletCount(int count)
        {
            // Grow pool as needed.
            while (_pellets.Count < count)
            {
                var go = new GameObject("Pellet");
                go.transform.SetParent(_root, worldPositionStays: false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _sphereMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sharedMaterial = EnsureSharedMaterial();
                _pellets.Add(go.transform);
                _renderers.Add(mr);
            }
            // Toggle visibility.
            for (int i = 0; i < _pellets.Count; i++)
            {
                bool on = i < count;
                if (_pellets[i].gameObject.activeSelf != on)
                    _pellets[i].gameObject.SetActive(on);
            }
        }

        private void ApplyColor()
        {
            _mpb.SetColor("_BaseColor", _color);
            _mpb.SetColor("_Color", _color);
            _mpb.SetColor("_EmissionColor", _color * Mathf.Lerp(0.6f, 2.2f, _intensity));
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i].gameObject.activeSelf)
                    _renderers[i].SetPropertyBlock(_mpb);
        }

        private Material EnsureSharedMaterial()
        {
            if (_sharedMat != null) return _sharedMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Standard");
            _sharedMat = new Material(sh) { name = "ItemFlowPellet" };
            if (_sharedMat.HasProperty("_Smoothness")) _sharedMat.SetFloat("_Smoothness", 0.2f);
            if (_sharedMat.HasProperty("_Metallic"))   _sharedMat.SetFloat("_Metallic", 0.0f);
            _sharedMat.EnableKeyword("_EMISSION");
            return _sharedMat;
        }

        private static void EnsureSphereMesh()
        {
            if (_sphereMesh != null) return;
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            // Strip the collider that CreatePrimitive added before discarding.
            if (Application.isPlaying) Destroy(temp); else DestroyImmediate(temp);
        }
    }
}
