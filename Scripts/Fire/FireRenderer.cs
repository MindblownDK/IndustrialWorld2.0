// Assets/Scripts/VoxelEngine/Fire/FireRenderer.cs
//
// 9.16.0 fire system (Liquids Overhaul, Part 2) — procedural fire visuals.
//
//   • One dynamic mesh holds a crossed-quad "flame column" per burning cell, raised
//     to the liquid's actual surface height and oriented to the planet's radial up.
//     The FireURP shader animates each column with world-space noise — no textures,
//     no particles, nothing spawned per flame.
//   • A small pool of shadowless point lights snaps to the flames nearest the camera
//     and flickers with per-light noise, so a burning lake genuinely lights its shore.
//   • Everything is rebuilt at the fire tick rate and costs nothing when no fire burns.
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Player;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Fire
{
    public class FireRenderer : MonoBehaviour
    {
        [Tooltip("Max flame columns drawn (matches the sim's burning-cell cap).")]
        public int maxVisualCells = 2048;
        [Tooltip("Point lights pooled for the nearest flames (0 = lights off).")]
        public int lightPool = 6;
        public float lightRange = 9f;
        public float lightIntensity = 1.6f;
        public Color lightColor = new Color(1f, 0.55f, 0.18f);

        private Mesh _mesh;
        private Material _mat;
        private readonly List<Vector3Int> _cells = new List<Vector3Int>(512);
        private readonly List<Vector3> _verts = new List<Vector3>(16384);
        private readonly List<Vector2> _uvs = new List<Vector2>(16384);
        private readonly List<Color32> _cols = new List<Color32>(16384);
        private readonly List<int> _tris = new List<int>(24576);
        private Light[] _lights;
        private readonly List<(Vector3 pos, float heat)> _lightTargets = new List<(Vector3, float)>(8);
        private float _rebuildTimer;

        private void Awake()
        {
            _mesh = new Mesh { name = "FireMesh" };
            _mesh.MarkDynamic();

            var go = new GameObject("FireMeshObject");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var rend = go.AddComponent<MeshRenderer>();

            // Per-renderer material instance (project convention) — falls back to
            // the URP unlit template so the renderer can never go magenta.
            Shader shader = Shader.Find("VoxelEngine/FireURP")
                          ?? Shader.Find("Universal Render Pipeline/Unlit");
            _mat = new Material(shader);
            rend.sharedMaterial = _mat;

            if (lightPool > 0)
            {
                _lights = new Light[lightPool];
                for (int i = 0; i < lightPool; i++)
                {
                    var lo = new GameObject("FireLight" + i);
                    lo.transform.SetParent(transform, false);
                    var l = lo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.shadows = LightShadows.None;
                    l.range = lightRange;
                    l.color = lightColor;
                    l.intensity = 0f;
                    l.enabled = false;
                    _lights[i] = l;
                }
            }
        }

        private void Update()
        {
            var fm = FireManager.Instance;
            if (fm == null) return;
            fm.CopyBurningCells(_cells);

            // The mesh only needs rebuilding at the fire tick rate — between rebuilds
            // the shader keeps every column animated (noise, sway, flicker).
            _rebuildTimer -= Time.deltaTime;
            if (_rebuildTimer <= 0f)
            {
                _rebuildTimer = 0.1f;
                RebuildMesh(fm);
            }
            UpdateLights(fm);
        }

        private void RebuildMesh(FireManager fm)
        {
            var world = ActiveWorld.Current;
            if (world == null || _cells.Count == 0)
            {
                _mesh.Clear();
                return;
            }
            FluidManager.EnsureInstance();
            var fluid = FluidManager.Instance;

            _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();

            int cellCount = Mathf.Min(_cells.Count, maxVisualCells);
            float time = Time.time;
            int vi = 0;
            for (int c = 0; c < cellCount; c++)
            {
                Vector3Int v = _cells[c];
                byte heat = fm.HeatAt(v);
                float heatFrac = heat / 255f;

                var liquid = fluid != null ? fluid.GetLiquidType(v) : LiquidType.Water;
                byte level = fluid != null ? fluid.GetLiquidLevel(v, liquid) : (byte)0;
                float surfaceFrac = level / 255f;

                // Flame base sits ON the liquid surface, centred in the cell.
                Vector3 basePos = new Vector3(v.x + 0.5f, v.y + surfaceFrac, v.z + 0.5f);
                Vector3 up = GravityProvider.GetUp(basePos);

                float seed = Mathf.Repeat(v.x * 12.9898f + v.y * 78.233f + v.z * 37.719f, 1f);
                float flicker = 0.72f + 0.45f * Mathf.PerlinNoise(seed * 13.7f, time * 3.1f);
                float height = (1.1f + 1.4f * heatFrac) * flicker;
                float width = 0.40f + 0.20f * heatFrac;
                float heatTint = 0.45f + 0.55f * heatFrac;

                Vector3 fwd = Vector3.Cross(up, Vector3.right);
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.Cross(up, Vector3.forward);
                fwd.Normalize();
                float yaw = Mathf.Repeat(seed * 6.28318f, 6.28318f);
                fwd = Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, up) * fwd;
                Vector3 right = Vector3.Cross(up, fwd).normalized;

                BuildQuad(basePos, fwd, up, height, width, heatTint, seed, vi);
                BuildQuad(basePos, right, up, height, width, heatTint, seed + 0.37f, vi + 4);
                vi += 8;
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_cols);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        private void BuildQuad(Vector3 basePos, Vector3 dir, Vector3 up, float height, float width,
                               float heatTint, float seed, int vi)
        {
            Vector3 side = Vector3.Cross(up, dir).normalized * (width * 0.5f);
            Vector3 p0 = basePos - side;
            Vector3 p1 = basePos + side;
            Vector3 p2 = basePos + side + up * height;
            Vector3 p3 = basePos - side + up * height;

            _verts.Add(p0); _verts.Add(p1); _verts.Add(p2); _verts.Add(p3);
            _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f));
            _uvs.Add(new Vector2(1f, 1f)); _uvs.Add(new Vector2(0f, 1f));

            byte tint = (byte)(Mathf.Clamp01(heatTint) * 255f);
            byte seedByte = (byte)(Mathf.Repeat(seed, 1f) * 255f);
            _cols.Add(new Color32(tint, tint, tint, seedByte));
            _cols.Add(new Color32(tint, tint, tint, seedByte));
            _cols.Add(new Color32(tint, tint, tint, seedByte));
            _cols.Add(new Color32(tint, tint, tint, seedByte));

            _tris.Add(vi); _tris.Add(vi + 1); _tris.Add(vi + 2);
            _tris.Add(vi); _tris.Add(vi + 2); _tris.Add(vi + 3);
        }

        private void UpdateLights(FireManager fm)
        {
            if (_lights == null || _lights.Length == 0) return;

            var cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 camPos = cam != null ? cam.position
                : (PlayerStats.Instance != null ? PlayerStats.Instance.transform.position : transform.position);

            _lightTargets.Clear();
            for (int i = 0; i < _cells.Count; i++)
            {
                Vector3 c = new Vector3(_cells[i].x + 0.5f, _cells[i].y + 0.7f, _cells[i].z + 0.5f);
                _lightTargets.Add((c, fm.HeatAt(_cells[i]) / 255f));
            }
            _lightTargets.Sort((a, b) =>
                (a.pos - camPos).sqrMagnitude.CompareTo((b.pos - camPos).sqrMagnitude));

            int n = Mathf.Min(_lights.Length, _lightTargets.Count);
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                if (i < n)
                {
                    var (pos, heat) = _lightTargets[i];
                    l.transform.position = pos;
                    l.enabled = true;
                    float flick = 0.75f + 0.45f * Mathf.PerlinNoise(Time.time * 7.3f + i * 3.71f, i * 1.31f);
                    l.intensity = lightIntensity * Mathf.Max(0.15f, heat) * flick;
                }
                else
                {
                    l.enabled = false;
                }
            }
        }
    }
}
