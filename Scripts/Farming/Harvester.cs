// Assets/Scripts/VoxelEngine/Farming/Harvester.cs
//
// Automation block: auto-harvests mature crops from nearby beds and pushes the
// items into adjacent ItemPipes or its internal buffer. Requires power.
//
// 18.2.0-dev — the harvester can finally see the whole farm.
//
// It was written against tilled soil and only tilled soil: it wrote growth
// fields directly, ignored the greenhouse rule, and could not see a Seed
// Planter at all, so a corridor of trays was still a paddock for the player.
// Three changes and no new block:
//
//   * every bed is worked through FarmHarvestService, which asks the belt for
//     room before taking a crop - a full belt is a wait, not a lost harvest;
//   * soil, planters and sapling beds are all covered, each filterable, so a
//     player can point a machine at one family without moving anything;
//   * a dead bed is cleared and reported instead of being left to block the
//     row forever.
//
// Balance is unchanged: 40 W, an 8 m scan every 2 s, and the same six output
// slots as before. The bed list is cached between scans so a large farm costs
// one scene pass per refresh rather than one per bed.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Simulation;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Automated harvester. Place near farm beds. When a crop is mature, the
    /// harvester picks it and pushes items into connected belts, pipes or its
    /// internal output buffer.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class Harvester : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Radius to scan for mature farm beds.")]
        public float scanRadius = 8f;
        [Tooltip("Seconds between harvest scans.")]
        public float scanInterval = 2f;
        [Tooltip("If true, the same crop is planted straight back after a harvest.")]
        public bool autoReplant = true;
        [Tooltip("If true, a bed whose crop was lost to drought is cleared so the row keeps moving. The lost crop yields nothing.")]
        public bool clearDeadBeds = true;

        [Header("Coverage")]
        [Tooltip("Work tilled soil plots.")]
        public bool harvestSoilPlots = true;
        [Tooltip("Work the one-metre seed planter trays.")]
        public bool harvestPlanters = true;
        [Tooltip("Work the 3x1x3 sapling beds.")]
        public bool harvestTreeBeds = true;

        [Header("Output")]
        public int outputSlots = 6;
        [Tooltip("How far the harvester reaches to hand a harvest to a belt, chute, funnel or pipe.")]
        public float pushRadius = 1.8f;

        public ItemContainer Output { get { EnsureOutput(); return _output; } }

        private ItemContainer _output;
        private PowerConsumer _power;
        private float _timer;
        private float _bedsRefreshedAt = -1000f;

        /// <summary>How stale the bed list may get. A bed that is placed inside this window is
        /// picked up on the next refresh, not the next frame.</summary>
        private const float BedCacheSeconds = 2f;

        private readonly List<CropPlanter> _planters = new List<CropPlanter>();
        private readonly List<FarmPlot> _plots = new List<FarmPlot>();
        private readonly List<TreeFarmPlot> _trees = new List<TreeFarmPlot>();

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
            EnsureOutput();
        }

        private void OnEnable() => RefreshBedLists(force: true);

        private void Update()
        {
            // One copy of each machine runs per session (MP readiness rule): a guest's
            // harvester renders the host's outcome instead of harvesting a second time.
            if (VoxelEngine.Networking.NetworkSession.SimulationIsRemote) return;

            // A machine with no power consumer still runs - that is the prefab's own
            // business, exactly as it was before this round.
            if (_power != null && !_power.IsPowered) return;

            _timer += Time.deltaTime;
            if (_timer < Mathf.Max(0.25f, scanInterval)) return;
            _timer = 0f;

            RefreshBedLists(force: false);
            ScanAndHarvest();
        }

        /// <summary>
        /// Cache the beds once per refresh instead of walking the whole scene on every
        /// tick. The lists hold destroyed entries safely - each one is null-checked when
        /// it is worked - and the refresh timer is the same order of cost the old code
        /// paid on every single scan.
        /// </summary>
        private void RefreshBedLists(bool force)
        {
            if (!force && Time.time - _bedsRefreshedAt < BedCacheSeconds) return;
            _bedsRefreshedAt = Time.time;

            _planters.Clear();
            _plots.Clear();
            _trees.Clear();

            float radiusSq = scanRadius * scanRadius;
            Vector3 here = transform.position;

            if (harvestPlanters)
            {
                var found = FindObjectsByType<CropPlanter>(FindObjectsInactive.Exclude);
                for (int i = 0; i < found.Length; i++)
                    if (found[i] != null && Vector3.SqrMagnitude(found[i].transform.position - here) <= radiusSq)
                        _planters.Add(found[i]);
            }

            if (harvestSoilPlots)
            {
                var found = FindObjectsByType<FarmPlot>(FindObjectsInactive.Exclude);
                for (int i = 0; i < found.Length; i++)
                    if (found[i] != null && Vector3.SqrMagnitude(found[i].transform.position - here) <= radiusSq)
                        _plots.Add(found[i]);
            }

            if (harvestTreeBeds)
            {
                var found = FindObjectsByType<TreeFarmPlot>(FindObjectsInactive.Exclude);
                for (int i = 0; i < found.Length; i++)
                    if (found[i] != null && Vector3.SqrMagnitude(found[i].transform.position - here) <= radiusSq)
                        _trees.Add(found[i]);
            }
        }

        private void ScanAndHarvest()
        {
            // One pass over every bed each scan: planter trays first because a row of
            // them is the common case, then soil, then sapling beds.
            for (int i = 0; i < _planters.Count; i++)
            {
                var planter = _planters[i];
                if (planter == null) continue;
                FarmHarvestService.WorkBed(planter, transform, pushRadius, Output, autoReplant, clearDeadBeds);
            }

            for (int i = 0; i < _plots.Count; i++)
            {
                var plot = _plots[i];
                if (plot == null) continue;
                FarmHarvestService.WorkBed(plot, transform, pushRadius, Output, autoReplant, clearDeadBeds);
            }

            for (int i = 0; i < _trees.Count; i++)
            {
                var plot = _trees[i];
                if (plot == null || !plot.IsMature) continue;

                if (plot.TryAutomatedHarvest(
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

        /// <summary>Hand a stack to the nearest route, then to the buffer for whatever is left.</summary>
        private void OutputItems(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;

            int moved = FarmItemRouter.Push(transform, pushRadius, item, count);
            int remaining = count - moved;
            if (remaining <= 0) return;

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
