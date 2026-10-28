// Assets/Scripts/VoxelEngine/Farming/FarmAutomation.cs
//
// 18.2.0-dev — the piece that was missing between a tray of crops and a belt.
//
// A farm that runs while the player is away needs three things the project did
// not have: one place that decides what a harvest is worth, one place that
// moves it out of the bed, and one place that knows which crop a saved name
// points at. Without them every caller invented its own answer - the Harvester
// wrote growth fields directly, the planter refused to touch a port at all, and
// a saved tray had no way to become a tray again after a reload.
//
// The split is deliberate:
//
//   FarmHarvestYield   what one bed gives up right now (crop plus seed return),
//   FarmItemRouter     moving a stack into whatever is standing next to a bed,
//   FarmHarvestService the single authority entry point for working a bed,
//   CropCatalog        a saved crop asset name back into a CropDefinition.
//
// Save safety: nothing here touches the save schema. The router is stateless,
// the yield is a value type, and CropCatalog only reads assets that already
// exist. A world saved before this round loads exactly as it did.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Simulation;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// What one bed gives up when it is worked: the crop, and the seeds a harvest
    /// returns. A value type on purpose - a yield is computed, checked against the
    /// belt it is about to travel on, and only then committed, so nothing is ever
    /// taken off a bed that has nowhere to put it.
    /// </summary>
    public struct FarmHarvestYield
    {
        public ItemDefinition item;
        public int count;
        public ItemDefinition seedItem;
        public int seedCount;

        public bool IsEmpty => (item == null || count <= 0) && (seedItem == null || seedCount <= 0);

        public static FarmHarvestYield Crop(ItemDefinition item, int count,
            ItemDefinition seedItem, int seedCount)
            => new FarmHarvestYield
            {
                item = item,
                count = Mathf.Max(0, count),
                seedItem = seedItem,
                seedCount = Mathf.Max(0, seedCount)
            };
    }

    /// <summary>What one automated pass over a bed actually did.</summary>
    public enum FarmWorkOutcome
    {
        /// <summary>Nothing to do: the bed is empty, growing, or not this machine's business.</summary>
        Idle,

        /// <summary>A ripe crop was taken and moved.</summary>
        Harvested,

        /// <summary>A ripe crop was taken and the same crop was planted straight back.</summary>
        HarvestedAndReplanted,

        /// <summary>A lost crop was cleared off the bed. It yields nothing.</summary>
        ClearedDead,

        /// <summary>Ripe, but the belt or pipe alongside cannot take the load yet - try again next pass.</summary>
        PortBlocked
    }

    /// <summary>
    /// Moves items out of a bed and into whatever is standing next to it. Shared by
    /// the Harvester block and by the output port on a Seed Planter, so both speak
    /// the same rule: belts, chutes, funnels and item pipes first, and never a loss.
    /// </summary>
    public static class FarmItemRouter
    {
        private static readonly Collider[] Overlap = new Collider[32];

        /// <summary>True when everything next to <paramref name="origin"/> can take the whole stack.</summary>
        public static bool HasRoomFor(Transform origin, float radius, ItemDefinition item, int count)
        {
            if (origin == null || item == null || count <= 0) return true;
            return RoomFor(origin.position, radius, origin.gameObject, item, count) >= count;
        }

        /// <summary>
        /// Insert up to <paramref name="count"/> units into the nearest consumers and
        /// return how many were actually taken. Callers hand the remainder to their own
        /// buffer; this method never drops anything on the floor.
        /// </summary>
        public static int Push(Transform origin, float radius, ItemDefinition item, int count)
        {
            if (origin == null || item == null || count <= 0) return 0;

            int remaining = count;
            int found = Physics.OverlapSphereNonAlloc(origin.position, Mathf.Max(0.75f, radius), Overlap,
                ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < found && remaining > 0; i++)
            {
                var col = Overlap[i];
                if (col == null) continue;
                var consumer = col.GetComponentInParent<IItemConsumer>();
                if (consumer == null) continue;

                int capacity = consumer.GetInputCapacity(item);
                if (capacity <= 0) continue;

                int accepted = consumer.TryInsert(item, Mathf.Min(capacity, remaining));
                if (accepted <= 0) continue;
                remaining -= accepted;
            }

            return count - remaining;
        }

        /// <summary>
        /// How many units the neighbours could take, without moving anything. Used to
        /// ask permission before a harvest is committed.
        /// </summary>
        public static int RoomFor(Vector3 position, float radius, GameObject ignore, ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;

            int room = 0;
            int found = Physics.OverlapSphereNonAlloc(position, Mathf.Max(0.75f, radius), Overlap,
                ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < found && room < count; i++)
            {
                var col = Overlap[i];
                if (col == null) continue;
                if (ignore != null && col.gameObject == ignore) continue;
                var consumer = col.GetComponentInParent<IItemConsumer>();
                if (consumer == null) continue;
                if (ignore != null && consumer is Component c && c.gameObject == ignore) continue;
                room += Mathf.Max(0, consumer.GetInputCapacity(item));
            }

            return Mathf.Min(room, count);
        }
    }

    /// <summary>
    /// The one place a bed is worked automatically. A Harvester, a future farm drone
    /// and a server-side tick all call <see cref="WorkBed"/>; none of them reach into a
    /// bed's fields themselves, which is what kept soil and planters from agreeing on
    /// what a harvest was.
    /// </summary>
    public static class FarmHarvestService
    {
        /// <summary>
        /// Work one bed: clear a lost crop if asked, harvest a ripe one if the route can
        /// take it, replant when asked. Items go to the neighbours first and to
        /// <paramref name="buffer"/> for whatever is left over - the buffer is never
        /// silently skipped, so a Harvester with no belt still collects the crop.
        /// </summary>
        public static FarmWorkOutcome WorkBed(IFarmBed bed, Transform origin, float pushRadius,
            ItemContainer buffer, bool autoReplant, bool clearDeadBeds)
        {
            if (bed == null) return FarmWorkOutcome.Idle;

            if (bed.IsDead)
            {
                if (!clearDeadBeds) return FarmWorkOutcome.Idle;
                return ClearDead(bed) ? FarmWorkOutcome.ClearedDead : FarmWorkOutcome.Idle;
            }

            if (bed.IsEmpty || !bed.IsReady) return FarmWorkOutcome.Idle;

            if (!FarmBedAccess.TryPeekHarvest(bed, out var harvest))
                return FarmWorkOutcome.Idle;
            if (harvest.IsEmpty) return FarmWorkOutcome.Idle;

            // Ask the route before taking anything. A ripe tray that waits one more
            // pass is fine; a tray emptied into a full belt is a lost harvest.
            bool routed = origin != null
                && (harvest.item == null
                    || FarmItemRouter.RoomFor(origin.position, pushRadius, origin.gameObject, harvest.item, harvest.count) >= harvest.count)
                && (harvest.seedItem == null
                    || FarmItemRouter.RoomFor(origin.position, pushRadius, origin.gameObject, harvest.seedItem, harvest.seedCount) >= harvest.seedCount);

            if (!routed && buffer == null)
                return FarmWorkOutcome.PortBlocked;

            if (!FarmBedAccess.TryTakeHarvest(bed, autoReplant, out harvest))
                return FarmWorkOutcome.Idle;

            // Push what the route takes, then hand every leftover unit to the buffer. The
            // pre-check above keeps the common case loss-free; this reconciles the rare one
            // where two consumers share a collider and the room was counted twice.
            if (routed && origin != null)
            {
                if (harvest.item != null && harvest.count > 0)
                    harvest.count -= FarmItemRouter.Push(origin, pushRadius, harvest.item, harvest.count);
                if (harvest.seedItem != null && harvest.seedCount > 0)
                    harvest.seedCount -= FarmItemRouter.Push(origin, pushRadius, harvest.seedItem, harvest.seedCount);
            }

            if (buffer != null)
            {
                if (harvest.item != null && harvest.count > 0)
                    buffer.Insert(new ItemStack(harvest.item, harvest.count));
                if (harvest.seedItem != null && harvest.seedCount > 0)
                    buffer.Insert(new ItemStack(harvest.seedItem, harvest.seedCount));
            }
            else if (routed && (harvest.count > 0 || harvest.seedCount > 0))
            {
                Debug.LogWarning("[Farming] A harvest was taken with no buffer to hold the leftovers; "
                    + "give the harvesting machine an output container.");
            }

            return autoReplant ? FarmWorkOutcome.HarvestedAndReplanted : FarmWorkOutcome.Harvested;
        }

        /// <summary>Clear a lost crop. Returns false when the bed had nothing to clear.</summary>
        public static bool ClearDead(IFarmBed bed)
        {
            switch (bed)
            {
                case PlantBox box:
                    return box.ClearAllDead() > 0;
                case CropPlanter planter:
                    if (!planter.IsDead) return false;
                    planter.ClearCrop();
                    return true;
                case FarmPlot plot:
                    if (!plot.IsDead) return false;
                    plot.ClearCrop();
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Two-step access to the beds that expose it, without widening IFarmBed for the
    /// beds that cannot be harvested automatically (a sapling bed keeps its own
    /// mature-harvest call and is not part of a peek/commit pair).
    /// </summary>
    internal static class FarmBedAccess
    {
        public static bool TryPeekHarvest(IFarmBed bed, out FarmHarvestYield harvest)
        {
            switch (bed)
            {
                case PlantBox box when box.CanHarvestNow:
                    harvest = box.PeekHarvest();
                    return true;
                case CropPlanter planter when planter.CanHarvestNow:
                    harvest = planter.PeekHarvest();
                    return true;
                case FarmPlot plot when plot.CanHarvestNow:
                    harvest = plot.PeekHarvest();
                    return true;
                default:
                    harvest = default;
                    return false;
            }
        }

        public static bool TryTakeHarvest(IFarmBed bed, bool replant, out FarmHarvestYield harvest)
        {
            switch (bed)
            {
                case PlantBox box:
                    return box.TakeHarvest(replant, out harvest);
                case CropPlanter planter:
                    return planter.TakeHarvest(replant, out harvest);
                case FarmPlot plot:
                    return plot.TakeHarvest(replant, out harvest);
                default:
                    harvest = default;
                    return false;
            }
        }
    }

    /// <summary>
    /// A saved crop asset name back into a live <see cref="CropDefinition"/>. Runtime-safe:
    /// it never reaches for the asset database, and it prefers the seed item when one was
    /// saved, because the item catalog is the one lookup a build is guaranteed to have.
    /// </summary>
    public static class CropCatalog
    {
        private static readonly List<CropDefinition> Cache = new List<CropDefinition>();
        private static float _cachedAt = -1000f;

        /// <summary>Every crop this running game can see. Cached for a second; the set does
        /// not change at play time.</summary>
        public static IReadOnlyList<CropDefinition> All()
        {
            if (Time.unscaledTime - _cachedAt < 1f && Cache.Count > 0) return Cache;

            _cachedAt = Time.unscaledTime;
            Cache.Clear();
            Cache.AddRange(Resources.LoadAll<CropDefinition>(string.Empty));
            var loaded = Resources.FindObjectsOfTypeAll<CropDefinition>();
            for (int i = 0; i < loaded.Length; i++)
                if (loaded[i] != null && !Cache.Contains(loaded[i]))
                    Cache.Add(loaded[i]);
            return Cache;
        }

        /// <summary>
        /// Resolve a saved tray: the crop asset name first (what gets written), then the
        /// seed item the tray was planted from, which survives a crop asset being renamed.
        /// Returns null - never a guess - when neither answers.
        /// </summary>
        public static CropDefinition Resolve(string cropAssetName, string seedItemId)
        {
            if (!string.IsNullOrEmpty(cropAssetName))
            {
                var all = All();
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && string.Equals(all[i].name, cropAssetName))
                        return all[i];
            }

            if (!string.IsNullOrEmpty(seedItemId))
            {
                var seed = VoxelEngine.Persistence.WorldStatePersistence.Instance != null
                    ? VoxelEngine.Persistence.WorldStatePersistence.Instance.FindItemById(seedItemId)
                    : null;

                if (seed == null)
                {
                    var seeds = Resources.FindObjectsOfTypeAll<SeedItem>();
                    for (int i = 0; i < seeds.Length; i++)
                        if (seeds[i] != null && string.Equals(seeds[i].itemId, seedItemId))
                        {
                            seed = seeds[i];
                            break;
                        }
                }

                if (seed is SeedItem seedItem && seedItem.crop != null) return seedItem.crop;
            }

            if (!string.IsNullOrEmpty(cropAssetName) || !string.IsNullOrEmpty(seedItemId))
                Debug.LogWarning($"[Farming] Saved planter crop '{cropAssetName}' / seed '{seedItemId}' "
                    + "no longer resolves; the tray comes back empty rather than with the wrong crop.");
            return null;
        }
    }
}
