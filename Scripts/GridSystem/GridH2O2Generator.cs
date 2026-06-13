// Assets/Scripts/VoxelEngine/GridSystem/GridH2O2Generator.cs
//
// H2/O2 Generator (grid block). Electrolyses WATER (from a connected Liquid Tank)
// or melts ICE (from connected cargo) into a water buffer, then splits it into
// Hydrogen and Oxygen held in two internal output tanks. Those output tanks feed
// the grid gas pool. If an output tank is full the player chooses whether to VOID
// the overflow or pause production.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridH2O2Generator : GridBlock
    {
        public const int ICE_SLOTS = 4;
        private const string IceId = "ice";

        [Header("Buffers (litres)")]
        public float waterCapacity = 200f;
        public float waterStored;
        public float h2Capacity = 200f;
        public float h2Stored;
        public float o2Capacity = 200f;
        public float o2Stored;

        [Header("Rates")]
        public float waterPerIce = 20f;
        public float waterPerSecond    = 4f;
        public float hydrogenPerSecond = 4f;
        public float oxygenPerSecond   = 2f;
        public float powerDraw         = 150f;
        public float pullInterval = 1.5f;

        [Header("Overflow")]
        [Tooltip("If true, gas produced when an output tank is full is vented (lost). If false, production pauses.")]
        public bool voidOverflow = false;

        public ItemContainer iceInput;

        public bool   IsProducing { get; private set; }
        public float  CurrentWattage { get; private set; }
        public string Status { get; private set; } = "Idle";
        public float  WaterFill01 => waterCapacity > 0 ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;
        public float  H2Fill01    => h2Capacity > 0 ? Mathf.Clamp01(h2Stored / h2Capacity) : 0f;
        public float  O2Fill01    => o2Capacity > 0 ? Mathf.Clamp01(o2Stored / o2Capacity) : 0f;

        public override float ContentMass =>
            (iceInput != null ? MassUtil.ContainerMass(iceInput) : 0f) + waterStored * 1.0f + (h2Stored + o2Stored) * 0.05f;

        public override float PowerDraw => IsProducing ? powerDraw : 0f;

        private float _pullTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (iceInput == null) iceInput = new ItemContainer("Ice", ICE_SLOTS);
            else iceInput.Resize(ICE_SLOTS);
            iceInput.AcceptFilter = (item, wanted) => IsIce(item) ? wanted : 0;
        }

        private void Update()
        {
            _pullTimer += Time.deltaTime;
            if (_pullTimer >= pullInterval) { _pullTimer = 0f; AutoPullIce(); }
        }

        private void FixedUpdate()
        {
            if (!Enabled) { Status = "Disabled"; IsProducing = false; CurrentWattage = 0; return; }
            if (Grid == null) { Status = "No Grid"; IsProducing = false; CurrentWattage = 0; return; }

            float dt = Time.fixedDeltaTime;

            // 1) Refill the water buffer from ice (melt) or a connected water tank.
            if (waterStored < waterCapacity)
            {
                if (!MeltOneIce()) PullWaterFromTanks(dt);
            }

            bool powered  = Grid.HasPower;
            bool hasWater = waterStored > 0f;

            // 2) Output-space check (or void overflow).
            bool h2Room = h2Stored < h2Capacity;
            bool o2Room = o2Stored < o2Capacity;
            bool blocked = (!h2Room || !o2Room) && !voidOverflow;

            IsProducing = powered && hasWater && !blocked;
            CurrentWattage = IsProducing ? powerDraw : 0f;

            if (!powered)        Status = "No Power";
            else if (!hasWater)  Status = "No Water";
            else if (blocked)    Status = "Output Full";
            else                 Status = "Producing";

            if (IsProducing)
            {
                float consume = Mathf.Min(waterStored, waterPerSecond * dt);
                waterStored -= consume;
                float frac = waterPerSecond > 0 ? consume / (waterPerSecond * dt) : 1f;

                h2Stored += hydrogenPerSecond * dt * frac;
                o2Stored += oxygenPerSecond   * dt * frac;
                if (voidOverflow)
                {
                    if (h2Stored > h2Capacity) h2Stored = h2Capacity;   // vent excess
                    if (o2Stored > o2Capacity) o2Stored = o2Capacity;
                }
            }

            // 3) Push gas through gas pipes into matching grid gas tanks. Tanks then feed
            // the shared grid pool when set to Auto. Oxygen no longer leaks into the ship
            // pool unless an Oxygen tank actually receives it.
            if (GridGasNetwork.Instance != null && GridGasNetwork.Instance.HasPipes(Grid))
            {
                PushGasToTanks(VoxelEngine.Gas.GasType.Hydrogen, ref h2Stored, 30f * dt);
                PushGasToTanks(VoxelEngine.Gas.GasType.Oxygen, ref o2Stored, 30f * dt);
            }
        }

        private void PushGasToTanks(VoxelEngine.Gas.GasType type, ref float storedGas, float maxLitres)
        {
            if (Grid == null || storedGas <= 0f || maxLitres <= 0f) return;
            float remaining = Mathf.Min(storedGas, maxLitres);
            foreach (var kv in Grid.Blocks)
            {
                if (remaining <= 0f) break;
                if (!(kv.Value is GridGasTank tank) || tank == null) continue;
                float accepted = tank.Add(type, remaining);
                if (accepted <= 0f) continue;
                storedGas -= accepted;
                remaining -= accepted;
            }
        }

        private bool MeltOneIce()
        {
            if (iceInput == null) return false;
            for (int i = 0; i < iceInput.Size; i++)
            {
                var s = iceInput.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null || !IsIce(s.item)) continue;
                if (iceInput.Remove(s.item, 1) > 0)
                {
                    waterStored = Mathf.Min(waterCapacity, waterStored + waterPerIce);
                    return true;
                }
            }
            return false;
        }

        // Draw liquid water from connected Liquid Tanks set to Water.
        private void PullWaterFromTanks(float dt)
        {
            if (Grid == null || GridLiquidNetwork.Instance == null || !GridLiquidNetwork.Instance.HasPipes(Grid)) return;
            float want = waterPerSecond * 2f * dt;
            foreach (var t in GridLiquidNetwork.Instance.GetTanks(Grid, LiquidType.Water))
            {
                if (want <= 0f) break;
                if (t == null || t.mode == GridTankMode.Stockpile) continue;
                float got = t.Remove(want);
                waterStored = Mathf.Min(waterCapacity, waterStored + got);
                want -= got;
            }
        }

        private void AutoPullIce()
        {
            if (iceInput == null || Grid == null || GridItemNetwork.Instance == null) return;
            foreach (var cargo in GridItemNetwork.Instance.GetConnectedContainers(Grid))
            {
                if (cargo == null || cargo.container == null) continue;
                for (int i = 0; i < cargo.container.Size; i++)
                {
                    var s = cargo.container.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null || !IsIce(s.item)) continue;
                    if (!iceInput.HasSpace(s.item, 1)) return;
                    int moved = cargo.container.Remove(s.item, s.count);
                    if (moved > 0)
                    {
                        var leftover = iceInput.Insert(new ItemStack { item = s.item, count = moved });
                        if (leftover != null && !leftover.IsEmpty) cargo.container.Insert(leftover);
                    }
                }
            }
        }

        public void ToggleVoidOverflow() => voidOverflow = !voidOverflow;

        private static bool IsIce(ItemDefinition item)
            => item != null && item.itemId != null &&
               item.itemId.Equals(IceId, System.StringComparison.OrdinalIgnoreCase);
    }
}
