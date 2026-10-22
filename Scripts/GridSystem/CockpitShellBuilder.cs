// Assets/Scripts/VoxelEngine/GridSystem/CockpitShellBuilder.cs
//
// 17.43.0-dev - spacious 2x2x2 industrial cockpits with high-visibility glazing:
//   - Wraparound Cockpit (StyleCrawler = 1): panoramic harvester cab with full front,
//     corner, side, lower-chin and bottom floor observation glass, overhead work lamps,
//     and slim tubular ROPS posts. Lighter hull integrity.
//   - Reinforced Cockpit (StyleCab = 2): rugged heavy-duty machinery cab with bolted
//     armored glacis plates, heavy FOPS cage, clevised dozer bumper, solid steel deck,
//     and tall unobstructed upper panoramic windows. High hull integrity.
//
// A block is authored facing -Z in this project: the ghost draws its FRONT marker on the
// minimum Z face. Everything inside Cabin, Shell_Crawler and Shell_Cab is authored in a
// symmetric [-2, +2]^3 room (for CellUnit = 2 m, total size 4 m x 4 m x 4 m) facing +Z
// and then rotated 180 degrees around Y under ShellRoot at (+0.5, +0.5, -0.5) * cellSize,
// so the finished 2x2x2 block spans cells {0, 1} x {0, 1} x {-1, 0} cleanly.

