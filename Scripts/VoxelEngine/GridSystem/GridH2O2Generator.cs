// Assets/Scripts/VoxelEngine/GridSystem/GridH2O2Generator.cs
//
// H2/O2 Generator (grid block). Melts Ice into a water buffer, then electrolyses
// the water into Hydrogen and Oxygen which feed the grid gas pool.
//
//   • 4 ice input slots (UI-visible)
//   • Internal water tank (shown as a gauge, TankContents / TankCapacity)
//   • Live wattage + status readout
//   • Auto-pulls Ice from any cargo container connected on the grid

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridH2O2Generator : GridBlock
    {
        public const int ICE_SLOTS = 4;
        private const string IceId = "ice";

        [Header("H2/O2 Generator")]
        [Tooltip("Litres of water held in the internal buffer tank.")]
        public float waterCapacity = 200f;
        public float waterStored;

        [Tooltip("Litres of water produced when one Ice melts.")]
        public float waterPerIce = 20f;

        public float waterPerSecond     = 4f;
        public float hydrogenPerSecond  = 2f;
        public float oxygenPerSecond    = 1f;
        public float powerDraw          = 150f;

        [Tooltip("Auto-pull ice from connected cargo every N seconds.")]
        public float pullInterval = 1.5f;

        public ItemContainer iceInput;

        // Runtime/UI state
        public bool   IsProducing { get; private set; }
        public float  CurrentWattage { get; private set; }
        public string Status { get; private set; } = "Idle";
        public float  WaterFill01 => waterCapacity > 0 ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;

        // Held ice contributes a little mass.
        public override float ContentMass =>
            (iceInput != null ? MassUtil.ContainerMass(iceInput) : 0f) + waterStored * 1.0f;

        public override float PowerDraw => IsProducing ? powerDraw : 0f;

        private float _pullTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (iceInput == null) iceInput = new ItemContainer("Ice", ICE_SLOTS);
            else iceInput.Resize(ICE_SLOTS);
            // Only ice may be placed in the ice slots.
            iceInput.AcceptFilter = (item, wanted) => IsIce(item) ? wanted : 0;
        }

        private void Update()
        {
            _pullTimer += Time.deltaTime;
            if (_pullTimer >= pullInterval)
            {
                _pullTimer = 0f;
                AutoPullIce();
            }
        }

        private void FixedUpdate()
        {
            if (Grid == null) { Status = "No Grid"; IsProducing = false; CurrentWattage = 0; return; }

            float dt = Time.fixedDeltaTime;

            // 1) Melt ice into the water buffer if there's room.
            if (waterStored < waterCapacity) MeltOneIce();

            // 2) Electrolyse: requires power + water.
            bool powered = Grid.HasPower;
            bool hasWater = waterStored > 0f;
            IsProducing = powered && hasWater;
            CurrentWattage = IsProducing ? powerDraw : 0f;

            if (!powered)        Status = "No Power";
            else if (!hasWater)  Status = "No Water";
            else                 Status = "Producing";

            if (IsProducing)
            {
                float consume = Mathf.Min(waterStored, waterPerSecond * dt);
                waterStored -= consume;
                float frac = waterPerSecond > 0 ? consume / (waterPerSecond * dt) : 1f;
                Grid.HydrogenStored += hydrogenPerSecond * dt * frac;
                Grid.OxygenStored   += oxygenPerSecond   * dt * frac;
            }
        }

        private void MeltOneIce()
        {
            if (iceInput == null) return;
            for (int i = 0; i < iceInput.Size; i++)
            {
                var s = iceInput.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                if (!IsIce(s.item)) continue;
                if (iceInput.Remove(s.item, 1) > 0)
                {
                    waterStored = Mathf.Min(waterCapacity, waterStored + waterPerIce);
                    return;
                }
            }
        }

        // Pull ice from any connected cargo container into the 4 ice slots.
        private void AutoPullIce()
        {
            if (iceInput == null || Grid == null || GridItemNetwork.Instance == null) return;

            var containers = GridItemNetwork.Instance.GetConnectedContainers(Grid);
            foreach (var cargo in containers)
            {
                if (cargo == null || cargo.container == null) continue;
                for (int i = 0; i < cargo.container.Size; i++)
                {
                    var s = cargo.container.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null || !IsIce(s.item)) continue;
                    if (!iceInput.HasSpace(s.item, 1)) return; // ice slots full

                    int moved = cargo.container.Remove(s.item, s.count);
                    if (moved > 0)
                    {
                        var leftover = iceInput.Insert(new ItemStack { item = s.item, count = moved });
                        // Anything we couldn't fit goes back into the cargo.
                        if (leftover != null && !leftover.IsEmpty)
                            cargo.container.Insert(leftover);
                    }
                }
            }
        }

        private static bool IsIce(ItemDefinition item)
            => item != null && item.itemId != null &&
               item.itemId.Equals(IceId, System.StringComparison.OrdinalIgnoreCase);
    }
}
