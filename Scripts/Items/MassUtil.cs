// Assets/Scripts/VoxelEngine/Items/MassUtil.cs
//
// Helpers for summing the mass (kg) of item containers. Used by inventory weight
// headers, cargo mass caps, and grid ship-mass calculation.

using System.Collections.Generic;

namespace VoxelEngine.Items
{
    public static class MassUtil
    {
        /// <summary>Total mass (kg) of every stack in a container.</summary>
        public static float ContainerMass(ItemContainer c)
        {
            if (c == null) return 0f;
            float kg = 0f;
            for (int i = 0; i < c.Size; i++)
            {
                var s = c.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                kg += s.item.massPerUnit * s.count;
            }
            return kg;
        }

        /// <summary>Total mass (kg) across several containers.</summary>
        public static float ContainersMass(IEnumerable<ItemContainer> containers)
        {
            float kg = 0f;
            if (containers == null) return 0f;
            foreach (var c in containers) kg += ContainerMass(c);
            return kg;
        }

        /// <summary>How much mass (kg) adding <paramref name="count"/> of <paramref name="item"/> would add.</summary>
        public static float StackMass(ItemDefinition item, int count)
            => item == null ? 0f : item.massPerUnit * count;
    }
}
