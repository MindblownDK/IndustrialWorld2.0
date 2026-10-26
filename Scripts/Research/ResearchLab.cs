// Assets/Scripts/VoxelEngine/Research/ResearchLab.cs
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Research
{
    /// <summary>
    /// Workstation that drives the ResearchManager. Holds an internal input container
    /// where the player drops science packs; consumes them whenever the active research
    /// needs them, then ticks progress in real time.
    /// </summary>
    [RequireComponent(typeof(CraftingStation))]
    public class ResearchLab : MonoBehaviour
    {
        public ItemContainer scienceInput;
        public const int INPUT_SLOTS = VoxelEngine.Items.ScienceItem.MaxTier; // 6 slots for Science Packs I–VI

        private void Awake()
        {
            EnsureContainers();
            var station = GetComponent<CraftingStation>();
            if (station != null && station.tier != StationTier.None)
                station.tier = StationTier.None;
        }

        public void EnsureContainers()
        {
            if (scienceInput == null) scienceInput = new ItemContainer("Science", INPUT_SLOTS);
            else scienceInput.Resize(INPUT_SLOTS);
        }

        private void Update()
        {
            EnsureContainers();
            var rm = ResearchManager.Instance;
            if (rm == null || rm.ActiveResearch == null) return;

            // Consume packs once per research session.
            if (!rm.ActiveHasCost)
            {
                if (TryConsumeCost(rm.ActiveResearch))
                    rm.MarkCostPaid();
            }
            rm.TickProgress(Time.deltaTime);
        }

        public bool TryConsumeCost(ResearchNode n, ItemContainer fallbackInventory = null)
        {
            if (n == null || n.cost == null) return false;
            EnsureContainers();
            var rm = ResearchManager.Instance;

            // First check we have everything across lab scienceInput + optional fallback inventory.
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = rm != null ? rm.GetEffectiveCount(n, c.count) : c.count;
                int haveLab = CountPack(scienceInput, c.pack);
                int haveInv = fallbackInventory != null ? CountPack(fallbackInventory, c.pack) : 0;
                if (haveLab + haveInv < need) return false;
            }
            // Remove from lab scienceInput first, then any remainder from fallbackInventory.
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = rm != null ? rm.GetEffectiveCount(n, c.count) : c.count;
                int haveLab = CountPack(scienceInput, c.pack);
                int fromLab = Mathf.Min(haveLab, need);
                if (fromLab > 0) RemovePack(scienceInput, c.pack, fromLab);
                int rem = need - fromLab;
                if (rem > 0 && fallbackInventory != null)
                    RemovePack(fallbackInventory, c.pack, rem);
            }
            return true;
        }

        private static int CountPack(ItemContainer container, ItemDefinition pack)
        {
            if (container == null || pack == null) return 0;
            return string.IsNullOrEmpty(pack.itemId)
                ? container.CountOf(pack)
                : container.CountOfId(pack.itemId);
        }

        private static void RemovePack(ItemContainer container, ItemDefinition pack, int count)
        {
            if (container == null || pack == null || count <= 0) return;
            if (string.IsNullOrEmpty(pack.itemId)) container.Remove(pack, count);
            else container.RemoveId(pack.itemId, count);
        }
    }
}
