// Assets/Scripts/VoxelEngine/Building/PlacementOptions.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          PLACEMENT OPTIONS — the shared placement contract       ║
// ║                                                                  ║
// ║  One block, many copies. A foundation run is ten cells, not ten    ║
// ║  clicks, and the mode that lays them is a property of the        ║
// ║  PLAYER, not of the item: you pick SPAN once and every block you ║
// ║  hold afterwards runs. That is the whole reason this file is     ║
// ║  static state rather than a component on the player rig.         ║
// ║                                                                  ║
// ║  The three patterns we ship are named for what they build, not   ║
// ║  for a gesture:                                                  ║
// ║    SINGLE  one block per click, exactly as the game always did   ║
// ║    SPAN    drag a straight run across the aimed face             ║
// ║    PILLAR  drag a stack straight up or down that face            ║
// ║    DECK    drag a rectangle across the face, anchor corner first  ║
// ║                                                                  ║
// ║  Nothing here touches the world. It is a selection plus the      ║
// ║  eligibility rule that decides which items may use it, so the    ║
// ║  builder, the HUD and the build wheel all read one source.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    /// <summary>How a click lays blocks down. Named for the structure, never for a gesture.</summary>
    public enum PlacementPattern
    {
        Single = 0,
        Span = 1,
        Pillar = 2,
        Deck = 3,
    }

    /// <summary>The step the placement lattice quantises to while placing.</summary>
    public enum PlacementStep
    {
        Metre = 0,
        Half = 1,
        Free = 2,
    }

    /// <summary>
    /// The player's current placement pattern and lattice step, plus the rule that says
    /// which items are allowed to use them. Read-only outside <see cref="CycledPattern"/>
    /// and friends so no builder can drift out of agreement with the HUD.
    /// </summary>
    public static class PlacementOptions
    {
        /// <summary>Hard cap on a SPAN or PILLAR run. Ten cells is one gesture, not a career.</summary>
        public const int MaxRun = 10;

        /// <summary>Hard cap on a DECK edge; a 4 x 4 deck is sixteen cells including the anchor.</summary>
        public const int MaxDeckSide = 4;

        /// <summary>Hard cap on any single commit, whatever the mode asks for.</summary>
        public const int MaxCells = 16;

        private const string K_PATTERN = "ve.placePattern";
        private const string K_STEP = "ve.placeStep";

        /// <summary>Raised whenever the pattern, the step or the eligibility changes.</summary>
        public static event System.Action Changed;

        private static bool _loaded;
        private static PlacementPattern _pattern = PlacementPattern.Single;
        private static PlacementStep _step = PlacementStep.Metre;

        public static PlacementPattern Pattern
        {
            get { EnsureLoaded(); return _pattern; }
        }

        public static PlacementStep Step
        {
            get { EnsureLoaded(); return _step; }
        }

        /// <summary>True when a click should lay a run instead of a single block.</summary>
        public static bool IsMultiPlace => Pattern != PlacementPattern.Single;

        /// <summary>The lattice step in metres, or 0 when the player wants free placement.</summary>
        public static float StepMetres => Step switch
        {
            PlacementStep.Half => 0.5f,
            PlacementStep.Free => 0f,
            _ => 1f,
        };

        public static string PatternName(PlacementPattern pattern) => pattern switch
        {
            PlacementPattern.Span => "SPAN",
            PlacementPattern.Pillar => "PILLAR",
            PlacementPattern.Deck => "DECK",
            _ => "SINGLE",
        };

        public static string PatternBlurb(PlacementPattern pattern) => pattern switch
        {
            PlacementPattern.Span => "Drag a straight run across the face. Up to ten cells.",
            PlacementPattern.Pillar => "Drag a stack up or down the face. Up to ten cells.",
            PlacementPattern.Deck => "Drag a rectangle across the face. Up to four by four.",
            _ => "One block per click. The classic.",
        };

        public static string StepName(PlacementStep step) => step switch
        {
            PlacementStep.Half => "0.5 m",
            PlacementStep.Free => "FREE",
            _ => "1 m",
        };

        public static string StepBlurb(PlacementStep step) => step switch
        {
            PlacementStep.Half => "Half-metre lattice: finer detail on decks and interiors.",
            PlacementStep.Free => "No lattice. The block sits exactly where you aim.",
            _ => "The structural metre every foundation and machines share.",
        };

        public static PlacementPattern PatternAt(int index)
            => index switch
            {
                1 => PlacementPattern.Span,
                2 => PlacementPattern.Pillar,
                3 => PlacementPattern.Deck,
                _ => PlacementPattern.Single,
            };

        public static PlacementStep StepAt(int index)
            => index switch
            {
                1 => PlacementStep.Half,
                2 => PlacementStep.Free,
                _ => PlacementStep.Metre,
            };

        public static void SetPattern(PlacementPattern pattern)
        {
            EnsureLoaded();
            if (_pattern == pattern) return;
            _pattern = pattern;
            PlayerPrefs.SetInt(K_PATTERN, (int)_pattern);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static void SetStep(PlacementStep step)
        {
            EnsureLoaded();
            if (_step == step) return;
            _step = step;
            PlayerPrefs.SetInt(K_STEP, (int)_step);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Steps the pattern forward through the ring, wrapping to SINGLE.</summary>
        public static PlacementPattern CycledPattern() => PatternAt(((int)Pattern + 1) % 4);

        public static PlacementStep CycledStep() => StepAt(((int)Step + 1) % 3);

        /// <summary>
        /// Can this item lay runs? Deliberately conservative: anything with a dedicated
        /// snap of its own (conduits, cabled pipes, rails, road cells) keeps its own
        /// placement contract, because a second copy of a pipe is not a copy - it is a
        /// connection, and the connection solver has to see each one individually.
        /// </summary>
        public static bool IsPatternEligible(BlockItem block)
        {
            if (block == null) return false;
            var prefab = block.placedPrefab;
            if (prefab == null) return false;

            // 17.45.0-dev: an explosive owns the build key's wheel. It arms on placement
            // and counts down, so a run of ten of them is not a structure - it is one
            // click away from deleting the base. The bomb claims the dial for its FUSE
            // list instead (see BombHud), and never lays a run.
            if (prefab.GetComponentInChildren<VoxelEngine.Combat.ExplosiveBlock>(true) != null) return false;
            if (prefab.GetComponentInChildren<VoxelEngine.Combat.AntimatterBomb>(true) != null) return false;

            // Rails are laid by the rail tool, never by hand (12.2.0).
            if (prefab.GetComponentInChildren<VoxelEngine.Building.RailTrack>(true) != null) return false;
            if (VoxelEngine.Building.RoadPaver.IsRoadBlock(block)) return false;

            // Conduits and cables: each copy is a graph edge, not a decoration.
            if (prefab.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>(true) != null) return false;
            if (prefab.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null) return false;
            if (prefab.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null) return false;
            if (prefab.GetComponentInChildren<VoxelEngine.Power.PowerCable>(true) != null) return false;
            if (prefab.GetComponentInChildren<VoxelEngine.Networks.DataCable>(true) != null) return false;

            return true;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            int pattern = PlayerPrefs.GetInt(K_PATTERN, 0);
            int step = PlayerPrefs.GetInt(K_STEP, 0);
            _pattern = (PlacementPattern)Mathf.Clamp(pattern, 0, 3);
            _step = (PlacementStep)Mathf.Clamp(step, 0, 2);
        }
    }
}
