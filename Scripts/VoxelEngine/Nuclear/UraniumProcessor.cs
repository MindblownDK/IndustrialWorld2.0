// Assets/Scripts/VoxelEngine/Nuclear/UraniumProcessor.cs
//
// Enrichment centrifuge: converts raw Uranium Ore into:
//   - Enriched Uranium Fuel Rods (for the big reactor)
//   - LEU Pellets (Low-Enriched Uranium, for the small portable reactor)
//   - Depleted Uranium (waste byproduct)
//
// Requires power to run. Single input slot, two output slots.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Nuclear
{
    [RequireComponent(typeof(PlacedBlock))]
    public class UraniumProcessor : MonoBehaviour
    {
        [Header("Processing")]
        [Tooltip("Seconds to process one batch of uranium ore.")]
        public float processTime = 30f;

        [Tooltip("Raw uranium ore consumed per batch.")]
        public int orePerBatch = 5;

        [Header("Outputs")]
        [Tooltip("Enriched fuel rod item (for big reactor).")]
        public ItemDefinition enrichedFuelRod;
        public int fuelRodOutput = 1;

        [Tooltip("LEU pellet item (for small reactor).")]
        public ItemDefinition leuPellet;
        public int leuPelletOutput = 2;

        [Tooltip("Depleted uranium waste.")]
        public ItemDefinition depletedUranium;
        public int wasteOutput = 1;

        [Header("Containers")]
        public ItemContainer inputC;
        public ItemContainer enrichedOutputC;
        public ItemContainer wasteOutputC;

        [Header("Required Input Item")]
        public ItemDefinition uraniumOreItem;

        public float Progress01 => _timer / Mathf.Max(0.01f, processTime);
        public bool IsProcessing { get; private set; }

        private float _timer;
        private PowerConsumer _power;

        private void Awake()
        {
            EnsureContainers();
            _power = GetComponent<PowerConsumer>();
        }

        public void EnsureContainers()
        {
            if (inputC == null) inputC = new ItemContainer("Uranium Input", 1);
            else inputC.Resize(1);
            if (enrichedOutputC == null) enrichedOutputC = new ItemContainer("Enriched Output", 4);
            else enrichedOutputC.Resize(4);
            if (wasteOutputC == null) wasteOutputC = new ItemContainer("Waste Output", 4);
            else wasteOutputC.Resize(4);
        }

        private void Update()
        {
            EnsureContainers();
            if (_power != null && !_power.IsPowered) { IsProcessing = false; return; }

            // Check if we have enough uranium ore.
            if (uraniumOreItem == null) return;
            int have = inputC.CountOf(uraniumOreItem);
            if (have < orePerBatch) { IsProcessing = false; _timer = 0; return; }

            // Check output space.
            if (enrichedFuelRod != null && !enrichedOutputC.HasSpace(enrichedFuelRod, fuelRodOutput))
            { IsProcessing = false; return; }

            IsProcessing = true;
            _timer += Time.deltaTime;

            if (_timer >= processTime)
            {
                _timer = 0f;
                // Consume input.
                inputC.Remove(uraniumOreItem, orePerBatch);

                // Produce outputs.
                if (enrichedFuelRod != null)
                    enrichedOutputC.Insert(new ItemStack(enrichedFuelRod, fuelRodOutput));
                if (leuPellet != null)
                    enrichedOutputC.Insert(new ItemStack(leuPellet, leuPelletOutput));
                if (depletedUranium != null)
                    wasteOutputC.Insert(new ItemStack(depletedUranium, wasteOutput));
            }
        }
    }
}
