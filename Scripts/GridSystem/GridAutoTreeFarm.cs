// Assets/Scripts/VoxelEngine/GridSystem/GridAutoTreeFarm.cs
//
// 18.8.0-dev — Grid Automatic Tree Farm.
//
// A ship- and station-mounted arboricultural gantry that continuously cultivates
// and fells timber (Wood Log, Tree Sapling, Biomass, Wood Chips) using grid power,
// piped water from GridLiquidNetwork, and breathable air from
// FarmEnvironment.HasBreathableAir.
//
// Mirrors the ground AutoTreeFarm while using the grid networks:
//   * Starts with a dry tank (waterStored = 0 L) and draws water from connected
//     GridLiquidTanks (or a hand canister via TryAddWater).
//   * Pauses cleanly without killing the standing tree when breathable air or
//     water is missing.
//   * Self-reseeds by keeping one sapling back from each harvest, or pulls a
//     sapling from inputC / connected GridCargoContainers.
//   * Optional auto-fertilizer intake consumes 1 Biomass / Organic Fertilizer
//     per cycle for +45% growth speed and +2 Wood Log, +1 Biomass yield.
//   * Accepts 2 universal FurnaceUpgradeItem modules (Speed / Efficiency).
//   * Delivers harvested timber to connected GridCargoContainers first and its
//     own 8-slot output bin second.

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Farming;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridAutoTreeFarm : GridBlock, IGridDataProvider, IFarmBed, IMachineProcessState
    {
        public const int INPUT_SLOTS = 4;
        public const int OUTPUT_SLOTS = 8;
        public const int UPGRADE_SLOTS = 2;

        [Header("Items")]
        public ItemDefinition woodLogItem;
        public ItemDefinition saplingItem;
        public ItemDefinition biomassItem;
        public ItemDefinition woodChipsItem;

        [Header("Water Tank (L)")]
        public float waterCapacity = 400f;
        public float waterStored = 0f;
        [Tooltip("Litres per second consumed while actively growing a tree.")]
        public float waterConsumptionLps = 0.22f;
        [Tooltip("Litres per second drawn from connected grid liquid tanks to top up the internal reservoir.")]
        public float refillLitresPerSecond = 4.0f;

        [Header("Power")]
        public float basePowerDraw = 120f;
        public float idlePowerDraw = 8f;

        [Header("Cycle & Yield")]
        public float baseGrowthSeconds = 85f;
        [Tooltip("Multiplier applied to baseGrowthSeconds so a full cycle is something the player can watch.")]
        public float growthSpeedMultiplier = 0.50f;
        public float fertilizedSpeedMultiplier = 1.45f;
        public int logsPerCycle = 8;
        public int biomassPerCycle = 3;
        public int woodChipsPerCycle = 2;
        public int surplusSaplingsPerCycle = 1;

        [Header("Automation")]
        public bool replantAfterHarvest = true;
        public bool automatic = true;
        public bool autoFertilizeFromInput = true;
        public bool autoPullFromCargo = true;

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer outputC;
        public ItemContainer upgradeC;

        private bool _hasTree;
        private float _growth01;
        private bool _cycleFertilized;
        private int _treesHarvested;
        private float _scanTimer;

        private const string VisualRootName = "Generated_TreeFarmVisuals";
        private static readonly Color TrunkBark     = new Color(0.38f, 0.25f, 0.14f);
        private static readonly Color CanopyFoliage = new Color(0.22f, 0.54f, 0.22f);
        private Transform _treeVisualRoot;
        private Transform _canopyVisual;
        private Transform _sawCarriageVisual;

        // ── Readouts ──────────────────────────────────────────────────────────
        public bool HasTree => _hasTree;
        public float Growth01 => Mathf.Clamp01(_growth01);
        public bool IsReady => _hasTree && _growth01 >= 0.999f;
        public bool IsCycleFertilized => _cycleFertilized;
        public int TreesHarvested => _treesHarvested;
        public bool HasWater => waterStored > 0.01f;
        public float Water01 => waterCapacity > 0.01f ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;
        public bool HasAir => FarmEnvironment.HasBreathableAir(transform.position);
        public bool HasWaterSupply => Grid != null && GridLiquidNetwork.Instance != null
                                      && GridLiquidNetwork.Instance.GetTanks(Grid, LiquidType.Water).Count > 0;

        public float SpeedMultiplier
        {
            get
            {
                float speed = 1f;
                if (upgradeC != null)
                {
                    for (int i = 0; i < upgradeC.Size; i++)
                    {
                        var st = upgradeC.GetSlot(i);
                        if (st == null || st.IsEmpty || st.item == null) continue;
                        if (st.item is FurnaceUpgradeItem upg && upg.speedMultiplier > 0f)
                            speed *= upg.speedMultiplier;
                    }
                }
                return Mathf.Clamp(speed, 0.25f, 5f);
            }
        }

        public float EfficiencyMultiplier
        {
            get
            {
                float eff = 1f;
                if (upgradeC != null)
                {
                    for (int i = 0; i < upgradeC.Size; i++)
                    {
                        var st = upgradeC.GetSlot(i);
                        if (st == null || st.IsEmpty || st.item == null) continue;
                        if (st.item is FurnaceUpgradeItem upg && upg.efficiencyMultiplier > 0f)
                            eff *= upg.efficiencyMultiplier;
                    }
                }
                return Mathf.Clamp(eff, 0.2f, 3f);
            }
        }

        public float EffectiveCycleSeconds
        {
            get
            {
                float mul = Mathf.Max(0.1f, SpeedMultiplier);
                if (_cycleFertilized) mul *= Mathf.Max(1f, fertilizedSpeedMultiplier);
                float secs = baseGrowthSeconds * Mathf.Max(0.1f, growthSpeedMultiplier);
                return Mathf.Max(10f, secs / mul);
            }
        }

        public override float PowerDraw
        {
            get
            {
                if (!Enabled) return 0f;
                bool active = _hasTree && _growth01 < 0.999f && HasAir && HasWater;
                return active ? basePowerDraw * EfficiencyMultiplier : idlePowerDraw;
            }
        }

        public override float ContentMass => (waterStored * 1f) + (_hasTree ? 40f : 0f);

        public string StageLabel
        {
            get
            {
                if (!_hasTree) return "No Sapling";
                if (_growth01 < 0.25f) return "Seedling";
                if (_growth01 < 0.60f) return "Sapling";
                if (_growth01 < 0.90f) return "Young Tree";
                if (_growth01 < 0.999f) return "Felling";
                return "Timber Ready";
            }
        }

        public string Status
        {
            get
            {
                if (!Enabled) return "OFFLINE";
                if (Grid != null && !Grid.HasPower) return "NO POWER";
                if (!_hasTree) return "NO SAPLING";
                if (_growth01 >= 0.999f) return automatic ? "OUTPUT FULL" : "TIMBER READY";
                if (!HasAir) return "NO O2";
                if (!HasWater) return "DRY";
                return _growth01 >= 0.90f ? "FELLING" : "GROWING";
            }
        }

        // ── IFarmBed implementation (for ship screens) ────────────────────────
        string IFarmBed.BedName => string.IsNullOrEmpty(blockName) ? "Grid Tree Farm" : blockName;
        string IFarmBed.CropName => _hasTree ? "Timber" : "Empty";
        float IFarmBed.Growth01 => Growth01;
        bool IFarmBed.IsReady => IsReady;
        bool IFarmBed.NeedsWater => _hasTree && !HasWater;
        bool IFarmBed.NeedsOxygen => _hasTree && !HasAir;
        bool IFarmBed.IsEmpty => !_hasTree;
        float IFarmBed.Water01 => Water01;
        bool IFarmBed.IsDead => false;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Reset()
        {
            blockName = "Grid Automatic Tree Farm";
            BlockMass = 920f;
            maxHP = 720f;
            waterCapacity = 400f;
            waterStored = 0f;
            basePowerDraw = 120f;
            idlePowerDraw = 8f;
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            EnsureContainers();
            EnsureVisuals();
            UpdateTreeVisuals();
        }

        private void Awake()
        {
            EnsureContainers();
        }

        private void Start()
        {
            EnsureContainers();
            EnsureVisuals();
            UpdateTreeVisuals();
        }

        public void EnsureContainers()
        {
            if (inputC == null) inputC = new ItemContainer("Tree Farm Input", INPUT_SLOTS);
            else if (inputC.Size != INPUT_SLOTS) inputC.Resize(INPUT_SLOTS);
            inputC.AcceptFilter = (item, count) =>
                (TreeFarmPlot.IsSaplingOrTreeSeed(item) || TreeFarmPlot.IsFertilizerOrBiomass(item)) ? count : 0;

            if (outputC == null) outputC = new ItemContainer("Tree Farm Output", OUTPUT_SLOTS);
            else if (outputC.Size != OUTPUT_SLOTS) outputC.Resize(OUTPUT_SLOTS);

            if (upgradeC == null) upgradeC = new ItemContainer("Tree Farm Upgrades", UPGRADE_SLOTS);
            else if (upgradeC.Size != UPGRADE_SLOTS) upgradeC.Resize(UPGRADE_SLOTS);
            upgradeC.AcceptFilter = (item, count) => item is FurnaceUpgradeItem ? count : 0;

            ResolveFallbackItems();
        }

        private void ResolveFallbackItems()
        {
            if (woodLogItem != null && saplingItem != null && biomassItem != null && woodChipsItem != null) return;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                string id = it.itemId ?? "";
                if (woodLogItem == null && (id == "item_wood_log" || id == "wood_log" || it.displayName == "Wood Log"))
                    woodLogItem = it;
                else if (saplingItem == null && (id == "seed_tree_sapling" || id == "item_tree_sapling" || id == "tree_sapling" || it.displayName == "Tree Sapling"))
                    saplingItem = it;
                else if (biomassItem == null && (id == "item_biomass" || id == "biomass" || it.displayName == "Biomass"))
                    biomassItem = it;
                else if (woodChipsItem == null && (id == "item_wood_chips" || id == "wood_chips" || it.displayName == "Wood Chips"))
                    woodChipsItem = it;
            }
        }

        private void Update()
        {
            if (Networking.NetworkSession.Mode == Networking.SessionMode.Client) return;
            if (!Enabled) return;
            if (Grid != null && !Grid.HasPower) return;

            float dt = Time.deltaTime;
            RefillWaterFromGrid(dt);

            _scanTimer += dt;
            if (_scanTimer >= 0.75f)
            {
                _scanTimer = 0f;
                if (!_hasTree)
                    TryStartCycle();
                else if (!_cycleFertilized && autoFertilizeFromInput && _growth01 < 0.95f)
                {
                    if (ConsumeOneFertilizer())
                        _cycleFertilized = true;
                }
            }

            if (_hasTree && _growth01 < 1f)
            {
                if (HasAir)
                {
                    float drain = waterConsumptionLps * dt;
                    if (waterStored >= drain)
                    {
                        waterStored -= drain;
                        float dur = EffectiveCycleSeconds;
                        _growth01 = Mathf.Clamp01(_growth01 + dt / dur);
                        UpdateTreeVisuals();
                    }
                    else
                    {
                        waterStored = 0f;
                    }
                }
            }

            if (_hasTree && _growth01 >= 0.999f && automatic)
                TryCompleteHarvest();
        }

        // ── Water, cycle start, and harvest ───────────────────────────────────
        private void RefillWaterFromGrid(float dt)
        {
            if (Grid == null || waterStored >= waterCapacity - 0.01f) return;
            if (GridLiquidNetwork.Instance == null) return;

            float want = Mathf.Min(refillLitresPerSecond * dt, waterCapacity - waterStored);
            if (want <= 0f) return;

            float got = GridLiquidNetwork.Instance.DrawLiquidFor(this, LiquidType.Water, want);
            if (got > 0f)
                waterStored = Mathf.Min(waterCapacity, waterStored + got);
        }

        public float TryAddWater(float litres)
        {
            if (litres <= 0f || waterCapacity <= 0f) return 0f;
            float before = waterStored;
            waterStored = Mathf.Clamp(waterStored + litres, 0f, waterCapacity);
            return waterStored - before;
        }

        private void TryStartCycle()
        {
            if (_hasTree) return;
            if (!ConsumeOneSapling()) return;

            _hasTree = true;
            _growth01 = 0f;
            _cycleFertilized = autoFertilizeFromInput && ConsumeOneFertilizer();
            UpdateTreeVisuals();
        }

        private bool ConsumeOneSapling()
        {
            if (inputC != null)
            {
                for (int i = 0; i < inputC.Size; i++)
                {
                    var s = inputC.GetSlot(i);
                    if (s != null && !s.IsEmpty && TreeFarmPlot.IsSaplingOrTreeSeed(s.item))
                    {
                        inputC.Remove(s.item, 1);
                        return true;
                    }
                }
            }

            if (autoPullFromCargo && Grid != null && GridItemNetwork.Instance != null)
            {
                var containers = GridItemNetwork.Instance.GetConnectedContainers(Grid);
                for (int c = 0; c < containers.Count; c++)
                {
                    var box = containers[c]?.container;
                    if (box == null) continue;
                    for (int i = 0; i < box.Size; i++)
                    {
                        var s = box.GetSlot(i);
                        if (s != null && !s.IsEmpty && TreeFarmPlot.IsSaplingOrTreeSeed(s.item))
                        {
                            box.Remove(s.item, 1);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private bool ConsumeOneFertilizer()
        {
            if (inputC != null)
            {
                for (int i = 0; i < inputC.Size; i++)
                {
                    var s = inputC.GetSlot(i);
                    if (s != null && !s.IsEmpty && TreeFarmPlot.IsFertilizerOrBiomass(s.item))
                    {
                        inputC.Remove(s.item, 1);
                        return true;
                    }
                }
            }

            if (autoPullFromCargo && Grid != null && GridItemNetwork.Instance != null)
            {
                var containers = GridItemNetwork.Instance.GetConnectedContainers(Grid);
                for (int c = 0; c < containers.Count; c++)
                {
                    var box = containers[c]?.container;
                    if (box == null) continue;
                    for (int i = 0; i < box.Size; i++)
                    {
                        var s = box.GetSlot(i);
                        if (s != null && !s.IsEmpty && TreeFarmPlot.IsFertilizerOrBiomass(s.item))
                        {
                            box.Remove(s.item, 1);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Harvests a mature tree into connected grid cargo or the output bin, keeping
        /// one sapling back to replant when <see cref="replantAfterHarvest"/> is on.
        /// </summary>
        public bool HarvestNowToOutput()
        {
            EnsureContainers();
            if (!_hasTree || _growth01 < 0.999f) return false;
            return TryCompleteHarvest();
        }

        private bool TryCompleteHarvest()
        {
            ResolveFallbackItems();

            int logs  = Mathf.Max(1, logsPerCycle) + (_cycleFertilized ? 2 : 0);
            int bio   = Mathf.Max(0, biomassPerCycle) + (_cycleFertilized ? 1 : 0);
            int chips = Mathf.Max(0, woodChipsPerCycle);
            int saps  = Mathf.Max(0, surplusSaplingsPerCycle);

            if (woodLogItem != null && !Deliver(woodLogItem, logs))
                return false;

            if (biomassItem != null && bio > 0) Deliver(biomassItem, bio);
            if (woodChipsItem != null && chips > 0) Deliver(woodChipsItem, chips);
            if (saplingItem != null && saps > 0) Deliver(saplingItem, saps);

            _treesHarvested++;

            if (replantAfterHarvest)
            {
                _hasTree = true;
                _growth01 = 0f;
                _cycleFertilized = autoFertilizeFromInput && ConsumeOneFertilizer();
            }
            else
            {
                _hasTree = false;
                _growth01 = 0f;
                _cycleFertilized = false;
            }

            UpdateTreeVisuals();
            return true;
        }

        private bool Deliver(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return true;
            int remaining = count;

            if (Grid != null && GridItemNetwork.Instance != null)
            {
                var containers = GridItemNetwork.Instance.GetConnectedContainers(Grid);
                for (int i = 0; i < containers.Count && remaining > 0; i++)
                {
                    var c = containers[i]?.container;
                    if (c == null) continue;
                    if (!c.HasSpace(item, remaining)) continue;
                    var leftover = c.Insert(new ItemStack { item = item, count = remaining });
                    remaining = leftover != null && !leftover.IsEmpty ? leftover.count : 0;
                }
            }

            if (remaining > 0 && outputC != null)
            {
                var leftover = outputC.Insert(new ItemStack { item = item, count = remaining });
                remaining = leftover != null && !leftover.IsEmpty ? leftover.count : 0;
            }

            return remaining < count;
        }

        // ── 3D Tree & Saw Carriage Visuals ────────────────────────────────────
        private void EnsureVisuals()
        {
            var root = transform.Find(VisualRootName);
            if (root == null)
            {
                var go = new GameObject(VisualRootName);
                go.transform.SetParent(transform, false);
                root = go.transform;
            }

            var mat = SharedLitMaterial();
            float h = EffectiveCellSize * 0.5f;

            var tree = root.Find("TreeRoot");
            if (tree == null)
            {
                tree = new GameObject("TreeRoot").transform;
                tree.SetParent(root, false);
                tree.localPosition = new Vector3(0f, -h * 0.44f, 0f);

                Prim(PrimitiveType.Cylinder, "Trunk", tree,
                    new Vector3(0f, 0.45f, 0f), new Vector3(0.22f, 0.45f, 0.22f),
                    Quaternion.identity, mat, TrunkBark);
                Prim(PrimitiveType.Cylinder, "TrunkTop", tree,
                    new Vector3(0.02f, 0.98f, -0.01f), new Vector3(0.15f, 0.26f, 0.15f),
                    Quaternion.Euler(3f, 0f, -4f), mat, TrunkBark);

                var canopy = Prim(PrimitiveType.Sphere, "Canopy_Main", tree,
                    new Vector3(0f, 1.32f, 0f), new Vector3(1.10f, 0.88f, 1.10f),
                    Quaternion.identity, mat, CanopyFoliage);
                Prim(PrimitiveType.Sphere, "Canopy_Crown", canopy,
                    new Vector3(0.04f, 0.34f, -0.03f), new Vector3(0.70f, 0.62f, 0.70f),
                    Quaternion.identity, mat, new Color(0.26f, 0.60f, 0.24f));
                Prim(PrimitiveType.Sphere, "Canopy_West", canopy,
                    new Vector3(-0.32f, -0.05f, 0.10f), new Vector3(0.62f, 0.55f, 0.62f),
                    Quaternion.identity, mat, new Color(0.20f, 0.48f, 0.20f));
                Prim(PrimitiveType.Sphere, "Canopy_East", canopy,
                    new Vector3(0.30f, -0.04f, -0.10f), new Vector3(0.60f, 0.52f, 0.60f),
                    Quaternion.identity, mat, new Color(0.24f, 0.56f, 0.22f));
            }
            _treeVisualRoot = tree;
            _canopyVisual = tree.Find("Canopy_Main");

            var saw = root.Find("SawCarriage");
            if (saw == null)
            {
                saw = Prim(PrimitiveType.Cube, "SawCarriage", root,
                    new Vector3(0f, -h * 0.24f, -h * 0.52f),
                    new Vector3(EffectiveCellSize * 0.68f, 0.08f, 0.18f),
                    Quaternion.identity, mat, new Color(0.84f, 0.58f, 0.16f));
            }
            _sawCarriageVisual = saw;
        }

        private static Transform Prim(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat, Color tint)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null) r.sharedMaterial = mat;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", tint);
                mpb.SetColor("_Color", tint);
                r.SetPropertyBlock(mpb);
            }
            return go.transform;
        }

        private static Material s_treeMat;
        private static Material SharedLitMaterial()
        {
            if (s_treeMat != null) return s_treeMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return null;
            s_treeMat = new Material(sh) { name = "GridAutoTreeFarm_Runtime" };
            if (s_treeMat.HasProperty("_Smoothness")) s_treeMat.SetFloat("_Smoothness", 0.25f);
            return s_treeMat;
        }

        public void UpdateTreeVisuals()
        {
            if (_treeVisualRoot == null) EnsureVisuals();
            if (_treeVisualRoot == null) return;

            if (!_hasTree)
            {
                if (_treeVisualRoot.gameObject.activeSelf)
                    _treeVisualRoot.gameObject.SetActive(false);
                return;
            }

            if (!_treeVisualRoot.gameObject.activeSelf)
                _treeVisualRoot.gameObject.SetActive(true);

            float g = Mathf.Clamp01(_growth01);
            float trunkXZ = Mathf.Lerp(0.22f, 0.78f, g);
            float trunkY  = Mathf.Lerp(0.18f, 0.76f, g);
            _treeVisualRoot.localScale = new Vector3(trunkXZ, trunkY, trunkXZ);

            if (_canopyVisual != null)
            {
                float c = Mathf.Lerp(0.28f, 1.0f, Mathf.SmoothStep(0f, 1f, g));
                _canopyVisual.localScale = new Vector3(1.10f * c, 0.88f * c, 1.10f * c);
            }

            if (_sawCarriageVisual != null)
            {
                float h = EffectiveCellSize * 0.5f;
                float z = g >= 0.90f
                    ? Mathf.Lerp(-h * 0.52f, h * 0.52f, Mathf.PingPong((g - 0.90f) * 20f, 1f))
                    : -h * 0.52f;
                _sawCarriageVisual.localPosition = new Vector3(0f, -h * 0.24f, z);
            }
        }

        // ── Persistence (IMachineProcessState) ────────────────────────────────
        public void CaptureProcessState(MachineProcessState state)
        {
            if (state == null) return;
            EnsureContainers();
            state.SetExtra("water", Mathf.Max(0f, waterStored));
            state.SetExtra("enabled", Enabled ? 1f : 0f);
            state.SetExtra("hasTree", _hasTree ? 1f : 0f);
            state.SetExtra("growth", Mathf.Clamp01(_growth01));
            state.SetExtra("fert", _cycleFertilized ? 1f : 0f);
            state.SetExtra("trees", _treesHarvested);
            state.SetExtra("replant", replantAfterHarvest ? 1f : 0f);
            state.SetExtra("auto", automatic ? 1f : 0f);
            state.SetExtra("autoFert", autoFertilizeFromInput ? 1f : 0f);
            state.SetExtra("autoPull", autoPullFromCargo ? 1f : 0f);
        }

        public void RestoreProcessState(MachineProcessState state)
        {
            if (state == null || state.IsEmpty) return;
            EnsureContainers();
            waterStored = Mathf.Clamp(
                MachineProcessPersistence.FiniteOr(state.GetExtra("water", waterStored), waterStored),
                0f, Mathf.Max(0f, waterCapacity));
            Enabled = state.GetExtra("enabled", Enabled ? 1f : 0f) >= 0.5f;
            _hasTree = state.GetExtra("hasTree", 0f) >= 0.5f;
            _growth01 = MachineProcessPersistence.ClampOr(state.GetExtra("growth", 0f), 0f, 1f, 0f);
            _cycleFertilized = state.GetExtra("fert", 0f) >= 0.5f;
            _treesHarvested = Mathf.Max(0, Mathf.RoundToInt(MachineProcessPersistence.FiniteOr(state.GetExtra("trees", 0f), 0f)));
            replantAfterHarvest = state.GetExtra("replant", replantAfterHarvest ? 1f : 0f) >= 0.5f;
            automatic = state.GetExtra("auto", automatic ? 1f : 0f) >= 0.5f;
            autoFertilizeFromInput = state.GetExtra("autoFert", autoFertilizeFromInput ? 1f : 0f) >= 0.5f;
            autoPullFromCargo = state.GetExtra("autoPull", autoPullFromCargo ? 1f : 0f) >= 0.5f;

            EnsureVisuals();
            UpdateTreeVisuals();
        }

        // ── IGridDataProvider for screens ─────────────────────────────────────
        public string SourceName => string.IsNullOrEmpty(blockName) ? "Grid Auto Tree Farm" : blockName;

        public string DataCategory => "Life Support";

        public string GetDisplayData()
        {
            string treeStr = _hasTree
                ? $"Timber {Growth01 * 100f:0}% ({StageLabel})"
                : "No Sapling";
            return $"TREE FARM\n{Status}\n{treeStr}\n"
                 + $"Water {waterStored:0}/{waterCapacity:0} L {(HasWaterSupply ? "(piped)" : "(dry)")}\n"
                 + $"Air {(HasAir ? "OK" : "NONE")} · Felled {_treesHarvested}";
        }
    }
}
