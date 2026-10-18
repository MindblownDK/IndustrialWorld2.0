// Assets/Scripts/VoxelEngine/GridSystem/UI/ScreenPageCatalog.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        WHAT A SCREEN CAN BE — THE PAGE CATALOGUE (17.35.0-dev)   ║
// ║                                                                  ║
// ║  Before this file, a screen's nine display types were a single   ║
// ║  wrap of identical buttons in the config panel: no order,        ║
// ║  no explanation, no grouping, and no way to tell what any of     ║
// ║  them would actually show until you pressed it and looked at     ║
// ║  the wall. That is the "messy" part - the types were fine, the   ║
// ║  way they were offered was not.                                  ║
// ║                                                                  ║
// ║  Every page now describes itself once, here: its name, the       ║
// ║  category it belongs to, one line about what it answers, the     ║
// ║  smallest screen it fits on, and the words a player would type   ║
// ║  into a search box looking for it. The config panel and anything ║
// ║  else that offers a page read THIS one list, so a page lives in  ║
// ║  one place and cannot be offered in one surface and be missing   ║
// ║  from another.                                                   ║
// ║                                                                  ║
// ║  The enum values are APPEND-ONLY: a screen persists its page as  ║
// ║  the enum NAME, so adding to the end is save-compatible and      ║
// ║  renumbering never is.                                           ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;

namespace VoxelEngine.GridSystem
{
    /// <summary>How a page is grouped in the picker. Order here is the order shown.</summary>
    public enum ScreenPageCategory
    {
        Overview,
        Vitals,
        Power,
        Cargo,
        Production,
        Flight,
        Navigation,
        Media,
    }

    /// <summary>Everything a surface needs to offer one page to a player.</summary>
    public readonly struct ScreenPageInfo
    {
        public readonly ScreenDataMode Mode;
        public readonly string Label;
        public readonly ScreenPageCategory Category;
        /// <summary>One sentence, in the player's terms: what question does this page answer?</summary>
        public readonly string Blurb;
        /// <summary>Smallest screen this page fits on comfortably. The picker warns below it
        /// rather than refusing - a Small screen can still be set to a page that wants more
        /// room, it simply shows less of it.</summary>
        public readonly ScreenSize MinSize;
        /// <summary>Extra words the search box should match (synonyms a player would type).</summary>
        public readonly string Keywords;

        public ScreenPageInfo(ScreenDataMode mode, string label, ScreenPageCategory category,
            string blurb, ScreenSize minSize, string keywords)
        {
            Mode = mode;
            Label = label;
            Category = category;
            Blurb = blurb;
            MinSize = minSize;
            Keywords = keywords ?? string.Empty;
        }

        /// <summary>Does this page match a search query? Empty query matches everything.</summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string q = query.Trim();
            return Contains(Label, q) || Contains(Blurb, q) || Contains(Keywords, q)
                || Contains(Mode.ToString(), q);
        }

