// Assets/Scripts/VoxelEngine/Combat/WeaponItem.cs
//
// A wieldable weapon (sword, pistol, …). Extends ToolItem so it sits in the
// existing tool/hotbar/equipment pipeline, but is intercepted by the combat
// dispatch in PlayerInteractionTool (LMB attacks instead of mining).

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Combat
{
    public class WeaponItem : ToolItem
    {
        public enum AttackMode { Melee, Ranged }

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

        public WeaponItem()
        {
            // Neutral tool type — weapons never fall through to mining logic.
            toolType = ToolType.Other;
        }
    }
}
