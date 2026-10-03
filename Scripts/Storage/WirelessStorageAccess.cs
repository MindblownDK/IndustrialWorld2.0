// Assets/Scripts/VoxelEngine/Storage/WirelessStorageAccess.cs
//
// 14.40.0 - One resolver for every handheld-wireless question:
// "can THIS player, standing HERE, reach a storage network right now?"
//
// Three conditions, all mandatory:
//   1. The player carries a handheld Wireless Terminal item.
//   2. An ONLINE Wireless Transmitter is within its playerRange.
//   3. No armed Security Block on that transmitter's network refuses
//      wireless access (owner always; team only when shared; never global).
//
// Used by the inventory TERMINAL COMMANDS bay, the fullscreen remote
// terminal, inventory crafting and the building wheel's network costs.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    public static class WirelessStorageAccess
    {
        /// <summary>Does this container hold a handheld Wireless Terminal?</summary>
        public static bool HasHandheldTerminal(IItemContainer container)
        {
            if (container == null) return false;
            for (int i = 0; i < container.Slots.Count; i++)
            {
                var s = container.GetSlot(i);
                if (!s.IsEmpty && s.item is WirelessTerminalItem) return true;
            }
            return false;
        }

        /// <summary>The best transmitter this player can actually use from this
        /// position (closest permitted one), or null. Does NOT check the
        /// handheld item - pass the inventory to TryGetRack for the full gate.</summary>
        public static WirelessTransmitter FindUsableTransmitter(Vector3 playerPos, string playerId)
        {
            var all = WirelessTransmitter.GetAllOnline();
            WirelessTransmitter best = null;
            float bestSqr = float.MaxValue;
            foreach (var t in all)
            {
                if (t == null || t.ConnectedRack == null) continue;
                if (!t.InPlayerRange(playerPos)) continue;
                if (SecurityBlock.WirelessDenierForRack(t.ConnectedRack, playerId) != null) continue;
                float d = (t.transform.position - playerPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = t; }
            }
            return best;
        }

        /// <summary>The full gate: handheld item + transmitter in range +
        /// security clearance. Returns the reachable controller or null.</summary>
        public static ServerRack TryGetRack(IItemContainer inventory, Vector3 playerPos, string playerId)
        {
            if (!HasHandheldTerminal(inventory)) return null;
            var tx = FindUsableTransmitter(playerPos, playerId);
            return tx != null ? tx.ConnectedRack : null;
        }
    }
}
