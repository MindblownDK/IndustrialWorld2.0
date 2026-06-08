// Assets/Scripts/VoxelEngine/GridSystem/GridWeapon.cs
//
// Ship weapon block.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridWeapon : GridBlock
    {
        [Header("Weapon")]
        public float damage = 50f;
        public float fireRate = 4f;
        public float range = 200f;
        public float powerPerShot = 80f;

        public override float PowerDraw => _isFiring ? powerPerShot * fireRate : 0f;

        private bool _isFiring;
        private float _fireTimer;

        private void Update()
        {
            if (Grid == null || !Grid.IsControlled || !Grid.HasPower) { _isFiring = false; return; }

            _isFiring = Input.GetMouseButton(0);

            if (!_isFiring) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer < 1f / fireRate) return;
            _fireTimer = 0;

            Fire();
        }

        private void Fire()
        {
            if (Physics.Raycast(transform.position, transform.forward, out var hit, range))
            {
                var target = hit.collider.GetComponent<GridBlock>();
                if (target != null && target.Grid != Grid)
                    target.Damage(damage);
            }
        }
    }
}