using System.Collections.Generic;
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

        /// <summary>Base Large-grid single-cell unit (2 m). The 2x2x2 cockpit spans 2 * CellUnit (4 m) per axis.</summary>
        private const float CellUnit = 2f;

        /// <summary>Pilot eye in the authored (+Z forward) cabin frame: elevated high above
        /// the low-mounted curved MFD pod so the entire horizon, flanks and harvester floor stay open.</summary>
        private static readonly Vector3 SeatEye = new(0f, 0.36f, -0.16f);

        private static readonly Vector3Int[] s_localExtraCells =
        {
            new(1, 0, 0),
            new(0, 1, 0),
            new(0, 0, -1),
            new(1, 1, 0),
            new(1, 0, -1),
            new(0, 1, -1),
            new(1, 1, -1),
        };

        private const string ShellPreferenceKey = "cockpit.shell";
        private static int _selectedStyle = -1;

        // ═══════════════════════════════════════════════════════════════════════
        //  WHICH SHELL IS ARMED
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The shell the next cockpit placed in the world will wear. Chosen on the build
        /// wheel and persisted across sessions.
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
        public static int Normalise(int style) => style == StyleCab ? StyleCab : StyleCrawler;

        /// <summary>The name the wheel, HUD and item description use for a shell.</summary>
        public static string NameOf(int style) => Normalise(style) == StyleCab
            ? "Reinforced Cockpit"
            : "Wraparound Cockpit";

        /// <summary>One line about what a shell is for, printed in the wheel's hub.</summary>
        public static string BlurbOf(int style) => Normalise(style) == StyleCab
            ? "Heavy-duty armored machinery cab with bolted glacis plating, FOPS cage and high hull integrity."
            : "Harvester-style panoramic glass across the front, sides and floor for maximum downward and wide visibility; lighter hull.";

        // ═══════════════════════════════════════════════════════════════════════
        //  2x2x2 FOOTPRINT & COLLIDER BOUNDS
        // ═══════════════════════════════════════════════════════════════════════

        public static bool UsesMultiCellFootprint(int style) => style != StyleLegacy;

        public static Bounds LocalBoundsForStyle(int style, float cellSize)
        {
            float cs = Mathf.Max(0.01f, cellSize);
            if (!UsesMultiCellFootprint(style))
                return new Bounds(Vector3.zero, Vector3.one * cs);

            return new Bounds(
                new Vector3(cs * 0.5f, cs * 0.5f, -cs * 0.5f),
                new Vector3(cs * 2f, cs * 2f, cs * 2f));
        }

        /// <summary>Appends the 7 non-anchor grid cells occupied by the rotated 2x2x2 cockpit.</summary>
        public static void AppendAdditionalFootprintOffsets(int style, Quaternion localRotation, List<Vector3Int> results)
        {
            if (results == null || !UsesMultiCellFootprint(style)) return;
            for (int i = 0; i < s_localExtraCells.Length; i++)
                results.Add(RotateGridOffset(s_localExtraCells[i], localRotation));
        }

        public static Vector3Int RotateGridOffset(Vector3Int localOffset, Quaternion localRotation)
        {
            Vector3 rotated = localRotation * (Vector3)localOffset;
            return new Vector3Int(
                Mathf.RoundToInt(rotated.x),
                Mathf.RoundToInt(rotated.y),
                Mathf.RoundToInt(rotated.z));
        }

        public static void SyncColliderBounds(GameObject root, float cellSize, int style)
        {
            if (root == null) return;
            var box = root.GetComponent<BoxCollider>();
            if (box == null) return;
            bool hasShell = root.transform.Find(ShellRootName) != null;
            Bounds bounds = LocalBoundsForStyle(hasShell ? Normalise(style) : StyleLegacy, cellSize);
            box.center = bounds.center;
            box.size = bounds.size;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  BUILD
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build the whole 2x2x2 cockpit: shared open-sightline cabin plus both shells,
        /// with <paramref name="style"/> active. Editor only.
        /// </summary>
        public static void Build(GameObject root, float cellSize, Color bodyColor, int style)
        {
            if (root == null || cellSize <= 0.01f) return;
            if (Application.isPlaying) return;
            if (style == StyleLegacy) return;

            Bounds bounds = LocalBoundsForStyle(style, cellSize);
            var existing = root.transform.Find(ShellRootName);
            Transform shellRoot = existing != null
                ? existing
                : Group(root.transform, ShellRootName, bounds.center).transform;
            shellRoot.localPosition = bounds.center;
            shellRoot.localRotation = Quaternion.identity;
            shellRoot.localScale = Vector3.one;

            // Replace only the four subtrees owned by this builder so unrelated custom children stay intact.
            RemoveGeneratedChild(shellRoot, InteriorName);
            RemoveGeneratedChild(shellRoot, CrawlerName);
            RemoveGeneratedChild(shellRoot, CabName);
            RemoveGeneratedChild(shellRoot, SeatAnchorName);

            var palette = new Palette(bodyColor);
            float scale = cellSize / CellUnit;

            // Authored in [-2, +2]^3 facing +Z, rotated 180 degrees around Y to face -Z.
            var cabin = Group(shellRoot, InteriorName, Vector3.zero);
            FaceForward(cabin, scale);
            BuildInterior(cabin, palette);

            var crawler = Group(shellRoot, CrawlerName, Vector3.zero);
            FaceForward(crawler, scale);
            BuildWraparoundHarvester(crawler, palette);

            var cab = Group(shellRoot, CabName, Vector3.zero);
            FaceForward(cab, scale);
            BuildReinforcedHeavyCab(cab, palette);

            // SeatAnchor sits under shellRoot (already centred on the 2x2x2 volume) in finished -Z space.
            var anchor = Group(shellRoot, SeatAnchorName,
                new Vector3(SeatEye.x, SeatEye.y, -SeatEye.z) * scale);
            anchor.transform.localRotation = Quaternion.Euler(2.5f, 180f, 0f);

            SyncColliderBounds(root, cellSize, style);
            ApplyShell(root, style);
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
        /// which hull subtree is active.
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

        /// <summary>Assigns shell style, activates visuals, updates collider bounds and recalculates durability.</summary>
        public static void SetShell(GridCockpit cockpit, int style)
        {
            if (cockpit == null) return;
            cockpit.shellStyle = Normalise(style);
            ApplyShell(cockpit.gameObject, cockpit.shellStyle);
            cockpit.ApplyShellDurability();
        }

        /// <summary>Returns the pilot eye pose in the cockpit root's local frame.</summary>
        public static bool TryGetSeatAnchor(GameObject root, out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
            if (root == null) return false;

            var shellRoot = root.transform.Find(ShellRootName);
            if (shellRoot == null) return false;
            var anchor = shellRoot.Find(SeatAnchorName);
            if (anchor == null) return false;

            localPosition = shellRoot.localPosition + shellRoot.localRotation * Vector3.Scale(shellRoot.localScale, anchor.localPosition);
            localRotation = shellRoot.localRotation * anchor.localRotation;
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHARED OPEN-SIGHTLINE CABIN ([-2, +2]^3 FOR LARGE 4m x 4m x 4m BLOCK)
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildInterior(GameObject root, Palette p)
        {
            // Rear structural bulkhead (seals z = -2.0 face cleanly against attached hull blocks)
            Box(root, p.HullDark, "Bulkhead_Main", new Vector3(0f, 0.00f, -1.93f), new Vector3(3.86f, 3.84f, 0.12f), Vector3.zero);
            Box(root, p.Dark, "Bulkhead_Stiffener_Top", new Vector3(0f, 1.72f, -1.85f), new Vector3(3.64f, 0.22f, 0.08f), Vector3.zero);
            Box(root, p.Dark, "Bulkhead_Stiffener_Bot", new Vector3(0f, -1.72f, -1.85f), new Vector3(3.64f, 0.22f, 0.08f), Vector3.zero);
            Box(root, p.MetalDark, "Bulkhead_Rib_L", new Vector3(-1.32f, 0f, -1.85f), new Vector3(0.14f, 3.30f, 0.07f), Vector3.zero);
            Box(root, p.MetalDark, "Bulkhead_Rib_R", new Vector3(1.32f, 0f, -1.85f), new Vector3(0.14f, 3.30f, 0.07f), Vector3.zero);

            // Pressurized rear crew hatch
            Box(root, p.Dark, "Hatch_Frame", new Vector3(0f, -0.34f, -1.84f), new Vector3(1.36f, 2.26f, 0.08f), Vector3.zero);
            Box(root, p.Hull, "Hatch_Door", new Vector3(0f, -0.34f, -1.79f), new Vector3(1.14f, 2.04f, 0.06f), Vector3.zero);
            Box(root, p.Glass, "Hatch_Porthole", new Vector3(0f, 0.22f, -1.75f), new Vector3(0.42f, 0.30f, 0.03f), Vector3.zero);
            Box(root, p.Accent, "Hatch_HeaderStripe", new Vector3(0f, 0.84f, -1.80f), new Vector3(1.10f, 0.055f, 0.03f), Vector3.zero);
            for (int i = 0; i < 3; i++)
            {
                float hy = -0.96f + i * 0.62f;
                Box(root, p.Metal, "Hatch_Dog_L" + i, new Vector3(-0.50f, hy, -1.75f), new Vector3(0.12f, 0.06f, 0.05f), Vector3.zero);
                Box(root, p.Metal, "Hatch_Dog_R" + i, new Vector3(0.50f, hy, -1.75f), new Vector3(0.12f, 0.06f, 0.05f), Vector3.zero);
            }

            // Narrow central operator spine (x in [-0.44, +0.44]): deliberately leaves left, right,
            // and forward floor completely open so Wraparound floor glass has unobstructed sightlines!
            Box(root, p.Dark, "Spine_Catwalk", new Vector3(0f, -1.88f, -0.66f), new Vector3(0.88f, 0.14f, 2.36f), Vector3.zero);
            Box(root, p.MetalDark, "Spine_Trim_L", new Vector3(-0.45f, -1.84f, -0.66f), new Vector3(0.04f, 0.18f, 2.38f), Vector3.zero);
            Box(root, p.MetalDark, "Spine_Trim_R", new Vector3(0.45f, -1.84f, -0.66f), new Vector3(0.04f, 0.18f, 2.38f), Vector3.zero);
            for (int i = 0; i < 6; i++)
                Box(root, p.Metal, "Catwalk_Tread_" + i, new Vector3(0f, -1.80f, -1.62f + i * 0.42f),
                    new Vector3(0.76f, 0.02f, 0.08f), Vector3.zero);

            // Footwell rudder / travel pedals forward on the narrow spine
            Box(root, p.Dark, "Pedal_Box", new Vector3(0f, -1.72f, 0.34f), new Vector3(0.68f, 0.16f, 0.34f), new Vector3(-14f, 0f, 0f));
            Box(root, p.Metal, "Pedal_L", new Vector3(-0.19f, -1.56f, 0.44f), new Vector3(0.16f, 0.24f, 0.04f), new Vector3(-28f, 0f, 0f));
            Box(root, p.Metal, "Pedal_R", new Vector3(0.19f, -1.56f, 0.44f), new Vector3(0.16f, 0.24f, 0.04f), new Vector3(-28f, 0f, 0f));

            // Elevated air-ride industrial operator seat (eye sits cleanly at y = +0.36, z = -0.16)
            Box(root, p.Dark, "Seat_Riser", new Vector3(0f, -1.36f, -0.34f), new Vector3(0.54f, 0.90f, 0.52f), Vector3.zero);
            Cylinder(root, p.MetalDark, "Seat_Shock", new Vector3(0f, -1.10f, -0.34f), new Vector3(0.30f, 0.36f, 0.30f), Vector3.zero);
            Box(root, p.Seat, "Seat_Pan", new Vector3(0f, -0.68f, -0.26f), new Vector3(0.78f, 0.16f, 0.72f), new Vector3(-4f, 0f, 0f));
            Box(root, p.Seat, "Seat_Back", new Vector3(0f, -0.10f, -0.58f), new Vector3(0.76f, 1.02f, 0.16f), new Vector3(-11f, 0f, 0f));
            Box(root, p.Seat, "Seat_Headrest", new Vector3(0f, 0.54f, -0.69f), new Vector3(0.46f, 0.28f, 0.13f), new Vector3(-11f, 0f, 0f));
            Box(root, p.Seat, "Seat_Bolster_L", new Vector3(-0.42f, -0.52f, -0.26f), new Vector3(0.12f, 0.30f, 0.64f), Vector3.zero);
            Box(root, p.Seat, "Seat_Bolster_R", new Vector3(0.42f, -0.52f, -0.26f), new Vector3(0.12f, 0.30f, 0.64f), Vector3.zero);
            Box(root, p.Strap, "Harness_L", new Vector3(-0.17f, 0.10f, -0.49f), new Vector3(0.09f, 0.68f, 0.035f), new Vector3(-12f, 0f, 4f));
            Box(root, p.Strap, "Harness_R", new Vector3(0.17f, 0.10f, -0.49f), new Vector3(0.09f, 0.68f, 0.035f), new Vector3(-12f, 0f, -4f));
            Box(root, p.Metal, "Harness_Buckle", new Vector3(0f, -0.22f, -0.42f), new Vector3(0.13f, 0.11f, 0.04f), Vector3.zero);

            // Chair-mounted side control pods (kept strictly beside the hips at y = -0.42, out of all outward glass!)
            Box(root, p.Dark, "Armrest_Pod_L", new Vector3(-0.56f, -0.44f, -0.06f), new Vector3(0.20f, 0.12f, 0.72f), new Vector3(-4f, 0f, 0f));
            Box(root, p.Dark, "Armrest_Pod_R", new Vector3(0.56f, -0.44f, -0.06f), new Vector3(0.20f, 0.12f, 0.72f), new Vector3(-4f, 0f, 0f));

            // Left hand: multi-axis control stick
            Cylinder(root, p.Metal, "Stick_Shaft", new Vector3(-0.56f, -0.28f, 0.20f), new Vector3(0.045f, 0.13f, 0.045f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Seat, "Stick_Grip", new Vector3(-0.56f, -0.14f, 0.23f), new Vector3(0.085f, 0.13f, 0.09f), new Vector3(-12f, 0f, 0f));
            Box(root, p.Accent, "Stick_Trigger", new Vector3(-0.56f, -0.12f, 0.29f), new Vector3(0.035f, 0.04f, 0.03f), Vector3.zero);

            // Right hand: twin industrial throttle levers + status keypad
            Box(root, p.MetalDark, "Throttle_Base", new Vector3(0.56f, -0.35f, 0.18f), new Vector3(0.17f, 0.08f, 0.28f), Vector3.zero);
            Box(root, p.Metal, "Throttle_Arm_A", new Vector3(0.52f, -0.23f, 0.21f), new Vector3(0.03f, 0.17f, 0.035f), new Vector3(-22f, 0f, 0f));
            Box(root, p.Metal, "Throttle_Arm_B", new Vector3(0.60f, -0.23f, 0.19f), new Vector3(0.03f, 0.17f, 0.035f), new Vector3(-14f, 0f, 0f));
            Box(root, p.Accent, "Throttle_Grip", new Vector3(0.56f, -0.14f, 0.24f), new Vector3(0.14f, 0.045f, 0.055f), Vector3.zero);
            for (int k = 0; k < 3; k++)
                Box(root, p.GlowDim, "Arm_Button_" + k, new Vector3(0.56f, -0.37f, -0.10f - k * 0.09f),
                    new Vector3(0.07f, 0.02f, 0.055f), Vector3.zero);

            // Low-profile cantilevered instrument arc:
            // Positioned at y = -0.68, z = 0.88..0.94 (top edge sits ~37 degrees BELOW eye level!),
            // leaving 100% of the forward horizon, upper glass, side glass, and lower floor glass open!
            Box(root, p.MetalDark, "Console_Pedestal_L", new Vector3(-0.34f, -1.24f, 0.56f),
                new Vector3(0.055f, 1.08f, 0.065f), new Vector3(24f, 0f, 0f));
            Box(root, p.MetalDark, "Console_Pedestal_R", new Vector3(0.34f, -1.24f, 0.56f),
                new Vector3(0.055f, 1.08f, 0.065f), new Vector3(24f, 0f, 0f));
            Box(root, p.Dark, "Console_Crossbar", new Vector3(0f, -0.78f, 0.82f),
                new Vector3(1.24f, 0.05f, 0.06f), Vector3.zero);

            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.56f;
                float z = i == 1 ? 0.94f : 0.86f;
                float inwardYaw = (i - 1) * -14f;
                Quaternion screenRot = Quaternion.Euler(-32f, 180f + inwardYaw, 0f);
                Vector3 screenFace = screenRot * Vector3.forward;
                Vector3 centre = new Vector3(x, -0.64f, z);

                Box(root, p.Dark, "MFD_Bezel_" + i, centre, new Vector3(0.50f, 0.32f, 0.032f), screenRot.eulerAngles);
                Box(root, p.Accent, "MFD_Trim_" + i, centre + screenRot * new Vector3(0f, 0.162f, 0.004f),
                    new Vector3(0.46f, 0.012f, 0.034f), screenRot.eulerAngles);
                Box(root, p.Screen, "MFD_Screen_" + i, centre + screenFace * 0.018f,
                    new Vector3(0.45f, 0.27f, 0.010f), screenRot.eulerAngles);

                Box(root, p.Glow, "MFD_Bar_" + i, centre + screenRot * new Vector3(-0.10f, 0.07f, 0.026f),
                    new Vector3(0.18f, 0.024f, 0.005f), screenRot.eulerAngles);
                Box(root, p.Accent, "MFD_Tick_" + i, centre + screenRot * new Vector3(0.12f, -0.08f, 0.026f),
                    new Vector3(0.09f, 0.018f, 0.005f), screenRot.eulerAngles);
                for (int g = 0; g < 3; g++)
                    Box(root, p.GlowDim, "MFD_Graph_" + i + "_" + g,
                        centre + screenRot * new Vector3(-0.11f + g * 0.08f, -0.05f, 0.026f),
                        new Vector3(0.04f, 0.018f + g * 0.022f, 0.005f), screenRot.eulerAngles);
            }

            // Compact overhead console tucked behind pilot sightline
            Box(root, p.Dark, "Overhead_Pod", new Vector3(0f, 1.84f, -0.42f), new Vector3(1.18f, 0.10f, 0.56f), Vector3.zero);
            Box(root, p.Glow, "Cabin_LightStrip", new Vector3(0f, 1.78f, -0.42f), new Vector3(0.92f, 0.02f, 0.12f), Vector3.zero);

            var lamp = Group(root.transform, "Cabin_Lamp", new Vector3(0f, 1.54f, -0.10f));
            var light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.78f, 0.91f, 1f);
            light.intensity = 1.45f;
            light.range = 5.4f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHELL 1 - WRAPAROUND HARVESTER COCKPIT (FRONT + SIDES + BOTTOM GLASS)
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildWraparoundHarvester(GameObject root, Palette p)
        {
            // Rear lower base anchor only under the rear seat bulkhead; everything forward
            // and beside the central spine is floor glass!
            Box(root, p.HullDark, "Harvester_RearBase", new Vector3(0f, -1.94f, -1.62f),
                new Vector3(3.92f, 0.12f, 0.74f), Vector3.zero);

            // ── Full Bottom Observation Glass (Harvester Belly Floor) ───────────
            // Three crystal-clear structural belly panes surrounding the central spine:
            Box(root, p.Glass, "Glass_Floor_Front", new Vector3(0f, -1.95f, 0.62f),
                new Vector3(3.74f, 0.045f, 1.54f), Vector3.zero);
            Box(root, p.Glass, "Glass_Floor_Left", new Vector3(-1.16f, -1.95f, -0.62f),
                new Vector3(1.42f, 0.045f, 1.26f), Vector3.zero);
            Box(root, p.Glass, "Glass_Floor_Right", new Vector3(1.16f, -1.95f, -0.62f),
                new Vector3(1.42f, 0.045f, 1.26f), Vector3.zero);

            // Slender perimeter tubular safety frame around the floor glass
            Box(root, p.Dark, "Floor_Frame_L", new Vector3(-1.93f, -1.94f, -0.26f),
                new Vector3(0.11f, 0.11f, 3.44f), Vector3.zero);
            Box(root, p.Dark, "Floor_Frame_R", new Vector3(1.93f, -1.94f, -0.26f),
                new Vector3(0.11f, 0.11f, 3.44f), Vector3.zero);
            Box(root, p.MetalDark, "Floor_Mullion_Cross", new Vector3(0f, -1.95f, 1.38f),
                new Vector3(3.84f, 0.08f, 0.09f), Vector3.zero);

            // ── Raked Lower Harvester Chin Glass (connects floor at z=+1.38, y=-1.95 up to front nose z=+1.94, y=-0.78) ──
            Box(root, p.Glass, "Glass_Chin_Centre", new Vector3(0f, -1.36f, 1.66f),
                new Vector3(2.58f, 1.30f, 0.04f), new Vector3(25.5f, 0f, 0f));
            Box(root, p.GlassSheen, "Glass_Chin_Sheen", new Vector3(-0.42f, -1.34f, 1.68f),
                new Vector3(1.10f, 0.04f, 0.01f), new Vector3(25.5f, 0f, -18f));
            Box(root, p.Glass, "Glass_Chin_Corner_L", new Vector3(-1.56f, -1.36f, 1.50f),
                new Vector3(0.84f, 1.30f, 0.04f), new Vector3(22f, -38f, 0f));
            Box(root, p.Glass, "Glass_Chin_Corner_R", new Vector3(1.56f, -1.36f, 1.50f),
                new Vector3(0.84f, 1.30f, 0.04f), new Vector3(22f, 38f, 0f));

            // Slim front waist-rail mullion joining chin glass to main tall windscreen cleanly
            Box(root, p.Dark, "Nose_Transom_Bar", new Vector3(0f, -0.76f, 1.94f),
                new Vector3(2.68f, 0.075f, 0.085f), Vector3.zero);
            Box(root, p.Accent, "Nose_Transom_PinStripe", new Vector3(0f, -0.76f, 1.985f),
                new Vector3(2.40f, 0.022f, 0.015f), Vector3.zero);

            // Low tubular brush-guard bumper along bottom chin edge so rocks read without blocking view
            Box(root, p.MetalDark, "Chin_BrushBar_Main", new Vector3(0f, -1.91f, 1.46f),
                new Vector3(3.60f, 0.11f, 0.12f), Vector3.zero);
            Box(root, p.Accent, "Chin_Hazard_L", new Vector3(-1.35f, -1.91f, 1.525f),
                new Vector3(0.55f, 0.045f, 0.02f), Vector3.zero);
            Box(root, p.Accent, "Chin_Hazard_R", new Vector3(1.35f, -1.91f, 1.525f),
                new Vector3(0.55f, 0.045f, 0.02f), Vector3.zero);

            // ── Tall Sweeping Harvester Front Windscreen & 45-Deg Corner Wraps ──
            Box(root, p.Glass, "Glass_Front_Main", new Vector3(0f, 0.54f, 1.82f),
                new Vector3(2.64f, 2.62f, 0.04f), new Vector3(-6.5f, 0f, 0f));
            Box(root, p.GlassSheen, "Glass_Front_Sheen", new Vector3(-0.56f, 0.86f, 1.80f),
                new Vector3(1.35f, 0.045f, 0.01f), new Vector3(-6.5f, 0f, -20f));
            Box(root, p.Glass, "Glass_Wrap_Corner_L", new Vector3(-1.58f, 0.54f, 1.52f),
                new Vector3(0.86f, 2.60f, 0.04f), new Vector3(-5f, -38f, 0f));
            Box(root, p.Glass, "Glass_Wrap_Corner_R", new Vector3(1.58f, 0.54f, 1.52f),
                new Vector3(0.86f, 2.60f, 0.04f), new Vector3(-5f, 38f, 0f));

            // ── Full Floor-to-Ceiling Panoramic Side Glass ──────────────────────
            Box(root, p.Glass, "Glass_Side_Main_L", new Vector3(-1.93f, -0.06f, -0.26f),
                new Vector3(0.04f, 3.74f, 3.04f), Vector3.zero);
            Box(root, p.Glass, "Glass_Side_Main_R", new Vector3(1.93f, -0.06f, -0.26f),
                new Vector3(0.04f, 3.74f, 3.04f), Vector3.zero);
            Box(root, p.GlassSheen, "Glass_Side_Sheen_L", new Vector3(-1.955f, 0.45f, 0.15f),
                new Vector3(0.01f, 0.05f, 1.65f), new Vector3(16f, 0f, 0f));
            Box(root, p.GlassSheen, "Glass_Side_Sheen_R", new Vector3(1.955f, 0.45f, 0.15f),
                new Vector3(0.01f, 0.05f, 1.65f), new Vector3(16f, 0f, 0f));

            // ── Slender Tubular ROPS Corner Posts (thin structural mullions, zero blindspots) ──
            Box(root, p.Dark, "ROPS_FrontPost_L", new Vector3(-1.30f, 0.54f, 1.80f),
                new Vector3(0.075f, 2.66f, 0.085f), new Vector3(-6.5f, 0f, 0f));
            Box(root, p.Dark, "ROPS_FrontPost_R", new Vector3(1.30f, 0.54f, 1.80f),
                new Vector3(0.075f, 2.66f, 0.085f), new Vector3(-6.5f, 0f, 0f));
            Box(root, p.Dark, "ROPS_ChinPost_L", new Vector3(-1.30f, -1.36f, 1.65f),
                new Vector3(0.075f, 1.32f, 0.085f), new Vector3(25.5f, 0f, 0f));
            Box(root, p.Dark, "ROPS_ChinPost_R", new Vector3(1.30f, -1.36f, 1.65f),
                new Vector3(0.075f, 1.32f, 0.085f), new Vector3(25.5f, 0f, 0f));
            Box(root, p.Dark, "ROPS_SidePost_L", new Vector3(-1.92f, -0.04f, 1.24f),
                new Vector3(0.085f, 3.80f, 0.085f), Vector3.zero);
            Box(root, p.Dark, "ROPS_SidePost_R", new Vector3(1.92f, -0.04f, 1.24f),
                new Vector3(0.085f, 3.80f, 0.085f), Vector3.zero);
            Box(root, p.HullDark, "ROPS_RearPillar_L", new Vector3(-1.91f, 0.00f, -1.82f),
                new Vector3(0.16f, 3.88f, 0.24f), Vector3.zero);
            Box(root, p.HullDark, "ROPS_RearPillar_R", new Vector3(1.91f, 0.00f, -1.82f),
                new Vector3(0.16f, 3.88f, 0.24f), Vector3.zero);

            // Top-hinged pantograph wipers parked high at top edge out of sightline
            Box(root, p.Metal, "Wiper_Arm_L", new Vector3(-0.62f, 1.54f, 1.72f),
                new Vector3(0.03f, 0.56f, 0.03f), new Vector3(-6.5f, 0f, 74f));
            Box(root, p.Metal, "Wiper_Arm_R", new Vector3(0.62f, 1.54f, 1.72f),
                new Vector3(0.03f, 0.56f, 0.03f), new Vector3(-6.5f, 0f, -74f));

            // ── Harvester Overhanging Canopy Roof & 6-Lamp Work Floodlight Visor ──
            Box(root, p.Hull, "Canopy_Roof_Main", new Vector3(0f, 1.90f, -0.10f),
                new Vector3(3.92f, 0.16f, 3.72f), Vector3.zero);
            Box(root, p.Dark, "Canopy_Visor_Brow", new Vector3(0f, 1.88f, 1.84f),
                new Vector3(3.84f, 0.14f, 0.30f), new Vector3(12f, 0f, 0f));
            Box(root, p.Accent, "Canopy_Visor_Trim", new Vector3(0f, 1.95f, 1.97f),
                new Vector3(3.56f, 0.035f, 0.04f), Vector3.zero);
            Box(root, p.HullDark, "Canopy_HVAC_Pod", new Vector3(0f, 1.99f, -0.78f),
                new Vector3(2.10f, 0.10f, 1.65f), Vector3.zero);
            for (int r = 0; r < 4; r++)
                Box(root, p.MetalDark, "Canopy_RoofRib_" + r, new Vector3(0f, 2.04f, -1.35f + r * 0.56f),
                    new Vector3(1.94f, 0.03f, 0.07f), Vector3.zero);

            // 6 Harvester Flood Work-Lamps across the brow
            for (int i = 0; i < 6; i++)
            {
                float lx = -1.35f + i * 0.54f;
                Box(root, p.Dark, "WorkLamp_Housing_" + i, new Vector3(lx, 1.84f, 1.95f),
                    new Vector3(0.28f, 0.11f, 0.10f), new Vector3(14f, 0f, 0f));
                Box(root, p.Glow, "WorkLamp_Lens_" + i, new Vector3(lx, 1.83f, 2.00f),
                    new Vector3(0.23f, 0.075f, 0.02f), new Vector3(14f, 0f, 0f));
            }

            // Folding side mirrors mounted high on upper side posts + amber rear beacons
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, p.MetalDark, "Mirror_Arm_" + s, new Vector3(s * 1.96f, 1.38f, 1.24f),
                    new Vector3(0.05f, 0.05f, 0.22f), Vector3.zero);
                Box(root, p.Dark, "Mirror_Head_" + s, new Vector3(s * 1.98f, 1.18f, 1.32f),
                    new Vector3(0.05f, 0.44f, 0.18f), new Vector3(0f, s * 14f, 0f));
                Cylinder(root, p.WarnAmber, "Roof_Beacon_" + s, new Vector3(s * 1.72f, 2.04f, -1.72f),
                    new Vector3(0.16f, 0.08f, 0.16f), Vector3.zero);
                Box(root, p.Metal, "Grab_Rail_" + s, new Vector3(s * 1.97f, -0.35f, -1.62f),
                    new Vector3(0.035f, 1.45f, 0.035f), Vector3.zero);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SHELL 2 - REINFORCED HEAVY-DUTY MACHINERY COCKPIT (ARMORED CAB)
        // ═══════════════════════════════════════════════════════════════════════
        private static void BuildReinforcedHeavyCab(GameObject root, Palette p)
        {
            // Solid heavy ribbed steel floor across the entire 4 m x 4 m footprint (zero floor glass)
            Box(root, p.HullDark, "Cab_Solid_Floor", new Vector3(0f, -1.93f, 0f),
                new Vector3(3.92f, 0.14f, 3.92f), Vector3.zero);
            for (int i = 0; i < 6; i++)
                Box(root, p.MetalDark, "Cab_Floor_PlateRib_" + i, new Vector3(0f, -1.85f, -1.45f + i * 0.58f),
                    new Vector3(3.56f, 0.025f, 0.09f), Vector3.zero);

            // ── Rugged Heavy-Machinery Lower Armored Hull & Sloped Glacis Nose ──
            // Sill stays at y = -0.36 so everything from -18 deg UP to +33 deg is crisp clear glass!
            Box(root, p.Hull, "Glacis_Main_Bulk", new Vector3(0f, -1.16f, 1.56f),
                new Vector3(3.84f, 1.48f, 0.78f), Vector3.zero);
            Box(root, p.HullDark, "Glacis_Sloped_Plate", new Vector3(0f, -0.46f, 1.66f),
                new Vector3(3.80f, 0.24f, 0.72f), new Vector3(22f, 0f, 0f));
            Box(root, p.ArmorPlate, "Glacis_Applique_L", new Vector3(-1.12f, -1.02f, 1.94f),
                new Vector3(1.34f, 0.86f, 0.09f), Vector3.zero);
            Box(root, p.ArmorPlate, "Glacis_Applique_R", new Vector3(1.12f, -1.02f, 1.94f),
                new Vector3(1.34f, 0.86f, 0.09f), Vector3.zero);

            // Heavy recessed center radiator louver grille between armor plates
            Box(root, p.Dark, "Radiator_Recess", new Vector3(0f, -1.04f, 1.93f),
                new Vector3(0.84f, 0.86f, 0.08f), Vector3.zero);
            for (int g = 0; g < 6; g++)
                Box(root, p.Metal, "Radiator_Louver_" + g, new Vector3(0f, -1.36f + g * 0.13f, 1.97f),
                    new Vector3(0.74f, 0.045f, 0.04f), new Vector3(18f, 0f, 0f));

            // Massive front push-dozer bumper, recovery clevises & hazard chevrons
            Box(root, p.MetalDark, "Dozer_Bumper", new Vector3(0f, -1.74f, 1.88f),
                new Vector3(3.96f, 0.44f, 0.24f), Vector3.zero);
            Box(root, p.Metal, "Bumper_WearLip", new Vector3(0f, -1.94f, 1.94f),
                new Vector3(3.92f, 0.09f, 0.16f), Vector3.zero);
            Box(root, p.Accent, "Bumper_Hazard_L", new Vector3(-1.26f, -1.72f, 2.005f),
                new Vector3(1.06f, 0.11f, 0.02f), Vector3.zero);
            Box(root, p.Accent, "Bumper_Hazard_R", new Vector3(1.26f, -1.72f, 2.005f),
                new Vector3(1.06f, 0.11f, 0.02f), Vector3.zero);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, p.Metal, "Tow_Clevis_" + s, new Vector3(s * 0.56f, -1.74f, 2.02f),
                    new Vector3(0.16f, 0.22f, 0.14f), Vector3.zero);
                Cylinder(root, p.Dark, "Tow_Pin_" + s, new Vector3(s * 0.56f, -1.74f, 2.05f),
                    new Vector3(0.05f, 0.11f, 0.05f), new Vector3(0f, 0f, 90f));

                // Armored headlight pods with protective rock cages
                Box(root, p.Dark, "Headlight_Pod_" + s, new Vector3(s * 1.48f, -0.78f, 1.96f),
                    new Vector3(0.50f, 0.24f, 0.09f), Vector3.zero);
                Box(root, p.Glow, "Headlight_Lens_" + s, new Vector3(s * 1.48f, -0.78f, 2.005f),
                    new Vector3(0.40f, 0.15f, 0.02f), Vector3.zero);
                Box(root, p.Metal, "Headlight_GuardTop_" + s, new Vector3(s * 1.48f, -0.68f, 2.02f),
                    new Vector3(0.52f, 0.025f, 0.03f), Vector3.zero);
                Box(root, p.Metal, "Headlight_GuardMid_" + s, new Vector3(s * 1.48f, -0.78f, 2.025f),
                    new Vector3(0.52f, 0.022f, 0.03f), Vector3.zero);
                Box(root, p.Metal, "Headlight_GuardBot_" + s, new Vector3(s * 1.48f, -0.88f, 2.02f),
                    new Vector3(0.52f, 0.025f, 0.03f), Vector3.zero);
            }

            // ── Wide, Tall Upper Panoramic Armored Windows (y = -0.34 .. +1.64 = 1.98 m clear height!) ──
            Box(root, p.Glass, "Cab_Windscreen_Main", new Vector3(0f, 0.65f, 1.88f),
                new Vector3(2.62f, 1.98f, 0.05f), new Vector3(-5f, 0f, 0f));
            Box(root, p.GlassSheen, "Cab_Windscreen_Sheen", new Vector3(-0.52f, 0.90f, 1.86f),
                new Vector3(1.20f, 0.045f, 0.01f), new Vector3(-5f, 0f, -18f));
            Box(root, p.Glass, "Cab_QuarterGlass_L", new Vector3(-1.58f, 0.65f, 1.56f),
                new Vector3(0.84f, 1.96f, 0.05f), new Vector3(-4f, -38f, 0f));
            Box(root, p.Glass, "Cab_QuarterGlass_R", new Vector3(1.58f, 0.65f, 1.56f),
                new Vector3(0.84f, 1.96f, 0.05f), new Vector3(-4f, 38f, 0f));
            Box(root, p.Glass, "Cab_SideWindow_L", new Vector3(-1.90f, 0.65f, -0.10f),
                new Vector3(0.05f, 1.94f, 2.74f), Vector3.zero);
            Box(root, p.Glass, "Cab_SideWindow_R", new Vector3(1.90f, 0.65f, -0.10f),
                new Vector3(0.05f, 1.94f, 2.74f), Vector3.zero);

            // ── Heavy Lower Side Armor Skirts, Crew Doors, Steps & Fenders ──────
            for (int s = -1; s <= 1; s += 2)
            {
                Box(root, p.Hull, "Side_LowerArmor_" + s, new Vector3(s * 1.88f, -1.14f, -0.12f),
                    new Vector3(0.22f, 1.58f, 3.66f), Vector3.zero);
                Box(root, p.ArmorPlate, "Side_SkirtPlate_" + s, new Vector3(s * 1.97f, -1.36f, -0.12f),
                    new Vector3(0.07f, 0.96f, 3.24f), Vector3.zero);
                Box(root, p.Dark, "Side_CrewDoor_" + s, new Vector3(s * 1.975f, -1.02f, 0.06f),
                    new Vector3(0.05f, 1.18f, 1.38f), Vector3.zero);
                Box(root, p.Metal, "Side_DoorHandle_" + s, new Vector3(s * 2.00f, -0.82f, 0.52f),
                    new Vector3(0.035f, 0.06f, 0.24f), Vector3.zero);
                Box(root, p.Metal, "Side_DoorHingeA_" + s, new Vector3(s * 2.00f, -0.68f, -0.56f),
                    new Vector3(0.04f, 0.14f, 0.09f), Vector3.zero);
                Box(root, p.Metal, "Side_DoorHingeB_" + s, new Vector3(s * 2.00f, -1.32f, -0.56f),
                    new Vector3(0.04f, 0.14f, 0.09f), Vector3.zero);
                Box(root, p.Accent, "Side_HazardStripe_" + s, new Vector3(s * 2.005f, -1.72f, -0.12f),
                    new Vector3(0.02f, 0.10f, 3.10f), Vector3.zero);
                Box(root, p.MetalDark, "Side_StepUpper_" + s, new Vector3(s * 1.90f, -1.52f, 0.06f),
                    new Vector3(0.20f, 0.06f, 0.68f), Vector3.zero);
                Box(root, p.Metal, "Side_StepLower_" + s, new Vector3(s * 1.92f, -1.88f, 0.06f),
                    new Vector3(0.22f, 0.06f, 0.68f), Vector3.zero);

                // Beefy structural A / B / C FOPS cage pillars
                Box(root, p.Dark, "FOPS_PillarA_" + s, new Vector3(s * 1.31f, 0.65f, 1.86f),
                    new Vector3(0.13f, 2.04f, 0.16f), new Vector3(-5f, 0f, 0f));
                Box(root, p.Dark, "FOPS_PillarWrap_" + s, new Vector3(s * 1.91f, 0.65f, 1.28f),
                    new Vector3(0.14f, 2.04f, 0.16f), Vector3.zero);
                Box(root, p.ArmorPlate, "FOPS_RearCorner_" + s, new Vector3(s * 1.88f, 0.04f, -1.72f),
                    new Vector3(0.24f, 3.84f, 0.52f), Vector3.zero);
                Box(root, p.MetalDark, "FOPS_RoofRail_" + s, new Vector3(s * 1.88f, 1.86f, -0.02f),
                    new Vector3(0.16f, 0.22f, 3.88f), Vector3.zero);
            }

            // ── Heavy Armored FOPS Roof Cap, Rock-Guard Ribs, HVAC & Sensor Dome ──
            Box(root, p.Hull, "Cab_Armored_Roof", new Vector3(0f, 1.80f, -0.02f),
                new Vector3(3.86f, 0.24f, 3.88f), Vector3.zero);
            Box(root, p.ArmorPlate, "Cab_Front_BrowArmor", new Vector3(0f, 1.72f, 1.90f),
                new Vector3(3.92f, 0.24f, 0.22f), new Vector3(8f, 0f, 0f));
            for (int r = 0; r < 5; r++)
                Box(root, p.MetalDark, "FOPS_OverheadGuard_" + r, new Vector3(0f, 1.96f, -1.45f + r * 0.72f),
                    new Vector3(3.64f, 0.075f, 0.14f), Vector3.zero);

            // Recessed armored LED roof bar + amber marker lamps
            for (int i = 0; i < 4; i++)
                Box(root, p.Glow, "Cab_RoofLamp_" + i, new Vector3(-1.05f + i * 0.70f, 1.74f, 2.00f),
                    new Vector3(0.42f, 0.085f, 0.03f), Vector3.zero);
            for (int m = -1; m <= 1; m++)
                Box(root, p.WarnAmber, "Cab_ClearanceLamp_" + m, new Vector3(m * 1.55f, 1.86f, 1.99f),
                    new Vector3(0.14f, 0.055f, 0.03f), Vector3.zero);

            // Twin heavy roof HVAC filtration packs + armored comms radome
            Box(root, p.Dark, "Roof_HVAC_L", new Vector3(-0.88f, 1.99f, -0.82f),
                new Vector3(0.86f, 0.16f, 1.34f), Vector3.zero);
            Box(root, p.HullDark, "Roof_HVAC_R", new Vector3(0.88f, 1.98f, -1.06f),
                new Vector3(0.82f, 0.14f, 0.92f), Vector3.zero);
            Cylinder(root, p.Dark, "Radome_Mount", new Vector3(0.88f, 1.96f, -0.18f),
                new Vector3(0.62f, 0.05f, 0.62f), Vector3.zero);
            Sphere(root, p.ArmorPlate, "Radome_Cap", new Vector3(0.88f, 2.02f, -0.18f),
                new Vector3(0.54f, 0.22f, 0.54f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  MATERIALS
        // ═══════════════════════════════════════════════════════════════════════
        private struct Palette
        {
            public readonly Material Hull;
            public readonly Material HullDark;
            public readonly Material ArmorPlate;
            public readonly Material Dark;
            public readonly Material Metal;
            public readonly Material MetalDark;
            public readonly Material Glass;
            public readonly Material GlassSheen;
            public readonly Material Accent;
            public readonly Material WarnAmber;
            public readonly Material Glow;
            public readonly Material GlowDim;
            public readonly Material Screen;
            public readonly Material Seat;
            public readonly Material Strap;

            public Palette(Color bodyColor)
            {
                Color industrialBase = new Color(0.18f, 0.23f, 0.19f);
                Color hull = Color.Lerp(industrialBase, bodyColor, 0.14f);
                Hull = GridBlockMeshBuilder.CreateMaterial(hull, 0.58f, 0.46f);
                HullDark = GridBlockMeshBuilder.CreateMaterial(hull * 0.72f, 0.64f, 0.38f);
                ArmorPlate = GridBlockMeshBuilder.CreateMaterial(new Color(0.25f, 0.28f, 0.27f), 0.76f, 0.42f);
                Dark = GridBlockMeshBuilder.CreateMaterial(new Color(0.065f, 0.078f, 0.078f), 0.72f, 0.36f);
                Metal = GridBlockMeshBuilder.CreateMaterial(new Color(0.58f, 0.63f, 0.64f), 0.86f, 0.58f);
                MetalDark = GridBlockMeshBuilder.CreateMaterial(new Color(0.30f, 0.34f, 0.36f), 0.80f, 0.48f);
                Accent = GridBlockMeshBuilder.CreateMaterial(new Color(0.76f, 0.86f, 0.22f), 0.32f, 0.56f);
                WarnAmber = GridBlockMeshBuilder.CreateMaterial(new Color(1.0f, 0.68f, 0.12f), 0.1f, 0.85f,
                    new Color(0.95f, 0.52f, 0.06f));
                Seat = GridBlockMeshBuilder.CreateMaterial(new Color(0.14f, 0.16f, 0.16f), 0.06f, 0.26f);
                Strap = GridBlockMeshBuilder.CreateMaterial(new Color(0.34f, 0.36f, 0.24f), 0.04f, 0.20f);
                Glow = GridBlockMeshBuilder.CreateMaterial(new Color(0.76f, 0.96f, 1f), 0f, 0.92f,
                    new Color(0.32f, 0.84f, 1f));
                GlowDim = GridBlockMeshBuilder.CreateMaterial(new Color(0.20f, 0.50f, 0.48f), 0f, 0.80f,
                    new Color(0.08f, 0.32f, 0.36f));
                Screen = GridBlockMeshBuilder.CreateMaterial(new Color(0.025f, 0.045f, 0.065f), 0.12f, 0.78f,
                    new Color(0.08f, 0.30f, 0.40f));
                Glass = MakeGlass(new Color(0.68f, 0.90f, 0.98f, 0.065f), 0.96f);
                GlassSheen = MakeGlass(new Color(0.84f, 0.96f, 1.00f, 0.14f), 0.98f);
            }

            private static Material MakeGlass(Color tint, float smoothness)
            {
                var glass = GridBlockMeshBuilder.CreateMaterial(tint, 0.08f, smoothness);
                if (glass == null) return null;
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
