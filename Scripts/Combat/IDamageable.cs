// Assets/Scripts/VoxelEngine/Combat/IDamageable.cs
//
// Shared damage contract for everything that can be hurt: training dummies,
// mythical enemies, future grid blocks, and the player. Centralising this keeps
// weapons, turrets, explosions, and hazards talking to one interface.

using UnityEngine;

namespace VoxelEngine.Combat
{
    /// <summary>Typed damage so armour/resistances can react differently per source.</summary>
    public enum DamageType
    {
        Kinetic,     // bullets, slugs
        Melee,       // swords, clubs
        Explosive,   // bombs, shells
        Fire,        // flamethrower, Ifrit
        Electrical,  // energy/relic weapons
    }

    /// <summary>A single damage application (amount, type, where it hit, where it came from).</summary>
    public struct DamageEvent
    {
        public float      amount;
        public DamageType type;
        public Vector3    point;
        public Vector3    direction;
        public GameObject source;
    }

    /// <summary>Anything with health that weapons/turrets/hazards can damage.</summary>
    public interface IDamageable
    {
        void TakeDamage(DamageEvent e);
        bool IsAlive { get; }
    }
}
