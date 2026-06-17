// Assets/Scripts/VoxelEngine/GridSystem/GridOreDetector.cs
//
// Grid Ore Detector — scans the terrain below for ore deposits and reports
// what it finds. Shows detected ore types, depths, and quantities in the UI.
// Draws power while scanning.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.GridSystem
{
    [System.Serializable]
    public struct OreDeposit
    {
        public MaterialId material;
        public Vector3Int voxelPos;
        public float depth;
        public int count;
    }

    public class GridOreDetector : GridBlock
    {
        [Header("Ore Detector")]
        public float powerDrawWatts = 100f;
        [Tooltip("Scan radius in blocks.")]
        public float scanRadius = 16f;
        [Tooltip("Maximum scan depth below the detector (blocks).")]
        public float maxScanDepth = 30f;
        [Tooltip("Seconds between scans.")]
        public float scanInterval = 2f;

        public bool IsScanning { get; private set; }
        public List<OreDeposit> DetectedOres { get; private set; } = new();

        public override float PowerDraw => (Enabled && Grid != null && Grid.HasPower) ? powerDrawWatts : 0f;

        private float _scanTimer;
        private GameObject _dish;

        public override void OnPlaced()
        {
            base.OnPlaced();
            BlockMass = 200f;
            maxHP = 400f;
            currentHP = maxHP;
            blockName = "Ore Detector";
            CreateDish();
        }

        private void Update()
        {
            bool powered = Enabled && Grid != null && Grid.HasPower;
            IsScanning = powered;

            if (!powered)
            {
                if (_dish != null) _dish.transform.Rotate(0, 0, 0);
                return;
            }

            // Rotate the dish.
            if (_dish != null)
                _dish.transform.Rotate(0, 60f * Time.deltaTime, 0);
        }

        private void FixedUpdate()
        {
            if (!Enabled || Grid == null || !Grid.HasPower) return;

            _scanTimer += Time.fixedDeltaTime;
            if (_scanTimer < scanInterval) return;
            _scanTimer = 0f;
            ScanForOres();
        }

        private void ScanForOres()
        {
            DetectedOres.Clear();
            var world = VoxelWorld.Instance;
            if (world == null) return;

            Vector3Int origin = world.WorldToVoxel(transform.position);
            int radius = Mathf.RoundToInt(scanRadius);
            int maxDepth = Mathf.RoundToInt(maxScanDepth);
            var seen = new HashSet<Vector3Int>();

            // Scan downward + radially in a sphere.
            var oreMap = new Dictionary<MaterialId, (Vector3Int pos, int depth, int count)>();

            for (int dy = 0; dy >= -maxDepth; dy--)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        if (dx * dx + dz * dz > radius * radius) continue;

                        var vp = new Vector3Int(origin.x + dx, origin.y + dy, origin.z + dz);
                        if (seen.Contains(vp)) continue;
                        seen.Add(vp);

                        var voxel = world.GetVoxelWorld(vp);
                        if (voxel.IsSolid && IsOre(voxel.material))
                        {
                            var matId = (MaterialId)voxel.material;
                            float depth = -dy; // positive = below
                            if (!oreMap.ContainsKey(matId) || oreMap[matId].depth > depth)
                            {
                                oreMap[matId] = (vp, Mathf.RoundToInt(depth), oreMap.TryGetValue(matId, out var v) ? v.count + 1 : 1);
                            }
                            else
                            {
                                oreMap[matId] = (oreMap[matId].pos, oreMap[matId].depth, oreMap[matId].count + 1);
                            }
                        }
                    }
                }
            }

            foreach (var kv in oreMap)
            {
                DetectedOres.Add(new OreDeposit
                {
                    material = kv.Key,
                    voxelPos = kv.Value.pos,
                    depth = kv.Value.depth,
                    count = kv.Value.count,
                });
            }

            // Sort by depth (shallowest first).
            DetectedOres.Sort((a, b) => a.depth.CompareTo(b.depth));
        }

        private static bool IsOre(byte material)
        {
            return material == (byte)MaterialId.Iron
                || material == (byte)MaterialId.Copper
                || material == (byte)MaterialId.Coal
                || material == (byte)MaterialId.Nickel
                || material == (byte)MaterialId.Silicon
                || material == (byte)MaterialId.Cobalt
                || material == (byte)MaterialId.Silver
                || material == (byte)MaterialId.Gold
                || material == (byte)MaterialId.Magnesium
                || material == (byte)MaterialId.Platinum
                || material == (byte)MaterialId.Uranium;
        }

        public static string OreDisplayName(MaterialId mat) => mat switch
        {
            MaterialId.Iron      => "Iron Ore",
            MaterialId.Copper    => "Copper Ore",
            MaterialId.Coal      => "Coal",
            MaterialId.Nickel    => "Nickel Ore",
            MaterialId.Silicon   => "Silicon Ore",
            MaterialId.Cobalt    => "Cobalt Ore",
            MaterialId.Silver    => "Silver Ore",
            MaterialId.Gold      => "Gold Ore",
            MaterialId.Magnesium => "Magnesium Ore",
            MaterialId.Platinum  => "Platinum Ore",
            MaterialId.Uranium   => "Uranium Ore",
            _ => mat.ToString(),
        };

        public static Color OreDisplayColor(MaterialId mat) => mat switch
        {
            MaterialId.Iron      => new Color(0.70f, 0.45f, 0.30f),
            MaterialId.Copper    => new Color(0.72f, 0.45f, 0.20f),
            MaterialId.Coal      => new Color(0.25f, 0.25f, 0.28f),
            MaterialId.Nickel    => new Color(0.70f, 0.72f, 0.68f),
            MaterialId.Silicon   => new Color(0.60f, 0.60f, 0.70f),
            MaterialId.Cobalt    => new Color(0.20f, 0.40f, 0.70f),
            MaterialId.Silver    => new Color(0.85f, 0.86f, 0.88f),
            MaterialId.Gold      => new Color(0.95f, 0.78f, 0.20f),
            MaterialId.Magnesium => new Color(0.80f, 0.80f, 0.75f),
            MaterialId.Platinum  => new Color(0.78f, 0.80f, 0.82f),
            MaterialId.Uranium   => new Color(0.30f, 0.70f, 0.25f),
            _ => Color.white,
        };

        private void CreateDish()
        {
            float cs = Grid != null ? Grid.gridSize.CellSize() : 2.5f;
            _dish = new GameObject("DetectorDish");
            _dish.transform.SetParent(transform, false);
            _dish.transform.localPosition = new Vector3(0, cs * 0.25f, 0);

            var dishMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            dishMat.color = new Color(0.5f, 0.52f, 0.57f);
            if (dishMat.HasProperty("_BaseColor")) dishMat.SetColor("_BaseColor", dishMat.color);
            dishMat.SetFloat("_Metallic", 0.8f);
            dishMat.SetFloat("_Smoothness", 0.4f);

            // Parabolic dish (flattened hemisphere).
            var dish = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dish.transform.SetParent(_dish.transform, false);
            dish.transform.localScale = new Vector3(cs * 0.5f, cs * 0.15f, cs * 0.5f);
            Object.DestroyImmediate(dish.GetComponent<Collider>());
            dish.GetComponent<Renderer>().sharedMaterial = dishMat;

            // Sensor node in the centre.
            var node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            node.transform.SetParent(_dish.transform, false);
            node.transform.localPosition = new Vector3(0, cs * 0.1f, 0);
            node.transform.localScale = Vector3.one * cs * 0.06f;
            Object.DestroyImmediate(node.GetComponent<Collider>());

            var glowMat = new Material(dishMat);
            if (glowMat.HasProperty("_EmissionColor"))
            {
                glowMat.EnableKeyword("_EMISSION");
                glowMat.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 1f));
            }
            node.GetComponent<Renderer>().sharedMaterial = glowMat;
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (_dish != null) Destroy(_dish);
        }
    }
}
