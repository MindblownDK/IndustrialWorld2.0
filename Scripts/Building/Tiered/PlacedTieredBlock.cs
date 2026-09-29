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
                // A fitted code lock survives demolition as an item (13.17.0).
                var codeLock = GetComponentInChildren<CodeLock>(true);
                if (codeLock != null && recipient != null)
                {
                    var lockItem = CodeLock.ResolveItem();
                    if (lockItem != null) recipient.Add(lockItem, 1);
                }
                // Multiplayer: the authority point for destruction (14.4.0).
                VoxelEngine.Networking.BuildingSync.AnnounceRemoved(definition.family, transform.position);
                Destroy(gameObject);
                return true;
            }
            // Visible cracks proportional to structural loss (9.30.0).
            int max = Mathf.Max(1, definition.GetStats(tier).hp);
            VoxelEngine.Thermal.BlockDamageVisual.ReportDamage(this, 1f - Mathf.Clamp01(hp / (float)max));
            // Multiplayer: surviving hits crack on every machine (14.5.1).
            VoxelEngine.Networking.BuildingSync.AnnounceDamaged(definition.family, transform.position, hp);
            return false;
        }

        private void OnDestroy()
        {
            // Scene unload and application quit also call OnDestroy; only a
            // gameplay demolition should wake the structural audits.
            if (!gameObject.scene.isLoaded) return;
            StructuralLoadState.NotifySupportRemoved(transform.position);
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
