// Assets/Scripts/VoxelEngine/GridSystem/CockpitShellBuilder.cs
//
// 17.37.0-dev - the cockpit, from the seat outwards.
//
// A cockpit is the one block a player sits inside for hours, so it is built like a room
// first and a hull second:
//
//   INTERIOR   shared by both shells - seat with bolsters and harness, side consoles,
//              a deck at glance height, three displays across the top, an overhead
//              switch panel, rudder pedals, grab handles, and a cabin lamp so the room
//              is readable with the ships lights off.
//   CRAWLER    a wide low nose with wraparound glazing, fenders and a roll bar - the
//              view from a land vehicle's cab, glass down to the sill.
//   CAB        a boxy upright cab: square windscreen, grille, yellow hazard stripes,
//              roof louver bank, satellite dome and mirrors - a survey workhorse.
//
// Both shells ship in one prefab and only one is enabled at a time; the player picks
// which with the build wheel before placing. Nothing is generated at play time, and
// every material is an asset written by `Voxel Engine Setup`.

using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.GridSystem
{
    public static class CockpitShellBuilder
    {
        /// <summary>Shell styles, matching <see cref="GridCockpit.shellStyle"/>. 0 is the
        /// pre-17.37 cockpit and builds no shell at all.</summary>
        public const int StyleLegacy = 0;
        public const int StyleCrawler = 1;
        public const int StyleCab = 2;

        public const string ShellRootName = "Generated_CockpitShell";
        public const string InteriorName = "Cabin";
        public const string SeatAnchorName = "SeatAnchor";
        public const string CrawlerName = "Shell_Crawler";
        public const string CabName = "Shell_Cab";

        /// <summary>Every measurement below is authored against a 2 m grid cell and then
        /// scaled with the block, so a cockpit reads the same on a large grid and stays a
        /// cockpit rather than a doll's house on a small one.</summary>
        private const float CellUnit = 2f;

        /// <summary>
        /// Build the whole cockpit: one shared cabin and both shells, with the shell named by
        /// <paramref name="style"/> enabled. Editor only - a prefab without a shell gets one
        /// from Setup, and nothing is generated at play time.
        /// </summary>
        public static void Build(GameObject root, float cellSize, Color bodyColor, int style)
        {
            if (root == null || cellSize <= 0.01f) return;
            if (Application.isPlaying) return;
            if (style == StyleLegacy) return;

            var existing = root.transform.Find(ShellRootName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var shellRoot = Group(root.transform, ShellRootName, Vector3.zero);
            var palette = new Palette(bodyColor);

            float scale = cellSize / CellUnit;

            var cabin = Group(shellRoot.transform, InteriorName, Vector3.zero);
            cabin.transform.localScale = new Vector3(scale, scale, scale);
            BuildInterior(cabin, palette);

            var crawler = Group(shellRoot.transform, CrawlerName, Vector3.zero);
            crawler.transform.localScale = new Vector3(scale, scale, scale);
            BuildCrawler(crawler, palette);

            var cab = Group(shellRoot.transform, CabName, Vector3.zero);
            cab.transform.localScale = new Vector3(scale, scale, scale);
            BuildCab(cab, palette);
            crawler.SetActive(style != StyleCab);
            cab.SetActive(style == StyleCab);

            // The seat pose lives beside the shells, not inside one: both cabins are the
            // same room, so there is exactly one answer to where the pilot's eyes go.
            var anchor = Group(shellRoot.transform, SeatAnchorName, new Vector3(0f, 0.10f, -0.40f) * scale);
            anchor.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        }

        /// <summary>
        /// Show the shell this cockpit is configured for. Both shells live in the prefab so a
        /// cockpit can change character by changing one number, and so the cabin is never
        /// duplicated; this only flips which hull is active.
        /// </summary>
        public static void ApplyShell(GameObject root, int style)
        {
            if (root == null || style == StyleLegacy) return;

            var shellRoot = root.transform.Find(ShellRootName);
            if (shellRoot == null) return;

            var crawler = shellRoot.Find(CrawlerName);
            var cab = shellRoot.Find(CabName);
            if (crawler != null) crawler.gameObject.SetActive(style != StyleCab);
            if (cab != null) cab.gameObject.SetActive(style == StyleCab);
        }

        /// <summary>True when this cockpit's prefab carries a shell and therefore a seat
        /// pose. A prefab that has not been through Setup says no, and the pilot keeps the
        /// standing camera instead of being pushed into geometry.</summary>
        public static bool TryGetSeatAnchor(GameObject root, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            if (root == null) return false;

            var anchor = root.transform.Find(ShellRootName + "/" + SeatAnchorName);
            if (anchor == null) return false;

            localPosition = anchor.localPosition;
            localRotation = anchor.localRotation;
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  THE CABIN - shared by both shells
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildInterior(GameObject root, Palette p)
        {
            const float cs = CellUnit;
            float h = cs * 0.5f;

            // ── Shell of the room ──────────────────────────────────────────────
            Box(root, p.Dark, "Floor", new Vector3(0f, -h + 0.06f, 0f), new Vector3(cs * 0.98f, 0.12f, cs * 0.98f), Vector3.zero);
            // Ribbed floor mat: reads as a floor rather than as a box interior.
            for (int i = 0; i < 5; i++)
                Box(root, p.Metal, "Floor_Rib_" + i, new Vector3(0f, -h + 0.13f, -0.72f + i * 0.32f),
                    new Vector3(cs * 0.86f, 0.02f, 0.06f), Vector3.zero);

            Box(root, p.Hull, "Bulkhead", new Vector3(0f, h * 0.10f, -h + 0.09f), new Vector3(cs * 0.98f, cs * 0.94f, 0.16f), Vector3.zero);

            // Rear hatch: a frame and six dogs, so the way out is visible from the seat.
            Box(root, p.Dark, "Hatch_Frame", new Vector3(0f, -0.20f, -h + 0.18f), new Vector3(0.94f, 1.24f, 0.05f), Vector3.zero);
            Box(root, p.Hull, "Hatch", new Vector3(0f, -0.20f, -h + 0.22f), new Vector3(0.78f, 1.06f, 0.04f), Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                float y = -0.58f + i * 0.38f;
                Box(root, p.Metal, "Hatch_Dog_L" + i, new Vector3(-0.34f, y, -h + 0.25f), new Vector3(0.10f, 0.05f, 0.04f), Vector3.zero);
                Box(root, p.Metal, "Hatch_Dog_R" + i, new Vector3(0.34f, y, -h + 0.25f), new Vector3(0.10f, 0.05f, 0.04f), Vector3.zero);
            }

            // ── Seat ──────────────────────────────────────────────────────────
            Box(root, p.Dark, "Seat_Pedestal", new Vector3(0f, -0.80f, -0.42f), new Vector3(0.46f, 0.34f, 0.40f), Vector3.zero);
            Box(root, p.Seat, "Seat_Pan", new Vector3(0f, -0.60f, -0.40f), new Vector3(0.66f, 0.14f, 0.58f), Vector3.zero);
            Box(root, p.Seat, "Seat_Back", new Vector3(0f, -0.22f, -0.68f), new Vector3(0.66f, 0.80f, 0.14f), new Vector3(-9f, 0f, 0f));
            Box(root, p.Seat, "Seat_Head", new Vector3(0f, 0.24f, -0.73f), new Vector3(0.42f, 0.26f, 0.12f), new Vector3(-9f, 0f, 0f));
            Box(root, p.Seat, "Seat_Bolster_L", new Vector3(-0.36f, -0.46f, -0.40f), new Vector3(0.12f, 0.30f, 0.52f), Vector3.zero);
            Box(root, p.Seat, "Seat_Bolster_R", new Vector3(0.36f, -0.46f, -0.40f), new Vector3(0.12f, 0.30f, 0.52f), Vector3.zero);
            Box(root, p.Seat, "Armrest_L", new Vector3(-0.42f, -0.34f, -0.28f), new Vector3(0.14f, 0.09f, 0.46f), new Vector3(-4f, 0f, 0f));
            Box(root, p.Seat, "Armrest_R", new Vector3(0.42f, -0.34f, -0.28f), new Vector3(0.14f, 0.09f, 0.46f), new Vector3(-4f, 0f, 0f));
            // Harness: two straps over the shoulders and one across the lap.
            Box(root, p.Strap, "Harness_L", new Vector3(-0.15f, 0.02f, -0.60f), new Vector3(0.10f, 0.56f, 0.03f), new Vector3(-11f, 0f, 4f));
            Box(root, p.Strap, "Harness_R", new Vector3(0.15f, 0.02f, -0.60f), new Vector3(0.10f, 0.56f, 0.03f), new Vector3(-11f, 0f, -4f));
            Box(root, p.Strap, "Harness_Lap", new Vector3(0f, -0.50f, -0.34f), new Vector3(0.56f, 0.10f, 0.03f), Vector3.zero);
            Box(root, p.Metal, "Seat_Rail_L", new Vector3(-0.46f, -0.94f, -0.42f), new Vector3(0.07f, 0.08f, 0.66f), Vector3.zero);
            Box(root, p.Metal, "Seat_Rail_R", new Vector3(0.46f, -0.94f, -0.42f), new Vector3(0.07f, 0.08f, 0.66f), Vector3.zero);

            // ── Console: knee wall, deck, controls ────────────────────────────
            Box(root, p.Dark, "Console_Knee", new Vector3(0f, -0.46f, 0.56f), new Vector3(cs * 0.90f, 0.46f, 0.20f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Metal, "Console_Deck", new Vector3(0f, -0.17f, 0.62f), new Vector3(cs * 0.92f, 0.10f, 0.44f), new Vector3(-12f, 0f, 0f));
            Box(root, p.GlowDim, "Console_Strip", new Vector3(0f, -0.11f, 0.84f), new Vector3(cs * 0.86f, 0.02f, 0.03f), Vector3.zero);

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.34f;
                Box(root, p.Accent, "Console_Switch_" + i, new Vector3(x, -0.09f, 0.62f), new Vector3(0.16f, 0.05f, 0.10f), new Vector3(-12f, 0f, 0f));
                Box(root, p.GlowDim, "Console_Lamp_" + i, new Vector3(x, -0.08f, 0.74f), new Vector3(0.05f, 0.02f, 0.05f), new Vector3(-12f, 0f, 0f));
            }

            // Throttle quadrant: housing, two levers, a gate plate.
            Box(root, p.Metal, "Throttle_Housing", new Vector3(0.72f, -0.06f, 0.50f), new Vector3(0.20f, 0.14f, 0.34f), new Vector3(-18f, 0f, 0f));
            Box(root, p.Accent, "Throttle_Lever_A", new Vector3(0.67f, 0.06f, 0.52f), new Vector3(0.04f, 0.20f, 0.04f), new Vector3(-22f, 0f, -6f));
            Box(root, p.Accent, "Throttle_Lever_B", new Vector3(0.77f, 0.04f, 0.54f), new Vector3(0.04f, 0.18f, 0.04f), new Vector3(-18f, 0f, 6f));
            Box(root, p.Glow, "Throttle_Indicator", new Vector3(0.72f, 0.03f, 0.66f), new Vector3(0.05f, 0.02f, 0.05f), Vector3.zero);

            // Left hand: a side stick. Right hand: the throttle. That is the whole layout.
            Box(root, p.Dark, "Stick_Base", new Vector3(-0.66f, -0.20f, 0.42f), new Vector3(0.16f, 0.10f, 0.16f), Vector3.zero);
            Cylinder(root, p.Metal, "Stick_Shaft", new Vector3(-0.66f, -0.02f, 0.42f), new Vector3(0.05f, 0.16f, 0.05f), new Vector3(-10f, 0f, 0f));
            Box(root, p.Seat, "Stick_Grip", new Vector3(-0.66f, 0.13f, 0.44f), new Vector3(0.09f, 0.12f, 0.09f), new Vector3(-10f, 0f, 0f));
            Box(root, p.Accent, "Stick_Trigger", new Vector3(-0.66f, 0.16f, 0.50f), new Vector3(0.04f, 0.04f, 0.03f), Vector3.zero);

            // Rudder pedals in the footwell.
            Box(root, p.Dark, "Footwell", new Vector3(0f, -0.92f, 0.98f), new Vector3(cs * 0.80f, 0.34f, 0.18f), new Vector3(10f, 0f, 0f));
            Box(root, p.Metal, "Pedal_L", new Vector3(-0.20f, -0.74f, 0.90f), new Vector3(0.16f, 0.16f, 0.05f), new Vector3(24f, 0f, 0f));
            Box(root, p.Metal, "Pedal_R", new Vector3(0.20f, -0.74f, 0.90f), new Vector3(0.16f, 0.16f, 0.05f), new Vector3(24f, 0f, 0f));

            // ── Three displays across the top ─────────────────────────────────
            Quaternion screenTilt = Quaternion.Euler(-20f, 0f, 0f);
            Vector3 screenForward = screenTilt * Vector3.forward;
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.62f;
                Vector3 centre = new Vector3(x, 0.16f, 0.86f);
                Box(root, p.Dark, "Display_Bezel_" + i, centre, new Vector3(0.62f, 0.48f, 0.08f), screenTilt.eulerAngles);
                Box(root, p.GlowDim, "Display_Panel_" + i, centre + screenForward * 0.052f, new Vector3(0.52f, 0.38f, 0.01f), screenTilt.eulerAngles);
                // Two knobs under each display: the bezel should not read as a bare slab.
                Box(root, p.Metal, "Display_Knob_A" + i, centre + new Vector3(-0.18f, -0.28f, 0.03f), new Vector3(0.05f, 0.05f, 0.04f), Vector3.zero);
                Box(root, p.Metal, "Display_Knob_B" + i, centre + new Vector3(-0.06f, -0.28f, 0.03f), new Vector3(0.05f, 0.05f, 0.04f), Vector3.zero);
            }

            // ── Overhead panel and cabin light ────────────────────────────────
            Box(root, p.Dark, "Overhead", new Vector3(0f, 0.86f, 0.34f), new Vector3(0.96f, 0.14f, 0.52f), new Vector3(8f, 0f, 0f));
            for (int i = 0; i < 4; i++)
                Box(root, p.GlowDim, "Overhead_Lamp_" + i, new Vector3(-0.30f + i * 0.20f, 0.78f, 0.44f), new Vector3(0.06f, 0.03f, 0.06f), Vector3.zero);
            Box(root, p.Glow, "Cabin_Light", new Vector3(0f, 0.82f, -0.18f), new Vector3(0.90f, 0.02f, 0.10f), Vector3.zero);

            // ── Side consoles ─────────────────────────────────────────────────
            Box(root, p.Dark, "Side_L", new Vector3(-0.96f, -0.30f, 0.02f), new Vector3(0.24f, 0.58f, 0.96f), new Vector3(-10f, 0f, 6f));
            Box(root, p.Dark, "Side_R", new Vector3(0.96f, -0.30f, 0.02f), new Vector3(0.24f, 0.58f, 0.96f), new Vector3(-10f, 0f, -6f));
            for (int i = 0; i < 3; i++)
            {
                Box(root, p.GlowDim, "Side_Lamp_L" + i, new Vector3(-0.88f, -0.05f + i * 0.09f, 0.30f), new Vector3(0.05f, 0.03f, 0.05f), Vector3.zero);
                Box(root, p.GlowDim, "Side_Lamp_R" + i, new Vector3(0.88f, -0.05f + i * 0.09f, 0.30f), new Vector3(0.05f, 0.03f, 0.05f), Vector3.zero);
            }

            // Grab handles over each shoulder: something to hold when the hull rolls.
            Box(root, p.Metal, "Handle_L", new Vector3(-0.86f, 0.52f, 0.52f), new Vector3(0.05f, 0.05f, 0.34f), Vector3.zero);
            Box(root, p.Metal, "Handle_R", new Vector3(0.86f, 0.52f, 0.52f), new Vector3(0.05f, 0.05f, 0.34f), Vector3.zero);

            // A real light in the cabin. One per cockpit, no shadows, so a pilot can read
            // the console with the ship's own lighting off - the interior is where the
            // player actually lives.
            var lamp = Group(root.transform, "Cabin_Lamp", new Vector3(0f, 0.74f, 0.16f));
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.72f, 0.88f, 1f);
            light.intensity = 1.25f;
            light.range = 3.4f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHELL 1 - the crawler nose
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildCrawler(GameObject root, Palette p)
        {
            const float cs = CellUnit;
            // Nose: low and wide. Everything below the eye line, so the windscreen is the
            // thing the pilot looks through rather than over.
            Box(root, p.Hull, "Nose", new Vector3(0f, -0.60f, 0.98f), new Vector3(cs * 0.97f, 0.62f, 0.56f), Vector3.zero);
            Box(root, p.Dark, "Nose_Seam", new Vector3(0f, -0.30f, 1.24f), new Vector3(cs * 0.90f, 0.05f, 0.04f), Vector3.zero);
            Box(root, p.Dark, "Bumper", new Vector3(0f, -1.02f, 1.10f), new Vector3(cs * 0.99f, 0.26f, 0.28f), Vector3.zero);
            Box(root, p.Accent, "Bumper_Stripe", new Vector3(0f, -0.98f, 1.245f), new Vector3(cs * 0.86f, 0.10f, 0.03f), Vector3.zero);
            Box(root, p.Metal, "Tow_Hook", new Vector3(0f, -1.14f, 1.16f), new Vector3(0.22f, 0.10f, 0.16f), Vector3.zero);
            Cylinder(root, p.Glow, "Lamp_L", new Vector3(-0.72f, -0.74f, 1.235f), new Vector3(0.22f, 0.06f, 0.22f), new Vector3(90f, 0f, 0f));
            Cylinder(root, p.Glow, "Lamp_R", new Vector3(0.72f, -0.74f, 1.235f), new Vector3(0.22f, 0.06f, 0.22f), new Vector3(90f, 0f, 0f));
            Box(root, p.Accent, "Marker_L", new Vector3(-1.02f, -0.42f, 1.20f), new Vector3(0.08f, 0.06f, 0.05f), Vector3.zero);
            Box(root, p.Accent, "Marker_R", new Vector3(1.02f, -0.42f, 1.20f), new Vector3(0.08f, 0.06f, 0.05f), Vector3.zero);

            // Glacis and glass: one centre pane, two corner panes wrapped back, four
            // pillars between them, wipers on the centre pane.
            Box(root, p.Hull, "Glacis", new Vector3(0f, 0.16f, 1.05f), new Vector3(cs * 0.97f, 0.30f, 0.12f), new Vector3(-32f, 0f, 0f));
            Box(root, p.Glass, "Glass_Centre", new Vector3(0f, 0.26f, 1.08f), new Vector3(cs * 0.52f, 0.58f, 0.04f), new Vector3(-32f, 0f, 0f));
            Box(root, p.Glass, "Glass_Wrap_L", new Vector3(-0.86f, 0.26f, 0.94f), new Vector3(cs * 0.26f, 0.58f, 0.04f), new Vector3(-30f, 30f, 0f));
            Box(root, p.Glass, "Glass_Wrap_R", new Vector3(0.86f, 0.26f, 0.94f), new Vector3(cs * 0.26f, 0.58f, 0.04f), new Vector3(-30f, -30f, 0f));
            Box(root, p.Dark, "Pillar_L", new Vector3(-0.52f, 0.24f, 1.03f), new Vector3(0.09f, 0.62f, 0.13f), new Vector3(-31f, 0f, 0f));
            Box(root, p.Dark, "Pillar_R", new Vector3(0.52f, 0.24f, 1.03f), new Vector3(0.09f, 0.62f, 0.13f), new Vector3(-31f, 0f, 0f));
            Box(root, p.Dark, "Pillar_Far_L", new Vector3(-1.09f, 0.22f, 0.80f), new Vector3(0.10f, 0.60f, 0.13f), new Vector3(-20f, 34f, 0f));
            Box(root, p.Dark, "Pillar_Far_R", new Vector3(1.09f, 0.22f, 0.80f), new Vector3(0.10f, 0.60f, 0.13f), new Vector3(-20f, -34f, 0f));
            Box(root, p.Metal, "Wiper_L", new Vector3(-0.20f, 0.02f, 1.10f), new Vector3(0.03f, 0.40f, 0.03f), new Vector3(-32f, 0f, 18f));
            Box(root, p.Metal, "Wiper_R", new Vector3(0.20f, 0.02f, 1.10f), new Vector3(0.03f, 0.40f, 0.03f), new Vector3(-32f, 0f, -18f));

            // Roof, crown, roll bar, two work lamps and an antenna.
            Box(root, p.Hull, "Roof", new Vector3(0f, 1.02f, 0.06f), new Vector3(cs * 0.99f, 0.18f, cs * 0.94f), Vector3.zero);
            Box(root, p.Dark, "Roof_Crown", new Vector3(0f, 1.16f, -0.02f), new Vector3(cs * 0.74f, 0.12f, cs * 0.72f), Vector3.zero);
            Box(root, p.Metal, "Roll_Bar", new Vector3(0f, 0.98f, -0.92f), new Vector3(cs * 0.94f, 0.12f, 0.12f), Vector3.zero);
            Cylinder(root, p.Metal, "Roll_Bar_Post_L", new Vector3(-1.00f, 0.60f, -0.92f), new Vector3(0.10f, 0.36f, 0.10f), Vector3.zero);
            Cylinder(root, p.Metal, "Roll_Bar_Post_R", new Vector3(1.00f, 0.60f, -0.92f), new Vector3(0.10f, 0.36f, 0.10f), Vector3.zero);
            Box(root, p.Glow, "Roof_Lamp_L", new Vector3(-0.30f, 1.20f, 0.86f), new Vector3(0.20f, 0.06f, 0.08f), new Vector3(-18f, 0f, 0f));
            Box(root, p.Glow, "Roof_Lamp_R", new Vector3(0.30f, 1.20f, 0.86f), new Vector3(0.20f, 0.06f, 0.08f), new Vector3(-18f, 0f, 0f));
            Cylinder(root, p.Metal, "Antenna", new Vector3(0.86f, 1.14f, -0.64f), new Vector3(0.03f, 0.22f, 0.03f), new Vector3(-8f, 0f, 0f));

            // Flanks: hull below the waistline, glass above it, a seam, fenders and a step.
            Box(root, p.Hull, "Flank_L", new Vector3(-1.17f, -0.62f, 0.02f), new Vector3(0.13f, 0.86f, cs * 0.95f), Vector3.zero);
            Box(root, p.Hull, "Flank_R", new Vector3(1.17f, -0.62f, 0.02f), new Vector3(0.13f, 0.86f, cs * 0.95f), Vector3.zero);
            Box(root, p.Glass, "Side_Glass_L", new Vector3(-1.19f, 0.30f, 0.10f), new Vector3(0.05f, 0.56f, cs * 0.50f), Vector3.zero);
            Box(root, p.Glass, "Side_Glass_R", new Vector3(1.19f, 0.30f, 0.10f), new Vector3(0.05f, 0.56f, cs * 0.50f), Vector3.zero);
            Box(root, p.Dark, "Side_Divider_L", new Vector3(-1.20f, 0.30f, -0.14f), new Vector3(0.04f, 0.58f, 0.06f), Vector3.zero);
            Box(root, p.Dark, "Side_Divider_R", new Vector3(1.20f, 0.30f, -0.14f), new Vector3(0.04f, 0.58f, 0.06f), Vector3.zero);
            Box(root, p.Dark, "Flank_Seam_L", new Vector3(-1.19f, -0.62f, 0.02f), new Vector3(0.02f, 0.06f, cs * 0.94f), Vector3.zero);
            Box(root, p.Dark, "Flank_Seam_R", new Vector3(1.19f, -0.62f, 0.02f), new Vector3(0.02f, 0.06f, cs * 0.94f), Vector3.zero);
            Box(root, p.Dark, "Fender_L", new Vector3(-1.20f, -1.02f, 0f), new Vector3(0.16f, 0.40f, cs * 0.92f), Vector3.zero);
            Box(root, p.Dark, "Fender_R", new Vector3(1.20f, -1.02f, 0f), new Vector3(0.16f, 0.40f, cs * 0.92f), Vector3.zero);
            Box(root, p.Metal, "Grab_L", new Vector3(-1.19f, 0.66f, 0.40f), new Vector3(0.05f, 0.05f, 0.70f), Vector3.zero);
            Box(root, p.Metal, "Step_L", new Vector3(-1.05f, -1.14f, 0.55f), new Vector3(0.26f, 0.07f, 0.42f), Vector3.zero);
            Box(root, p.Accent, "Tail_L", new Vector3(-0.92f, -0.62f, -1.22f), new Vector3(0.16f, 0.10f, 0.04f), Vector3.zero);
            Box(root, p.Accent, "Tail_R", new Vector3(0.92f, -0.62f, -1.22f), new Vector3(0.16f, 0.10f, 0.04f), Vector3.zero);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHELL 2 - the boxy survey cab
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildCab(GameObject root, Palette p)
        {
            const float cs = CellUnit;
            // Front: upright panel under a square windscreen, grille below it.
            Box(root, p.Hull, "Front", new Vector3(0f, -0.62f, 1.16f), new Vector3(cs * 0.99f, 0.96f, 0.14f), Vector3.zero);
            Box(root, p.Glass, "Windscreen", new Vector3(0f, 0.30f, 1.19f), new Vector3(cs * 0.78f, 0.78f, 0.05f), new Vector3(-5f, 0f, 0f));
            for (int i = 0; i < 5; i++)
                Box(root, p.Metal, "Grille_" + i, new Vector3(0f, -0.84f + i * 0.10f, 1.235f), new Vector3(cs * 0.56f, 0.05f, 0.04f), Vector3.zero);
            Box(root, p.Accent, "Nose_Stripe", new Vector3(0f, -0.26f, 1.235f), new Vector3(cs * 0.94f, 0.13f, 0.03f), Vector3.zero);
            Box(root, p.Dark, "Bumper", new Vector3(0f, -1.04f, 1.14f), new Vector3(cs * 0.99f, 0.22f, 0.24f), Vector3.zero);
            Cylinder(root, p.Glow, "Lamp_L", new Vector3(-0.80f, -0.38f, 1.225f), new Vector3(0.20f, 0.05f, 0.20f), new Vector3(90f, 0f, 0f));
            Cylinder(root, p.Glow, "Lamp_R", new Vector3(0.80f, -0.38f, 1.225f), new Vector3(0.20f, 0.05f, 0.20f), new Vector3(90f, 0f, 0f));
            Box(root, p.Accent, "Marker_L", new Vector3(-1.04f, -0.30f, 1.20f), new Vector3(0.07f, 0.05f, 0.05f), Vector3.zero);
            Box(root, p.Accent, "Marker_R", new Vector3(1.04f, -0.30f, 1.20f), new Vector3(0.07f, 0.05f, 0.05f), Vector3.zero);
            Box(root, p.Metal, "Wiper_L", new Vector3(-0.22f, 0.06f, 1.215f), new Vector3(0.03f, 0.42f, 0.03f), new Vector3(-5f, 0f, 14f));
            Box(root, p.Metal, "Wiper_R", new Vector3(0.22f, 0.06f, 1.215f), new Vector3(0.03f, 0.42f, 0.03f), new Vector3(-5f, 0f, -14f));

            // Upright pillars: the whole point of this shell is that it is square.
            Box(root, p.Dark, "Pillar_L", new Vector3(-1.03f, 0.32f, 1.14f), new Vector3(0.13f, 0.92f, 0.16f), Vector3.zero);
            Box(root, p.Dark, "Pillar_R", new Vector3(1.03f, 0.32f, 1.14f), new Vector3(0.13f, 0.92f, 0.16f), Vector3.zero);

            // Roof: slab, louvers, dome and a bank of marker lamps.
            Box(root, p.Hull, "Roof", new Vector3(0f, 1.00f, 0f), new Vector3(cs * 0.99f, 0.20f, cs * 0.99f), Vector3.zero);
            for (int i = 0; i < 6; i++)
                Box(root, p.Metal, "Roof_Louver_" + i, new Vector3(0f, 1.12f, -0.62f + i * 0.24f), new Vector3(cs * 0.68f, 0.05f, 0.08f), Vector3.zero);
            for (int i = 0; i < 3; i++)
                Box(root, p.Accent, "Roof_Marker_" + i, new Vector3(-0.36f + i * 0.36f, 1.11f, 1.10f), new Vector3(0.09f, 0.05f, 0.05f), Vector3.zero);
            Cylinder(root, p.Dark, "Dome_Base", new Vector3(0.34f, 1.13f, -0.52f), new Vector3(0.44f, 0.05f, 0.44f), Vector3.zero);
            Sphere(root, p.Hull, "Dome", new Vector3(0.34f, 1.16f, -0.52f), new Vector3(0.40f, 0.24f, 0.40f));
            Box(root, p.Metal, "Dome_Stripe", new Vector3(0.34f, 1.16f, -0.72f), new Vector3(0.06f, 0.06f, 0.20f), Vector3.zero);
            Cylinder(root, p.Metal, "Dome_Antenna", new Vector3(0.34f, 1.30f, -0.52f), new Vector3(0.02f, 0.20f, 0.02f), Vector3.zero);
            Box(root, p.Dark, "Roof_Rack", new Vector3(-0.72f, 1.12f, -0.30f), new Vector3(0.44f, 0.06f, 0.86f), Vector3.zero);

            // Flanks: solid, window band, yellow stripe, mirrors, step, mudguard, ladder.
            Box(root, p.Hull, "Flank_L", new Vector3(-1.17f, -0.10f, 0.02f), new Vector3(0.13f, cs * 0.92f, cs * 0.94f), Vector3.zero);
            Box(root, p.Hull, "Flank_R", new Vector3(1.17f, -0.10f, 0.02f), new Vector3(0.13f, cs * 0.92f, cs * 0.94f), Vector3.zero);
            Box(root, p.Glass, "Side_Glass_L", new Vector3(-1.19f, 0.34f, 0.12f), new Vector3(0.05f, 0.52f, cs * 0.52f), Vector3.zero);
            Box(root, p.Glass, "Side_Glass_R", new Vector3(1.19f, 0.34f, 0.12f), new Vector3(0.05f, 0.52f, cs * 0.52f), Vector3.zero);
            Box(root, p.Dark, "Side_Divider_L", new Vector3(-1.20f, 0.34f, 0.02f), new Vector3(0.04f, 0.54f, 0.06f), Vector3.zero);
            Box(root, p.Dark, "Side_Divider_R", new Vector3(1.20f, 0.34f, 0.02f), new Vector3(0.04f, 0.54f, 0.06f), Vector3.zero);
            Box(root, p.Accent, "Stripe_L", new Vector3(-1.20f, -0.36f, 0.02f), new Vector3(0.04f, 0.16f, cs * 0.92f), Vector3.zero);
            Box(root, p.Accent, "Stripe_R", new Vector3(1.20f, -0.36f, 0.02f), new Vector3(0.04f, 0.16f, cs * 0.92f), Vector3.zero);

            Box(root, p.Metal, "Mirror_Stalk_L", new Vector3(-1.16f, 0.70f, 0.92f), new Vector3(0.24f, 0.04f, 0.04f), Vector3.zero);
            Box(root, p.Dark, "Mirror_L", new Vector3(-1.10f, 0.70f, 0.84f), new Vector3(0.16f, 0.22f, 0.05f), Vector3.zero);
            Box(root, p.Metal, "Mirror_Stalk_R", new Vector3(1.16f, 0.70f, 0.92f), new Vector3(0.24f, 0.04f, 0.04f), Vector3.zero);
            Box(root, p.Dark, "Mirror_R", new Vector3(1.10f, 0.70f, 0.84f), new Vector3(0.16f, 0.22f, 0.05f), Vector3.zero);
            Box(root, p.Metal, "Step_R", new Vector3(1.02f, -1.14f, 0.62f), new Vector3(0.30f, 0.07f, 0.46f), Vector3.zero);
            Box(root, p.Metal, "Step_L", new Vector3(-1.02f, -1.14f, 0.62f), new Vector3(0.30f, 0.07f, 0.46f), Vector3.zero);
            Box(root, p.Dark, "Mudguard_L", new Vector3(-1.19f, -0.86f, 0.02f), new Vector3(0.16f, 0.42f, cs * 0.94f), Vector3.zero);
            Box(root, p.Dark, "Mudguard_R", new Vector3(1.19f, -0.86f, 0.02f), new Vector3(0.16f, 0.42f, cs * 0.94f), Vector3.zero);

            for (int i = 0; i < 3; i++)
                Box(root, p.Metal, "Ladder_Rung_" + i, new Vector3(0f, -0.86f + i * 0.34f, -1.22f), new Vector3(0.44f, 0.05f, 0.05f), Vector3.zero);
            Box(root, p.Accent, "Tail_L", new Vector3(-0.86f, -0.50f, -1.24f), new Vector3(0.14f, 0.20f, 0.04f), Vector3.zero);
            Box(root, p.Accent, "Tail_R", new Vector3(0.86f, -0.50f, -1.24f), new Vector3(0.14f, 0.20f, 0.04f), Vector3.zero);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Materials
        // ═══════════════════════════════════════════════════════════════════════
        /// <summary>
        /// The cockpit's palette, built once per prefab. Every material goes through the
        /// block builder's factory so a prefab saved by Setup holds asset references rather
        /// than transient materials that vanish on the next domain reload.
        /// </summary>
        private struct Palette
        {
            public readonly Material Hull;
            public readonly Material Dark;
            public readonly Material Metal;
            public readonly Material Glass;
            public readonly Material Accent;
            public readonly Material Glow;
            public readonly Material GlowDim;
            public readonly Material Seat;
            public readonly Material Strap;

            public Palette(Color bodyColor)
            {
                // A cockpit is a machine, not a paint sample: the base colour tints the
                // hull rather than becoming it.
                Color hull = Color.Lerp(new Color(0.26f, 0.28f, 0.32f), bodyColor, 0.35f);
                Hull = GridBlockMeshBuilder.CreateMaterial(hull, 0.55f, 0.45f);
                Dark = GridBlockMeshBuilder.CreateMaterial(hull * 0.55f, 0.72f, 0.35f);
                Metal = GridBlockMeshBuilder.CreateMaterial(new Color(0.55f, 0.57f, 0.62f), 0.82f, 0.55f);
                Accent = GridBlockMeshBuilder.CreateMaterial(new Color(0.90f, 0.69f, 0.16f), 0.35f, 0.55f);
                Seat = GridBlockMeshBuilder.CreateMaterial(new Color(0.17f, 0.19f, 0.23f), 0.05f, 0.25f);
                Strap = GridBlockMeshBuilder.CreateMaterial(new Color(0.32f, 0.30f, 0.26f), 0.02f, 0.18f);
                Glow = GridBlockMeshBuilder.CreateMaterial(new Color(0.55f, 0.90f, 1f), 0f, 0.9f,
                    new Color(0.20f, 0.70f, 0.95f));
                GlowDim = GridBlockMeshBuilder.CreateMaterial(new Color(0.20f, 0.45f, 0.55f), 0f, 0.8f,
                    new Color(0.06f, 0.24f, 0.34f));
                Glass = MakeGlass();
            }

            private static Material MakeGlass()
            {
                var glass = GridBlockMeshBuilder.CreateMaterial(new Color(0.30f, 0.55f, 0.62f, 0.28f), 0.05f, 0.95f);
                if (glass == null) return null;

                // URP transparency: surface type, blend mode and the keyword that turns the
                // shader's blend path on. Without all three the panes render opaque and the
                // interior disappears behind them.
                if (glass.HasProperty("_Surface")) glass.SetFloat("_Surface", 1f);
                if (glass.HasProperty("_Blend")) glass.SetFloat("_Blend", 0f);
                if (glass.HasProperty("_ZWrite")) glass.SetFloat("_ZWrite", 0f);
                glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                glass.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                glass.renderQueue = (int)RenderQueue.Transparent;
                return glass;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Primitives
        // ═══════════════════════════════════════════════════════════════════════
        private static GameObject Group(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go;
        }

        private static void Box(GameObject parent, Material m, string name, Vector3 pos, Vector3 scale, Vector3 euler)
            => Prim(parent, PrimitiveType.Cube, m, name, pos, scale, euler);

        private static void Cylinder(GameObject parent, Material m, string name, Vector3 pos, Vector3 scale, Vector3 euler)
            => Prim(parent, PrimitiveType.Cylinder, m, name, pos, scale, euler);

        private static void Sphere(GameObject parent, Material m, string name, Vector3 pos, Vector3 scale)
            => Prim(parent, PrimitiveType.Sphere, m, name, pos, scale, Vector3.zero);

        private static void Prim(GameObject parent, PrimitiveType type, Material m, string name,
            Vector3 pos, Vector3 scale, Vector3 euler)
        {
            if (parent == null || m == null) return;

            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = m;
        }
    }
}
