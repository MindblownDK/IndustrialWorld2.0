// Assets/Scripts/VoxelEngine/GridSystem/GridBiofarm.cs
//
// Grid-mounted biofarm / oxygen garden. Passive O2 generation for ships /
// off-grid bases. Requires grid power + water (liquid tanks/pipes) + biomass
// (cargo + item pipes). Produces oxygen into the grid gas pool via gas pipes.
//
// Mirrors the static Biofarm but uses GridLiquidNetwork + GridGasNetwork + GridItemNetwork.

using UnityEngine;
using VoxelEngine.Farming;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridBiofarm : GridBlock, IGridDataProvider
    {
        [Header("Biofarm Tuning")]
        [Tooltip("Watts drawn while producing")]
        public float powerDraw = 70f;
        [Tooltip("Water litres consumed per second")]
        public float waterConsumptionLps = 0.2f;
        [Tooltip("Oxygen litres produced per second (slower than H2O2 generator)")]
        public float oxygenPerSecond = 0.55f;
        [Tooltip("Seconds of production per 1 biomass item")]
        public float secondsPerBiomass = 45f;

        [Header("Buffers (litres)")]
        public float waterCapacity = 180f;
        public float waterStored;
        public float o2Capacity = 260f;
        public float o2Stored;

        [Header("Input")]
        public ItemContainer biomassInput;

        public float WaterFill01 => waterCapacity > 0 ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;
        public float O2Fill01 => o2Capacity > 0 ? Mathf.Clamp01(o2Stored / o2Capacity) : 0f;

        public bool IsProducing { get; private set; }
        public string Status { get; private set; } = "Idle";
        public float BiomassTimeRemaining { get; private set; }

        public override float PowerDraw => IsProducing ? powerDraw : 0f;
        public override float ContentMass =>
            (biomassInput != null ? MassUtil.ContainerMass(biomassInput) : 0f)
            + waterStored * 1f
            + o2Stored * 0.05f;

        private float _pullTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (biomassInput == null) biomassInput = new ItemContainer("Biomass", 6);
            else biomassInput.Resize(6);
            biomassInput.AcceptFilter = (item, wanted) => IsBiomass(item) ? wanted : 0;
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block") blockName = "Biofarm";
        }

        private void Update()
        {
            _pullTimer += Time.deltaTime;
            if (_pullTimer >= 1.2f)
            {
                _pullTimer = 0f;
                AutoPullBiomass();
            }
        }

        private void FixedUpdate()
        {
            if (!Enabled) { Status = "Disabled"; IsProducing = false; return; }
            if (Grid == null) { Status = "No Grid"; IsProducing = false; return; }

            float dt = Time.fixedDeltaTime;

            // Refill water from liquid tanks/pipes
            if (waterStored < waterCapacity)
                PullWaterFromTanks(dt);

            bool hasPower = Grid.HasPower;
            bool hasWater = waterStored > 0.05f;

            if (BiomassTimeRemaining <= 0f)
            {
                if (!TryConsumeBiomassItem())
                {
                    IsProducing = false;
                    if (!hasPower) Status = "No Power";
                    else if (!hasWater) Status = "No Water";
                    else Status = "No Biomass";
                    return;
                }
            }

            if (!hasPower) { IsProducing = false; Status = "No Power"; return; }
            if (!hasWater) { IsProducing = false; Status = "No Water"; return; }
            if (o2Stored >= o2Capacity - 0.01f) { IsProducing = false; Status = "O₂ Full"; }

            else
            {
                IsProducing = true;
                Status = "Producing";

                float needWater = waterConsumptionLps * dt;
                waterStored = Mathf.Max(0f, waterStored - needWater);

                BiomassTimeRemaining -= dt;

                float produced = oxygenPerSecond * dt;
                o2Stored = Mathf.Min(o2Capacity, o2Stored + produced);
            }

            // Push O2 into gas pipes / tanks
            if (GridGasNetwork.Instance != null && GridGasNetwork.Instance.HasPipes(Grid))
            {
                float moved = GridGasNetwork.Instance.FillGasFrom(this, VoxelEngine.Gas.GasType.Oxygen, Mathf.Min(o2Stored, 30f * dt));
                o2Stored -= moved;
            }
        }

        private void PullWaterFromTanks(float dt)
        {
            if (Grid == null || GridLiquidNetwork.Instance == null) return;
            float want = waterConsumptionLps * 2f * dt + 0.5f; // small buffer pull
            float got = GridLiquidNetwork.Instance.DrawLiquidFor(this, LiquidType.Water, want);
            waterStored = Mathf.Min(waterCapacity, waterStored + got);
        }

        private void AutoPullBiomass()
        {
            if (biomassInput == null || Grid == null || GridItemNetwork.Instance == null) return;
            foreach (var cargo in GridItemNetwork.Instance.GetConnectedContainers(Grid))
            {
                if (cargo == null || cargo.container == null) continue;
                for (int i = 0; i < cargo.container.Size; i++)
                {
                    var s = cargo.container.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null || !IsBiomass(s.item)) continue;
                    if (!biomassInput.HasSpace(s.item, 1)) return;
                    int moved = cargo.container.Remove(s.item, s.count);
                    if (moved > 0)
                    {
                        var leftover = biomassInput.Insert(new ItemStack { item = s.item, count = moved });
                        if (leftover != null && !leftover.IsEmpty) cargo.container.Insert(leftover);
                    }
                }
            }
        }

        private bool TryConsumeBiomassItem()
        {
            if (biomassInput == null) return false;
            for (int i = 0; i < biomassInput.Size; i++)
            {
                var slot = biomassInput.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;
                if (!IsBiomass(slot.item)) continue;
                if (biomassInput.Remove(slot.item, 1) > 0)
                {
                    BiomassTimeRemaining += secondsPerBiomass;
                    return true;
                }
            }
            return false;
        }

        private bool IsBiomass(ItemDefinition item)
        {
            if (item == null) return false;
            if (item is FoodItem) return true;
            if (item is SeedItem) return true;
            string id = item.itemId?.ToLowerInvariant() ?? "";
            if (id.Contains("biomass") || id.Contains("organic") || id.Contains("wheat") || id.Contains("corn") || id.Contains("carrot") || id.Contains("fiber") || id.Contains("algae") || id.Contains("compost"))
                return true;
            if (!string.IsNullOrEmpty(item.category) && item.category.ToLowerInvariant().Contains("farm"))
                return true;
            return false;
        }

        // IGridDataProvider for screens
        public string SourceName => blockName;
        public string DataCategory => "Life Support";
        public string GetDisplayData()
        {
            return $"BIOFARM\n{Status}\nWater {waterStored:0}/{waterCapacity:0} L\nO₂ {o2Stored:0}/{o2Capacity:0} L\nFuel {BiomassTimeRemaining:0}s";
        }
    }
}
