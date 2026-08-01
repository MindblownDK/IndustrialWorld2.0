// Assets/Scripts/VoxelEngine/GridSystem/GridGasTank.cs
//
// Gas storage tank for ships. Configurable gas type. When set to Hydrogen,
// docks Portable Hydrogen Tanks and fills them from bulk ship gas.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridGasTank : GridBlock, IGridDataProvider
    {
        [Header("Gas Storage")]
        public Gas.GasType gasType = Gas.GasType.Hydrogen;
        [Tooltip("Capacity in litres of gas (UI). Internally stored as the same numeric units as world tanks for H₂ gear (ml-equivalent).")]
        public float capacity = 500f;
        public float stored;

        [Tooltip("Auto feeds the grid gas pool. Stockpile keeps gas reserved in this tank.")]
        public GridTankMode mode = GridTankMode.Auto;

        [Header("Portable dock")]
        public float portableFillRateMlPerSecond = 400f;

        public ItemContainer PortableSlot { get; private set; }

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0f;
        public bool IsHydrogenMode => gasType == Gas.GasType.Hydrogen;

        public override float ContentMass => stored * 0.05f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            EnsureContainers();
            blockName = $"{gasType} Tank";
        }

        private void Awake() => EnsureContainers();

        private void Update()
        {
            stored = Mathf.Clamp(stored, 0f, capacity);
            if (IsHydrogenMode) TickPortableDock(Time.deltaTime);
        }

        public void EnsureContainers()
        {
            if (PortableSlot == null)
            {
                PortableSlot = new ItemContainer("Portable H₂ Dock", 1);
            }
            else PortableSlot.Resize(1);
            // Accepts Portable Hydrogen Tanks AND any hydrogen-fed jetpack.
            PortableSlot.AcceptFilter = (item, wanted) =>
            {
                if (HydrogenCanisterItem.IsPortableHydrogenTank(item)) return Mathf.Min(1, wanted);
                if (item is JetpackItem jp && jp.UsesHydrogenEffective) return Mathf.Min(1, wanted);
                return 0;
            };
        }

        public bool SetGasType(Gas.GasType t)
        {
            if (stored > 0.001f && gasType != t) return false;
            gasType = t;
            blockName = $"{gasType} Tank";
            return true;
        }

        public float Add(VoxelEngine.Gas.GasType type, float litres)
        {
            if (type == VoxelEngine.Gas.GasType.None || litres <= 0f) return 0f;
            if (stored > 0.001f && gasType != type) return 0f;
            if (stored <= 0.001f) gasType = type;
            float space = Mathf.Max(0f, capacity - stored);
            float take = Mathf.Min(space, litres);
            stored += take;
            blockName = $"{gasType} Tank";
            return take;
        }

        public float Draw(float litres, bool ignoreStockpile = false)
        {
            if (!ignoreStockpile && mode == GridTankMode.Stockpile) return 0f;
            float take = Mathf.Min(stored, Mathf.Max(0f, litres));
            stored -= take;
            return take;
        }

        /// <summary>Fill a portable H₂ tank from bulk. Returns ml transferred.</summary>
        public float FillPortable(ItemStack portable, float maxMl)
        {
            if (!IsHydrogenMode || portable == null) return 0f;
            if (!HydrogenCanisterItem.IsPortableHydrogenTank(portable.item)) return 0f;
            int space = HydrogenCanisterItem.GetCapacityMl(portable) - HydrogenCanisterItem.GetStoredMl(portable);
            if (space <= 0 || stored <= 0f) return 0f;
            float want = Mathf.Min(maxMl, space, stored);
            // Grid tank units treated as ml-equivalent for player gear fill.
            float taken = Draw(want, ignoreStockpile: true);
            if (taken <= 0f) return 0f;
            HydrogenCanisterItem.TryAddMl(portable, Mathf.RoundToInt(taken));
            return taken;
        }

        /// <summary>Fill a hydrogen jetpack stack from bulk ship H₂. Returns ml transferred.</summary>
        public float FillJetpack(ItemStack pack, float maxMl)
        {
            if (!IsHydrogenMode || pack == null || pack.IsEmpty) return 0f;
            if (pack.item is not JetpackItem jp || !jp.UsesHydrogenEffective) return 0f;
            int space = jp.HydrogenCapacityMl - JetpackItem.GetH2Ml(pack);
            if (space <= 0 || stored <= 0f) return 0f;
            float want = Mathf.Min(maxMl, space, stored);
            float taken = Draw(want, ignoreStockpile: true);
            if (taken <= 0f) return 0f;
            return JetpackItem.AddH2(pack, Mathf.RoundToInt(taken));
        }

        private void TickPortableDock(float dt)
        {
            EnsureContainers();
            if (dt <= 0f || stored <= 0f) return;
            var stack = PortableSlot.GetSlot(0);
            if (stack == null || stack.IsEmpty) return;
            float rate = Mathf.Max(1f, portableFillRateMlPerSecond) * dt;
            float got = FillPortable(stack, rate);
            if (got <= 0f && stack.item is JetpackItem jp && jp.UsesHydrogenEffective)
            {
                // Hydrogen jetpack in the dock — fill only what fits, never voiding ml.
                int space = Mathf.Max(0, jp.HydrogenCapacityMl - JetpackItem.GetH2Ml(stack));
                if (space > 0)
                {
                    float want = Mathf.Min(rate, space, stored);
                    float taken = Draw(want, ignoreStockpile: true);
                    if (taken > 0f) got = JetpackItem.AddH2(stack, Mathf.RoundToInt(taken));
                }
            }
            if (got > 0f) PortableSlot.SetSlot(0, stack);
        }

        public string SourceName => blockName;
        public string DataCategory => "Gas";
        public string GetDisplayData()
        {
            return $"GAS\n{gasType}\n{Fill01 * 100f:0}%\n{stored:0.0} / {capacity:0.0}\nMode: {mode}";
        }
    }
}
