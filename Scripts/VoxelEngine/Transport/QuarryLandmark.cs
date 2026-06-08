// Assets/Scripts/VoxelEngine/Transport/QuarryLandmark.cs
//
// BuildCraft-style landmark. Place 2 landmarks to define a rectangular quarry area.
// The Quarry block detects landmarks within range and uses them to set its mining area.
// Without landmarks, the quarry defaults to 16×16.

using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Place two of these on diagonally opposite corners to define a quarry area.
    /// The Quarry will detect them and mine the defined rectangle.
    /// When detected, landmarks project a visible beam upward (like BuildCraft).
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class QuarryLandmark : MonoBehaviour
    {
        [Header("Visuals")]
        [Tooltip("Color of the landmark beam.")]
        public Color beamColor = new Color(0.2f, 0.5f, 1.0f, 0.5f);

        [Tooltip("Beam height in world units.")]
        public float beamHeight = 20f;

        /// <summary>World position of this landmark (for quarry detection).</summary>
        public Vector3 WorldPosition => transform.position;

        private LineRenderer _beam;
        private bool _linked;

        private void Start()
        {
            CreateBeam();
        }

        /// <summary>Mark this landmark as linked to a quarry (changes beam color).</summary>
        public void SetLinked(bool linked)
        {
            _linked = linked;
            if (_beam != null)
            {
                Color c = linked ? new Color(0.2f, 1f, 0.3f, 0.6f) : beamColor;
                _beam.startColor = c;
                _beam.endColor = new Color(c.r, c.g, c.b, 0.05f);
            }
        }

        private void CreateBeam()
        {
            var go = new GameObject("LandmarkBeam");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _beam = go.AddComponent<LineRenderer>();
            _beam.positionCount = 2;
            _beam.SetPosition(0, Vector3.zero);
            _beam.SetPosition(1, Vector3.up * beamHeight);
            _beam.startWidth = 0.1f;
            _beam.endWidth = 0.02f;
            _beam.startColor = beamColor;
            _beam.endColor = new Color(beamColor.r, beamColor.g, beamColor.b, 0.05f);
            _beam.useWorldSpace = false;

            // Use a simple unlit material.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _beam.material = new Material(shader);
            _beam.material.color = beamColor;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;
        }
    }
}
