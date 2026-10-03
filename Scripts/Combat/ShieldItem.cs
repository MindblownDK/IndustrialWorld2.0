// Assets/Scripts/VoxelEngine/Combat/ShieldItem.cs
//
// 14.37.0-dev - the crusader shield. Hold it and HOLD RIGHT MOUSE to raise
// it: while raised, incoming damage is reduced by blockReduction and the
// shield pays a little durability for every real hit it eats. The face of
// the shield wears the holder's TEAM BANNER cloth - the same image the
// banner block and the grid screen fly, live from TeamBannerRegistry.
//
// ShieldBlock is the one static answer PlayerStats asks on every wound:
// "is a shield up right now, and how much does it eat?" The interaction
// tool keeps it current every frame and clears it on every early-out, so
// a stale "blocking" can never survive a menu opening mid-fight.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Shield", fileName = "Shield_New")]
    public class ShieldItem : ToolItem
    {
        [Header("Shield")]
        [Tooltip("Fraction of incoming damage absorbed while the shield is raised (0.65 = 65% blocked).")]
        [Range(0.1f, 0.95f)] public float blockReduction = 0.65f;
        [Tooltip("Durability charged per real hit absorbed while blocking.")]
        public int durabilityPerBlockedHit = 2;
    }

    /// <summary>Session-wide blocking state for the LOCAL player. Written by
    /// PlayerInteractionTool every frame, read by PlayerStats on damage and
    /// by PlayerAvatar when it replicates the blocking pose.</summary>
    public static class ShieldBlock
    {
        public static bool Active { get; private set; }
        public static float Reduction { get; private set; }

        private static ItemStack _stack;
        private static Inventory _inventory;
        private static int _durabilityPerHit;
        private static float _nextDurabilityCharge;

        public static void Set(bool active, float reduction, ItemStack stack,
            Inventory inventory, int durabilityPerHit)
        {
            Active = active;
            Reduction = active ? Mathf.Clamp(reduction, 0.1f, 0.95f) : 0f;
            _stack = active ? stack : null;
            _inventory = active ? inventory : null;
            _durabilityPerHit = Mathf.Max(1, durabilityPerHit);
        }

        public static void Clear() => Set(false, 0f, null, null, 1);

        /// <summary>Run one wound through the raised shield: returns what
        /// still gets through, and charges durability for real hits. The
        /// durability charge is rate-limited so damage-over-time ticks do
        /// not shred a shield in seconds.</summary>
        public static float AbsorbHit(float amount)
        {
            if (!Active || amount <= 0f) return amount;
            float through = amount * (1f - Reduction);

            if (amount >= 1f && Time.time >= _nextDurabilityCharge
                && _stack != null && !_stack.IsEmpty && _inventory != null)
            {
                _nextDurabilityCharge = Time.time + 0.25f;
                var inventory = _inventory;
                _stack.durability -= _durabilityPerHit;
                if (_stack.durability <= 0)
                {
                    // The shield splinters in the holder's hand.
                    var slot = inventory.container.GetSlot(inventory.activeHotbarIndex);
                    if (slot == _stack)
                        inventory.container.SetSlot(inventory.activeHotbarIndex, new ItemStack());
                    Clear();
                }
                inventory.container.RaiseChanged();
            }
            return through;
        }
    }
}
