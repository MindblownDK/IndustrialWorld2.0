// Assets/Scripts/VoxelEngine/Transport/ItemFlowVisualizer.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   ITEM FLOW VISUALIZER — continuous pellet stream in glass pipe  ║
// ║                                                                  ║
// ║   Driven by ItemPipe. The pipe calls SetFlow() whenever it is    ║
// ║   actively carrying items, supplying the item + the world-space   ║
// ║   directions it is currently feeding (one per active endpoint).   ║
// ║   While flow is "hot" the visualizer animates an evenly-spaced    ║
// ║   STREAM of small emissive pellets sliding hub → exit along each  ║
// ║   active direction, looping continuously so it's always visible.  ║
// ║   When the pipe stops moving items the stream fades out.          ║
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
        // Local-space directions the pipe is currently feeding (one per sink).
        private readonly List<Vector3> _streams = new(6);
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
        /// toward the given world-space directions (one per active sink/neighbour).
        /// Call this every tick the pipe moves something; the stream lingers
        /// briefly after the last call so brief gaps don't strobe.
        /// </summary>
        public void SetFlow(ItemDefinition item, List<Vector3> worldDirs)
        {
            if (item == null || worldDirs == null || worldDirs.Count == 0) return;

            Color c = item.iconTint;
            if (c.maxColorComponent < 0.05f) c = new Color(0.95f, 0.7f, 0.3f);
            c.a = 1f;
            _color = c;

            // Rebuild stream descriptors (cheap — at most 6).
            _streams.Clear();
            for (int i = 0; i < worldDirs.Count; i++)
            {
                Vector3 d = worldDirs[i];
                if (d.sqrMagnitude < 0.01f) continue;
                Vector3 local = transform.InverseTransformDirection(d).normalized;
                _streams.Add(local);
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
                Vector3 dir = _streams[s];
                for (int k = 0; k < pelletsPerStream; k++)
                {
                    // Evenly spaced pellets, all marching outward along the arm,
                    // looping 0→1 so the stream looks continuous.
                    float p = (globalPhase + (float)k / pelletsPerStream) % 1f;

                    var tf = _pellets[idx];
                    // Pellet rides from just inside the hub out to the arm tip.
                    Vector3 pos = dir * Mathf.Lerp(0.05f, armReach, p);
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
