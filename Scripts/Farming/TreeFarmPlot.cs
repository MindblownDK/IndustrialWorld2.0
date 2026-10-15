// Assets/Scripts/Farming/TreeFarmPlot.cs
//
// Wide 3x1x3 arboricultural tree planter plot designed so a full-sized growing
// timber tree actually fits with proper trunk and multi-tier canopy clearance.
// Supports manual planting/harvesting (RMB with Tree Sapling) and automatic
// harvesting + replanting by nearby Harvesters or Automated Tree Farms.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Fluids;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    [RequireComponent(typeof(PlacedBlock))]
    public class TreeFarmPlot : MonoBehaviour, IMachineProcessState
    {
        [Header("Tree Growth State")]
        public bool isPlanted = true;
        [Range(0f, 1f)] public float growthProgress;
        public bool isIrrigated;
        public bool isFertilized;

        [Header("Growth & Harvest Tuning")]
        [Tooltip("Seconds from sapling to full-grown timber tree (before irrigation/fertilizer bonuses).")]
        public float baseGrowthSeconds = 110f;
        [Tooltip("Growth speed multiplier when irrigated by a Sprinkler, WaterPipe, or nearby water.")]
        public float irrigatedSpeedMultiplier = 2.0f;
        [Tooltip("Growth speed multiplier when fertilized with Biomass.")]
        public float fertilizedSpeedMultiplier = 1.5f;
        public int woodLogsPerHarvest = 6;
        public int saplingsPerHarvest = 2;
        public int biomassPerHarvest = 2;

        [Header("Product References (auto-resolved if null)")]
        public ItemDefinition woodLogItem;
        public ItemDefinition saplingItem;
        public ItemDefinition biomassItem;

        private float _irrigCheckTimer;
        private Transform _treeRoot;
        private MeshRenderer _soilRenderer;
        private static readonly Collider[] OverlapBuf = new Collider[32];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public bool IsMature => isPlanted && growthProgress >= 1f;
        public bool HasTree => isPlanted;
        public bool IsIrrigated => isIrrigated;
        public string GrowthStageLabel => StageLabel;

        public float baseGrowTimeSeconds
        {
            get => baseGrowthSeconds;
            set => baseGrowthSeconds = value;
        }

        public static bool IsSaplingOrTreeSeed(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            if (id.Contains("sapling") || id.Contains("tree_seed") || item.displayName == "Tree Sapling")
                return true;
            return item is SeedItem si && si.crop == null;
        }

        public static bool IsFertilizerOrBiomass(ItemDefinition item)
        {
            if (item == null) return false;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            return id == "item_biomass" || id == "biomass" || id.Contains("fertilizer")
                || item.displayName == "Biomass" || item.displayName == "Organic Fertilizer";
        }

        public string StageLabel
        {
            get
            {
                if (!isPlanted) return "Empty Plot (Plant Tree Sapling)";
                if (growthProgress >= 1f) return "Mature Timber Tree (Ready to Harvest)";
                if (growthProgress >= 0.66f) return "Juvenile Canopy";
                if (growthProgress >= 0.33f) return "Young Sapling";
                return "Sprouting Sapling";
            }
        }

        private void Awake()
        {
            ResolveDefaultItems();
            EnsurePlanterVisuals();
            UpdateTreeGrowthVisuals();
        }

        private void OnEnable()
        {
            ResolveDefaultItems();
            EnsurePlanterVisuals();
            UpdateTreeGrowthVisuals();
        }

        private void Update()
        {
            _irrigCheckTimer += Time.deltaTime;
            if (_irrigCheckTimer >= 2.0f)
            {
                _irrigCheckTimer = 0f;
                CheckIrrigation();
                UpdateSoilColor();
            }

            if (!isPlanted) return;

            if (growthProgress < 1f)
            {
                float speed = 1f / Mathf.Max(10f, baseGrowthSeconds);
                if (isIrrigated) speed *= Mathf.Max(1f, irrigatedSpeedMultiplier);
                if (isFertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);

                growthProgress = Mathf.Clamp01(growthProgress + speed * Time.deltaTime);
                UpdateTreeGrowthVisuals();
            }
        }

        public bool TryPlant()
        {
            if (isPlanted) return false;
            isPlanted = true;
            growthProgress = 0f;
            isFertilized = false;
            UpdateTreeGrowthVisuals();
            return true;
        }

        public bool TryPlant(ItemDefinition sapling)
        {
            if (!IsSaplingOrTreeSeed(sapling)) return false;
            return TryPlant();
        }

        public bool TryApplyFertilizer()
        {
            if (!isPlanted || isFertilized || growthProgress >= 1f) return false;
            isFertilized = true;
            UpdateSoilColor();
            return true;
        }

        public bool TryApplyFertilizer(ItemDefinition fertItem)
        {
            if (!IsFertilizerOrBiomass(fertItem)) return false;
            if (isFertilized || growthProgress >= 1f) return false;
            isFertilized = true;
            UpdateSoilColor();
            return true;
        }

        public bool TryHarvest(Inventory inv, out int logs, out int saps, out int bio)
        {
            logs = 0; saps = 0; bio = 0;
            if (!IsMature || inv == null) return false;
            ResolveDefaultItems();

            int fertBonus = isFertilized ? 2 : 0;
            logs = Mathf.Max(1, woodLogsPerHarvest + fertBonus);
            saps = Mathf.Max(1, saplingsPerHarvest);
            bio  = Mathf.Max(1, biomassPerHarvest + (isFertilized ? 1 : 0));

            if (woodLogItem != null) inv.Add(woodLogItem, logs);
            if (saplingItem != null) inv.Add(saplingItem, saps);
            if (biomassItem != null) inv.Add(biomassItem, bio);

            isPlanted = false;
            growthProgress = 0f;
            isFertilized = false;
            UpdateTreeGrowthVisuals();
            return true;
        }

        /// <summary>Harvests the mature tree into the player's inventory (RMB interaction).</summary>
        public bool TryHarvestToInventory(Inventory inv, bool replant = false)
        {
            if (!IsMature || inv == null) return false;
            ResolveDefaultItems();

            if (woodLogItem != null) inv.Add(woodLogItem, Mathf.Max(1, woodLogsPerHarvest));
            if (biomassItem != null) inv.Add(biomassItem, Mathf.Max(1, biomassPerHarvest));

            int saplingReturn = Mathf.Max(1, saplingsPerHarvest);
            if (replant && saplingReturn > 0) saplingReturn--;
            if (saplingItem != null && saplingReturn > 0)
                inv.Add(saplingItem, saplingReturn);

            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Harvested Timber Tree",
                $"+{woodLogsPerHarvest} Wood Log, +{biomassPerHarvest} Biomass" + (replant ? " (Replanted)" : $", +{saplingsPerHarvest} Saplings"),
                woodLogItem != null ? woodLogItem.icon : null,
                new Color(0.40f, 0.82f, 0.32f));

            isPlanted = replant;
            growthProgress = 0f;
            isFertilized = false;
            UpdateTreeGrowthVisuals();
            return true;
        }

        /// <summary>Harvests the mature tree for an automated Harvester or AutoTreeFarm.</summary>
        public bool TryAutomatedHarvest(
            out ItemDefinition logOut, out int logCount,
            out ItemDefinition bioOut, out int bioCount,
            out ItemDefinition saplingOut, out int saplingCount,
            bool autoReplant = true)
        {
            logOut = null; logCount = 0;
            bioOut = null; bioCount = 0;
            saplingOut = null; saplingCount = 0;

            if (!IsMature) return false;
            ResolveDefaultItems();

            logOut = woodLogItem;
            logCount = Mathf.Max(1, woodLogsPerHarvest);
            bioOut = biomassItem;
            bioCount = Mathf.Max(1, biomassPerHarvest);
            saplingOut = saplingItem;
            saplingCount = autoReplant ? Mathf.Max(0, saplingsPerHarvest - 1) : Mathf.Max(1, saplingsPerHarvest);

            isPlanted = autoReplant;
            growthProgress = 0f;
            isFertilized = false;
            UpdateTreeGrowthVisuals();
            return true;
        }

        private void CheckIrrigation()
        {
            isIrrigated = false;

            // 1. Check nearby Sprinklers.
            var sprinklers = FindObjectsByType<Sprinkler>(FindObjectsInactive.Exclude);
            for (int i = 0; i < sprinklers.Length; i++)
            {
                var s = sprinklers[i];
                if (s == null || !s.IsActive) continue;
                if ((s.transform.position - transform.position).sqrMagnitude <= s.radius * s.radius)
                {
                    isIrrigated = true;
                    return;
                }
            }

            // 2. Check nearby WaterPipe / WaterTank nodes or AutoTreeFarm within 5m.
            int hits = Physics.OverlapSphereNonAlloc(transform.position, 5.0f, OverlapBuf, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits; i++)
            {
                var col = OverlapBuf[i];
                OverlapBuf[i] = null;
                if (col == null) continue;
                var autoFarm = col.GetComponentInParent<AutoTreeFarm>();
                if (autoFarm != null && autoFarm.IsIrrigated)
                {
                    isIrrigated = true;
                    return;
                }
                var node = col.GetComponentInParent<FluidNode>();
                if (node != null && node.network != null)
                {
                    foreach (var n in node.network.nodes)
                    {
                        if (n is WaterTank wt && wt.liquidType == LiquidType.Water && wt.water > 1f)
                        {
                            isIrrigated = true;
                            return;
                        }
                    }
                }
            }

            // 3. Check voxel water nearby.
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return;
            var pos = world.WorldToVoxel(transform.position);
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                var wv = world.GetVoxelWorld(new Vector3Int(pos.x + dx, pos.y - 1, pos.z + dz));
                if (wv.waterLevel > 0
                    || wv.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel
                    || wv.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid)
                {
                    isIrrigated = true;
                    return;
                }
            }
        }

        public void ResolveDefaultItems()
        {
            if (woodLogItem != null && saplingItem != null && biomassItem != null) return;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                string id = it.itemId ?? "";
                if (woodLogItem == null && (id == "item_wood_log" || id == "wood_log" || it.displayName == "Wood Log"))
                    woodLogItem = it;
                else if (saplingItem == null && (id == "seed_tree_sapling" || id == "item_tree_sapling" || it.displayName == "Tree Sapling"))
                    saplingItem = it;
                else if (biomassItem == null && (id == "item_biomass" || id == "biomass" || it.displayName == "Biomass"))
                    biomassItem = it;
            }
        }

        // ── Procedural 3x1x3 Tree Planter Bed & Growing Tree Visuals ───────────
        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_TreePlotVisuals");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
            EnsurePlanterVisuals();
            UpdateTreeGrowthVisuals();
        }

        public void EnsurePlanterVisuals()
        {
            var rootMr = GetComponent<MeshRenderer>();
            if (rootMr != null) rootMr.enabled = false;

            var existing = transform.Find("Generated_TreePlotVisuals");
            if (existing != null)
            {
                _treeRoot = existing.Find("GrowingTreeRoot");
                var soilTf = existing.Find("SoilBasin");
                if (soilTf != null) _soilRenderer = soilTf.GetComponent<MeshRenderer>();
                return;
            }

            var root = new GameObject("Generated_TreePlotVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color frameSteel = new Color(0.22f, 0.25f, 0.28f);
            Color timberCurb = new Color(0.42f, 0.28f, 0.16f);
            Color richSoil = new Color(0.28f, 0.20f, 0.12f);
            Color waterChannel = new Color(0.18f, 0.52f, 0.78f);
            Color trunkWood = new Color(0.40f, 0.26f, 0.14f);
            Color foliageMid = new Color(0.20f, 0.56f, 0.22f);
            Color foliageBright = new Color(0.28f, 0.68f, 0.26f);

            // 3m x 3m raised planter bed foundation & rich deep-root soil basin
            MakePart(root, PrimitiveType.Cube, "PlanterBase", new Vector3(0f, -0.34f, 0f), new Vector3(2.90f, 0.28f, 2.90f), Vector3.zero, frameSteel);
            var soilGo = MakePart(root, PrimitiveType.Cube, "SoilBasin", new Vector3(0f, -0.16f, 0f), new Vector3(2.56f, 0.16f, 2.56f), Vector3.zero, richSoil);
            _soilRenderer = soilGo.GetComponent<MeshRenderer>();

            // Raised timber/steel perimeter curbs around the 3x3 plot
            MakePart(root, PrimitiveType.Cube, "Curb_N", new Vector3(0f, -0.12f, 1.36f), new Vector3(2.92f, 0.22f, 0.18f), Vector3.zero, timberCurb);
            MakePart(root, PrimitiveType.Cube, "Curb_S", new Vector3(0f, -0.12f, -1.36f), new Vector3(2.92f, 0.22f, 0.18f), Vector3.zero, timberCurb);
            MakePart(root, PrimitiveType.Cube, "Curb_E", new Vector3(1.36f, -0.12f, 0f), new Vector3(0.18f, 0.22f, 2.56f), Vector3.zero, timberCurb);
            MakePart(root, PrimitiveType.Cube, "Curb_W", new Vector3(-1.36f, -0.12f, 0f), new Vector3(0.18f, 0.22f, 2.56f), Vector3.zero, timberCurb);

            // Corner structural bollards
            MakePart(root, PrimitiveType.Cube, "Post_NE", new Vector3(1.36f, -0.06f, 1.36f), new Vector3(0.24f, 0.34f, 0.24f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "Post_NW", new Vector3(-1.36f, -0.06f, 1.36f), new Vector3(0.24f, 0.34f, 0.24f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "Post_SE", new Vector3(1.36f, -0.06f, -1.36f), new Vector3(0.24f, 0.34f, 0.24f), Vector3.zero, frameSteel);
            MakePart(root, PrimitiveType.Cube, "Post_SW", new Vector3(-1.36f, -0.06f, -1.36f), new Vector3(0.24f, 0.34f, 0.24f), Vector3.zero, frameSteel);

            // Recessed irrigation ring channels inside the soil bed
            MakePart(root, PrimitiveType.Cube, "Irrig_N", new Vector3(0f, -0.07f, 0.92f), new Vector3(1.90f, 0.03f, 0.10f), Vector3.zero, waterChannel);
            MakePart(root, PrimitiveType.Cube, "Irrig_S", new Vector3(0f, -0.07f, -0.92f), new Vector3(1.90f, 0.03f, 0.10f), Vector3.zero, waterChannel);
            MakePart(root, PrimitiveType.Cube, "Irrig_E", new Vector3(0.92f, -0.07f, 0f), new Vector3(0.10f, 0.03f, 1.74f), Vector3.zero, waterChannel);
            MakePart(root, PrimitiveType.Cube, "Irrig_W", new Vector3(-0.92f, -0.07f, 0f), new Vector3(0.10f, 0.03f, 1.74f), Vector3.zero, waterChannel);

            // Growing tree hierarchy anchored at soil surface (y = -0.08)
            _treeRoot = new GameObject("GrowingTreeRoot").transform;
            _treeRoot.SetParent(root, false);
            _treeRoot.localPosition = new Vector3(0f, -0.08f, 0f);
            _treeRoot.localRotation = Quaternion.identity;

            // Full-scale timber tree geometry (scaled by growthProgress 0.18..1.0):
            // Fits naturally inside the 3m x 3m planter with a 3.4m tall trunk + multi-lobe canopy!
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Trunk_Lower", new Vector3(0f, 0.85f, 0f), new Vector3(0.42f, 0.85f, 0.42f), Vector3.zero, trunkWood);
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Trunk_Upper", new Vector3(0f, 1.90f, 0f), new Vector3(0.30f, 0.65f, 0.30f), Vector3.zero, trunkWood);
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Branch_L", new Vector3(-0.34f, 1.85f, 0.12f), new Vector3(0.16f, 0.42f, 0.16f), new Vector3(10f, 0f, 36f), trunkWood);
            MakePart(_treeRoot, PrimitiveType.Cylinder, "Branch_R", new Vector3(0.36f, 1.95f, -0.10f), new Vector3(0.16f, 0.42f, 0.16f), new Vector3(-8f, 0f, -34f), trunkWood);

            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_Main", new Vector3(0f, 2.75f, 0f), new Vector3(2.10f, 1.55f, 2.10f), Vector3.zero, foliageMid);
            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_Crown", new Vector3(0f, 3.42f, 0f), new Vector3(1.45f, 1.10f, 1.45f), Vector3.zero, foliageBright);
            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_West", new Vector3(-0.62f, 2.40f, 0.24f), new Vector3(1.25f, 1.05f, 1.25f), Vector3.zero, foliageBright);
            MakePart(_treeRoot, PrimitiveType.Sphere, "Canopy_East", new Vector3(0.64f, 2.48f, -0.20f), new Vector3(1.25f, 1.05f, 1.25f), Vector3.zero, foliageMid);
        }

        public void UpdateTreeGrowthVisuals()
        {
            if (_treeRoot == null)
            {
                var vis = transform.Find("Generated_TreePlotVisuals");
                if (vis != null) _treeRoot = vis.Find("GrowingTreeRoot");
            }
            if (_treeRoot == null) return;

            if (!isPlanted)
            {
                _treeRoot.gameObject.SetActive(false);
                return;
            }

            _treeRoot.gameObject.SetActive(true);
            float t = Mathf.Clamp01(growthProgress);
            float xzScale = Mathf.Lerp(0.18f, 1.0f, t);
            float yScale = Mathf.Lerp(0.16f, 1.0f, t);
            _treeRoot.localScale = new Vector3(xzScale, yScale, xzScale);
        }

        private void UpdateSoilColor()
        {
            if (_soilRenderer == null || !Application.isPlaying) return;
            Color c = isIrrigated
                ? new Color(0.20f, 0.14f, 0.08f)
                : new Color(0.34f, 0.24f, 0.14f);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, c);
            mpb.SetColor(ColorId, c);
            _soilRenderer.SetPropertyBlock(mpb);
        }

        private static GameObject MakePart(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && Application.isPlaying)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
            return go;
        }

        // ── Persistence (IMachineProcessState) ──────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            state.progressSeconds = growthProgress;
            state.SetExtra("isPlanted", isPlanted ? 1f : 0f);
            state.SetExtra("isIrrigated", isIrrigated ? 1f : 0f);
            state.SetExtra("isFertilized", isFertilized ? 1f : 0f);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null) return;
            growthProgress = Mathf.Clamp01(state.progressSeconds);
            isPlanted = state.GetExtra("isPlanted", 1f) >= 0.5f;
            isIrrigated = state.GetExtra("isIrrigated", 0f) >= 0.5f;
            isFertilized = state.GetExtra("isFertilized", 0f) >= 0.5f;
            UpdateTreeGrowthVisuals();
        }
    }
}
