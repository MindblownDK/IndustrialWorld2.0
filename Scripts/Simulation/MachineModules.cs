// Assets/Scripts/VoxelEngine/Simulation/MachineModules.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — UNIVERSAL MACHINE MODULES (18.15.0-dev)      ║
// ║  One resolver for every machine's upgrade slots.                 ║
// ╚══════════════════════════════════════════════════════════════════╝
//
// Before this round every machine carried its own copy of RecalculateUpgrades():
// nine near-identical loops that multiplied speed and efficiency, ignored the module
// role, applied no upper or lower bound, and installed no AcceptFilter on the slots.
// The consequences were all the same three:
//
//   1. Any item at all could be dropped into an upgrade slot. A Quarry Range upgrade
//      or a maritime engine module sat in a Crusher slot doing nothing, and the slot
//      gave no sign that it had been wasted.
//   2. A machine's slots offered the same modules whether or not it could use them,
//      because no machine declared what it could honour.
//   3. There was nowhere to hang a third module role: bonus yield needs the machine's
//      own batch-completion point, so it had to be added machine by machine or not at
//      all.
//
// This file is the single answer to all three. Machines call Prepare() when they build
// their containers (which installs the role filter), Read() when the slots change
// (which returns one profile instead of a scalar pair), and RollBonusUnit() where a
// batch lands. New module roles are a new enum member and one line here - no machine
// changes.
//
// Save safety: nothing here is saved. The profile is derived from the upgrade container
// on every read, so a save written before this round restores its modules exactly as
// before and recomputes the same multipliers. Neither the item ids nor the container
// layouts change.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Everything a machine's socketed modules add up to, read once per container change.
    /// Never store the multipliers separately: two copies of the same number is how a
    /// panel and a machine end up disagreeing.
    /// </summary>
    public struct MachineModuleProfile
    {
        /// <summary>Throughput multiplier. 1 = no change, 1.25 = a quarter faster.</summary>
        public float speedMultiplier;
        /// <summary>Power-draw multiplier. 1 = no change, 0.8 = a fifth less power.</summary>
        public float efficiencyMultiplier;
        /// <summary>Chance (0-1) that a finished batch yields one extra main-product unit.</summary>
        public float yieldChance;

        /// <summary>How many socketed modules declared each role (for the panel chips).</summary>
        public int speedModules;
        public int efficiencyModules;
        public int yieldModules;

        public int TotalModules => speedModules + efficiencyModules + yieldModules;
        public bool HasAny => TotalModules > 0;

        /// <summary>True when something actually changed, so a panel can say "no modules" instead of "x1 · x1".</summary>
        public bool HasEffect =>
            !Mathf.Approximately(speedMultiplier, 1f)
            || !Mathf.Approximately(efficiencyMultiplier, 1f)
            || yieldChance > 0f;

        /// <summary>No modules fitted. Every field is the neutral value.</summary>
        public static MachineModuleProfile Identity => new MachineModuleProfile
        {
            speedMultiplier = 1f,
            efficiencyMultiplier = 1f,
            yieldChance = 0f,
        };

        /// <summary>
        /// One muted line for the machine panel, built from what the modules actually do
        /// rather than from what they claim - a module whose role is stale still reports
        /// the effect the machine is really getting.
        /// </summary>
        public string Summary()
        {
            if (!HasEffect) return "No modules fitted — throughput and power draw are unaffected.";
            var parts = new System.Collections.Generic.List<string>(3);
            if (!Mathf.Approximately(speedMultiplier, 1f))
                parts.Add($"x{speedMultiplier:0.##} throughput");
            if (!Mathf.Approximately(efficiencyMultiplier, 1f))
                parts.Add($"x{efficiencyMultiplier:0.##} power draw");
            if (yieldChance > 0f)
                parts.Add($"+{Mathf.RoundToInt(yieldChance * 100f)}% bonus yield");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// The universal machine-module system: slot preparation, one read pass, and the
    /// bonus-yield roll. Statics here are pure helpers over a container the caller owns,
    /// so nothing crosses a save or a network boundary.
    /// </summary>
    public static class MachineModules
    {
        /// <summary>Slots on a standard machine. Two is the balance decision: enough for one
        /// role doubled or two roles at once, never enough to stack a fifth of everything.</summary>
        public const int DefaultSlots = 2;

        // Bounds. A module can be tuned in the inspector, and a typo of 12 instead of 1.2
        // must not turn a Crusher into a teleporter - the same ceilings the tree farm and
        // the biomass press already enforced locally now hold everywhere.
        public const float MaxSpeedMultiplier = 5f;
        public const float MinSpeedMultiplier = 0.25f;
        public const float MaxEfficiencyMultiplier = 3f;
        public const float MinEfficiencyMultiplier = 0.2f;
        /// <summary>Bonus yield is capped so no arrangement of modules can out-produce the recipe itself.</summary>
        public const float MaxYieldChance = 0.5f;

        /// <summary>
        /// May this item go into a slot on a machine that honours <paramref name="support"/>?
        /// A universal module qualifies only if the machine can honour its role AND the
        /// module carries a real effect; every other item class (Quarry upgrades, engine
        /// modules, cargo) is refused.
        /// </summary>
        public static bool Accepts(ItemDefinition item, MachineModuleSupport support)
        {
            if (item is not FurnaceUpgradeItem module) return false;
            if (!module.HasEffect) return false;
            return (support & module.SupportBit) != 0;
        }

        // One delegate per support mask, built once. A machine that runs EnsureContainers
        // from Update() (every processor does) would otherwise allocate a fresh closure every
        // frame just to reinstall a filter that is already correct.
        private static readonly System.Func<ItemDefinition, int, int>[] _filters = BuildFilters();

        private static System.Func<ItemDefinition, int, int>[] BuildFilters()
        {
            var filters = new System.Func<ItemDefinition, int, int>[8];
            for (int mask = 0; mask < filters.Length; mask++)
            {
                var support = (MachineModuleSupport)mask;
                filters[mask] = (item, count) => Accepts(item, support) ? count : 0;
            }
            return filters;
        }

        /// <summary>Install the cached role filter for a support mask. Idempotent and allocation-free after the first call.</summary>
        public static void InstallFilter(ItemContainer container, MachineModuleSupport support)
        {
            if (container == null) return;
            container.moduleSlotSupport = support;   // so a refusal can name the rule
            var filter = _filters[(int)support & 7];
            if (!ReferenceEquals(container.AcceptFilter, filter)) container.AcceptFilter = filter;
        }

        /// <summary>
        /// Create, size and filter a machine's upgrade container. Called from the machine's
        /// own EnsureContainers(), which every machine already runs in Awake and after a
        /// save restore - the filter is [NonSerialized], so re-installing it there is what
        /// makes a refused drop keep being refused after a reload.
        /// </summary>
        public static ItemContainer Prepare(ItemContainer container, string name, int slots, MachineModuleSupport support)
        {
            int size = Mathf.Max(1, slots);
            if (container == null) container = new ItemContainer(string.IsNullOrEmpty(name) ? "Upgrades" : name, size);
            else container.Resize(size);

            // A machine that honours nothing keeps its slots but refuses every module: the
            // solid-fuel foundry shows no upgrade section at all, so an item must never be
            // able to land in a slot the player cannot see.
            InstallFilter(container, support);
            return container;
        }

        /// <summary>
        /// Read every socketed module into one profile. Multipliers apply per unit of stack
        /// count, exactly as the per-machine loops did, and both the speed and efficiency
        /// multipliers are applied regardless of the module's declared role - a role is a
        /// label and a filter, never a reason to ignore a number the player can see.
        /// </summary>
        public static MachineModuleProfile Read(ItemContainer container)
        {
            var profile = MachineModuleProfile.Identity;
            if (container == null) return profile;

            for (int i = 0; i < container.Size; i++)
            {
                var stack = container.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;
                if (stack.item is not FurnaceUpgradeItem module) continue;

                int count = Mathf.Max(1, stack.count);

                float speedFactor = module.speedMultiplier > 0f ? module.speedMultiplier : 1f;
                float effFactor = module.efficiencyMultiplier > 0f ? module.efficiencyMultiplier : 1f;
                profile.speedMultiplier *= Mathf.Pow(speedFactor, count);
                profile.efficiencyMultiplier *= Mathf.Pow(effFactor, count);

                switch (module.moduleKind)
                {
                    case MachineModuleKind.Efficiency:
                        profile.efficiencyModules += count;
                        break;
                    case MachineModuleKind.Yield:
                        profile.yieldModules += count;
                        profile.yieldChance += Mathf.Max(0f, module.yieldBonusChance) * count;
                        break;
                    default:
                        profile.speedModules += count;
                        break;
                }
            }

            profile.speedMultiplier = Mathf.Clamp(profile.speedMultiplier, MinSpeedMultiplier, MaxSpeedMultiplier);
            profile.efficiencyMultiplier = Mathf.Clamp(profile.efficiencyMultiplier, MinEfficiencyMultiplier, MaxEfficiencyMultiplier);
            profile.yieldChance = Mathf.Clamp(profile.yieldChance, 0f, MaxYieldChance);
            return profile;
        }

        /// <summary>
        /// Roll the bonus-yield chance for one finished batch. Called from the machine's own
        /// completion point, because only the machine knows what its main product is.
        /// Returns the units actually inserted (0 or 1); the unit is dropped without a
        /// complaint when the output is full, because a blocked bonus must never consume
        /// the batch or stall the machine.
        /// </summary>
        public static int RollBonusUnit(MachineModuleProfile profile, ItemContainer output, ItemDefinition item, int baseCount)
        {
            if (output == null || item == null || baseCount <= 0) return 0;
            if (profile.yieldChance <= 0f) return 0;
            if (Random.value > profile.yieldChance) return 0;
            if (!output.HasSpace(item, 1)) return 0;

            var leftover = output.Insert(new ItemStack(item, 1));
            int granted = 1 - (leftover?.count ?? 0);
            if (granted > 0) ProductionStatsTracker.RecordProduced(item, granted);
            return granted;
        }

        /// <summary>
        /// The same roll for a machine driven by a shared ProcessingRecipe: the batch's first
        /// listed product is its main product. Machines that use the shared executor do not
        /// touch its containers themselves, so this overload is the whole wiring they need.
        /// </summary>
        public static int RollBonusUnit(MachineModuleProfile profile, ItemContainer output,
            VoxelEngine.Crafting.ProcessingIO[] outputs)
        {
            if (outputs == null || outputs.Length == 0) return 0;
            var primary = outputs[0];
            if (primary.item == null) return 0;
            return RollBonusUnit(profile, output, primary.item, Mathf.Max(1, primary.count));
        }

        /// <summary>
        /// A short version of <see cref="HintFor"/> for a transient toast, where the full
        /// sentence would not fit: "This slot takes Speed and Efficiency modules only."
        /// </summary>
        public static string ShortHintFor(MachineModuleSupport support)
        {
            if (support == MachineModuleSupport.None) return "This machine's module slots are not in service.";
            var accepted = new System.Collections.Generic.List<string>(3);
            if ((support & MachineModuleSupport.Speed) != 0) accepted.Add("Speed");
            if ((support & MachineModuleSupport.Efficiency) != 0) accepted.Add("Efficiency");
            if ((support & MachineModuleSupport.Yield) != 0) accepted.Add("Yield");
            return accepted.Count == 1
                ? $"It takes a {accepted[0]} module only."
                : $"It takes {string.Join(", ", accepted)} modules.";
        }

        /// <summary>
        /// Player-facing description of which modules a machine takes, for the panel under
        /// its upgrade slots. Says what the machine can honour, so a refused drop reads as
        /// a rule rather than as a bug.
        /// </summary>
        public static string HintFor(MachineModuleSupport support)
        {
            if (support == MachineModuleSupport.None)
                return "This machine has no module slots in service.";
            var accepted = new System.Collections.Generic.List<string>(3);
            if ((support & MachineModuleSupport.Speed) != 0) accepted.Add("Machine Speed Module (x1.25 throughput)");
            if ((support & MachineModuleSupport.Efficiency) != 0) accepted.Add("Machine Efficiency Module (x0.8 power draw)");
            if ((support & MachineModuleSupport.Yield) != 0) accepted.Add("Machine Yield Module (+12% bonus batch yield)");
            return "Universal machine modules only: " + string.Join(", ", accepted)
                 + ". Quarry upgrades, maritime engine modules and cargo are refused by these slots.";
        }
    }
}
