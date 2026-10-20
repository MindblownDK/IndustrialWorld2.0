// Assets/Scripts/VoxelEngine/GridSystem/CockpitShellBuilder.cs
//
// 17.39.0-dev - one cockpit, two shells, and a cabin you can actually see out of.
//
// A block is authored facing -Z in this project: the ghost draws its FRONT marker on the
// minimum Z face. Everything below is drawn facing +Z because that is how a cockpit reads
// while it is being modelled, and the cabin and both shells are then turned 180 degrees
// about Y as they are parented. That single rotation is the difference between a pilot
// looking out of the windscreen and a pilot looking at the back of their own seat.
//
//   CABIN     seat and harness, a dash that stays Below the sight line so the horizon is
//             clear, three angled multi-function displays with drawn readouts, a holo ring,
//             side keypads, an overhead switch strip and a cabin light.
//   CRAWLER   a low nose, glass down to the sill, thin pillars, a roll bar behind.
//   CAB       an upright survey cab: square windscreen, mirrors, roof dome.
//
// Both shells ship inside the one cockpit prefab and only one is enabled at a time, so a
// cockpit changes character without changing item.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class CockpitShellBuilder
    {
        /// <summary>Shell styles, as stored on <see cref="GridCockpit.shellStyle"/>.</summary>
        public const int StyleLegacy = 0;
        public const int StyleCrawler = 1;
        public const int StyleCab = 2;

        public const string ShellRootName = "Generated_CockpitShell";
        public const string InteriorName = "Cabin";
        public const string SeatAnchorName = "SeatAnchor";
        public const string CrawlerName = "Shell_Crawler";
        public const string CabName = "Shell_Cab";

        /// <summary>Every shell a cockpit can wear, in the order the build wheel offers them.</summary>
        public static readonly int[] Styles = { StyleCrawler, StyleCab };

        /// <summary>Every measurement below is authored against a 2 m grid cell and then
        /// scaled with the block, so a cockpit reads the same whatever cell it is built on.</summary>
        private const float CellUnit = 2f;

        /// <summary>Where a pilot's seated eye lands, in cell units, before the cabin is
        /// turned to face -Z.</summary>
        private static readonly Vector3 SeatEye = new(0f, 0.14f, -0.40f);

        private const string ShellPreferenceKey = "cockpit.shell";
        private static int _selectedStyle = -1;

        // ═══════════════════════════════════════════════════════════════════════
        //  WHICH SHELL IS ARMED
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The shell the next cockpit placed in the world will wear. Chosen on the build
        /// wheel, remembered between sessions: a player who builds cabs wants cabs.
        /// </summary>
        public static int SelectedStyle
        {
            get
            {
                if (_selectedStyle < 0)
                    _selectedStyle = Normalise(PlayerPrefs.GetInt(ShellPreferenceKey, StyleCrawler));
                return _selectedStyle;
            }
            set
            {
                _selectedStyle = Normalise(value);
                PlayerPrefs.SetInt(ShellPreferenceKey, _selectedStyle);
            }
        }

        /// <summary>Any stored or requested value folded into a shell that exists.</summary>
        private static int Normalise(int style) => style == StyleCab ? StyleCab : StyleCrawler;

        /// <summary>The name the wheel and the item description use for a shell.</summary>
        public static string NameOf(int style) => Normalise(style) == StyleCab ? "Armored Cab" : "Wraparound Cockpit";

        /// <summary>One line about what a shell is for, printed in the wheel's hub.</summary>
        public static string BlurbOf(int style) => Normalise(style) == StyleCab
            ? "Broad panoramic glazing above a solid armored lower cab; the hull has increased integrity."
            : "Low-profile wraparound glass with three floating MFDs and an open forward sightline.";

        // ═══════════════════════════════════════════════════════════════════════
        //  BUILD
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build the whole cockpit: one cabin and both shells, with
        /// <paramref name="style"/> enabled. Editor only - a prefab without a shell gets one
        /// from Setup, and nothing is generated at play time.
        /// </summary>
        public static void Build(GameObject root, float cellSize, Color bodyColor, int style)
        {
            if (root == null || cellSize <= 0.01f) return;
            if (Application.isPlaying) return;
            if (style == StyleLegacy) return;

            var existing = root.transform.Find(ShellRootName);
            Transform shellRoot = existing != null
                ? existing
                : Group(root.transform, ShellRootName, Vector3.zero).transform;

            // Replace only the four subtrees owned by this builder. Custom prefab children
            // beside them stay attached to Generated_CockpitShell across this refresh.
            RemoveGeneratedChild(shellRoot, InteriorName);
            RemoveGeneratedChild(shellRoot, CrawlerName);
            RemoveGeneratedChild(shellRoot, CabName);
            RemoveGeneratedChild(shellRoot, SeatAnchorName);

            var palette = new Palette(bodyColor);
            float scale = cellSize / CellUnit;

            // Authored facing +Z, parented facing -Z. See the header.
            var cabin = Group(shellRoot.transform, InteriorName, Vector3.zero);
            FaceForward(cabin, scale);
            BuildInterior(cabin, palette);

            var crawler = Group(shellRoot.transform, CrawlerName, Vector3.zero);
            FaceForward(crawler, scale);
            BuildCrawler(crawler, palette);

            var cab = Group(shellRoot.transform, CabName, Vector3.zero);
            FaceForward(cab, scale);
            BuildCab(cab, palette);

            ApplyShell(root, style);

            // The seat pose lives beside the shells, not inside one: the room is shared, so
            // there is exactly one answer to where the pilot's eyes go. It is authored in
            // the finished (-Z facing) frame, which is why z is positive here.
            var anchor = Group(shellRoot.transform, SeatAnchorName,
                new Vector3(SeatEye.x, SeatEye.y, -SeatEye.z) * scale);
            anchor.transform.localRotation = Quaternion.Euler(6f, 180f, 0f);
        }

        private static void RemoveGeneratedChild(Transform parent, string name)
        {
            if (parent == null) return;
            Transform child = parent.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        private static void FaceForward(GameObject group, float scale)
        {
            group.transform.localScale = new Vector3(scale, scale, scale);
            group.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>
        /// Show the shell this cockpit wears. Both live in the prefab, so this only flips
        /// which hull is active - no rebuild, and nothing duplicated.
        /// </summary>
        public static void ApplyShell(GameObject root, int style)
        {
            if (root == null || style == StyleLegacy) return;

            var shellRoot = root.transform.Find(ShellRootName);
            if (shellRoot == null) return;

            var crawler = shellRoot.Find(CrawlerName);
            var cab = shellRoot.Find(CabName);
            bool cabWanted = Normalise(style) == StyleCab;
            if (crawler != null) crawler.gameObject.SetActive(!cabWanted);
            if (cab != null) cab.gameObject.SetActive(cabWanted);
        }

        /// <summary>Give a cockpit a shell: the field, the visual, and the record of which
        /// one it wears, in that order.</summary>
        public static void SetShell(GridCockpit cockpit, int style)
        {
            if (cockpit == null) return;
            cockpit.shellStyle = Normalise(style);
            ApplyShell(cockpit.gameObject, cockpit.shellStyle);
            cockpit.ApplyShellDurability();
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
        //  THE CABIN
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildInterior(GameObject root, Palette p)
        {
            const float cs = CellUnit;
            float h = cs * 0.5f;

            // ── Room ────────────────────────────────────────────────────────────
            Box(root, p.Dark, "Floor", new Vector3(0f, -h + 0.08f, 0f), new Vector3(1.94f, 0.10f, 1.94f), Vector3.zero);
            for (int i = 0; i < 5; i++)
                Box(root, p.Metal, "Floor_Rib_" + i, new Vector3(0f, -h + 0.15f, -0.68f + i * 0.34f),
                    new Vector3(1.70f, 0.02f, 0.06f), Vector3.zero);

            Box(root, p.Hull, "Bulkhead", new Vector3(0f, -0.06f, -0.94f), new Vector3(1.94f, 1.70f, 0.08f), Vector3.zero);
            Box(root, p.Hull, "Bulkhead_Header", new Vector3(0f, 0.86f, -0.94f), new Vector3(1.94f, 0.30f, 0.08f), Vector3.zero);
            Box(root, p.Dark, "Hatch_Frame", new Vector3(0f, -0.26f, -0.90f), new Vector3(0.92f, 1.20f, 0.05f), Vector3.zero);
            Box(root, p.Hull, "Hatch", new Vector3(0f, -0.26f, -0.86f), new Vector3(0.76f, 1.02f, 0.04f), Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                float y = -0.62f + i * 0.36f;
                Box(root, p.Metal, "Hatch_Dog_L" + i, new Vector3(-0.33f, y, -0.83f), new Vector3(0.10f, 0.05f, 0.04f), Vector3.zero);
                Box(root, p.Metal, "Hatch_Dog_R" + i, new Vector3(0.33f, y, -0.83f), new Vector3(0.10f, 0.05f, 0.04f), Vector3.zero);
            }

            // ── Seat ────────────────────────────────────────────────────────────
            Box(root, p.Dark, "Seat_Pedestal", new Vector3(0f, -0.76f, -0.40f), new Vector3(0.44f, 0.30f, 0.38f), Vector3.zero);
            Box(root, p.Seat, "Seat_Pan", new Vector3(0f, -0.58f, -0.40f), new Vector3(0.64f, 0.12f, 0.56f), Vector3.zero);
            Box(root, p.Seat, "Seat_Back", new Vector3(0f, -0.20f, -0.66f), new Vector3(0.64f, 0.74f, 0.13f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Seat, "Seat_Head", new Vector3(0f, 0.24f, -0.72f), new Vector3(0.40f, 0.24f, 0.11f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Seat, "Seat_Bolster_L", new Vector3(-0.35f, -0.45f, -0.40f), new Vector3(0.11f, 0.28f, 0.50f), Vector3.zero);
            Box(root, p.Seat, "Seat_Bolster_R", new Vector3(0.35f, -0.45f, -0.40f), new Vector3(0.11f, 0.28f, 0.50f), Vector3.zero);
            Box(root, p.Seat, "Armrest_L", new Vector3(-0.41f, -0.36f, -0.28f), new Vector3(0.13f, 0.08f, 0.44f), new Vector3(-4f, 0f, 0f));
            Box(root, p.Seat, "Armrest_R", new Vector3(0.41f, -0.36f, -0.28f), new Vector3(0.13f, 0.08f, 0.44f), new Vector3(-4f, 0f, 0f));
            Box(root, p.Strap, "Harness_L", new Vector3(-0.14f, 0.02f, -0.58f), new Vector3(0.09f, 0.52f, 0.03f), new Vector3(-13f, 0f, 4f));
            Box(root, p.Strap, "Harness_R", new Vector3(0.14f, 0.02f, -0.58f), new Vector3(0.09f, 0.52f, 0.03f), new Vector3(-13f, 0f, -4f));
            Box(root, p.Strap, "Harness_Lap", new Vector3(0f, -0.50f, -0.34f), new Vector3(0.54f, 0.09f, 0.03f), Vector3.zero);
            Box(root, p.Metal, "Seat_Rail_L", new Vector3(-0.44f, -0.90f, -0.40f), new Vector3(0.06f, 0.08f, 0.62f), Vector3.zero);
            Box(root, p.Metal, "Seat_Rail_R", new Vector3(0.44f, -0.90f, -0.40f), new Vector3(0.06f, 0.08f, 0.62f), Vector3.zero);

            // ── Floating display rail: three working screens, no tall dashboard ──
            // Keep the MFD object names and panel dimensions: GridCockpitMfd attaches
            // to these same transforms at runtime. Only the bulky binnacle and hood are
            // removed, leaving the pilot a clean view through the wraparound glazing.
            Quaternion screenTilt = Quaternion.Euler(-14f, 180f, 0f);
            Vector3 screenFace = screenTilt * Vector3.forward;

            Box(root, p.Dark, "Console_SupportRail", new Vector3(0f, -0.08f, 0.52f),
                new Vector3(1.40f, 0.045f, 0.065f), Vector3.zero);
            Box(root, p.Metal, "Console_SupportArm_L", new Vector3(-0.52f, -0.19f, 0.47f),
                new Vector3(0.035f, 0.28f, 0.045f), new Vector3(-10f, 0f, -4f));
            Box(root, p.Metal, "Console_SupportArm_R", new Vector3(0.52f, -0.19f, 0.47f),
                new Vector3(0.035f, 0.28f, 0.045f), new Vector3(-10f, 0f, 4f));

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.52f;
                Vector3 centre = new Vector3(x, 0.24f, 0.66f);

                Box(root, p.Dark, "MFD_Bezel_" + i, centre, new Vector3(0.50f, 0.38f, 0.05f), screenTilt.eulerAngles);
                Box(root, p.Screen, "MFD_Screen_" + i, centre + screenFace * 0.032f,
                    new Vector3(0.44f, 0.32f, 0.012f), screenTilt.eulerAngles);

                // A small readout stays baked into the unpowered screen; live TextMeshes
                // use the matching MFD_Screen_* surface in GridCockpitMfd.
                Box(root, p.Glow, "MFD_Bar_" + i, centre + screenTilt * new Vector3(-0.10f, 0.09f, 0.040f),
                    new Vector3(0.20f, 0.03f, 0.006f), screenTilt.eulerAngles);
                Box(root, p.GlowDim, "MFD_Bar2_" + i, centre + screenTilt * new Vector3(-0.14f, 0.04f, 0.040f),
                    new Vector3(0.12f, 0.025f, 0.006f), screenTilt.eulerAngles);
                Box(root, p.Accent, "MFD_Tick_" + i, centre + screenTilt * new Vector3(0.13f, -0.10f, 0.040f),
                    new Vector3(0.10f, 0.02f, 0.006f), screenTilt.eulerAngles);
                for (int g = 0; g < 3; g++)
                    Box(root, p.GlowDim, "MFD_Graph_" + i + "_" + g,
                        centre + screenTilt * new Vector3(-0.12f + g * 0.09f, -0.06f, 0.040f),
                        new Vector3(0.045f, 0.02f + g * 0.03f, 0.006f), screenTilt.eulerAngles);
            }

            // Side keypads, at hip height and angled inboard.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.84f;
                Box(root, p.Dark, "Side_Panel_" + side, new Vector3(x, -0.34f, 0.10f),
                    new Vector3(0.22f, 0.46f, 0.92f), new Vector3(-8f, 0f, side * -10f));
                for (int i = 0; i < 3; i++)
                    Box(root, p.GlowDim, "Side_Key_" + side + "_" + i,
                        new Vector3(x - side * 0.05f, -0.20f + i * 0.11f, 0.24f),
                        new Vector3(0.06f, 0.035f, 0.05f), Vector3.zero);
                Box(root, p.Accent, "Side_Toggle_" + side, new Vector3(x - side * 0.04f, -0.24f, -0.20f),
                    new Vector3(0.05f, 0.10f, 0.05f), new Vector3(-18f, 0f, 0f));
            }

            // Throttle and stick, where a hand expects them.
            Box(root, p.Metal, "Throttle_Housing", new Vector3(0.44f, -0.24f, 0.24f), new Vector3(0.18f, 0.10f, 0.26f), new Vector3(-16f, 0f, 0f));
            Box(root, p.Accent, "Throttle_Lever", new Vector3(0.44f, -0.12f, 0.26f), new Vector3(0.04f, 0.18f, 0.04f), new Vector3(-20f, 0f, 0f));
            Box(root, p.Dark, "Stick_Base", new Vector3(-0.44f, -0.30f, 0.20f), new Vector3(0.14f, 0.09f, 0.14f), Vector3.zero);
            Cylinder(root, p.Metal, "Stick_Shaft", new Vector3(-0.44f, -0.14f, 0.20f), new Vector3(0.045f, 0.14f, 0.045f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Seat, "Stick_Grip", new Vector3(-0.44f, 0.00f, 0.22f), new Vector3(0.08f, 0.11f, 0.08f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Accent, "Stick_Trigger", new Vector3(-0.44f, 0.02f, 0.27f), new Vector3(0.035f, 0.035f, 0.03f), Vector3.zero);

            // ── Overhead and light ──────────────────────────────────────────────
            Box(root, p.Dark, "Overhead", new Vector3(0f, 0.90f, 0.10f), new Vector3(0.92f, 0.12f, 0.46f), new Vector3(6f, 0f, 0f));
            for (int i = 0; i < 3; i++)
                Box(root, p.GlowDim, "Overhead_Lamp_" + i, new Vector3(-0.24f + i * 0.24f, 0.84f, 0.18f),
                    new Vector3(0.06f, 0.03f, 0.06f), Vector3.zero);
            Box(root, p.Glow, "Cabin_Light", new Vector3(0f, 0.86f, -0.20f), new Vector3(0.86f, 0.02f, 0.10f), Vector3.zero);

            var lamp = Group(root.transform, "Cabin_Lamp", new Vector3(0f, 0.70f, 0.02f));
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.74f, 0.88f, 1f);
            light.intensity = 1.35f;
            light.range = 3.2f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHELL 1 - the crawler nose
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildCrawler(GameObject root, Palette p)
        {
            // Compact, low nose: the armor stays below the pilot's horizon.
            Box(root, p.Hull, "Nose_ArmoredLow", new Vector3(0f, -0.70f, 0.80f),
                new Vector3(1.90f, 0.48f, 0.42f), Vector3.zero);
            Box(root, p.Dark, "Nose_Cowl", new Vector3(0f, -0.43f, 0.96f),
                new Vector3(1.82f, 0.09f, 0.18f), new Vector3(-18f, 0f, 0f));
            Box(root, p.Accent, "Nose_Ridge", new Vector3(0f, -0.39f, 1.01f),
                new Vector3(1.42f, 0.025f, 0.025f), new Vector3(-18f, 0f, 0f));
            Box(root, p.Metal, "Front_Bumper", new Vector3(0f, -1.00f, 0.94f),
                new Vector3(1.96f, 0.20f, 0.20f), Vector3.zero);
            Box(root, p.Accent, "Bumper_Stripe", new Vector3(0f, -0.97f, 1.045f),
                new Vector3(1.60f, 0.055f, 0.025f), Vector3.zero);
            Box(root, p.Metal, "Tow_Hook", new Vector3(0f, -1.12f, 0.99f),
                new Vector3(0.20f, 0.09f, 0.14f), Vector3.zero);
            Cylinder(root, p.Glow, "Lamp_L", new Vector3(-0.73f, -0.68f, 1.01f),
                new Vector3(0.18f, 0.045f, 0.18f), new Vector3(90f, 0f, 0f));
            Cylinder(root, p.Glow, "Lamp_R", new Vector3(0.73f, -0.68f, 1.01f),
                new Vector3(0.18f, 0.045f, 0.18f), new Vector3(90f, 0f, 0f));

            // One broad windscreen blends into two angled front wraps and full-length
            // side panes. The visible structural posts are narrow enough to keep the
            // panoramic feel from the supplied low-profile cockpit reference.
            Box(root, p.Glass, "Glass_Centre", new Vector3(0f, 0.27f, 0.94f),
                new Vector3(1.72f, 0.98f, 0.035f), new Vector3(-13f, 0f, 0f));
            Box(root, p.Glass, "Glass_Wrap_L", new Vector3(-0.84f, 0.27f, 0.75f),
                new Vector3(0.32f, 0.94f, 0.035f), new Vector3(-10f, 43f, 0f));
            Box(root, p.Glass, "Glass_Wrap_R", new Vector3(0.84f, 0.27f, 0.75f),
                new Vector3(0.32f, 0.94f, 0.035f), new Vector3(-10f, -43f, 0f));
            Box(root, p.Glass, "Side_Glass_L", new Vector3(-0.97f, 0.27f, -0.02f),
                new Vector3(0.035f, 0.90f, 1.58f), Vector3.zero);
            Box(root, p.Glass, "Side_Glass_R", new Vector3(0.97f, 0.27f, -0.02f),
                new Vector3(0.035f, 0.90f, 1.58f), Vector3.zero);
            Box(root, p.Hull, "Cowl_L", new Vector3(-0.62f, -0.27f, 0.99f),
                new Vector3(0.70f, 0.12f, 0.11f), new Vector3(-20f, 0f, 0f));
            Box(root, p.Hull, "Cowl_R", new Vector3(0.62f, -0.27f, 0.99f),
                new Vector3(0.70f, 0.12f, 0.11f), new Vector3(-20f, 0f, 0f));
            Box(root, p.Dark, "Pillar_Centre_L", new Vector3(-0.88f, 0.27f, 0.91f),
                new Vector3(0.045f, 0.99f, 0.085f), new Vector3(-13f, 0f, 0f));
            Box(root, p.Dark, "Pillar_Centre_R", new Vector3(0.88f, 0.27f, 0.91f),
                new Vector3(0.045f, 0.99f, 0.085f), new Vector3(-13f, 0f, 0f));
            Box(root, p.Metal, "Wrap_Post_L", new Vector3(-0.99f, 0.25f, 0.61f),
                new Vector3(0.045f, 0.92f, 0.07f), new Vector3(-9f, 42f, 0f));
            Box(root, p.Metal, "Wrap_Post_R", new Vector3(0.99f, 0.25f, 0.61f),
                new Vector3(0.045f, 0.92f, 0.07f), new Vector3(-9f, -42f, 0f));

            // A low canopy and thin rear roll structure protect the cabin without
            // dropping a bulky hood across the glass.
            Box(root, p.Hull, "Roof", new Vector3(0f, 0.94f, -0.04f),
                new Vector3(1.94f, 0.14f, 1.82f), Vector3.zero);
            Box(root, p.Dark, "Roof_Crown", new Vector3(0f, 1.045f, -0.12f),
                new Vector3(1.30f, 0.07f, 1.18f), Vector3.zero);
            for (int i = 0; i < 3; i++)
                Box(root, p.Metal, "Canopy_Rib_" + i, new Vector3(0f, 1.02f, -0.50f + i * 0.46f),
                    new Vector3(1.72f, 0.035f, 0.055f), Vector3.zero);
            Box(root, p.Metal, "Roll_Bar_Rear", new Vector3(0f, 0.98f, -0.89f),
                new Vector3(1.82f, 0.08f, 0.08f), Vector3.zero);
            Box(root, p.Metal, "Roll_Bar_Post_L", new Vector3(-0.91f, 0.57f, -0.89f),
                new Vector3(0.075f, 0.78f, 0.075f), Vector3.zero);
            Box(root, p.Metal, "Roll_Bar_Post_R", new Vector3(0.91f, 0.57f, -0.89f),
                new Vector3(0.075f, 0.78f, 0.075f), Vector3.zero);
            Box(root, p.Glow, "Roof_Lamp_L", new Vector3(-0.34f, 1.04f, 0.82f),
                new Vector3(0.20f, 0.045f, 0.075f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Glow, "Roof_Lamp_R", new Vector3(0.34f, 1.04f, 0.82f),
                new Vector3(0.20f, 0.045f, 0.075f), new Vector3(-12f, 0f, 0f));

            // Lower flanks and running gear keep the crawler silhouette visibly separate
            // from the taller armored cab, while leaving all side glass above the sill open.
            Box(root, p.Hull, "Flank_L", new Vector3(-0.94f, -0.67f, 0.00f),
                new Vector3(0.14f, 0.50f, 1.86f), Vector3.zero);
            Box(root, p.Hull, "Flank_R", new Vector3(0.94f, -0.67f, 0.00f),
                new Vector3(0.14f, 0.50f, 1.86f), Vector3.zero);
            Box(root, p.Accent, "Flank_Sill_L", new Vector3(-0.985f, -0.84f, -0.01f),
                new Vector3(0.025f, 0.045f, 1.62f), Vector3.zero);
            Box(root, p.Accent, "Flank_Sill_R", new Vector3(0.985f, -0.84f, -0.01f),
                new Vector3(0.025f, 0.045f, 1.62f), Vector3.zero);
            Box(root, p.Dark, "Fender_L", new Vector3(-0.99f, -1.00f, 0f),
                new Vector3(0.13f, 0.32f, 1.78f), Vector3.zero);
            Box(root, p.Dark, "Fender_R", new Vector3(0.99f, -1.00f, 0f),
                new Vector3(0.13f, 0.32f, 1.78f), Vector3.zero);
            Box(root, p.Metal, "Step_L", new Vector3(-0.85f, -1.13f, 0.52f),
                new Vector3(0.26f, 0.06f, 0.40f), Vector3.zero);
            Box(root, p.Metal, "Step_R", new Vector3(0.85f, -1.13f, 0.52f),
                new Vector3(0.26f, 0.06f, 0.40f), Vector3.zero);
            Box(root, p.Accent, "Tail_L", new Vector3(-0.84f, -0.60f, -0.98f),
                new Vector3(0.12f, 0.08f, 0.04f), Vector3.zero);
            Box(root, p.Accent, "Tail_R", new Vector3(0.84f, -0.60f, -0.98f),
                new Vector3(0.12f, 0.08f, 0.04f), Vector3.zero);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHELL 2 - the boxy survey cab
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildCab(GameObject root, Palette p)
        {
            // Broad windscreen and side glazing sit above a deliberate, continuous
            // armored lower half. There is no glass in the bumper, sill or lower doors.
            Box(root, p.Hull, "Armored_Lower_Nose", new Vector3(0f, -0.60f, 0.93f),
                new Vector3(1.94f, 0.68f, 0.20f), Vector3.zero);
            Box(root, p.Dark, "Cab_Sill", new Vector3(0f, -0.18f, 1.00f),
                new Vector3(1.96f, 0.22f, 0.16f), Vector3.zero);
            Box(root, p.Glass, "Windscreen", new Vector3(0f, 0.36f, 1.02f),
                new Vector3(1.84f, 0.98f, 0.045f), new Vector3(-5f, 0f, 0f));
            Box(root, p.Hull, "Front_Header", new Vector3(0f, 0.91f, 0.97f),
                new Vector3(1.96f, 0.13f, 0.15f), new Vector3(-5f, 0f, 0f));
            Box(root, p.Hull, "Front_Armor_Brow", new Vector3(0f, -0.28f, 1.07f),
                new Vector3(1.88f, 0.16f, 0.16f), Vector3.zero);
            for (int i = 0; i < 4; i++)
                Box(root, p.Metal, "Lower_Grille_" + i, new Vector3(0f, -0.67f + i * 0.10f, 1.045f),
                    new Vector3(1.00f, 0.035f, 0.035f), Vector3.zero);
            Box(root, p.Accent, "Front_Armor_Seam", new Vector3(0f, -0.37f, 1.045f),
                new Vector3(1.74f, 0.035f, 0.025f), Vector3.zero);
            Box(root, p.Metal, "Armored_Bumper", new Vector3(0f, -1.02f, 0.98f),
                new Vector3(1.98f, 0.22f, 0.20f), Vector3.zero);
            Box(root, p.Accent, "Bumper_RecoveryStripe", new Vector3(0f, -0.99f, 1.085f),
                new Vector3(1.65f, 0.055f, 0.025f), Vector3.zero);
            Cylinder(root, p.Glow, "Lamp_L", new Vector3(-0.79f, -0.46f, 1.03f),
                new Vector3(0.18f, 0.05f, 0.18f), new Vector3(90f, 0f, 0f));
            Cylinder(root, p.Glow, "Lamp_R", new Vector3(0.79f, -0.46f, 1.03f),
                new Vector3(0.18f, 0.05f, 0.18f), new Vector3(90f, 0f, 0f));

            Box(root, p.Dark, "Pillar_A_L", new Vector3(-0.96f, 0.37f, 0.98f),
                new Vector3(0.06f, 1.02f, 0.12f), new Vector3(-5f, 0f, 0f));
            Box(root, p.Dark, "Pillar_A_R", new Vector3(0.96f, 0.37f, 0.98f),
                new Vector3(0.06f, 1.02f, 0.12f), new Vector3(-5f, 0f, 0f));
            Box(root, p.Metal, "Wiper_L", new Vector3(-0.27f, 0.11f, 1.055f),
                new Vector3(0.022f, 0.42f, 0.022f), new Vector3(-5f, 0f, 12f));
            Box(root, p.Metal, "Wiper_R", new Vector3(0.27f, 0.11f, 1.055f),
                new Vector3(0.022f, 0.42f, 0.022f), new Vector3(-5f, 0f, -12f));

            // Armored roof cap, reinforced rails and a compact equipment dome.
            Box(root, p.Hull, "Roof", new Vector3(0f, 0.99f, -0.02f),
                new Vector3(1.98f, 0.18f, 1.96f), Vector3.zero);
            Box(root, p.Dark, "Roof_Armor_Ridge", new Vector3(0f, 1.105f, -0.12f),
                new Vector3(1.36f, 0.07f, 1.44f), Vector3.zero);
            for (int i = 0; i < 4; i++)
                Box(root, p.Metal, "Roof_Rib_" + i, new Vector3(0f, 1.09f, -0.55f + i * 0.38f),
                    new Vector3(1.72f, 0.035f, 0.065f), Vector3.zero);
            for (int i = 0; i < 3; i++)
                Box(root, p.Accent, "Roof_Marker_" + i, new Vector3(-0.30f + i * 0.30f, 1.105f, 0.96f),
                    new Vector3(0.065f, 0.035f, 0.035f), Vector3.zero);
            Cylinder(root, p.Dark, "Dome_Base", new Vector3(0.38f, 1.10f, -0.50f),
                new Vector3(0.36f, 0.04f, 0.36f), Vector3.zero);
            Sphere(root, p.Hull, "Dome", new Vector3(0.38f, 1.16f, -0.50f),
                new Vector3(0.32f, 0.18f, 0.32f));
            Cylinder(root, p.Metal, "Dome_Antenna", new Vector3(0.38f, 1.29f, -0.50f),
                new Vector3(0.018f, 0.14f, 0.018f), Vector3.zero);
            Box(root, p.Metal, "Roof_Rack_L", new Vector3(-0.74f, 1.10f, -0.24f),
                new Vector3(0.045f, 0.055f, 0.84f), Vector3.zero);
            Box(root, p.Metal, "Roof_Rack_R", new Vector3(-0.48f, 1.10f, -0.24f),
                new Vector3(0.045f, 0.055f, 0.84f), Vector3.zero);

            // Long side windows run from the front wrap to the rear pillar. Lower side
            // armor and door plates stop at the window sill rather than climbing behind it.
            Box(root, p.Glass, "Side_Glass_L", new Vector3(-0.96f, 0.36f, -0.03f),
                new Vector3(0.04f, 0.94f, 1.70f), Vector3.zero);
            Box(root, p.Glass, "Side_Glass_R", new Vector3(0.96f, 0.36f, -0.03f),
                new Vector3(0.04f, 0.94f, 1.70f), Vector3.zero);
            Box(root, p.Dark, "Pillar_B_L", new Vector3(-0.98f, 0.36f, -0.88f),
                new Vector3(0.07f, 0.98f, 0.09f), Vector3.zero);
            Box(root, p.Dark, "Pillar_B_R", new Vector3(0.98f, 0.36f, -0.88f),
                new Vector3(0.07f, 0.98f, 0.09f), Vector3.zero);
            Box(root, p.Hull, "Lower_Flank_L", new Vector3(-0.94f, -0.58f, 0.00f),
                new Vector3(0.16f, 0.82f, 1.90f), Vector3.zero);
            Box(root, p.Hull, "Lower_Flank_R", new Vector3(0.94f, -0.58f, 0.00f),
                new Vector3(0.16f, 0.82f, 1.90f), Vector3.zero);
            Box(root, p.Dark, "Door_Armor_L", new Vector3(-1.035f, -0.56f, -0.13f),
                new Vector3(0.035f, 0.54f, 0.90f), Vector3.zero);
            Box(root, p.Dark, "Door_Armor_R", new Vector3(1.035f, -0.56f, -0.13f),
                new Vector3(0.035f, 0.54f, 0.90f), Vector3.zero);
            Box(root, p.Accent, "Door_Seam_L", new Vector3(-1.057f, -0.61f, -0.13f),
                new Vector3(0.018f, 0.025f, 0.92f), Vector3.zero);
            Box(root, p.Accent, "Door_Seam_R", new Vector3(1.057f, -0.61f, -0.13f),
                new Vector3(0.018f, 0.025f, 0.92f), Vector3.zero);
            Box(root, p.Metal, "Door_Handle_L", new Vector3(-1.06f, -0.43f, 0.17f),
                new Vector3(0.025f, 0.035f, 0.14f), Vector3.zero);
            Box(root, p.Metal, "Door_Handle_R", new Vector3(1.06f, -0.43f, 0.17f),
                new Vector3(0.025f, 0.035f, 0.14f), Vector3.zero);
            Box(root, p.Accent, "Lower_Armor_Stripe_L", new Vector3(-1.035f, -0.84f, 0.02f),
                new Vector3(0.026f, 0.075f, 1.72f), Vector3.zero);
            Box(root, p.Accent, "Lower_Armor_Stripe_R", new Vector3(1.035f, -0.84f, 0.02f),
                new Vector3(0.026f, 0.075f, 1.72f), Vector3.zero);
            Box(root, p.Metal, "Side_Step_L", new Vector3(-0.84f, -1.11f, 0.54f),
                new Vector3(0.28f, 0.06f, 0.44f), Vector3.zero);
            Box(root, p.Metal, "Side_Step_R", new Vector3(0.84f, -1.11f, 0.54f),
                new Vector3(0.28f, 0.06f, 0.44f), Vector3.zero);
            Box(root, p.Dark, "Mudguard_L", new Vector3(-0.99f, -0.93f, 0.02f),
                new Vector3(0.14f, 0.34f, 1.90f), Vector3.zero);
            Box(root, p.Dark, "Mudguard_R", new Vector3(0.99f, -0.93f, 0.02f),
                new Vector3(0.14f, 0.34f, 1.90f), Vector3.zero);
            Box(root, p.Accent, "Tail_L", new Vector3(-0.82f, -0.52f, -0.99f),
                new Vector3(0.12f, 0.16f, 0.04f), Vector3.zero);
            Box(root, p.Accent, "Tail_R", new Vector3(0.82f, -0.52f, -0.99f),
                new Vector3(0.12f, 0.16f, 0.04f), Vector3.zero);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  MATERIALS
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
            public readonly Material Screen;
            public readonly Material Seat;
            public readonly Material Strap;

            public Palette(Color bodyColor)
            {
                Color industrialGreen = new Color(0.19f, 0.25f, 0.16f);
                Color hull = Color.Lerp(industrialGreen, bodyColor, 0.10f);
                Hull = GridBlockMeshBuilder.CreateMaterial(hull, 0.55f, 0.45f);
                Dark = GridBlockMeshBuilder.CreateMaterial(new Color(0.065f, 0.085f, 0.065f), 0.72f, 0.35f);
                Metal = GridBlockMeshBuilder.CreateMaterial(new Color(0.53f, 0.60f, 0.52f), 0.82f, 0.55f);
                Accent = GridBlockMeshBuilder.CreateMaterial(new Color(0.72f, 0.84f, 0.23f), 0.35f, 0.55f);
                Seat = GridBlockMeshBuilder.CreateMaterial(new Color(0.15f, 0.18f, 0.15f), 0.05f, 0.25f);
                Strap = GridBlockMeshBuilder.CreateMaterial(new Color(0.32f, 0.34f, 0.25f), 0.02f, 0.18f);
                Glow = GridBlockMeshBuilder.CreateMaterial(new Color(0.70f, 0.94f, 1f), 0f, 0.9f,
                    new Color(0.25f, 0.80f, 1f));
                GlowDim = GridBlockMeshBuilder.CreateMaterial(new Color(0.20f, 0.48f, 0.46f), 0f, 0.8f,
                    new Color(0.08f, 0.30f, 0.34f));
                // The emissive screens remain unchanged in role: readable when unpowered,
                // restrained enough to retain the clear outward sightline.
                Screen = GridBlockMeshBuilder.CreateMaterial(new Color(0.03f, 0.05f, 0.07f), 0.10f, 0.75f,
                    new Color(0.10f, 0.34f, 0.44f));
                Glass = MakeGlass();
            }

            private static Material MakeGlass()
            {
                Color tint = new Color(0.72f, 0.90f, 0.96f, 0.07f);
                var glass = GridBlockMeshBuilder.CreateMaterial(tint, 0.08f, 0.95f);
                if (glass == null) return null;

                // The canonical URP transparent state lives in RuntimeMaterials: keyword,
                // surface type, blend mode, both blend factors, the render queue and the
                // depth/shadow passes. Setting only _Surface leaves the pane opaque, which
                // is exactly what "I cannot see out of it" looks like from the seat.
                VoxelEngine.Rendering.RuntimeMaterials.ApplyTranslucent(glass, tint);
                return glass;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  PRIMITIVES
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
