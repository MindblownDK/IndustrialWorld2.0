// Assets/Scripts/VoxelEngine/GridSystem/GridWeapon.cs
//
// Basic ship weapon block. Fires projectiles or beams.
// Better than Space Engineers: modular damage types, power-based, sleek FX, grid-aware targeting.

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

            #if ENABLE_INPUT_SYSTEM
            _isFiring = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
            #else
            _isFiring = Input.GetMouseButton(0);
            #endif

            if (!_isFiring) return;

            _fireTimer += Time.deltaTime;
            if (_fireTimer < 1f / fireRate) return;
            _fireTimer = 0;

            Fire();
        }

        private void Fire()
        {
            // Raycast forward
            if (Physics.Raycast(transform.position, transform.forward, out var hit, range))
            {
                // Damage grid blocks or entities
                var targetBlock = hit.collider.GetComponent<GridBlock>();
                if (targetBlock != null && targetBlock.Grid != Grid)
                {
                    targetBlock.Damage(damage);
                }

                // FX
                CreateMuzzleFlash();
                // Future: Projectile or beam
            }
        }

        private void CreateMuzzleFlash()
        {
            // Simple particle or light flash (production polish)
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.transform.position = transform.position + transform.forward * 0.5f;
            flash.transform.localScale = Vector3.one * 0.3f;
            Destroy(flash, 0.1f);
        }
    }
}