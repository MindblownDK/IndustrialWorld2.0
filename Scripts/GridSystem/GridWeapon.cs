// Assets/Scripts/VoxelEngine/GridSystem/GridWeapon.cs
//
// Ship weapon block. Fires while controlled + powered, consuming ammunition
// from its 6-slot ammo inventory.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridWeapon : GridBlock
    {
        public const int AMMO_SLOTS = 6;
        private const string AmmoId = "ammo";

        [Header("Weapon")]
        public float damage = 50f;
        public float fireRate = 4f;
        public float range = 200f;
        public float powerPerShot = 80f;

        [Tooltip("Ammunition inventory (6 slots).")]
        public ItemContainer ammo;

        public override float PowerDraw => _isFiring ? powerPerShot * fireRate : 0f;
        public override float ContentMass => ammo != null ? MassUtil.ContainerMass(ammo) : 0f;

        private bool _isFiring;
        private float _fireTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (ammo == null) ammo = new ItemContainer("Ammo", AMMO_SLOTS);
            else ammo.Resize(AMMO_SLOTS);
            // Only ammunition may be placed in the ammo slots.
            ammo.AcceptFilter = (item, wanted) => IsAmmo(item) ? wanted : 0;
        }

        private void Update()
        {
            if (Grid == null || !Grid.IsControlled || !Grid.HasPower || !Grid.IsSelectedTool(this)) { _isFiring = false; return; }

            _isFiring = GridInput.Mouse0 && HasAmmo();
            if (!_isFiring) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer < 1f / fireRate) return;
            _fireTimer = 0;

            Fire();
        }

        private bool HasAmmo()
        {
            if (ammo == null) return false;
            for (int i = 0; i < ammo.Size; i++)
            {
                var s = ammo.GetSlot(i);
                if (s != null && !s.IsEmpty && s.item != null && IsAmmo(s.item)) return true;
            }
            return false;
        }

        private void ConsumeOneAmmo()
        {
            if (ammo == null) return;
            for (int i = 0; i < ammo.Size; i++)
            {
                var s = ammo.GetSlot(i);
                if (s != null && !s.IsEmpty && s.item != null && IsAmmo(s.item))
                {
                    ammo.Remove(s.item, 1);
                    return;
                }
            }
        }

        private void Fire()
        {
            ConsumeOneAmmo();
            if (Physics.Raycast(transform.position, transform.forward, out var hit, range))
            {
                var target = hit.collider.GetComponent<GridBlock>();
                if (target != null && target.Grid != Grid)
                    target.Damage(damage);
            }
        }

        private static bool IsAmmo(ItemDefinition item)
            => item != null && item.itemId != null &&
               item.itemId.IndexOf(AmmoId, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
