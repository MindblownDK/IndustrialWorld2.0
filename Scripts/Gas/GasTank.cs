// Assets/Scripts/VoxelEngine/Gas/GasTank.cs
//
// World/static gas storage. Configurable gas type, I/O flags, and (when set to
// Hydrogen) a portable-tank dock that fills Portable Hydrogen Tanks from bulk.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    public class GasTank : MonoBehaviour
    {
        [Header("Tank")]
        [Tooltip("Maximum gas units this tank can store (ml-equivalent for H₂ player gear).")]
        public float capacity = 1000f;

        [Tooltip("Locked / selected gas type. None = auto-detect from first fill.")]
        public GasType selectedGasType = GasType.None;

        [Tooltip("What gas is currently stored.")]
        public GasType storedGasType = GasType.None;

        [Tooltip("Current amount of gas stored.")]
        public float storedAmount;

        [Header("I/O")]
        public bool acceptInput = true;
        public bool allowOutput = true;

        [Header("Portable dock")]
        [Tooltip("When selected/stored gas is Hydrogen, empty portable tanks in this slot are filled from bulk.")]
        public float portableFillRateMlPerSecond = 400f;

        public ItemContainer PortableSlot { get; private set; }

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(storedAmount / capacity) : 0f;
        public GasType EffectiveType => storedGasType != GasType.None ? storedGasType : selectedGasType;
        public bool IsHydrogenMode => EffectiveType == GasType.Hydrogen;

        private void Awake() => EnsureContainers();

        private void Update()
        {
            if (!IsHydrogenMode) return;
            TickPortableDock(Time.deltaTime);
        }

        public void EnsureContainers()
        {
            if (PortableSlot == null)
            {
                PortableSlot = new ItemContainer("Portable H₂ Dock", 1);
            }
            else PortableSlot.Resize(1);
            // Accepts Portable Hydrogen Tanks AND any hydrogen-fed jetpack
            // (Hydrogen Boost / Hybrid) — both fill straight from bulk H₂.
            PortableSlot.AcceptFilter = (item, wanted) =>
            {
                if (HydrogenCanisterItem.IsPortableHydrogenTank(item)) return Mathf.Min(1, wanted);
                if (item is VoxelEngine.Items.JetpackItem jp && jp.UsesHydrogenEffective) return Mathf.Min(1, wanted);
                return 0;
            };
        }

        /// <summary>Player-selected gas type. Fails if tank holds a different gas.</summary>
        public bool TrySetSelectedGasType(GasType type)
        {
            if (storedAmount > 0.001f && storedGasType != GasType.None && storedGasType != type)
                return false;
            selectedGasType = type;
            if (storedAmount <= 0.001f)
                storedGasType = type == GasType.None ? GasType.None : type;
            return true;
        }

        public float TryAdd(GasType type, float amount)
        {
            if (!acceptInput || amount <= 0f) return 0f;
            if (selectedGasType != GasType.None && type != selectedGasType) return 0f;
            if (storedGasType != GasType.None && storedGasType != type) return 0f;
            storedGasType = type;
            if (selectedGasType == GasType.None) selectedGasType = type;
            float space = capacity - storedAmount;
            float add = Mathf.Min(space, amount);
            storedAmount += add;
            return add;
        }

        public float TryTake(GasType type, float amount)
        {
            if (!allowOutput || amount <= 0f) return 0f;
            if (storedGasType != type) return 0f;
            float take = Mathf.Min(storedAmount, amount);
            storedAmount -= take;
            if (storedAmount <= 0.0001f)
            {
                storedAmount = 0f;
                // Keep selected type so the tank stays configured; clear stored type only if unlocked.
                if (selectedGasType == GasType.None) storedGasType = GasType.None;
                else storedGasType = selectedGasType;
            }
            return take;
        }

        /// <summary>Fill a portable hydrogen tank stack from bulk. Returns ml transferred.</summary>
        public float FillPortable(ItemStack portable, float maxMl)
        {
            if (portable == null || !HydrogenCanisterItem.IsPortableHydrogenTank(portable.item)) return 0f;
            if (EffectiveType != GasType.Hydrogen || storedAmount <= 0f || !allowOutput) return 0f;
            int space = HydrogenCanisterItem.GetCapacityMl(portable) - HydrogenCanisterItem.GetStoredMl(portable);
            if (space <= 0) return 0f;
            float want = Mathf.Min(maxMl, space);
            float taken = TryTake(GasType.Hydrogen, want);
            if (taken <= 0f) return 0f;
            HydrogenCanisterItem.TryAddMl(portable, Mathf.RoundToInt(taken));
            return taken;
        }

        /// <summary>Fill a hydrogen jetpack stack from bulk H₂. Returns ml transferred.</summary>
        public float FillJetpack(ItemStack pack, float maxMl)
        {
            if (pack == null || pack.IsEmpty || pack.item is not VoxelEngine.Items.JetpackItem jp) return 0f;
            if (!jp.UsesHydrogenEffective) return 0f;
            if (EffectiveType != GasType.Hydrogen || storedAmount <= 0f || !allowOutput) return 0f;
            int space = jp.HydrogenCapacityMl - VoxelEngine.Items.JetpackItem.GetH2Ml(pack);
            if (space <= 0) return 0f;
            float want = Mathf.Min(maxMl, space);
            float taken = TryTake(GasType.Hydrogen, want);
            if (taken <= 0f) return 0f;
            return VoxelEngine.Items.JetpackItem.AddH2(pack, Mathf.RoundToInt(taken));
        }

        private void TickPortableDock(float dt)
        {
            EnsureContainers();
            if (dt <= 0f || storedAmount <= 0f || !allowOutput) return;
            var stack = PortableSlot.GetSlot(0);
            if (stack == null || stack.IsEmpty) return;
            float rate = Mathf.Max(1f, portableFillRateMlPerSecond) * dt;
            float got = FillPortable(stack, rate);
            if (got <= 0f && stack.item is VoxelEngine.Items.JetpackItem jp && jp.UsesHydrogenEffective)
            {
                // Hydrogen jetpack in the dock — fill its H₂ tank straight from bulk,
                // taking ONLY what fits (never drawing ml the pack would void).
                int space = Mathf.Max(0, jp.HydrogenCapacityMl - VoxelEngine.Items.JetpackItem.GetH2Ml(stack));
                if (space > 0)
                {
                    float want = Mathf.Min(rate, space);
                    float taken = TryTake(GasType.Hydrogen, want);
                    if (taken > 0f)
                        got = VoxelEngine.Items.JetpackItem.AddH2(stack, Mathf.RoundToInt(taken));
                }
            }
            if (got > 0f) PortableSlot.SetSlot(0, stack);
        }
    }
}
