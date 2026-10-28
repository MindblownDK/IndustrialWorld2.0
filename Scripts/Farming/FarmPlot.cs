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
    public class FarmPlot : MonoBehaviour, IFarmBed
    {
        [Header("State")]
        public CropDefinition plantedCrop;
        public float growthProgress;       // 0..1 (1 = harvestable)
        public bool isIrrigated;
        public bool isOxygenated;
        public float timeSinceLastWater;

        [Tooltip("True when the crop on this plot was lost to drought. A dead plot keeps its " +
                 "crop visible, cannot be harvested, and cannot be replanted until it is cleared.")]
        public bool isDead;

        [Header("Settings")]
        public float irrigationCheckRadius = 6f;
        public float irrigationCheckInterval = 2f;

        [Header("Auto Farm Plot (18.3.0-dev)")]
        [Tooltip("Powered plots harvest and transfer on their own. Tilled Soil leaves this off and stays a manual plot.")]
        public bool automatic = false;
        [Tooltip("Empty a ripe plot into a belt, chute, funnel or item pipe standing next to it.")]
        public bool autoDumpWhenRipe = false;
        [Tooltip("Keep one seed of each port harvest back and replant the plot, so a watered plot cycles by itself.")]
        public bool replantAfterPortHarvest = true;
        [Tooltip("How far the plot reaches for a route.")]
        public float dumpRadius = 1.7f;
        [Tooltip("Seconds between attempts to empty a ripe plot.")]
        public float dumpIntervalSeconds = 1f;

        [Header("Water (18.3.0-dev)")]
        [Tooltip("Litres this plot holds when no sprinkler is feeding it. Zero on Tilled Soil keeps it sprinkler-only.")]
        public float waterCapacity = 0f;
        public float waterStored = 0f;
        [Tooltip("Litres drunk per second by a growing crop from the plot's own tank.")]
        public float litresPerSecond = 0.02f;

        // ── IFarmBed (17.36.0-dev) ───────────────────────────────────────────
        // Soil was the only bed a screen could see. It now reports through the
        // same interface as the planters, so one page lists all of them.
        public string BedName => "SOIL";
        public string CropName => plantedCrop != null ? plantedCrop.cropName : string.Empty;
        public float Growth01 => plantedCrop != null ? Mathf.Clamp01(growthProgress) : 0f;
        public bool IsReady => plantedCrop != null && !isDead && growthProgress >= 0.999f;
        public bool NeedsWater => plantedCrop != null && !isDead && plantedCrop.requiresWater && !isIrrigated;
        public bool NeedsOxygen => plantedCrop != null && !isDead && plantedCrop.requiresOxygen && !isOxygenated;
        public bool IsEmpty => plantedCrop == null;
        public bool IsDead => isDead && plantedCrop != null;

        /// <summary>0..1 hydration the soil is holding: full while watered, then falling over
        /// the crop's own drought tolerance, so a screen can draw a level instead of a guess.</summary>
        public float Water01
        {
            get
            {
                if (plantedCrop == null) return 0f;
                if (isDead) return 0f;
                if (isIrrigated) return 1f;
                if (waterCapacity > 0f) return Mathf.Clamp01(waterStored / waterCapacity);
                float tolerance = Mathf.Max(1f, plantedCrop.droughtToleranceSec * 2f);
                return Mathf.Clamp01(1f - timeSinceLastWater / tolerance);
            }
        }

        /// <summary>Pour water in by hand with a canister. Returns the litres accepted.</summary>
        public float TryAddWater(float litres)
        {
            if (litres <= 0f || waterCapacity <= 0f) return 0f;
            float added = Mathf.Min(Mathf.Max(0f, waterCapacity - waterStored), litres);
            waterStored += added;
            return added;
        }

        /// <summary>True when a harvest would succeed right now.</summary>
        public bool CanHarvestNow => IsReady;

        // Visual
        private GameObject _cropVisual;
        private MeshRenderer _soilRenderer;
        private MeshRenderer _cropRenderer;
        private int _lastStage = -1;
        private float _irrigCheckTimer;
        private float _dumpTimer;

        private void Awake()
        {
            _soilRenderer = GetComponent<MeshRenderer>();
            isOxygenated = FarmEnvironment.HasBreathableAir(transform.position);
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
                isOxygenated = FarmEnvironment.HasBreathableAir(transform.position);
            }

            if (plantedCrop == null) return;

            // A lost crop does not grow back, whatever the weather does next.
            if (isDead)
            {
                UpdateSoilColor();
                return;
            }

            // Track drought.
            if (!isIrrigated)
            {
                timeSinceLastWater += Time.deltaTime;
                if (plantedCrop.requiresWater &&
                    timeSinceLastWater > plantedCrop.droughtToleranceSec * 2f)
                {
                    // Crop dies from drought. It stays on the plot as a brown, wilted
                    // husk so the player can see which plot was lost and clear it; a plot
                    // that silently emptied itself read as "nothing was ever planted".
                    KillCrop();
                    return;
                }
            }
            else
            {
                timeSinceLastWater = 0f;
            }

            // Crop growth pauses until the bed has both its required water and breathable
            // air - a sealed room, or the world outside when the world has air. The same
            // rule the planting boxes and the grid plots use, so a plot on Earth does not
            // stop for want of an enclosure the planet is already providing.
            if (growthProgress < 1f && !NeedsOxygen)
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

            if (automatic && autoDumpWhenRipe) TryDumpToPort();
        }

        /// <summary>
        /// Empty a ripe plot into whatever stands next to it. The plot only releases a crop
        /// once the route can take the whole load, and it keeps one seed back to replant
        /// when <see cref="replantAfterPortHarvest"/> is on - the same two rules every other
        /// automated bed follows.
        /// </summary>
        private void TryDumpToPort()
        {
            if (!CanHarvestNow) return;
            if (VoxelEngine.Networking.NetworkSession.SimulationIsRemote) return;

            _dumpTimer += Time.deltaTime;
            if (_dumpTimer < Mathf.Max(0.1f, dumpIntervalSeconds)) return;
            _dumpTimer = 0f;

            var peek = PeekHarvest();
            if (peek.IsEmpty) return;

            bool selfReplant = replantAfterPortHarvest && peek.seedItem != null && peek.seedCount > 0;
            int seedsLeaving = selfReplant ? peek.seedCount - 1 : peek.seedCount;

            if (peek.item != null && !FarmItemRouter.HasRoomFor(transform, dumpRadius, peek.item, peek.count)) return;
            if (seedsLeaving > 0 && peek.seedItem != null
                && !FarmItemRouter.HasRoomFor(transform, dumpRadius, peek.seedItem, seedsLeaving)) return;

            if (!TakeHarvest(selfReplant, out var harvest)) return;

            if (harvest.item != null && harvest.count > 0)
                FarmItemRouter.Push(transform, dumpRadius, harvest.item, harvest.count);
            if (seedsLeaving > 0 && harvest.seedItem != null)
                FarmItemRouter.Push(transform, dumpRadius, harvest.seedItem, seedsLeaving);
        }

        /// <summary>Plant a seed on this plot.</summary>
        public bool TryPlant(CropDefinition crop)
        {
            if (plantedCrop != null) return false; // already planted (a dead crop must be cleared first)
            plantedCrop = crop;
            growthProgress = 0f;
            isDead = false;
            timeSinceLastWater = 0f;
            isOxygenated = FarmEnvironment.HasBreathableAir(transform.position);
            _lastStage = -1;
            CreateCropVisual();
            return true;
        }

        /// <summary>What a harvest would give up right now, without taking it.</summary>
        public FarmHarvestYield PeekHarvest()
        {
            if (!CanHarvestNow) return default;
            return FarmHarvestYield.Crop(
                plantedCrop.harvestItem,
                Mathf.Max(1, plantedCrop.harvestAmount),
                plantedCrop.seedItem,
                Mathf.Max(0, plantedCrop.seedReturnAmount));
        }

        /// <summary>
        /// Take the crop off the plot. <paramref name="replant"/> keeps the same crop with its
        /// growth reset, otherwise the plot is emptied. This and <see cref="TryHarvest"/> are
        /// the only two ways a crop leaves a plot - an automated harvester used to write
        /// growthProgress directly, which is how soil and trays disagreed on the yield.
        /// </summary>
        public bool TakeHarvest(bool replant, out FarmHarvestYield harvest)
        {
            harvest = default;
            if (!CanHarvestNow) return false;

            harvest = PeekHarvest();

            if (replant)
            {
                growthProgress = 0f;
                timeSinceLastWater = 0f;
                _lastStage = -1;
                UpdateCropVisual();
            }
            else
            {
                ClearCrop();
            }
            return true;
        }

        /// <summary>Harvest the mature crop. Returns items to the player.</summary>
        public bool TryHarvest(Inventory inv)
        {
            var crop = plantedCrop;
            if (crop == null || !TakeHarvest(false, out var harvest)) return false;

            if (harvest.item != null && inv != null)
            {
                inv.Add(harvest.item, harvest.count);
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    $"Harvested {crop.cropName}",
                    $"+{harvest.count} {harvest.item.displayName}",
                    harvest.item.icon,
                    new Color(0.40f, 0.80f, 0.30f));

                // Feed the player if there's food value.
                if (crop.foodValue > 0)
                {
                    var stats = inv.GetComponent<VoxelEngine.Player.PlayerStats>();
                    if (stats != null) stats.Feed(crop.foodValue);
                }
            }
            if (harvest.seedItem != null && harvest.seedCount > 0 && inv != null)
                inv.Add(harvest.seedItem, harvest.seedCount);

            return true;
        }

        /// <summary>Water this plot manually (bucket or sprinkler).</summary>
        public void Water()
        {
            isIrrigated = true;
            timeSinceLastWater = 0f;
            UpdateSoilColor();
        }

        /// <summary>Empty the plot. A live crop is lost, not refunded; a dead one is simply cleared.</summary>
        public void ClearCrop()
        {
            plantedCrop = null;
            growthProgress = 0f;
            isDead = false;
            timeSinceLastWater = 0f;
            _lastStage = -1;
            if (_cropVisual != null) Destroy(_cropVisual);
            _cropVisual = null;
            UpdateSoilColor();
        }

        /// <summary>Lose the crop to drought. The plot keeps it, brown and wilted, so the
        /// player can see which plot died and clear it themselves.</summary>
        private void KillCrop()
        {
            if (plantedCrop == null || isDead) return;
            isDead = true;
            Debug.Log($"[Farm] {plantedCrop.cropName} died from drought on tilled soil.");
            _lastStage = -1;
            UpdateCropVisual(force: true);
            UpdateSoilColor();
        }

        // ── Irrigation Detection ─────────────────────────────────────
        private void CheckIrrigation()
        {
            isIrrigated = false;

            // A plot that holds its own water is irrigated until the tank runs out, which
            // is what makes a hand-watered Auto Farm Plot worth building before plumbing.
            if (waterCapacity > 0f && waterStored > 0.005f)
            {
                if (plantedCrop != null)
                    waterStored = Mathf.Max(0f, waterStored - litresPerSecond * Mathf.Max(1f, irrigationCheckInterval));
                isIrrigated = true;
                return;
            }


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
            var world = VoxelEngine.Core.ActiveWorld.Current;
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

        private void UpdateCropVisual(bool force = false)
        {
            if (plantedCrop == null || _cropVisual == null) return;

            // A lost crop is drawn brown and collapsed, so a dead plot never looks like a
            // ripe one from across the base.
            if (isDead)
            {
                if (!force && _lastStage == DeadStage) return;
                _lastStage = DeadStage;
                _cropVisual.transform.localScale = new Vector3(0.24f, 0.08f, 0.24f);
                _cropVisual.transform.localPosition = new Vector3(0, 0.14f, 0);
                TintCropVisual(new Color(0.42f, 0.31f, 0.18f));
                return;
            }

            int stage = Mathf.FloorToInt(growthProgress * (plantedCrop.growthStages - 1));
            stage = Mathf.Clamp(stage, 0, plantedCrop.growthStages - 1);

            if (!force && stage == _lastStage) return;
            _lastStage = stage;

            // Scale
            float scaleY = 0.3f;
            if (plantedCrop.stageScales != null && stage < plantedCrop.stageScales.Length)
                scaleY = plantedCrop.stageScales[stage];
            _cropVisual.transform.localScale = new Vector3(0.3f, scaleY, 0.3f);
            _cropVisual.transform.localPosition = new Vector3(0, scaleY * 0.5f + 0.1f, 0);

            // Color
            if (plantedCrop.stageColors != null && stage < plantedCrop.stageColors.Length)
                TintCropVisual(plantedCrop.stageColors[stage]);
        }

        /// <summary>Sentinel for "the visual is currently showing a lost crop".</summary>
        private const int DeadStage = -2;

        private void TintCropVisual(Color c)
        {
            if (_cropRenderer == null || _cropRenderer.material == null) return;
            _cropRenderer.material.color = c;
            if (_cropRenderer.material.HasProperty("_BaseColor"))
                _cropRenderer.material.SetColor("_BaseColor", c);
        }
    }
}
