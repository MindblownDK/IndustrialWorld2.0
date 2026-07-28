// Assets/Scripts/VoxelEngine/Building/Tiered/PlacedTieredBlock.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// State for a player-placed tiered building piece. The owning prefab is one of
    /// definition.{wood/stone/iron/steel}Prefab depending on current tier.
    /// </summary>
    public class PlacedTieredBlock : MonoBehaviour
    {
        public TieredBlockDefinition definition;
        public BuildTier tier;
        public int       hp;

        public void Initialize(TieredBlockDefinition def, BuildTier t)
        {
            definition = def;
            tier = t;
            hp = def.GetStats(t).hp;
            // Make sure we have a collider for raycasts even if the prefab forgot one.
            if (GetComponentInChildren<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();
        }

        /// <summary>Apply damage from a tool. Returns true if destroyed.</summary>
        public bool Damage(int amount, int toolTier, Inventory recipient)
        {
            // Tool tier check: weaker tools do nothing.
            if (toolTier < definition.GetStats(tier).miningTier) return false;

            hp -= amount;
            if (hp <= 0)
            {
                RefundOnDestroy(recipient);
                Destroy(gameObject);
                return true;
            }
            return false;
        }

        private void RefundOnDestroy(Inventory recipient)
        {
            // Refund 50% of place cost (Rust-ish — discourages tearing things down for full mats).
            if (recipient == null || definition == null) return;
            foreach (var i in definition.placeCost.items)
            {
                if (i.item == null || i.count <= 0) continue;
                int give = Mathf.Max(1, i.count / 2);
                recipient.Add(i.item, give);
            }
        }
    }
}
