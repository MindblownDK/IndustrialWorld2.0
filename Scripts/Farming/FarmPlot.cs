// Assets/Scripts/VoxelEngine/Farming/FarmPlot.cs
//
// A single tilled soil tile. Created by using the Hoe tool on terrain.
// The player plants a seed here; the crop grows over time.
// Irrigation from nearby water sources or sprinklers speeds growth.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// A tilled farm plot that can hold one crop.
    /// Place via the Hoe tool or a "Tilled Soil" block item.
    /// </summary>
    public class FarmPlot : MonoBehaviour
    {
        [Header("State")]
        public CropDefinition plantedCrop;
        public float growthProgress;       // 0..1 (1 = harvestable)
        public bool isIrrigated;
        public float timeSinceLastWater;

        [Header("Settings")]
        public float irrigationCheckRadius = 6f;
        public float irrigationCheckInterval = 2f;

        // Visual
        private GameObject _cropVisual;
        private MeshRenderer _soilRenderer;
        private MeshRenderer _cropRenderer;
        private int _lastStage = -1;
        private float _irrigCheckTimer;

        private void Awake()
        {
            _soilRenderer = GetComponent<MeshRenderer>();
            UpdateSoilColor();
        }

        private void Update()
        {
            // Periodically check for water sources.
            _irrigCheckTimer += Time.deltaTime;
            if (_irrigCheckTimer >= irrigationCheckInterval)
            {
                _irrigCheckTimer = 0f;
                CheckIrrigation();
            }

            if (plantedCrop == null) return;

            // Track drought.
            if (!isIrrigated)
            {
                timeSinceLastWater += Time.deltaTime;
                if (plantedCrop.requiresWater &&
                    timeSinceLastWater > plantedCrop.droughtToleranceSec * 2f)
                {
                    // Crop dies from drought.
                    Debug.Log($"[Farm] {plantedCrop.cropName} died from drought.");
                    ClearCrop();
                    return;
                }
            }
            else
            {
                timeSinceLastWater = 0f;
            }

            // Grow the crop.
            if (growthProgress < 1f)
            {
                float speed = 1f / plantedCrop.growthTime;
                if (isIrrigated) speed *= plantedCrop.irrigatedSpeedMultiplier;
                // Slow down if not irrigated and crop wants water.
                else if (plantedCrop.requiresWater) speed *= 0.3f;

                growthProgress += speed * Time.deltaTime;
                growthProgress = Mathf.Clamp01(growthProgress);

                UpdateCropVisual();
            }

            UpdateSoilColor();
        }

        /// <summary>Plant a seed on this plot.</summary>
        public bool TryPlant(CropDefinition crop)
        {
            if (plantedCrop != null) return false; // already planted
            plantedCrop = crop;
            growthProgress = 0f;
            timeSinceLastWater = 0f;
            _lastStage = -1;
            CreateCropVisual();
            return true;
        }

        /// <summary>Harvest the mature crop. Returns items to the player.</summary>
        public bool TryHarvest(Inventory inv)
        {
            if (plantedCrop == null || growthProgress < 1f) return false;

            if (plantedCrop.harvestItem != null && inv != null)
            {
                inv.Add(plantedCrop.harvestItem, plantedCrop.harvestAmount);
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"Harvested {plantedCrop.cropName}",
                    $"+{plantedCrop.harvestAmount} {plantedCrop.harvestItem.displayName}",
                    plantedCrop.harvestItem.icon,
                    new Color(0.40f, 0.80f, 0.30f));

                // Feed the player if there's food value.
                if (plantedCrop.foodValue > 0)
                {
                    var stats = inv.GetComponent<VoxelEngine.Player.PlayerStats>();
                    if (stats != null) stats.Feed(plantedCrop.foodValue);
                }
            }
            if (plantedCrop.seedItem != null && inv != null)
                inv.Add(plantedCrop.seedItem, plantedCrop.seedReturnAmount);

            ClearCrop();
            return true;
        }

        /// <summary>Water this plot manually (bucket or sprinkler).</summary>
        public void Water()
        {
            isIrrigated = true;
            timeSinceLastWater = 0f;
            UpdateSoilColor();
        }

        private void ClearCrop()
        {
            plantedCrop = null;
            growthProgress = 0f;
            _lastStage = -1;
            if (_cropVisual != null) Destroy(_cropVisual);
            _cropVisual = null;
            UpdateSoilColor();
        }

        // ── Irrigation Detection ─────────────────────────────────────
        private void CheckIrrigation()
        {
            isIrrigated = false;

            // Check for sprinklers in range.
            var sprinklers = FindObjectsByType<Sprinkler>(FindObjectsInactive.Exclude);
            foreach (var s in sprinklers)
            {
                if (s == null || !s.IsActive) continue;
                if (Vector3.SqrMagnitude(s.transform.position - transform.position)
                    <= s.radius * s.radius)
                {
                    isIrrigated = true;
                    return;
                }
            }

            // Check for water in the fluid sim nearby.
            var world = VoxelEngine.Core.VoxelWorld.Instance;
            if (world == null) return;
            var pos = world.WorldToVoxel(transform.position);
            // Check the 4 adjacent horizontal voxels for water.
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                var checkPos = new Vector3Int(pos.x + dx, pos.y - 1, pos.z + dz);
                // Check voxel waterLevel for irrigation (new water system).
                var wv = world.GetVoxelWorld(checkPos);
                if (wv.waterLevel > 0)
                {
                    isIrrigated = true;
                    return;
                }

                // Also check the voxel data for WaterVoxel material.
                var v = world.GetVoxelWorld(checkPos);
                if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel ||
                    v.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid)
                {
                    isIrrigated = true;
                    return;
                }
            }
        }

        // ── Visuals ──────────────────────────────────────────────────
        private void UpdateSoilColor()
        {
            if (_soilRenderer == null) return;
            Color soil = isIrrigated
                ? new Color(0.25f, 0.18f, 0.10f) // dark wet soil
                : new Color(0.45f, 0.35f, 0.22f); // dry soil
            if (_soilRenderer.material != null)
            {
                _soilRenderer.material.color = soil;
                if (_soilRenderer.material.HasProperty("_BaseColor"))
                    _soilRenderer.material.SetColor("_BaseColor", soil);
            }
        }

        private void CreateCropVisual()
        {
            if (_cropVisual != null) Destroy(_cropVisual);
            _cropVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cropVisual.name = "CropVisual";
            _cropVisual.transform.SetParent(transform);
            _cropVisual.transform.localPosition = new Vector3(0, 0.3f, 0);
            _cropVisual.transform.localScale = new Vector3(0.3f, 0.1f, 0.3f);

            // Remove collider from visual (the plot itself handles interaction).
            var col = _cropVisual.GetComponent<BoxCollider>();
            if (col != null) Destroy(col);

            _cropRenderer = _cropVisual.GetComponent<MeshRenderer>();
            if (_cropRenderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null) _cropRenderer.material = new Material(shader);
            }

            UpdateCropVisual();
        }

        private void UpdateCropVisual()
        {
            if (plantedCrop == null || _cropVisual == null) return;

            int stage = Mathf.FloorToInt(growthProgress * (plantedCrop.growthStages - 1));
            stage = Mathf.Clamp(stage, 0, plantedCrop.growthStages - 1);

            if (stage == _lastStage) return;
            _lastStage = stage;

            // Scale
            float scaleY = 0.3f;
            if (plantedCrop.stageScales != null && stage < plantedCrop.stageScales.Length)
                scaleY = plantedCrop.stageScales[stage];
            _cropVisual.transform.localScale = new Vector3(0.3f, scaleY, 0.3f);
            _cropVisual.transform.localPosition = new Vector3(0, scaleY * 0.5f + 0.1f, 0);

            // Color
            if (_cropRenderer != null && plantedCrop.stageColors != null
                && stage < plantedCrop.stageColors.Length)
            {
                Color c = plantedCrop.stageColors[stage];
                _cropRenderer.material.color = c;
                if (_cropRenderer.material.HasProperty("_BaseColor"))
                    _cropRenderer.material.SetColor("_BaseColor", c);
            }
        }
    }
}
