// Assets/Scripts/VoxelEngine/Transport/QuarryLandmark.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  QUARRY LANDMARK — area-definition beacon for Quarry machines  ║
// ║                                                                ║
// ║  Place TWO landmarks to define a rectangular quarry area.      ║
// ║  Each landmark projects a sleek holographic beam upward.       ║
// ║  When linked to a quarry, the beam turns green.                ║
// ║                                                                ║
// ║  Design: Minimal dark beacon base + subtle cyan beam.          ║
// ║  Premium OS-dashboard aesthetic.                               ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Transport
{
    [RequireComponent(typeof(PlacedBlock))]
    public class QuarryLandmark : MonoBehaviour
    {
        // ── Static Registry ────────────────────────────────────────
        private static readonly List<QuarryLandmark> _allLandmarks = new();
        private static readonly HashSet<QuarryLandmark> _pendingRemoval = new();

        /// <summary>All active landmarks in the world (thread-safe snapshot).</summary>
        public static IReadOnlyList<QuarryLandmark> GetAllLandmarks()
        {
            _allLandmarks.RemoveAll(l => l == null);
            return _allLandmarks;
        }

        // ── Inspector ──────────────────────────────────────────────
        [Header("Beam")]
        [Tooltip("Color of the unlinked beam.")]
        public Color beamColor = new Color(0.18f, 0.55f, 0.95f, 0.55f);

        [Tooltip("Beam height in world units.")]
        public float beamHeight = 32f;

        [Tooltip("Width of the beam line.")]
        public float beamWidth = 0.08f;

        [Header("Base")]
        [Tooltip("Base plate color — dark steel.")]
        public Color baseColor = new Color(0.15f, 0.16f, 0.2f);

        // ── Runtime ────────────────────────────────────────────────
        public Vector3 WorldPosition => transform.position;

        /// <summary>Is this landmark available for a new quarry connection?</summary>
        public bool IsAvailable => _linkedQuarry == null || _linkedQuarry == null;

        private LineRenderer _beam;
        private GameObject _basePlate;
        private bool _linked;
        private Quarry _linkedQuarry;

        // ── Lifecycle ──────────────────────────────────────────────
        private void OnEnable()
        {
            _allLandmarks.Add(this);
            CreateBeam();
            CreateBasePlate();
        }

        private void OnDisable()
        {
            _allLandmarks.Remove(this);
            if (_linkedQuarry != null)
                SetLinked(false, null);
        }

        private void OnDestroy()
        {
            _allLandmarks.Remove(this);
        }

        // ── Public API ─────────────────────────────────────────────
        /// <summary>
        /// Mark this landmark as linked/unlinked to a quarry.
        /// Linked landmarks glow green; unlinked glow blue.
        /// </summary>
        public void SetLinked(bool linked, Quarry quarry)
        {
            _linked = linked;
            _linkedQuarry = linked ? quarry : null;

            if (_beam != null)
            {
                Color c = linked
                    ? new Color(0.15f, 0.92f, 0.3f, 0.65f)
                    : beamColor;

                _beam.startColor = c;
                _beam.endColor = new Color(c.r, c.g, c.b, 0.03f);
            }
        }

        // ── Visuals ────────────────────────────────────────────────
        private void CreateBeam()
        {
            var go = new GameObject("LandmarkBeam");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.25f;

            _beam = go.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.SetPosition(0, Vector3.zero);
            _beam.SetPosition(1, Vector3.up * beamHeight);
            _beam.startWidth = beamWidth;
            _beam.endWidth = beamWidth * 0.15f;

            Color unlinkedColor = beamColor;
            _beam.startColor = unlinkedColor;
            _beam.endColor = new Color(unlinkedColor.r, unlinkedColor.g, unlinkedColor.b, 0.03f);
            _beam.useWorldSpace = false;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            _beam.material = new Material(shader);
            _beam.material.color = unlinkedColor;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
        }

        private void CreateBasePlate()
        {
            _basePlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _basePlate.name = "LandmarkBase";
            _basePlate.transform.SetParent(transform, false);
            _basePlate.transform.localPosition = Vector3.zero;
            _basePlate.transform.localScale = new Vector3(0.35f, 0.06f, 0.35f);

            var col = _basePlate.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = baseColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.85f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
            _basePlate.GetComponent<MeshRenderer>().material = m;
        }
    }
}
