// Assets/Scripts/VoxelEngine/Farming/Sprinkler.cs
//
// Powered sprinkler block. Irrigates all FarmPlots within its radius
// as long as it has power AND a water source connected.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Fluids;
using VoxelEngine.Power;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Automatic sprinkler. Place near farm plots and connect to power + water.
    /// All FarmPlots within <see cref="radius"/> are considered irrigated.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class Sprinkler : MonoBehaviour
    {
        [Header("Coverage")]
        [Tooltip("Radius in world units that this sprinkler irrigates.")]
        public float radius = 8f;

        [Header("Water Source")]
        [Tooltip("If true, requires a FluidNode (tank/pipe) nearby to function.")]
        public bool requiresWaterConnection = true;
        [Tooltip("Litres consumed per second while active.")]
        public float waterConsumption = 2f;

        /// <summary>Whether this sprinkler is currently active and irrigating.</summary>
        public bool IsActive { get; private set; }

        private PowerConsumer _power;
        private float _waterCheckTimer;

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
        }

        private void Update()
        {
            // Power check.
            bool hasPower = (_power == null || _power.IsPowered);
            if (!hasPower) { IsActive = false; return; }

            // Water source check.
            if (requiresWaterConnection)
            {
                _waterCheckTimer += Time.deltaTime;
                if (_waterCheckTimer >= 2f)
                {
                    _waterCheckTimer = 0f;
                    IsActive = CheckWaterSource();
                }
            }
            else
            {
                IsActive = true;
            }

            // Consume water from connected fluid network.
            if (IsActive && requiresWaterConnection)
            {
                ConsumeWater(waterConsumption * Time.deltaTime);
            }
        }

        private bool CheckWaterSource()
        {
            // Look for a FluidNode (tank/pipe) nearby.
            var hits = Physics.OverlapSphere(transform.position, 3f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var node = col.GetComponent<FluidNode>();
                if (node != null && node.network != null)
                {
                    // Check if any tank in the network has water.
                    foreach (var n in node.network.nodes)
                    {
                        if (n is WaterTank t && t.water > 1f)
                            return true;
                    }
                }
            }

            // Also accept water in the voxel world nearby.
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world != null)
            {
                var pos = world.WorldToVoxel(transform.position);
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    var v = world.GetVoxelWorld(new Vector3Int(pos.x + dx, pos.y - 1, pos.z + dz));
                    if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel)
                        return true;
                }
            }

            return false;
        }

        private void ConsumeWater(float litres)
        {
            var hits = Physics.OverlapSphere(transform.position, 3f);
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var node = col.GetComponent<FluidNode>();
                if (node == null || node.network == null) continue;
                foreach (var n in node.network.nodes)
                {
                    if (n is WaterTank t && t.water > litres)
                    {
                        t.TakeSome(litres);
                        return;
                    }
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsActive ? new Color(0.2f, 0.7f, 1f, 0.3f) : new Color(0.5f, 0.5f, 0.5f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
