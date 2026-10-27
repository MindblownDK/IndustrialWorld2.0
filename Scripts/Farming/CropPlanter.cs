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
//
// 18.2.0-dev — the tray joined the factory, on its own terms.
//
// The "deliberately dumb" rule above still holds for the PLAYER's half: no
// power, no panel, no per-tray switches. But a row of trays that only a person
// can empty is a row of trays that stops the moment the player walks away, so
// three things changed:
//
//   * an output port. Point a belt, chute, funnel or item pipe at the tray and
//     it empties itself when ripe - only when the route can take the whole
//     load, so a full belt never eats a crop. That is the opt-in: no toggle,
//     no UI, the belt next to it IS the switch.
//   * a real moisture reading. Water01 falls while the tray goes unwatered
//     instead of flipping between dry and full, and a crop that runs out turns
//     DEAD rather than vanishing - the player can see what happened, and the
//     tray refuses to be replanted until it is cleared.
//   * persistence. A planted tray used to come back empty after a reload; the
//     crop, its growth, its fertilizer and its loss are now saved through the
//     same additive machine-process record every other machine already uses.
//
// All of it is additive: no item id changes, no new save field on the block
// record itself, and an old save's trays simply restore as empty trays.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Simulation;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// A one-metre seed planter. RMB with a seed to plant, RMB when ripe to
    /// harvest. Water comes from a sprinkler or an adjacent water source.
    /// With a belt or pipe alongside, it also empties itself.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class CropPlanter : MonoBehaviour, IFarmBed, IMachineProcessState
    {
        [Header("State")]
        public CropDefinition plantedCrop;
        [Range(0f, 1f)] public float growthProgress;
        public bool isIrrigated;
        public bool isOxygenated;
        public bool isFertilized;
        public float timeSinceLastWater;

        [Tooltip("True when the crop on this tray was lost to drought. A dead tray keeps its " +
                 "crop visible, cannot be harvested, and cannot be replanted until it is cleared.")]
        public bool isDead;

        [Header("Settings")]
        [Tooltip("Radius in metres to look for a working Sprinkler.")]
        public float irrigationCheckRadius = 6f;
        [Tooltip("Seconds between water checks. Cheap enough to keep small, slow enough to stay free.")]
        public float irrigationCheckInterval = 2f;
        [Tooltip("Growth multiplier while the tray is fertilized.")]
        public float fertilizedSpeedMultiplier = 1.5f;

        [Header("Output Port")]
        [Tooltip("When a belt, chute, funnel or item pipe stands next to the tray, a ripe crop " +
                 "leaves the tray by itself. Turn this off to keep every harvest in the player's hands.")]
        public bool autoDumpWhenRipe = true;
        [Tooltip("How far the tray reaches for a route. One metre reaches the neighbouring cell.")]
        public float dumpRadius = 1.6f;
        [Tooltip("Seconds between attempts to empty a ripe tray.")]
        public float dumpIntervalSeconds = 1f;
        [Tooltip("Keep one seed back from each port harvest and replant the tray, so a watered " +
                 "row keeps cycling on its own. Turn this off and every harvest has to be replanted " +
                 "by hand or by a Harvester.")]
        public bool replantAfterPortHarvest = true;

        // ── IFarmBed ─────────────────────────────────────────────────────────
        public string BedName => "PLANTER";
        public string CropName => plantedCrop != null ? plantedCrop.cropName : string.Empty;
        public float Growth01 => plantedCrop != null ? Mathf.Clamp01(growthProgress) : 0f;
        public bool IsReady => plantedCrop != null && !isDead && growthProgress >= 0.999f;
        public bool NeedsWater => plantedCrop != null && !isDead && plantedCrop.requiresWater && !isIrrigated;
        public bool NeedsOxygen => plantedCrop != null && !isDead && plantedCrop.requiresOxygen && !isOxygenated;
        public bool IsEmpty => plantedCrop == null;
        public bool IsDead => isDead && plantedCrop != null;

        /// <summary>
        /// 0..1 hydration the tray is holding. Full while it is being watered, then falling
        /// over the crop's own drought tolerance so the screen draws a level rather than a lie.
        /// </summary>
        public float Water01
        {
            get
            {
                if (plantedCrop == null) return 0f;
                if (isDead) return 0f;
                if (isIrrigated) return 1f;
                float tolerance = Mathf.Max(1f, plantedCrop.droughtToleranceSec * 2f);
                return Mathf.Clamp01(1f - timeSinceLastWater / tolerance);
            }
        }

        /// <summary>True while the tray is being watered. Public because the interaction
        /// prompt reads it; the field stays the serialized one.</summary>
        public bool IsIrrigated => isIrrigated;

        /// <summary>True once fertilizer has been applied to the current crop.</summary>
        public bool IsFertilized => isFertilized;

        /// <summary>True when a harvest would succeed right now, port or player alike.</summary>
        public bool CanHarvestNow => IsReady;

        /// <summary>"Growing", "Ripe", "Dry", "Empty" - the word the panel prints.</summary>
        public string StateLabel
        {
            get
            {
                if (plantedCrop == null) return "EMPTY";
                if (isDead) return "DEAD";
                if (IsReady) return "READY";
                if (NeedsOxygen) return "NO O2";
                if (NeedsWater) return "DRY";
                return (Growth01 * 100f).ToString("0") + "%";
            }
        }

        private Transform _cropRoot;
        private MeshRenderer[] _cropRenderers = System.Array.Empty<MeshRenderer>();
        private MeshRenderer _portIndicator;
        private int _lastStage = -1;
        private float _irrigationTimer;
        private float _dumpTimer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color DeadTint = new Color(0.42f, 0.31f, 0.18f);

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

            if (isDead)
            {
                ApplyStageVisuals(force: false);
                return;
            }

            if (!isIrrigated) timeSinceLastWater += Time.deltaTime;
            else timeSinceLastWater = 0f;

            if (plantedCrop.requiresWater && !isIrrigated
                && timeSinceLastWater > Mathf.Max(1f, plantedCrop.droughtToleranceSec * 2f))
            {
                KillCrop();
                ApplyStageVisuals(force: true);
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

            TryDumpToPort();
            UpdatePortIndicator();
        }

        // ── Planting & harvesting ────────────────────────────────────────────

        /// <summary>Plant a crop. Fails when the tray is occupied - including by a lost crop,
        /// which has to be cleared first so a dead tray can never be overwritten in silence.</summary>
        public bool TryPlant(CropDefinition crop)
        {
            if (crop == null || plantedCrop != null) return false;
            plantedCrop = crop;
            growthProgress = 0f;
            timeSinceLastWater = 0f;
            isFertilized = false;
            isDead = false;
            _lastStage = -1;
            isIrrigated = SampleIrrigation();
            isOxygenated = FarmEnvironment.HasBreathableEnclosure(transform.position);
            ApplyStageVisuals(force: true);
            UpdatePortIndicator();
            return true;
        }

        /// <summary>What a harvest would give up right now, without taking it.</summary>
        public FarmHarvestYield PeekHarvest()
        {
            if (!CanHarvestNow) return default;
            var crop = plantedCrop;
            return FarmHarvestYield.Crop(
                crop.harvestItem,
                Mathf.Max(1, crop.harvestAmount) + (isFertilized ? 1 : 0),
                crop.seedItem,
                Mathf.Max(0, crop.seedReturnAmount));
        }

        /// <summary>
        /// Take the yield off the tray. <paramref name="replant"/> keeps the same crop in the
        /// tray with its growth reset (a Harvester's auto-replant), otherwise the tray is emptied.
        /// This and <see cref="TryHarvest(Inventory)"/> are the only two ways a crop leaves a tray.
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
                isFertilized = false;
                _lastStage = -1;
                ApplyStageVisuals(force: true);
            }
            else
            {
                ClearCrop();
            }

            UpdatePortIndicator();
            return true;
        }

        /// <summary>Harvest a ripe crop into <paramref name="inventory"/>.</summary>
        public bool TryHarvest(Inventory inventory)
        {
            // The crop is read before the tray is emptied - TakeHarvest clears it.
            var crop = plantedCrop;
            if (crop == null || !TakeHarvest(false, out var harvest)) return false;

            if (harvest.item != null && inventory != null)
            {
                inventory.Add(harvest.item, harvest.count);
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Harvested " + crop.cropName,
                    "+" + harvest.count + " " + harvest.item.displayName,
                    harvest.item.icon,
                    new Color(0.40f, 0.80f, 0.30f));

                if (crop.foodValue > 0f)
                {
                    var stats = inventory.GetComponent<VoxelEngine.Player.PlayerStats>();
                    if (stats != null) stats.Feed(crop.foodValue);
                }
            }

            if (harvest.seedItem != null && harvest.seedCount > 0 && inventory != null)
                inventory.Add(harvest.seedItem, harvest.seedCount);

            return true;
        }

        /// <summary>Apply fertilizer or biomass once per crop.</summary>
        public bool TryApplyFertilizer(ItemDefinition item)
        {
            if (item == null || isFertilized || plantedCrop == null || isDead) return false;
            if (!TreeFarmPlot.IsFertilizerOrBiomass(item)) return false;
            isFertilized = true;
            return true;
        }

        /// <summary>Empty the tray. A live crop is lost, not refunded; a dead one is simply cleared.</summary>
        public void ClearCrop()
        {
            plantedCrop = null;
            growthProgress = 0f;
            isFertilized = false;
            isDead = false;
            timeSinceLastWater = 0f;
            _lastStage = -1;
            ApplyStageVisuals(force: true);
            UpdatePortIndicator();
        }

        /// <summary>Lose the crop to drought. The tray keeps what it is holding so the
        /// player can see which crop was lost and clear it themselves.</summary>
        private void KillCrop()
        {
            if (plantedCrop == null || isDead) return;
            isDead = true;
            isFertilized = false;
            // The tray itself is the report: it stays loaded with a brown, wilted crop and
            // reads DEAD on the farm screen. No HUD popup here - an automated row of trays
            // losing nine crops in a drought is not nine interruptions for the player.
            Debug.Log($"[Farm] {plantedCrop.cropName} dried out on a Seed Planter and is now dead.");
        }

        // ── Output port ──────────────────────────────────────────────────────

        /// <summary>
        /// Empty a ripe tray into whatever stands next to it. Nothing leaves the tray until
        /// the route can take the whole load - a full belt is a wait, not a lost harvest.
        ///
        /// When <see cref="replantAfterPortHarvest"/> is on, one seed of the harvest stays in
        /// the tray as the next planting, so a watered row with a belt against it keeps
        /// cycling with no player, no Harvester and no seed stock. That is the whole point:
        /// the tray's own yield pays for its next crop, and nothing is created.
        /// </summary>
        private void TryDumpToPort()
        {
            if (!autoDumpWhenRipe || !CanHarvestNow) return;

            // A tray empties itself on the host only; a guest's copy renders the result.
            // Growth stays local for every bed, but a port that moved items on both machines
            // would hand the session two harvests of one crop.
            if (VoxelEngine.Networking.NetworkSession.SimulationIsRemote) return;

            _dumpTimer += Time.deltaTime;
            if (_dumpTimer < Mathf.Max(0.1f, dumpIntervalSeconds)) return;
            _dumpTimer = 0f;

            var peek = PeekHarvest();
            if (peek.IsEmpty) return;

            // One seed buys the next crop when the tray replants itself; whatever is left of
            // the seed return travels with the produce.
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

        private void UpdatePortIndicator()
        {
            if (_portIndicator == null) return;

            Color tint = !autoDumpWhenRipe ? new Color(0.30f, 0.32f, 0.34f)
                : IsReady ? new Color(0.35f, 0.88f, 0.42f)
                : isDead ? new Color(0.80f, 0.35f, 0.22f)
                : new Color(0.22f, 0.44f, 0.34f);

            var block = new MaterialPropertyBlock();
            _portIndicator.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            _portIndicator.SetPropertyBlock(block);
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

        // ── Persistence ──────────────────────────────────────────────────────

        /// <summary>
        /// Write the tray's crop into the shared machine-process record: growth in
        /// progressSeconds, the crop and its seed as names, and the three switches a tray
        /// owns. Additive by construction - a save without this record restores an empty
        /// tray, exactly as it did before this round.
        /// </summary>
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;

            state.progressSeconds = plantedCrop != null ? Mathf.Clamp01(growthProgress) : 0f;
            state.SetText("crop", plantedCrop != null ? plantedCrop.name : string.Empty);
            state.SetText("cropSeed", plantedCrop != null && plantedCrop.seedItem != null
                ? plantedCrop.seedItem.itemId
                : string.Empty);
            state.SetExtra("hasCrop", plantedCrop != null ? 1f : 0f);
            state.SetExtra("isFertilized", isFertilized ? 1f : 0f);
            state.SetExtra("isDead", isDead ? 1f : 0f);
            state.SetExtra("timeSinceLastWater", Mathf.Max(0f, timeSinceLastWater));
        }

        /// <summary>Apply a saved tray back onto this one. A crop that no longer resolves
        /// leaves the tray empty rather than guessing.</summary>
        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;

            ClearCrop();

            if (state.GetExtra("hasCrop", 0f) < 0.5f)
            {
                ApplyStageVisuals(force: true);
                return;
            }

            var crop = CropCatalog.Resolve(state.GetText("crop", string.Empty), state.GetText("cropSeed", string.Empty));
            if (crop == null)
            {
                ApplyStageVisuals(force: true);
                return;
            }

            plantedCrop = crop;
            growthProgress = MachineProcessPersistence.ClampOr(state.progressSeconds, 0f, 1f, 0f);
            isFertilized = state.GetExtra("isFertilized", 0f) >= 0.5f;
            isDead = state.GetExtra("isDead", 0f) >= 0.5f;
            timeSinceLastWater = Mathf.Max(0f, state.GetExtra("timeSinceLastWater", 0f));
            _lastStage = -1;
            isIrrigated = SampleIrrigation();
            isOxygenated = FarmEnvironment.HasBreathableEnclosure(transform.position);
            ApplyStageVisuals(force: true);
            UpdatePortIndicator();
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
            UpdatePortIndicator();
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

            // Output port: a short discharge chute on the tray's +X face, with its own
            // indicator so a player can see whether it is armed, waiting or lost a crop.
            MakePart(root, PrimitiveType.Cube, "OutputPort_Chute",
                new Vector3(0.50f, 0.16f, 0f), new Vector3(0.22f, 0.14f, 0.34f), frameSteel);
            MakePart(root, PrimitiveType.Cube, "OutputPort_Lip",
                new Vector3(0.58f, 0.22f, 0f), new Vector3(0.06f, 0.02f, 0.30f), waterChannel);
            MakePart(root, PrimitiveType.Cube, "OutputPort_Indicator",
                new Vector3(0.50f, 0.25f, 0.20f), new Vector3(0.06f, 0.03f, 0.06f),
                new Color(0.22f, 0.44f, 0.34f));

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
            var indicator = vis != null ? vis.Find("OutputPort_Indicator") : null;
            _portIndicator = indicator != null ? indicator.GetComponent<MeshRenderer>() : null;
        }

        /// <summary>Squash the plant down to a seedling and tint it by growth stage. A lost
        /// crop keeps its last size but goes brown, so it reads as dead at a glance.</summary>
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

            if (isDead)
            {
                if (!force && _lastStage == -2) return;
                _lastStage = -2;
                _cropRoot.localScale = new Vector3(0.68f, 0.42f, 0.68f);
                TintCrop(DeadTint);
                return;
            }

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
            TintCrop(plantedCrop.stageColors[Mathf.Clamp(stage, 0, plantedCrop.stageColors.Length - 1)]);
        }

        private void TintCrop(Color tint)
        {
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
