// Assets/Scripts/VoxelEngine/Combat/WeaponItem.cs
//
// A wieldable weapon (sword, pistol, grenade, …). Extends ToolItem so it sits in the
// existing tool/hotbar/equipment pipeline, but is intercepted by the combat dispatch
// in PlayerInteractionTool (LMB attacks instead of mining).

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    public class WeaponItem : ToolItem
    {
        public enum AttackMode { Melee, Ranged, Thrown }

        [Header("Combat")]
        public AttackMode attackMode = AttackMode.Melee;

        [Tooltip("Damage applied per hit.")]
        public float damage = 25f;

        [Tooltip("Melee reach or ranged maximum range, in metres.")]
        public float range = 2.5f;

        [Tooltip("Damage type — used for resistances/armour.")]
        public DamageType damageType = DamageType.Melee;

        [Tooltip("Seconds between attacks (auto-fire while LMB is held).")]
        public float attackCooldown = 0.5f;

        [Header("Ammo (Ranged weapons)")]
        [Tooltip("Ammo item consumed per shot. Leave null for no ammo requirement.")]
        public ItemDefinition ammoItem;
        [Tooltip("Ammo consumed per shot.")]
        public int ammoPerShot = 1;

        [Header("Thrown / Explosive (AttackMode.Thrown)")]
        [Tooltip("Explosion radius in metres.")]
        public float explosionRadius = 5f;
        [Tooltip("Radius (metres) of the voxel-terrain crater carved on detonation. 0 = no terrain damage.")]
        public float voxelDamageRadius = 2.5f;
        [Tooltip("Explosive damage applied to every Damageable in the radius.")]
        public float explosionDamage = 80f;
        [Tooltip("Fuse seconds before the thrown bomb detonates.")]
        public float fuseTime = 1.6f;
        [Tooltip("Initial throw speed (m/s).")]
        public float throwForce = 13f;
        [Tooltip("Material used for the explosion VFX. Assigned by the setup wizard.")]
        public Material explosionMaterial;

        public WeaponItem()
        {
            // Neutral tool type — weapons never fall through to mining logic.
            toolType = ToolType.Other;
        }

        // Stackable when configured (e.g. a consumable grenade) so it can be carried in
        // stacks and consumed per throw; unique weapons (sword/pistol) keep maxStack = 1.
        public override bool IsStackable => maxStack > 1;
    }
}
