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
        public const int INPUT_SLOTS = 3; // 3 slots for the 3 science tiers

        private void Awake() => EnsureContainers();

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

        private bool TryConsumeCost(ResearchNode n)
        {
            // First check we have everything.
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                if (scienceInput.CountOf(c.pack) < c.count) return false;
            }
            // Now actually remove.
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                scienceInput.Remove(c.pack, c.count);
            }
            return true;
        }
    }
}