        private static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack)
               && haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// The one list of pages a screen can show. Read by the config panel (to offer them), by
    /// the block (to cycle them and to know which want providers) and by anything that later
    /// wants to describe a screen to a player.
    /// </summary>
    public static class ScreenPageCatalog
    {
        /// <summary>Every page, in picker order (grouped by category, then by usefulness).</summary>
        public static readonly ScreenPageInfo[] All =
        {
            // ── Overview ──────────────────────────────────────────────────────────
            new(ScreenDataMode.Summary, "Mixed",
                ScreenPageCategory.Overview,
                "Whatever the linked sources want to say, side by side. The general-purpose page.",
                ScreenSize.Small, "summary overview mixed all sources general"),
            new(ScreenDataMode.System, "System",
                ScreenPageCategory.Overview,
                "One linked source, full width, with its own section header.",
                ScreenSize.Small, "system block detail single"),
            new(ScreenDataMode.Bars, "Bars",
                ScreenPageCategory.Overview,
                "Every percentage a linked source reports, drawn as a ten-segment bar.",
                ScreenSize.Small, "bars progress percent gauge fill"),

            // ── Vitals ────────────────────────────────────────────────────────────
            new(ScreenDataMode.Environment, "Environment",
                ScreenPageCategory.Vitals,
                "Pressure, gravity, temperature and altitude at this screen - the room it is in, or the sky it is under.",
                ScreenSize.Medium, "pressure gravity atmosphere room air temperature altitude life support"),
            new(ScreenDataMode.Speed, "Flight",
                ScreenPageCategory.Vitals,
                "Speed, heading and altitude of the construct this screen is bolted to.",
                ScreenSize.Small, "speed velocity heading flight nav"),

            // ── Power ─────────────────────────────────────────────────────────────
            new(ScreenDataMode.Power, "Power",
                ScreenPageCategory.Power,
                "Generation, draw and the balance right now.",
                ScreenSize.Small, "power energy watts generation draw deficit"),
            new(ScreenDataMode.BatteryBank, "Batteries",
                ScreenPageCategory.Power,
                "Every battery on this construct: stored against capacity, one bar each.",
                ScreenSize.Medium, "battery bank charge stored capacity cells"),
            new(ScreenDataMode.PowerGraph, "Power Graph",
                ScreenPageCategory.Power,
                "The last few minutes of generation and draw as a live trace, with the high-water mark.",
                ScreenSize.Large, "power graph history chart trend sparkline log"),

            // ── Cargo ─────────────────────────────────────────────────────────────
            new(ScreenDataMode.Manifest, "Manifest",
                ScreenPageCategory.Cargo,
                "Every item on the construct, heaviest first, with its mass and the total fill.",
                ScreenSize.Medium, "manifest cargo storage contents items mass heavy inventory list"),
            new(ScreenDataMode.Inventory, "Storage Monitor",
                ScreenPageCategory.Cargo,
                "A monitor for one container: every item as a picture with its count, and the container's name in the corner.",
                ScreenSize.Medium, "storage monitor inventory chest container box items pictures counts"),
            new(ScreenDataMode.FillBars, "Fill Bars",
                ScreenPageCategory.Cargo,
                "One percentage bar per linked container. Built for the long wide panel.",
                ScreenSize.Wide, "fill bar percentage full containers storage gauge long wide"),

            // ── Production ────────────────────────────────────────────────────────
            new(ScreenDataMode.InventoryGrid, "Inventory Grid",
                ScreenPageCategory.Cargo,
                "The whole hold as a dense wall of item pictures and counts - what is aboard, at a glance.",
                ScreenSize.Medium, "inventory grid items counts list hold contents stock"),

            new(ScreenDataMode.Farm, "Farm",
                ScreenPageCategory.Production,
                "Every bed near this screen - tilled soil, seed planters and sapling beds: what is planted, how far along, and whether it has water.",
                ScreenSize.Medium, "farm crops plots planters saplings growth water harvest agriculture seeds"),

            // ── Flight / work ─────────────────────────────────────────────────────
            new(ScreenDataMode.Mining, "Mining",
                ScreenPageCategory.Flight,
                "The mining gauge: lift thrust against the ship's own weight, and how full the hold is.",
                ScreenSize.Small, "mining lift thrust cargo hold ore drill excavation gauge"),
            new(ScreenDataMode.MiningCargo, "Mining Cargo",
                ScreenPageCategory.Flight,
                "What the hold is carrying while mining - heaviest first, with pictures and masses. Pair it under the gauge.",
                ScreenSize.Medium, "mining cargo hold ore weight heaviest load freight"),

            // ── Media and text ────────────────────────────────────────────────────
            new(ScreenDataMode.PlanetMap, "Planet Map",
                ScreenPageCategory.Navigation,
                "The body below as a disc, with a line grid and the construct's own position on it.",
                ScreenSize.Medium, "planet map orbit latitude longitude position navigation globe"),

            new(ScreenDataMode.Camera, "Camera",
                ScreenPageCategory.Media,
                "A live feed from a linked camera block.",
                ScreenSize.Small, "camera feed video view live"),
            new(ScreenDataMode.TeamBanner, "Team Banner",
                ScreenPageCategory.Media,
                "The team's own banner, filling the panel. Edited in PAUSE > TEAMS, not here.",
                ScreenSize.Small, "team banner emblem flag heraldry"),
            new(ScreenDataMode.Custom, "Custom Text",
                ScreenPageCategory.Media,
                "Type it yourself. Signs, labels, warnings.",
                ScreenSize.Small, "custom text sign label written"),
        };

        private static readonly ScreenPageInfo[] s_none = System.Array.Empty<ScreenPageInfo>();
        private static readonly Dictionary<ScreenPageCategory, List<ScreenPageInfo>> s_byCategory = BuildGroups();

        private static Dictionary<ScreenPageCategory, List<ScreenPageInfo>> BuildGroups()
        {
            var map = new Dictionary<ScreenPageCategory, List<ScreenPageInfo>>();
            foreach (var page in All)
            {
                if (!map.TryGetValue(page.Category, out var list))
                {
                    list = new List<ScreenPageInfo>();
                    map[page.Category] = list;
                }
                list.Add(page);
            }
            return map;
        }

        /// <summary>Every page in one category, in catalogue order.</summary>
        public static IReadOnlyList<ScreenPageInfo> InCategory(ScreenPageCategory category)
            => s_byCategory.TryGetValue(category, out var list) ? list : s_none;

        /// <summary>The categories, in the order the picker shows them.</summary>
        public static readonly ScreenPageCategory[] Categories =
        {
            ScreenPageCategory.Overview, ScreenPageCategory.Vitals, ScreenPageCategory.Power,
            ScreenPageCategory.Cargo, ScreenPageCategory.Production, ScreenPageCategory.Flight,
            ScreenPageCategory.Navigation, ScreenPageCategory.Media,
        };

        /// <summary>Player-facing name of a category.</summary>
        public static string CategoryLabel(ScreenPageCategory category) => category switch
        {
            ScreenPageCategory.Overview => "OVERVIEW",
            ScreenPageCategory.Vitals => "VITALS",
            ScreenPageCategory.Power => "POWER",
            ScreenPageCategory.Cargo => "CARGO",
            ScreenPageCategory.Production => "PRODUCTION",
            ScreenPageCategory.Flight => "FLIGHT",
            ScreenPageCategory.Navigation => "NAVIGATION",
            ScreenPageCategory.Media => "MEDIA",
            _ => "OTHER",
        };

        /// <summary>The catalogue entry for a mode, or a safe fallback for an unknown one.</summary>
        public static ScreenPageInfo For(ScreenDataMode mode)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Mode == mode) return All[i];
            return new ScreenPageInfo(mode, mode.ToString(), ScreenPageCategory.Overview,
                "No description is registered for this page.", ScreenSize.Small, string.Empty);
        }

        /// <summary>
        /// The next page in catalogue order, wrapping - what the block's cycle action uses.
        /// Cycling goes through the catalogue rather than the raw enum so the order a player
        /// sees is the order they get.
        /// </summary>
        public static ScreenDataMode Next(ScreenDataMode mode)
        {
            int index = IndexOf(mode);
            if (index < 0) return All[0].Mode;
            return All[(index + 1) % All.Length].Mode;
        }

        /// <summary>Previous page in catalogue order, wrapping.</summary>
        public static ScreenDataMode Previous(ScreenDataMode mode)
        {
            int index = IndexOf(mode);
            if (index < 0) return All[0].Mode;
            return All[(index - 1 + All.Length) % All.Length].Mode;
        }

        private static int IndexOf(ScreenDataMode mode)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Mode == mode) return i;
            return -1;
        }

        /// <summary>Does this page need at least one linked source to say anything useful?</summary>
        public static bool NeedsSource(ScreenDataMode mode) => mode switch
        {
            // These read the construct or the world directly.
            ScreenDataMode.Power or ScreenDataMode.BatteryBank or ScreenDataMode.PowerGraph
                or ScreenDataMode.Manifest or ScreenDataMode.Mining or ScreenDataMode.Environment
                or ScreenDataMode.Farm or ScreenDataMode.Custom or ScreenDataMode.Camera
                or ScreenDataMode.TeamBanner or ScreenDataMode.Speed => false,
            // These are a view ON a linked container.
            ScreenDataMode.FillBars => true,
            // These read the construct and the world by themselves.
            ScreenDataMode.Inventory or ScreenDataMode.InventoryGrid
                or ScreenDataMode.PlanetMap or ScreenDataMode.MiningCargo => false,
            _ => true,
        };

        /// <summary>Human-readable screen size, for the picker's fit warning.</summary>
        public static string SizeLabel(ScreenSize size) => size switch
        {
            ScreenSize.Small => "Small",
            ScreenSize.Medium => "Medium",
            ScreenSize.Large => "Large",
            ScreenSize.ExtraLarge => "Extra Large",
            ScreenSize.Wide => "Wide",
            _ => size.ToString(),
        };

        /// <summary>
        /// True when a page's preferred minimum is no larger than the panel it is on.
        /// Ranked by usable display room rather than by the enum's own numbering: a Wide panel
        /// is short but 1.4 m across, so it sits between Small and Medium, which is exactly the
        /// order of the line counts the block already uses.
        /// </summary>
        public static bool FitsOn(ScreenSize panel, ScreenSize wants)
            => Rank(panel) >= Rank(wants);

        private static int Rank(ScreenSize size) => size switch
        {
            ScreenSize.Small => 0,
            ScreenSize.Wide => 1,
            ScreenSize.Medium => 2,
            ScreenSize.Large => 3,
            ScreenSize.ExtraLarge => 4,
            _ => 0,
        };
    }
}
