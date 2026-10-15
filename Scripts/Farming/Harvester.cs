// Assets/Scripts/VoxelEngine/Farming/Harvester.cs
//
// Automation block: auto-harvests mature crops from nearby FarmPlots
// and pushes the items into adjacent ItemPipes or its internal buffer.
// Requires power. Optionally auto-replants seeds.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Simulation;
using VoxelEngine.Transport;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Automated harvester. Place near farm plots. When a crop is mature,
    /// the harvester picks it and pushes items into connected pipes or
    /// an internal output buffer.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class Harvester : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Radius to scan for mature FarmPlots.")]
        public float scanRadius = 8f;
        [Tooltip("Seconds between harvest scans.")]
        public float scanInterval = 2f;
        [Tooltip("If true, auto-replant seeds after harvesting.")]
        public bool autoReplant = true;

        [Header("Output")]
        public int outputSlots = 6;

        public ItemContainer Output { get { EnsureOutput(); return _output; } }

        private ItemContainer _output;
        private PowerConsumer _power;
        private float _timer;

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
            EnsureOutput();
        }

        private void Update()
        {
            if (_power != null && !_power.IsPowered) return;

            _timer += Time.deltaTime;
            if (_timer < scanInterval) return;
            _timer = 0f;

            ScanAndHarvest();
        }

        private void ScanAndHarvest()
        {
            var plots = FindObjectsByType<FarmPlot>(FindObjectsInactive.Exclude);
            foreach (var plot in plots)
            {
                if (plot == null || plot.plantedCrop == null) continue;
                if (plot.growthProgress < 1f) continue;
                if (Vector3.SqrMagnitude(plot.transform.position - transform.position)
                    > scanRadius * scanRadius) continue;

                var crop = plot.plantedCrop;

                // Harvest items into output.
                if (crop.harvestItem != null)
                    OutputItems(crop.harvestItem, crop.harvestAmount);

                // Auto-replant if enabled.
                CropDefinition replantCrop = autoReplant ? crop : null;

                // Clear the plot.
                plot.growthProgress = 0f;

                if (replantCrop != null)
                {
                    // Keep the same crop planted, reset growth.
                    plot.growthProgress = 0f;
                }
                else
                {
                    // Fully clear — output seeds too.
                    if (crop.seedItem != null)
                        OutputItems(crop.seedItem, crop.seedReturnAmount);
                    // Use reflection-free clear: set plantedCrop to null via method.
                    // Since TryHarvest does the full clear, we call a simulated version:
                    plot.plantedCrop = null;
                }
            }

            // Also scan and auto-harvest any standalone TreeFarmPlots within scanRadius.
            var treePlots = FindObjectsByType<TreeFarmPlot>(FindObjectsInactive.Exclude);
            foreach (var tPlot in treePlots)
            {
                if (tPlot == null || !tPlot.IsMature) continue;
                if (Vector3.SqrMagnitude(tPlot.transform.position - transform.position)
                    > scanRadius * scanRadius) continue;

                if (tPlot.TryAutomatedHarvest(
                    out var logIt, out int logCnt,
                    out var bioIt, out int bioCnt,
                    out var sapIt, out int sapCnt,
                    autoReplant: autoReplant))
                {
                    if (logIt != null && logCnt > 0) OutputItems(logIt, logCnt);
                    if (bioIt != null && bioCnt > 0) OutputItems(bioIt, bioCnt);
                    if (sapIt != null && sapCnt > 0) OutputItems(sapIt, sapCnt);
                }
            }
        }

        private void OutputItems(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
            int remaining = count;

            // Try belts, chutes, funnels, or item pipes first.
            var hits = Physics.OverlapSphere(transform.position, 1.8f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var consumer = col.GetComponentInParent<IItemConsumer>();
                if (consumer != null)
                {
                    int cap = consumer.GetInputCapacity(item);
                    if (cap > 0)
                    {
                        int accepted = consumer.TryInsert(item, Mathf.Min(cap, remaining));
                        remaining -= accepted;
                        if (remaining <= 0) return;
                    }
                }
            }

            // Overflow to internal buffer.
            EnsureOutput();
            _output.Insert(new ItemStack(item, remaining));
        }

        private void EnsureOutput()
        {
            if (_output == null) _output = new ItemContainer("Harvester Output", outputSlots);
            else _output.Resize(outputSlots);
        }

        public void EnsureOutputPublic() => EnsureOutput();
    }
}
