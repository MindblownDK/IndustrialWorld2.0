// Assets/Scripts/VoxelEngine/Items/FurnaceUpgradeItem.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// What a universal machine module DOES. Exactly one role per module asset, so the
    /// machine panels can name the effect, the slots can refuse a module that machine
    /// cannot honour, and the shared resolver in <c>VoxelEngine.Simulation.MachineModules</c>
    /// can report it without guessing from the numbers.
    ///
    /// 18.15.0-dev: the two modules authored by setup step 75 (Speed, Efficiency) had no
    /// role at all - a machine read every module's multipliers unconditionally, which is
    /// why a module could be socketed into a machine that had no use for it and simply do
    /// nothing. The role is a NEW field, so an asset authored before this round keeps its
    /// default (Speed) and is repaired by itemId in setup step 127; reading a module never
    /// depends on the role being right, only the panel label and the slot filter do.
    /// </summary>
    public enum MachineModuleKind : byte
    {
        /// <summary>Multiplies throughput. x1.25 per module by default.</summary>
        Speed = 0,
        /// <summary>Multiplies power draw. x0.8 per module by default (less power for the same work).</summary>
        Efficiency = 1,
        /// <summary>Gives every finished batch a chance at one extra unit of the main product.</summary>
        Yield = 2,
    }

    /// <summary>
    /// Which module roles a machine can actually honour, as a flag set. A machine's upgrade
    /// slots only accept the roles listed here, so a Yield module can never sit in a slot on
    /// a block that has no batch output to boost - it is refused at the slot instead of
    /// silently doing nothing.
    /// </summary>
    [System.Flags]
    public enum MachineModuleSupport : byte
    {
        None = 0,
        // One bit per MachineModuleKind, written as literals so the values are compile-time
        // constants: bit 0 = Speed, bit 1 = Efficiency, bit 2 = Yield.
        Speed = 1,
        Efficiency = 2,
        Yield = 4,
        All = Speed | Efficiency | Yield,
    }

    /// <summary>
    /// Upgrade module that can be inserted into a machine's universal upgrade slots.
    /// Multipliers stack multiplicatively per stack count (1.5 with 2 stacks = 2.25x).
    ///
    /// This is the ONE item class every universal machine module uses - Speed, Efficiency
    /// and Yield alike - so a tuned asset, a saved stack and a research unlock keep working
    /// across the whole factory instead of forking into a class per module family.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Furnace Upgrade", fileName = "Upgrade_New")]
    public class FurnaceUpgradeItem : ItemDefinition
    {
        [Tooltip("Multiplier applied per upgrade module to processing SPEED. Use values like 1.25 (25% faster).")]
        public float speedMultiplier = 1.25f;
        [Tooltip("Multiplier applied per module to power DRAW. Use values like 0.8 (uses 20% less power).")]
        public float efficiencyMultiplier = 1.0f;

        [Header("Universal Module (18.15.0-dev)")]
        [Tooltip("What this module does. Speed and Efficiency modules scale their two multipliers above; "
               + "the Yield module uses the bonus chance below. A machine only accepts the roles it can honour.")]
        public MachineModuleKind moduleKind = MachineModuleKind.Speed;

        [Tooltip("Yield modules only: chance per module that a finished batch produces one extra unit of the "
               + "recipe's main product. 0.10 = 10% per module. Ignored by every other module role.")]
        [Range(0f, 0.5f)]
        public float yieldBonusChance = 0f;

        /// <summary>The flag bit this module occupies in <see cref="MachineModuleSupport"/>.</summary>
        public MachineModuleSupport SupportBit => (MachineModuleSupport)(1 << (int)moduleKind);

        /// <summary>
        /// True when the module has a real effect to contribute. A module with no role
        /// effect at all (a hand-authored placeholder) is refused by every slot rather
        /// than occupying one and doing nothing.
        /// </summary>
        public bool HasEffect => moduleKind switch
        {
            MachineModuleKind.Yield => yieldBonusChance > 0f,
            _ => !Mathf.Approximately(speedMultiplier, 1f) || !Mathf.Approximately(efficiencyMultiplier, 1f),
        };

        /// <summary>One-line effect summary used by the module tooltip and the machine panel.</summary>
        public string EffectSummary() => moduleKind switch
        {
            MachineModuleKind.Yield => $"+{Mathf.RoundToInt(yieldBonusChance * 100f)}% bonus batch yield",
            _ => $"{SpeedLabel()} throughput · {PowerLabel()} power draw",
        };

        private string SpeedLabel()
            => Mathf.Approximately(speedMultiplier, 1f) ? "x1" : $"x{speedMultiplier:0.##}";

        private string PowerLabel()
            => Mathf.Approximately(efficiencyMultiplier, 1f) ? "x1" : $"x{efficiencyMultiplier:0.##}";
    }
}
