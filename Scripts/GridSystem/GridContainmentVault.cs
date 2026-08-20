// Assets/Scripts/VoxelEngine/GridSystem/GridContainmentVault.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║                   CONTAINMENT VAULT — grid block (Phase 5)            ║
// ║                                                                       ║
// ║  Specialized storage for exotic matter. Antimatter and dark matter    ║
// ║  are containment-class items: plain grid cargo REFUSES them. This     ║
// ║  vault is the only grid storage built to hold them.                   ║
// ║                                                                       ║
// ║  Armoured, hazard-marked, with a violet containment ring — it reads   ║
// ║  like the most dangerous thing on your ship.                          ║
// ║                                                                       ║
// ║  Prefab / item / recipe / research authored by Setup Step 54.         ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridContainmentVault : GridCargoContainer
    {
        public override void OnPlaced()
        {
            base.OnPlaced();
            if (blockName == "Armor Block" || blockName == "Cargo Container")
                blockName = "Containment Vault";
            // Containment-grade storage: this container DOES accept containment items.
            if (container != null) container.allowContainment = true;
        }

        /// <summary>Only containment-class items (antimatter, dark matter, singularity matter).</summary>
        protected override bool MatchesFilter(ItemDefinition item)
        {
            if (item == null) return false;
            return item.requiresContainment;
        }

        // ── LCD data provider display ─────────────────────────────
        public override string DataCategory => "Containment";
        public override string GetDisplayData()
        {
            int total = 0;
            if (container != null)
                for (int i = 0; i < container.Size; i++)
                {
                    var s = container.GetSlot(i);
                    if (s != null && !s.IsEmpty) total += s.count;
                }
            return $"CONTAINMENT VAULT\n{total} exotic units\n{CurrentMassKg / 1000f:0.0} t contained\n{Fill01 * 100f:0}% capacity";
        }
    }
}
