// Assets/Scripts/VoxelEngine/Cosmos/CosmosDensityPreview.cs
//
// Visual validator for the Phase-1 radial density field. Drop it on the same GameObject as a
// CelestialBody (and optionally assign a BiomeRegistry so real biomes are used). It samples the
// SphereDensity.EvaluateColumn field across a spherical grid of directions and renders colour-
// coded markers in the Scene view:
//   • blue   = ocean (surface below sea level)
//   • green  = plains / lowland (near sea level)
//   • brown  = highland
//   • white  = peaks / snow line (high altitude)
//
// This is the fastest way to confirm continents, oceans and mountain ranges look right on a
// SPHERE before the Phase-2 face-streamer turns it into real voxel terrain.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Biomes;
using VoxelEngine.Generation;

namespace VoxelEngine.Cosmos
{
    [ExecuteAlways]
    public class CosmosDensityPreview : MonoBehaviour
    {
        [Tooltip("Body whose density to preview.")]
        public CelestialBody body;

        [Tooltip("Optional biome registry (otherwise a default plains set is used).")]
        public BiomeRegistry biomeRegistry;

        [Range(64, 6000)]
        [Tooltip("Number of sample directions on the sphere (more = finer preview).")]
        public int samples = 1500;

        [Range(0.5f, 8f)]
        [Tooltip("Preview sphere radius in scene units (visualisation only).")]
        public float previewRadius = 3f;

        private NativeArray<BiomeData> _biomes;
        private NativeArray<OreLayer>  _ores;
        private Vector3[] _points;
        private Color[]   _colors;
        private int       _lastSamples;
        private int       _lastSeed;

        private void OnEnable() => Rebuild();
        private void OnDisable() => Release();

        private void Update()
        {
            // Rebuild if the sample count or seed changed in the inspector.
            if (_biomes.IsCreated && body != null &&
                (_lastSamples != samples || _lastSeed != body.genParams.seed))
                Rebuild();
        }

        private void Rebuild()
        {
            Release();
            if (body == null) return;

            body.ApplySettings();

            BiomeData[] biomeArr = body.BuildBiomeData(biomeRegistry);
            OreLayer[]  oreArr   = body.BuildOreLayers();

            _biomes = new NativeArray<BiomeData>(biomeArr.Length, Allocator.Persistent);
            for (int i = 0; i < biomeArr.Length; i++) _biomes[i] = biomeArr[i];

            _ores = new NativeArray<OreLayer>(oreArr.Length, Allocator.Persistent);
            for (int i = 0; i < oreArr.Length; i++) _ores[i] = oreArr[i];

            var dirs = FibonacciSphere(samples);
            _points = new Vector3[dirs.Length];
            _colors = new Color[dirs.Length];

            var prm = body.genParams;
            for (int i = 0; i < dirs.Length; i++)
            {
                SphereDensity.EvaluateColumn(prm, _biomes, dirs[i], out float surfaceR, out int biomeI);
                float alt = surfaceR - prm.MeanSurfaceRadius; // metres above/below mean surface
                float rel = alt / prm.MeanSurfaceRadius;      // normalised

                Vector3 p = transform.position + (Vector3)dirs[i] * previewRadius;
                _points[i] = p;
                _colors[i] = ColorFor(alt, rel, biomeI);
            }

            _lastSamples = samples;
            _lastSeed    = body.genParams.seed;
        }

        private static Color ColorFor(float altMetres, float rel, int biomeI)
        {
            // Below sea level → ocean blue (deeper = darker).
            if (altMetres < 0f)
            {
                float depth = Mathf.Clamp01(-altMetres / 40f);
                return Color.Lerp(new Color(0.20f, 0.45f, 0.70f), new Color(0.03f, 0.10f, 0.30f), depth);
            }
            // Beach/shallow.
            if (altMetres < 2f) return new Color(0.80f, 0.74f, 0.52f);

            // Land: green lowland → brown highland → white peaks.
            float h = Mathf.Clamp01(altMetres / 60f);
            Color land = Color.Lerp(new Color(0.30f, 0.55f, 0.25f), new Color(0.50f, 0.40f, 0.28f), h);
            if (h > 0.75f) land = Color.Lerp(land, Color.white, (h - 0.75f) / 0.25f); // snow caps
            return land;
        }

        // Even distribution of N points on a unit sphere via the Fibonacci spiral.
        private static float3[] FibonacciSphere(int n)
        {
            var pts = new float3[n];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - (i / (float)(n - 1)) * 2f;
                float r = Mathf.Sqrt(1f - y * y);
                float phi = golden * i;
                pts[i] = new float3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
            }
            return pts;
        }

        private void OnDrawGizmos()
        {
            if (_points == null || _colors == null) return;
            for (int i = 0; i < _points.Length; i++)
            {
                Gizmos.color = _colors[i];
                Gizmos.DrawSphere(_points[i], previewRadius * 0.05f);
            }
        }

        private void Release()
        {
            if (_biomes.IsCreated) _biomes.Dispose();
            if (_ores.IsCreated)   _ores.Dispose();
            _biomes = default;
            _ores   = default;
            _points = null;
            _colors = null;
        }
    }
}
