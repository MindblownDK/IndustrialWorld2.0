// Assets/Scripts/VoxelEngine/Building/Tiered/HammerTierSelection.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      HAMMER TIER SELECTION — which tier the hammer works to      ║
// ║                                                                  ║
// ║  The hammer used to be able to do one thing to a placed piece:   ║
// ║  raise it one tier. Wood became Stone, Stone became Iron, and a  ║
// ║  wall that wanted to be Steel took three clicks and three full   ║
// ║  payments in a row.                                              ║
// ║                                                                  ║
// ║  Now the player names the tier ONCE (the wheel on U, rebindable  ║
// ║  in Settings - Keybinds), and every click takes a piece straight  ║
// ║  to it.                                                          ║
// ║                                                                  ║
// ║  The cost is a SINGLE amount, not a stack: a Wood wall taken to  ║
// ║  Steel is charged the Steel step only. The intermediate steps    ║
// ║  are the price of arriving there one tier at a time, and the     ║
// ║  player who names the destination has already skipped them.      ║
// ║                                                                  ║
// ║  Plain static data plus a PlayerPrefs value, so the wheel, the   ║
// ║  build system and the hammer's own click path all read one       ║
// ║  answer and no copy can drift.                                   ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>The tier the Building Hammer upgrades to, and the single-step cost of it.</summary>
    public static class HammerTierSelection
    {
        private const string K_TIER = "ve.hammerUpgradeTier";

        /// <summary>
        /// 17.45.3-dev: what the hammer charges, as a multiple of the authored upgrade cost.
        /// A named destination skips the stopovers, so it costs more than the single step it
        /// is charged against - two of that step, decided with the player. It scales the
        /// number the wheel shows and the number the build system takes, because both read
        /// the same cost.
        /// </summary>
        public const int UpgradeCostMultiplier = 2;

        /// <summary>Wood is the tier everything is born at, so it is never a destination.</summary>
        public static readonly BuildTier[] Offerable =
        {
            BuildTier.Wood, BuildTier.Stone, BuildTier.Iron, BuildTier.Steel,
        };

        private static bool _loaded;
        private static BuildTier _selected = BuildTier.Stone;

        /// <summary>Raised whenever the selection changes, so a HUD can re-read it.</summary>
        public static event System.Action Changed;

        public static BuildTier Selected
        {
            get { EnsureLoaded(); return _selected; }
        }

        public static void Set(BuildTier tier)
        {
            EnsureLoaded();
            if (tier < BuildTier.Wood) tier = BuildTier.Wood;
            if (tier > BuildTier.Steel) tier = BuildTier.Steel;
            if (_selected == tier) return;
            _selected = tier;
            PlayerPrefs.SetInt(K_TIER, (int)_selected);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Steps the selection by one tier, clamped at Steel.</summary>
        public static BuildTier Cycled()
        {
            int next = Mathf.Clamp((int)Selected + 1, 0, 3);
            return (BuildTier)next;
        }

        public static BuildTier TierAt(int index) => Offerable[Mathf.Clamp(index, 0, Offerable.Length - 1)];

        public static string Name(BuildTier tier) => tier switch
        {
            BuildTier.Stone => "STONE",
            BuildTier.Iron => "IRON",
            BuildTier.Steel => "STEEL",
            _ => "WOOD",
        };

        /// <summary>One line about what the tier is for, shown in the wheel's hub.</summary>
        public static string Blurb(BuildTier tier) => tier switch
        {
            BuildTier.Stone => "Weather and wildlife out. The everyday upgrade",
            BuildTier.Iron => "Machinery loads, heavy roofs, real pressure",
            BuildTier.Steel => "The top tier. Nothing the hammer carries is tougher",
            _ => "Rough-cut timber. The tier everything is born at",
        };

        /// <summary>
        /// The cost of the ONE step that produces <paramref name="destination"/>, whatever
        /// tier the piece is standing at now. Wood taken to Steel pays the Steel materials
        /// only - not Stone, then Iron, then Steel.
        /// </summary>
        public static TierCost CostToReach(TieredBlockDefinition definition, BuildTier destination)
        {
            if (definition == null) return null;
            if (destination <= BuildTier.Wood) return null;

            var step = definition.GetUpgradeCost(PreviousTier(destination));
            if (step?.items == null || step.items.Length == 0) return step;
            if (UpgradeCostMultiplier <= 1) return step;

            // A copy, never the definition's own array: scaling it in place would double the
            // cost again on the next call and rewrite the asset's value for the session.
            var scaled = new Ingredient[step.items.Length];
            for (int i = 0; i < step.items.Length; i++)
            {
                var ingredient = step.items[i];
                ingredient.count = Mathf.Max(0, ingredient.count * UpgradeCostMultiplier);
                scaled[i] = ingredient;
            }
            return new TierCost { items = scaled };
        }

        /// <summary>The tier below this one; Wood is its own floor.</summary>
        public static BuildTier PreviousTier(BuildTier tier) => tier switch
        {
            BuildTier.Stone => BuildTier.Wood,
            BuildTier.Iron => BuildTier.Stone,
            BuildTier.Steel => BuildTier.Iron,
            _ => BuildTier.Wood,
        };

        /// <summary>A compact read of a cost, for a toast or the wheel hub.</summary>
        public static string Describe(TierCost cost)
        {
            if (cost == null || cost.items == null || cost.items.Length == 0) return "Free";
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < cost.items.Length; i++)
            {
                var ingredient = cost.items[i];
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(ingredient.count).Append(' ').Append(ingredient.item.displayName);
            }
            return builder.Length == 0 ? "Free" : builder.ToString();
        }

        /// <summary>
        /// A wedge icon for one tier. The tier dial's choices are materials, not machines,
        /// so each one gets its own glyph: planks for wood, a block for stone, a roof angle
        /// for iron, a diamond for steel. Cached by the shared icon atlas like every other
        /// wheel icon in the game.
        /// </summary>
        public static Texture2D Icon(BuildTier tier) => tier switch
        {
            BuildTier.Stone => VoxelEngine.UI.IconAtlas.Get("hammer:tier:stone", b =>
            {
                b.Polyline(new Vector2(-0.34f, -0.30f), new Vector2(-0.34f, 0.30f),
                           new Vector2(0.34f, 0.30f), new Vector2(0.34f, -0.30f),
                           new Vector2(-0.34f, -0.30f));
                b.Line(new Vector2(-0.34f, 0.02f), new Vector2(0.34f, 0.02f));
            }),
            BuildTier.Iron => VoxelEngine.UI.IconAtlas.Get("hammer:tier:iron", b =>
            {
                b.Polyline(new Vector2(-0.40f, -0.14f), new Vector2(0f, 0.24f), new Vector2(0.40f, -0.14f));
                b.Line(new Vector2(-0.26f, -0.34f), new Vector2(0.26f, -0.34f));
            }),
            BuildTier.Steel => VoxelEngine.UI.IconAtlas.Get("hammer:tier:steel", b =>
            {
                b.Polyline(new Vector2(0f, 0.38f), new Vector2(0.34f, 0f), new Vector2(0f, -0.38f),
                           new Vector2(-0.34f, 0f), new Vector2(0f, 0.38f));
                b.Line(new Vector2(-0.16f, 0f), new Vector2(0.16f, 0f));
            }),
            _ => VoxelEngine.UI.IconAtlas.Get("hammer:tier:wood", b =>
            {
                b.Line(new Vector2(-0.34f, 0.26f), new Vector2(0.34f, 0.26f));
                b.Line(new Vector2(-0.34f, 0f), new Vector2(0.34f, 0f));
                b.Line(new Vector2(-0.34f, -0.26f), new Vector2(0.34f, -0.26f));
            }),
        };

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _selected = (BuildTier)Mathf.Clamp(
                PlayerPrefs.GetInt(K_TIER, (int)BuildTier.Stone), 0, 3);
        }
    }
}
