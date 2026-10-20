// Assets/Scripts/VoxelEngine/Farming/CropPlanter.cs
//
// 17.36.0-dev — the planter a base was missing.
//
// Tilled soil lives on the ground and is made with a hoe; a sapling bed is
// three metres wide and grows timber. Neither is something a player can line
// up along a corridor inside a base, which is exactly what a colony needs:
// a one-metre tray that takes a seed, waters itself from a sprinkler, and
// reports to the farm screen on the wall.
//
// It is deliberately dumb: no power, no ports, no conveyor. Soil, water and
// time. Everything else in the base is already complicated.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// A one-metre seed planter. RMB with a seed to plant, RMB when ripe to
    /// harvest. Water comes from a sprinkler or an adjacent water source.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class CropPlanter : MonoBehaviour, IFarmBed
    {
        [Header("State")]
        public CropDefinition plantedCrop;
        [Range(0f, 1f)] public float growthProgress;
        public bool isIrrigated;
        public bool isOxygenated;
        public bool isFertilized;
        public float timeSinceLastWater;

        [Header("Settings")]
        [Tooltip("Radius in metres to look for a working Sprinkler.")]
        public float irrigationCheckRadius = 6f;
        [Tooltip("Seconds between water checks. Cheap enough to keep small, slow enough to stay free.")]
        public float irrigationCheckInterval = 2f;
        [Tooltip("Growth multiplier while the tray is fertilized.")]
        public float fertilizedSpeedMultiplier = 1.5f;

        // ── IFarmBed ─────────────────────────────────────────────────────────
        public string BedName => "PLANTER";
        public string CropName => plantedCrop != null ? plantedCrop.cropName : string.Empty;
        public float Growth01 => plantedCrop != null ? Mathf.Clamp01(growthProgress) : 0f;
        public bool IsReady => plantedCrop != null && growthProgress >= 0.999f;
        public bool NeedsWater => plantedCrop != null && plantedCrop.requiresWater && !isIrrigated;
        public bool NeedsOxygen => plantedCrop != null && plantedCrop.requiresOxygen && !isOxygenated;
        public bool IsEmpty => plantedCrop == null;

        /// <summary>True while the tray is being watered. Public because the interaction
        /// prompt reads it; the field stays the serialized one.</summary>
        public bool IsIrrigated => isIrrigated;

        /// <summary>True once fertilizer has been applied to the current crop.</summary>
        public bool IsFertilized => isFertilized;

        /// <summary>"Growing", "Ripe", "Dry", "Empty" - the word the panel prints.</summary>
        public string StateLabel
        {
            get
            {
                if (plantedCrop == null) return "EMPTY";
                if (IsReady) return "READY";
                if (NeedsOxygen) return "NO O2";
                if (NeedsWater) return "DRY";
                return (Growth01 * 100f).ToString("0") + "%";
            }
        }

        private Transform _cropRoot;
        private MeshRenderer[] _cropRenderers = System.Array.Empty<MeshRenderer>();
        private int _lastStage = -1;
        private float _irrigationTimer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            isOxygenated = FarmEnvironment.HasBreathableEnclosure(transform.position);
            CacheVisuals();
            ApplyStageVisuals(force: true);
        }

        private void Update()
        {
            _irrigationTimer += Time.deltaTime;
            if (_irrigationTimer >= Mathf.Max(0.25f, irrigationCheckInterval))
            {
                _irrigationTimer = 0f;
                isIrrigated = SampleIrrigation();
                isOxygenated = FarmEnvironment.HasBreathableEnclosure(transform.position);
            }

            if (plantedCrop == null) return;

            if (!isIrrigated) timeSinceLastWater += Time.deltaTime;
            else timeSinceLastWater = 0f;

            if (plantedCrop.requiresWater && !isIrrigated
                && timeSinceLastWater > Mathf.Max(1f, plantedCrop.droughtToleranceSec * 2f))
            {
                ClearCrop();
                return;
            }

            if (growthProgress < 1f && !NeedsOxygen)
            {
                float speed = 1f / Mathf.Max(0.5f, plantedCrop.growthTime);
                if (isIrrigated) speed *= Mathf.Max(1f, plantedCrop.irrigatedSpeedMultiplier);
                else if (plantedCrop.requiresWater) speed *= 0.3f;
                if (isFertilized) speed *= Mathf.Max(1f, fertilizedSpeedMultiplier);

                growthProgress = Mathf.Clamp01(growthProgress + speed * Time.deltaTime);
                ApplyStageVisuals(force: false);
            }
        }

        // ── Planting & harvesting ────────────────────────────────────────────

        /// <summary>Plant a crop. Fails when the tray is occupied.</summary>
        public bool TryPlant(CropDefinition crop)
        {
            if (crop == null || plantedCrop != null) return false;
            plantedCrop = crop;
            growthProgress = 0f;
            timeSinceLastWater = 0f;
            isFertilized = false;
            _lastStage = -1;
            isIrrigated = SampleIrrigation();
            isOxygenated = FarmEnvironment.HasBreathableEnclosure(transform.position);
            ApplyStageVisuals(force: true);
            return true;
        }

        /// <summary>Harvest a ripe crop into <paramref name="inventory"/>.</summary>
        public bool TryHarvest(Inventory inventory)
        {
            if (!IsReady) return false;

            var crop = plantedCrop;
            int amount = Mathf.Max(1, crop.harvestAmount) + (isFertilized ? 1 : 0);

            if (crop.harvestItem != null && inventory != null)
            {
                inventory.Add(crop.harvestItem, amount);
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Harvested " + crop.cropName,
                    "+" + amount + " " + crop.harvestItem.displayName,
                    crop.harvestItem.icon,
                    new Color(0.40f, 0.80f, 0.30f));

                if (crop.foodValue > 0f)
                {
                    var stats = inventory.GetComponent<VoxelEngine.Player.PlayerStats>();
                    if (stats != null) stats.Feed(crop.foodValue);
                }
            }

            if (crop.seedItem != null && inventory != null)
                inventory.Add(crop.seedItem, Mathf.Max(0, crop.seedReturnAmount));

            ClearCrop();
            return true;
        }

        /// <summary>Apply fertilizer or biomass once per crop.</summary>
        public bool TryApplyFertilizer(ItemDefinition item)
        {
            if (item == null || isFertilized || plantedCrop == null) return false;
            if (!TreeFarmPlot.IsFertilizerOrBiomass(item)) return false;
            isFertilized = true;
            return true;
        }

        /// <summary>Empty the tray. The crop is lost, not refunded.</summary>
        public void ClearCrop()
        {
            plantedCrop = null;
            growthProgress = 0f;
            isFertilized = false;
            _lastStage = -1;
            ApplyStageVisuals(force: true);
        }

        // ── Water ────────────────────────────────────────────────────────────

        private bool SampleIrrigation()
        {
            var sprinklers = FindObjectsByType<Sprinkler>(FindObjectsInactive.Exclude);
            float radiusSq = irrigationCheckRadius * irrigationCheckRadius;
            for (int i = 0; i < sprinklers.Length; i++)
            {
                var s = sprinklers[i];
                if (s == null || !s.IsActive) continue;
                if (Vector3.SqrMagnitude(s.transform.position - transform.position) <= radiusSq) return true;
            }

            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return false;

            var pos = world.WorldToVoxel(transform.position);
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                var v = world.GetVoxelWorld(new Vector3Int(pos.x + dx, pos.y - 1, pos.z + dz));
                if (v.waterLevel > 0) return true;
                if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel) return true;
                if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid) return true;
            }
            return false;
        }

        // ── Visuals ──────────────────────────────────────────────────────────
        /// <summary>
        /// Rebuild the tray and the plant. Called by the editor setup before the
        /// prefab is saved, so a built planter never creates primitives at play
        /// time - the geometry is already in the prefab.
        /// </summary>
        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_PlanterVisuals");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            if (!Application.isPlaying) BuildVisuals();
            CacheVisuals();
            ApplyStageVisuals(force: true);
        }

        private void BuildVisuals()
        {
            var root = new GameObject("Generated_PlanterVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color frameSteel = new Color(0.24f, 0.27f, 0.30f);
            Color curbTimber = new Color(0.42f, 0.28f, 0.16f);
            Color richSoil = new Color(0.26f, 0.17f, 0.10f);
            Color waterChannel = new Color(0.20f, 0.62f, 0.88f);
            Color cropGreen = new Color(0.30f, 0.66f, 0.28f);

            MakePart(root, PrimitiveType.Cube, "PlanterFrame",
                new Vector3(0f, 0.10f, 0f), new Vector3(0.96f, 0.20f, 0.96f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "SoilBed",
                new Vector3(0f, 0.24f, 0f), new Vector3(0.78f, 0.10f, 0.78f), richSoil);

            // Timber curbs, so a row of trays reads as a bed rather than as boxes.
            MakePart(root, PrimitiveType.Cube, "Curb_N", new Vector3(0f, 0.26f, 0.42f), new Vector3(0.96f, 0.14f, 0.10f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_S", new Vector3(0f, 0.26f, -0.42f), new Vector3(0.96f, 0.14f, 0.10f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_E", new Vector3(0.42f, 0.26f, 0f), new Vector3(0.10f, 0.14f, 0.78f), curbTimber);
            MakePart(root, PrimitiveType.Cube, "Curb_W", new Vector3(-0.42f, 0.26f, 0f), new Vector3(0.10f, 0.14f, 0.78f), curbTimber);

            // Irrigation strip along the inside of the tray.
            MakePart(root, PrimitiveType.Cube, "Irrig_Strip",
                new Vector3(0f, 0.30f, 0.30f), new Vector3(0.66f, 0.02f, 0.06f), waterChannel);

            var cropRoot = new GameObject("CropRoot").transform;
            cropRoot.SetParent(root, false);
            cropRoot.localPosition = new Vector3(0f, 0.29f, 0f);
            cropRoot.localRotation = Quaternion.identity;

            MakePart(cropRoot, PrimitiveType.Cylinder, "Trunk_Stalk",
                new Vector3(0f, 0.17f, 0f), new Vector3(0.05f, 0.17f, 0.05f), new Color(0.34f, 0.52f, 0.22f));
            MakePart(cropRoot, PrimitiveType.Cube, "Canopy_Leaf_A",
                new Vector3(0.07f, 0.24f, 0.02f), new Vector3(0.18f, 0.03f, 0.09f), cropGreen, new Vector3(0f, 0f, 22f));
            MakePart(cropRoot, PrimitiveType.Cube, "Canopy_Leaf_B",
                new Vector3(-0.07f, 0.30f, -0.01f), new Vector3(0.17f, 0.03f, 0.09f), cropGreen, new Vector3(0f, 0f, -20f));
            MakePart(cropRoot, PrimitiveType.Cube, "Canopy_Leaf_C",
                new Vector3(0.01f, 0.35f, 0.06f), new Vector3(0.13f, 0.03f, 0.08f), cropGreen, new Vector3(12f, 30f, 0f));

            MakePart(root, PrimitiveType.Cube, "StatusBeacon",
                new Vector3(0.42f, 0.34f, 0.42f), new Vector3(0.06f, 0.06f, 0.06f), new Color(0.52f, 0.86f, 0.22f));
        }

        private void CacheVisuals()
        {
            var vis = transform.Find("Generated_PlanterVisuals");
            _cropRoot = vis != null ? vis.Find("CropRoot") : null;
            _cropRenderers = _cropRoot != null
                ? _cropRoot.GetComponentsInChildren<MeshRenderer>(true)
                : System.Array.Empty<MeshRenderer>();
        }

        /// <summary>Squash the plant down to a seedling and tint it by growth stage.</summary>
        private void ApplyStageVisuals(bool force)
        {
            if (_cropRoot == null)
            {
                _cropRoot = transform.Find("Generated_PlanterVisuals/CropRoot");
                if (_cropRoot == null) return;
                _cropRenderers = _cropRoot.GetComponentsInChildren<MeshRenderer>(true);
            }

            if (plantedCrop == null)
            {
                _cropRoot.gameObject.SetActive(false);
                _lastStage = -1;
                return;
            }

            _cropRoot.gameObject.SetActive(true);

            int stages = Mathf.Max(1, plantedCrop.growthStages);
            int stage = Mathf.Clamp(Mathf.FloorToInt(growthProgress * stages), 0, stages - 1);
            if (!force && stage == _lastStage) return;
            _lastStage = stage;

            float scale = 1f;
            if (plantedCrop.stageScales != null && plantedCrop.stageScales.Length > 0)
                scale = plantedCrop.stageScales[Mathf.Clamp(stage, 0, plantedCrop.stageScales.Length - 1)];
            _cropRoot.localScale = new Vector3(
                Mathf.Max(0.05f, scale),
                Mathf.Max(0.05f, scale),
                Mathf.Max(0.05f, scale));

            if (plantedCrop.stageColors == null || plantedCrop.stageColors.Length == 0) return;
            Color tint = plantedCrop.stageColors[Mathf.Clamp(stage, 0, plantedCrop.stageColors.Length - 1)];

            var block = new MaterialPropertyBlock();
            for (int i = 0; i < _cropRenderers.Length; i++)
            {
                var r = _cropRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, tint);
                block.SetColor(ColorId, tint);
                r.SetPropertyBlock(block);
            }
        }

        private static void MakePart(Transform parent, PrimitiveType type, string name,
            Vector3 pos, Vector3 scale, Color color, Vector3? euler = null)
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
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;

            // Tint is applied through a property block so every planter shares one
            // material asset instead of each tray owning a copy of it.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && Application.isPlaying)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
        }
    }
}
