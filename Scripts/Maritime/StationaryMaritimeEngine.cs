// Assets/Scripts/VoxelEngine/Maritime/StationaryMaritimeEngine.cs
//
// Stationary Maritime Engine — a world-placed diesel power plant for land.
// Burns fuel (solid items or liquid MGO/HFO) and feeds electricity directly
// into the PowerNetwork via a PowerGenerator component.
//
//   • No shafts / gearboxes needed — simplified for stationary use.
//   • Turbo toggle: +40% watt output (same boost ratio as the ship variant).
//   • Place next to a chest (solid fuel) or fluid tank (liquid fuel) and connect
//     a power cable.
//
// The ship-variant engines (GridMaritimeEngine) use the full Burst mechanical
// network; this stationary version is a lightweight MonoBehaviour for bases.

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Maritime
{
    [RequireComponent(typeof(PowerGenerator))]
    public class StationaryMaritimeEngine : MonoBehaviour
    {
        [Header("Fuel")]
        public MaritimeFuelKind fuelKind = MaritimeFuelKind.Liquid;
        public LiquidType liquidFuel = LiquidType.MarineGasOil;

        [Tooltip("Base watts produced while burning.")]
        public float baseWattOutput = 15000f;
        [Tooltip("Fuel buffer capacity. Solid = burn-seconds, Liquid = litres.")]
        public float fuelBufferCapacity = 200f;
        [Tooltip("Consumption per second at full load.")]
        public float consumptionRate = 4f;

        [Header("Turbo")]
        [Tooltip("When true, output is multiplied by 1.4× (simulates an attached turbocharger).")]
        public bool turbocharged = false;

        /// <summary>Current fuel buffer level.</summary>
        public float FuelBuffer { get; private set; }
        public float FuelFill01 => fuelBufferCapacity > 0f ? Mathf.Clamp01(FuelBuffer / fuelBufferCapacity) : 0f;
        public bool IsRunning { get; private set; }

        private PowerGenerator _gen;
        private float _wattOutput;

        private void Awake()
        {
            _gen = GetComponent<PowerGenerator>();
        }

        private void FixedUpdate()
        {
            if (_gen == null) return;

            // Consume fuel.
            float dt = Time.fixedDeltaTime;
            float consumption = consumptionRate * dt;
            FuelBuffer = Mathf.Max(0f, FuelBuffer - consumption);

            // Refill.
            RefillFuel(dt);

            // Toggle the generator.
            IsRunning = FuelBuffer > 0.01f;
            _gen.isOn = IsRunning;

            // Set output watts (with turbo boost).
            _wattOutput = baseWattOutput;
            if (turbocharged) _wattOutput *= MechanicalNode.TurboBoost;
            _gen.wattsPerSecond = _wattOutput;
        }

        private void RefillFuel(float dt)
        {
            float space = fuelBufferCapacity - FuelBuffer;
            if (space < 0.01f) return;

            if (fuelKind == MaritimeFuelKind.Solid)
            {
                if (FuelBuffer < fuelBufferCapacity * 0.3f)
                {
                    float burnSec = FindSolidFuel();
                    if (burnSec > 0f)
                        FuelBuffer = Mathf.Min(fuelBufferCapacity, FuelBuffer + burnSec);
                }
            }
            else
            {
                float drawn = FindLiquidFuel(Mathf.Min(space, consumptionRate * 2f * dt));
                FuelBuffer += drawn;
            }
        }

        // ── Fuel discovery (world blocks, not grid) ───────────────────
        // These scan nearby placed blocks for fuel sources. Simple and self-contained.
        private float FindSolidFuel()
        {
            // Look for a nearby IGridItemStore or Building.Chest within range.
            var colliders = Physics.OverlapSphere(transform.position, 3f);
            foreach (var col in colliders)
            {
                // Try grid cargo containers.
                var gridStore = col.GetComponentInParent<VoxelEngine.GridSystem.IGridItemStore>();
                if (gridStore != null && TryDrawSolidFromStore(gridStore.ItemStore, out float sec))
                    return sec;

                // Try world chests.
                var chest = col.GetComponentInParent<VoxelEngine.Building.Chest>();
                if (chest != null && TryDrawSolidFromStore(chest.container, out float sec2))
                    return sec2;
            }
            return 0f;
        }

        private bool TryDrawSolidFromStore(VoxelEngine.Items.ItemContainer container, out float burnSeconds)
        {
            burnSeconds = 0f;
            if (container == null) return false;
            for (int s = 0; s < container.Size; s++)
            {
                var stack = container.GetSlot(s);
                if (stack == null || stack.IsEmpty) continue;
                if (stack.item is not ResourceItem res) continue;
                if (res.fuelSeconds <= 0f) continue;
                int removed = container.Remove(res, 1);
                if (removed > 0) { burnSeconds = res.fuelSeconds; return true; }
            }
            return false;
        }

        private float FindLiquidFuel(float litres)
        {
            if (litres <= 0f) return 0f;
            var colliders = Physics.OverlapSphere(transform.position, 3f);
            float remaining = litres;
            foreach (var col in colliders)
            {
                if (remaining <= 0.01f) break;
                // World fluid tanks (Building system).
                var tank = col.GetComponentInParent<VoxelEngine.Fluids.WaterTank>();
                if (tank != null)
                {
                    // WaterTank stores water; for fuel tanks we'd need a dedicated block.
                    // This is a hook for Part 4 when MGO/HFO world tanks are added.
                    continue;
                }
            }
            // For now, stationary liquid-fuel engines need their buffer pre-filled
            // via the inspector or a future fluid-pipe connection. Part 4 will wire this.
            return litres - remaining;
        }
    }
}
