// Assets/Scripts/VoxelEngine/GridSystem/GridBlockMeshBuilder.cs
//
// Builds detailed, recognisable models for grid blocks — inspired by Space
// Engineers and real machinery. CRITICALLY: every model is authored to FILL its
// grid cell (footprint == cellSize) so blocks tile seamlessly with no gaps, and
// the ghost/placement collider always matches. Decorative shaping is done with
// child primitives scaled relative to the cell, never by resizing the root.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class GridBlockMeshBuilder
    {
        public enum Style
        {
            Armor, Cockpit, Thruster, Battery, Cargo, Drill, Grinder, Refinery,
            Weapon, DockingPort, Wheel, LandingGear, SolarPanel, Reactor,
            LiquidTank, GasTank, H2O2, HydrogenEngine, ChemicalPlant, Glass, Demolisher, ItemPipe,
            GasPipe, LiquidPipe, Gyroscope, Beacon, OreDetector, Generic
        }

        private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        /// <summary>Optional hook so editor tooling can persist generated materials as
        /// assets (otherwise runtime-only materials are lost when a prefab is saved,
        /// leaving blocks magenta). Args: (material, suggestedName) → returns the
        /// material to actually use (typically the saved asset). Reset to null at
        /// runtime so play-mode block creation just uses plain materials.</summary>
        public static System.Func<Material, string, Material> MaterialPersister;
        private static int _matCounter;

        /// <summary>Attach the styled visual to <paramref name="root"/>, sized to fill one cell.</summary>
        public static void Build(GameObject root, Style style, GridSize size, Color baseColor)
        {
            float cs = size.CellSize();
            // Shared materials for batching.
            var body  = Mat(baseColor, 0.55f, 0.45f);
            var dark  = Mat(baseColor * 0.55f, 0.7f, 0.35f);
            var metal = Mat(new Color(0.55f, 0.57f, 0.62f), 0.8f, 0.55f);
            var glow  = Mat(new Color(0.25f, 0.8f, 1f), 0f, 0.9f, emissive: new Color(0.1f, 0.6f, 0.9f));

            switch (style)
            {
                case Style.Armor:        BuildArmor(root, cs, body, metal); break;
                case Style.Glass:        BuildGlass(root, cs, baseColor); break;
                case Style.Cockpit:      BuildCockpit(root, cs, body, metal, glow); break;
                case Style.Thruster:     BuildThruster(root, cs, dark, metal); break;
                case Style.Battery:      BuildBattery(root, cs, body, metal, glow); break;
                case Style.Cargo:        BuildCargo(root, cs, body, metal); break;
                case Style.LiquidTank:
                case Style.GasTank:      BuildTank(root, cs, body, metal); break;
                case Style.Drill:        BuildDrill(root, cs, body, metal); break;
                case Style.Grinder:      BuildGrinder(root, cs, dark, metal); break;
                case Style.Weapon:       BuildWeapon(root, cs, dark, metal); break;
                case Style.DockingPort:  BuildDockingPort(root, cs, body, metal, glow); break;
                case Style.Wheel:        BuildWheel(root, cs, dark, metal); break;
                case Style.LandingGear:  BuildLandingGear(root, cs, metal); break;
                case Style.HydrogenEngine: BuildHydrogenEngine(root, cs, body, metal, glow); break;
                case Style.SolarPanel:   BuildSolarPanel(root, cs, metal); break;
                case Style.Reactor:      BuildReactor(root, cs, body, metal, glow); break;
                case Style.H2O2:
                case Style.ChemicalPlant:
                case Style.Refinery:     BuildIndustrial(root, cs, body, metal, glow); break;
                case Style.Demolisher:   BuildGrinder(root, cs, dark, metal); break;
                case Style.ItemPipe:     BuildPipe(root, cs, metal); break;
                case Style.GasPipe:      BuildPipe(root, cs, Mat(new Color(0.4f, 0.7f, 0.95f), 0.6f, 0.5f)); break;
                case Style.LiquidPipe:   BuildPipe(root, cs, Mat(new Color(0.3f, 0.55f, 0.9f), 0.6f, 0.5f)); break;
                case Style.Gyroscope:    BuildGyroscope(root, cs, body, metal, glow); break;
                case Style.Beacon:      BuildBeacon(root, cs, body, metal, glow); break;
                case Style.OreDetector: BuildOreDetector(root, cs, body, metal, glow); break;
                default:                 BuildArmor(root, cs, body, metal); break;
            }
        }

        // ── style builders ──────────────────────────────────────────────────────
        private static void BuildArmor(GameObject r, float cs, Material body, Material metal)
        {
            var b = Box(r, body, V0, One(cs) * 0.995f);
            // Bevelled corner studs for the SE plated look.
            float e = cs * 0.46f, s = cs * 0.10f;
            foreach (var c in Corners(e))
                Box(r, metal, c, new Vector3(s, s, s));
        }

        private static void BuildGlass(GameObject r, float cs, Color tint)
        {
            var g = new Color(tint.r, tint.g, tint.b, 0.35f);
            var glass = Mat(g, 0.1f, 0.95f); glass.SetFloat("_Surface", 1);
            Box(r, glass, V0, One(cs) * 0.97f);
            // Thin metal frame around the edges.
            var metal = Mat(new Color(0.5f, 0.52f, 0.56f), 0.8f, 0.5f);
            foreach (var edge in FrameEdges(cs)) Box(r, metal, edge.pos, edge.scale);
        }

        private static void BuildCockpit(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, new Vector3(0, -cs*0.15f, 0), new Vector3(cs*0.95f, cs*0.55f, cs*0.95f)); // base
            // Canopy (angled glass-ish dome).
            var canopy = Sphere(r, glow, new Vector3(0, cs*0.2f, cs*0.1f), cs*0.55f);
            canopy.transform.localScale = new Vector3(cs*0.7f, cs*0.5f, cs*0.7f);
            Box(r, metal, new Vector3(0, -cs*0.05f, -cs*0.35f), new Vector3(cs*0.5f, cs*0.35f, cs*0.2f)); // seat back
        }

        private static void BuildThruster(GameObject r, float cs, Material body, Material metal)
        {
            // Housing fills the cell; a flared nozzle points -Z.
            Box(r, body, V0, new Vector3(cs*0.95f, cs*0.95f, cs*0.6f));
            var nozzle = Cyl(r, metal, new Vector3(0, 0, -cs*0.45f), cs*0.38f, cs*0.4f);
            nozzle.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var bell = Cyl(r, metal, new Vector3(0, 0, -cs*0.62f), cs*0.48f, cs*0.12f);
            bell.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        private static void BuildBattery(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, V0, One(cs) * 0.92f);
            // Charge indicator strip.
            Box(r, glow, new Vector3(0, 0, cs*0.47f), new Vector3(cs*0.6f, cs*0.12f, cs*0.04f));
            Box(r, metal, new Vector3(0, cs*0.48f, 0), new Vector3(cs*0.3f, cs*0.06f, cs*0.3f)); // terminal
        }

        private static void BuildCargo(GameObject r, float cs, Material body, Material metal)
        {
            Box(r, body, V0, One(cs) * 0.96f);
            // Door + handle on +Z face.
            Box(r, metal, new Vector3(0, 0, cs*0.48f), new Vector3(cs*0.7f, cs*0.7f, cs*0.03f));
            Box(r, metal, new Vector3(cs*0.18f, 0, cs*0.5f), new Vector3(cs*0.05f, cs*0.25f, cs*0.04f));
        }

        private static void BuildTank(GameObject r, float cs, Material body, Material metal)
        {
            // Cell-filling housing so adjacent tanks touch (no corner gaps), with a
            // rounded tank body in front for the look.
            Box(r, metal, V0, One(cs) * 0.98f);
            Cyl(r, body, V0, cs*0.49f, cs*0.49f);
            Cyl(r, metal, new Vector3(0, cs*0.49f, 0), cs*0.34f, cs*0.04f);  // top cap
            Cyl(r, metal, new Vector3(0, -cs*0.49f, 0), cs*0.34f, cs*0.04f); // bottom cap
        }

        private static void BuildDrill(GameObject r, float cs, Material body, Material metal)
        {
            Box(r, body, new Vector3(0, 0, -cs*0.18f), new Vector3(cs*0.98f, cs*0.98f, cs*0.64f)); // cell-filling housing
            // Conical drill head pointing +Z.
            var head = Cyl(r, metal, new Vector3(0, 0, cs*0.35f), cs*0.4f, cs*0.3f);
            head.transform.localRotation = Quaternion.Euler(90, 0, 0);
            head.transform.localScale = new Vector3(cs*0.8f, cs*0.3f, cs*0.8f);
            Sphere(r, metal, new Vector3(0, 0, cs*0.5f), cs*0.18f);
        }

        private static void BuildGrinder(GameObject r, float cs, Material body, Material metal)
        {
            Box(r, body, new Vector3(0, 0, -cs*0.18f), new Vector3(cs*0.98f, cs*0.98f, cs*0.64f)); // cell-filling housing
            var wheel = Cyl(r, metal, new Vector3(0, 0, cs*0.35f), cs*0.42f, cs*0.12f);
            wheel.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }

        private static void BuildWeapon(GameObject r, float cs, Material body, Material metal)
        {
            Box(r, body, new Vector3(0, 0, -cs*0.22f), new Vector3(cs*0.98f, cs*0.98f, cs*0.56f)); // cell-filling mount
            // Multi-barrel gatling pointing +Z.
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var bar = Cyl(r, metal, new Vector3(Mathf.Cos(a)*cs*0.12f, Mathf.Sin(a)*cs*0.12f, cs*0.25f), cs*0.04f, cs*0.4f);
                bar.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
        }

        private static void BuildDockingPort(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, new Vector3(0, -cs*0.2f, 0), new Vector3(cs*0.9f, cs*0.5f, cs*0.9f));
            var ring = Cyl(r, metal, new Vector3(0, cs*0.2f, 0), cs*0.42f, cs*0.1f);
            Cyl(r, glow, new Vector3(0, cs*0.27f, 0), cs*0.3f, cs*0.03f); // guide light
        }

        private static void BuildWheel(GameObject r, float cs, Material body, Material metal)
        {
            int cells = 3;
            string lowerName = r.name.ToLowerInvariant();
            if (lowerName.Contains("2x2")) cells = 2;
            else if (lowerName.Contains("5x5")) cells = 5;

            float radius = cs * cells * 0.5f;
            float width = cs * Mathf.Lerp(0.38f, 0.62f, Mathf.InverseLerp(2f, 5f, cells));
            float mountY = cs * 0.42f;
            float wheelY = -radius * 0.45f;
            var rubber = Mat(new Color(0.025f, 0.026f, 0.030f), 0.25f, 0.28f);
            var rim = Mat(new Color(0.42f, 0.44f, 0.48f), 0.85f, 0.50f);
            var piston = Mat(new Color(0.72f, 0.74f, 0.78f), 0.95f, 0.68f);

            // Cell attachment, suspension body and armored shoulders.
            Box(r, metal, new Vector3(0, mountY, 0), new Vector3(cs * 0.95f, cs * 0.34f, cs * 0.90f));
            Box(r, body,  new Vector3(0, mountY - cs * 0.27f, 0), new Vector3(cs * 0.56f, cs * 0.45f, cs * 0.56f));
            Box(r, metal, new Vector3(-width * 0.72f, mountY - cs * 0.20f, 0), new Vector3(cs * 0.16f, cs * 0.42f, cs * 0.62f));
            Box(r, metal, new Vector3( width * 0.72f, mountY - cs * 0.20f, 0), new Vector3(cs * 0.16f, cs * 0.42f, cs * 0.62f));

            // Hydraulic suspension pistons and guide rails.
            float pistonHeight = Mathf.Abs(mountY - wheelY) * 0.82f;
            var pistonA = Cyl(r, piston, new Vector3(-width * 0.34f, (mountY + wheelY) * 0.5f, -cs * 0.18f), cs * 0.045f, pistonHeight);
            pistonA.transform.localRotation = Quaternion.identity;
            var pistonB = Cyl(r, piston, new Vector3( width * 0.34f, (mountY + wheelY) * 0.5f, -cs * 0.18f), cs * 0.045f, pistonHeight);
            pistonB.transform.localRotation = Quaternion.identity;
            Box(r, metal, new Vector3(-width * 0.50f, (mountY + wheelY) * 0.5f, cs * 0.18f), new Vector3(cs * 0.09f, pistonHeight, cs * 0.09f));
            Box(r, metal, new Vector3( width * 0.50f, (mountY + wheelY) * 0.5f, cs * 0.18f), new Vector3(cs * 0.09f, pistonHeight, cs * 0.09f));

            // Steering fork and axle yoke.
            Box(r, metal, new Vector3(0, wheelY + radius * 0.18f, 0), new Vector3(width * 1.65f, cs * 0.13f, cs * 0.18f));
            Box(r, metal, new Vector3(-width * 0.82f, wheelY, 0), new Vector3(cs * 0.13f, radius * 0.90f, cs * 0.16f));
            Box(r, metal, new Vector3( width * 0.82f, wheelY, 0), new Vector3(cs * 0.13f, radius * 0.90f, cs * 0.16f));

            // Steering pivot used by GridWheel. TireSpinPivot spins the tire separately.
            var pivot = new GameObject("WheelVisualPivot");
            pivot.transform.SetParent(r.transform, false);
            pivot.transform.localPosition = new Vector3(0, wheelY, 0);
            var spin = new GameObject("TireSpinPivot");
            spin.transform.SetParent(pivot.transform, false);

            // Tyre, rim side plates, hub and bolts. Cylinder axis along local X.
            var tyre = Cyl(spin, rubber, V0, radius, width);
            tyre.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var sideA = Cyl(spin, rim, new Vector3(-width * 0.52f, 0, 0), radius * 0.54f, cs * 0.055f);
            sideA.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var sideB = Cyl(spin, rim, new Vector3( width * 0.52f, 0, 0), radius * 0.54f, cs * 0.055f);
            sideB.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var hub = Cyl(spin, metal, V0, radius * 0.26f, width * 1.22f);
            hub.transform.localRotation = Quaternion.Euler(0, 0, 90);

            // Deep tread blocks around the tyre.
            int treadCount = cells >= 5 ? 24 : cells == 3 ? 18 : 14;
            for (int i = 0; i < treadCount; i++)
            {
                float a = i / (float)treadCount * Mathf.PI * 2f;
                var tread = Box(spin, metal,
                    new Vector3(0, Mathf.Sin(a) * radius * 0.94f, Mathf.Cos(a) * radius * 0.94f),
                    new Vector3(width * 1.05f, cs * 0.075f, cs * 0.22f));
                tread.transform.localRotation = Quaternion.Euler(Mathf.Rad2Deg * a, 0, 0);
            }

            // Rim bolts on the visible side.
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                Sphere(spin, metal, new Vector3(width * 0.58f, Mathf.Sin(a) * radius * 0.30f, Mathf.Cos(a) * radius * 0.30f), cs * 0.055f);
            }
        }

        private static void BuildHydrogenEngine(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, V0, One(cs) * 0.92f);
            Box(r, metal, new Vector3(0, -cs * 0.26f, cs * 0.42f), new Vector3(cs * 0.68f, cs * 0.22f, cs * 0.12f));
            Box(r, glow, new Vector3(0, cs * 0.10f, cs * 0.48f), new Vector3(cs * 0.56f, cs * 0.16f, cs * 0.04f));
            var cellA = Cyl(r, metal, new Vector3(-cs * 0.26f, 0, -cs * 0.10f), cs * 0.13f, cs * 0.70f);
            cellA.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var cellB = Cyl(r, metal, new Vector3( cs * 0.26f, 0, -cs * 0.10f), cs * 0.13f, cs * 0.70f);
            cellB.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var exhaust = Cyl(r, metal, new Vector3(0, 0, -cs * 0.48f), cs * 0.22f, cs * 0.22f);
            exhaust.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        private static void BuildLandingGear(GameObject r, float cs, Material metal)
        {
            Box(r, metal, new Vector3(0, cs*0.25f, 0), new Vector3(cs*0.5f, cs*0.4f, cs*0.5f)); // housing
            Box(r, metal, new Vector3(0, -cs*0.25f, 0), new Vector3(cs*0.15f, cs*0.5f, cs*0.15f)); // strut
            Box(r, metal, new Vector3(0, -cs*0.45f, 0), new Vector3(cs*0.6f, cs*0.08f, cs*0.6f)); // foot pad
        }

        private static void BuildSolarPanel(GameObject r, float cs, Material metal)
        {
            var cell = Mat(new Color(0.08f, 0.12f, 0.35f), 0.3f, 0.85f, emissive: new Color(0.02f, 0.04f, 0.12f));
            Box(r, metal, new Vector3(0, -cs*0.4f, 0), new Vector3(cs*0.3f, cs*0.2f, cs*0.3f)); // base
            var panel = Box(r, cell, new Vector3(0, 0, 0), new Vector3(cs*0.95f, cs*0.05f, cs*0.95f));
            panel.transform.localRotation = Quaternion.Euler(-20, 0, 0);
        }

        private static void BuildReactor(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, V0, One(cs) * 0.98f);
            Sphere(r, glow, V0, cs*0.55f);          // glowing core
            foreach (var c in Corners(cs*0.42f)) Cyl(r, metal, c, cs*0.06f, cs*0.9f); // cooling rods
        }

        private static void BuildIndustrial(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, V0, One(cs) * 0.98f);                                            // cell-filling housing
            Cyl(r, metal, new Vector3(cs*0.25f, cs*0.45f, 0.25f*cs), cs*0.14f, cs*0.5f); // chimney/tower
            Cyl(r, metal, new Vector3(-cs*0.25f, cs*0.4f, -0.2f*cs), cs*0.12f, cs*0.4f);
            Box(r, glow, new Vector3(0, 0, cs*0.46f), new Vector3(cs*0.5f, cs*0.1f, cs*0.03f)); // status panel
        }

        private static void BuildGyroscope(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            Box(r, body, V0, One(cs) * 0.96f);                       // housing
            var ring1 = Cyl(r, metal, V0, cs*0.40f, cs*0.06f);      // gimbal ring
            ring1.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var ring2 = Cyl(r, metal, V0, cs*0.34f, cs*0.06f);
            ring2.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Sphere(r, glow, V0, cs*0.30f);                          // spinning core
        }

        private static void BuildPipe(GameObject r, float cs, Material mat)
        {
            string lowerName = r.name.ToLowerInvariant();
            bool gas = lowerName.Contains("gas");
            bool liquid = lowerName.Contains("liquid");
            Color accentColor = gas ? new Color(0.95f, 0.78f, 0.22f) : liquid ? new Color(0.20f, 0.65f, 0.95f) : new Color(0.95f, 0.55f, 0.12f);
            Color darkColor = new Color(0.08f, 0.085f, 0.10f);
            Color? emissive = null;
            if (gas) emissive = new Color(0.20f, 0.12f, 0.02f);
            else if (liquid) emissive = new Color(0.02f, 0.08f, 0.16f);
            var accent = Mat(accentColor, 0.75f, 0.55f, emissive);
            var dark = Mat(darkColor, 0.85f, 0.38f);

            float radius = cs * (lowerName.Contains("large") ? 0.17f : 0.18f);
            float collarRadius = radius * 1.35f;
            float length = cs * 0.92f;

            // Main tube along Z spanning the cell so segments connect end-to-end.
            var tube = Cyl(r, mat, V0, radius, length * 0.5f);
            tube.transform.localRotation = Quaternion.Euler(90, 0, 0);

            // Dark support spine under the pipe.
            Box(r, dark, new Vector3(0, -radius * 1.25f, 0), new Vector3(radius * 0.72f, radius * 0.30f, length));

            // End collars and mid clamp bands.
            Cyl(r, accent, new Vector3(0, 0,  cs * 0.45f), collarRadius, cs * 0.045f).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cyl(r, accent, new Vector3(0, 0, -cs * 0.45f), collarRadius, cs * 0.045f).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cyl(r, dark,   new Vector3(0, 0,  cs * 0.18f), radius * 1.18f, cs * 0.035f).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cyl(r, dark,   new Vector3(0, 0, -cs * 0.18f), radius * 1.18f, cs * 0.035f).transform.localRotation = Quaternion.Euler(90, 0, 0);

            // Small readable flow indicator plates on the top.
            Box(r, accent, new Vector3(0, radius * 1.18f,  cs * 0.12f), new Vector3(radius * 1.4f, radius * 0.18f, cs * 0.10f));
            Box(r, accent, new Vector3(0, radius * 1.18f, -cs * 0.12f), new Vector3(radius * 1.4f, radius * 0.18f, cs * 0.10f));
        }

        // ── BEACON ──────────────────────────────────────────────────────────────
        private static void BuildBeacon(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            // Base mounting plate.
            Box(r, metal, new Vector3(0, -cs * 0.4f, 0), new Vector3(cs * 0.6f, cs * 0.08f, cs * 0.6f));
            // Antenna mast.
            var mast = Cyl(r, metal, new Vector3(0, 0, 0), cs * 0.04f, cs * 0.8f);
            mast.transform.localRotation = Quaternion.Euler(0, 0, 0);
            // Beacon lamp housing (rotating).
            var lamp = Cyl(r, body, new Vector3(0, cs * 0.35f, 0), cs * 0.08f, cs * 0.06f);
            // Lens (glowing).
            Sphere(r, glow, new Vector3(0, cs * 0.38f, cs * 0.06f), cs * 0.05f);
            // Antenna tip.
            Sphere(r, metal, new Vector3(0, cs * 0.45f, 0), cs * 0.03f);
            // Guy wire anchors.
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                Box(r, metal, new Vector3(Mathf.Cos(a) * cs * 0.25f, -cs * 0.3f, Mathf.Sin(a) * cs * 0.25f),
                    new Vector3(cs * 0.03f, cs * 0.15f, cs * 0.03f));
            }
        }

        // ── ORE DETECTOR ────────────────────────────────────────────────────────
        private static void BuildOreDetector(GameObject r, float cs, Material body, Material metal, Material glow)
        {
            // Base housing.
            Box(r, body, new Vector3(0, -cs * 0.2f, 0), new Vector3(cs * 0.8f, cs * 0.35f, cs * 0.8f));
            // Mounting post.
            var post = Cyl(r, metal, new Vector3(0, cs * 0.05f, 0), cs * 0.06f, cs * 0.25f);
            // Parabolic dish (flattened hemisphere — rotates).
            var dishPivot = new GameObject("DetectorDish");
            dishPivot.transform.SetParent(r.transform, false);
            dishPivot.transform.localPosition = new Vector3(0, cs * 0.25f, 0);
            var dish = Sphere(dishPivot, metal, V0, cs * 0.25f);
            dish.transform.localScale = new Vector3(cs * 0.5f, cs * 0.15f, cs * 0.5f);
            // Sensor node (glowing).
            Sphere(dishPivot, glow, new Vector3(0, cs * 0.1f, 0), cs * 0.05f);
            // Support arm.
            Cyl(dishPivot, metal, new Vector3(0, 0, cs * 0.12f), cs * 0.03f, cs * 0.2f).transform.localRotation = Quaternion.Euler(30, 0, 0);
            // Status indicator.
            Sphere(r, glow, new Vector3(cs * 0.3f, -cs * 0.15f, cs * 0.3f), cs * 0.03f);
        }

        // ── primitive helpers ─────────────────────────────────────────────────────
        private static readonly Vector3 V0 = Vector3.zero;
        private static Vector3 One(float cs) => new Vector3(cs, cs, cs);

        private static GameObject Box(GameObject parent, Material m, Vector3 pos, Vector3 scale)
            => Prim(parent, PrimitiveType.Cube, m, pos, scale);
        private static GameObject Sphere(GameObject parent, Material m, Vector3 pos, float d)
            => Prim(parent, PrimitiveType.Sphere, m, pos, new Vector3(d, d, d));
        private static GameObject Cyl(GameObject parent, Material m, Vector3 pos, float radius, float height)
            => Prim(parent, PrimitiveType.Cylinder, m, pos, new Vector3(radius*2f, height*0.5f, radius*2f));

        private static GameObject Prim(GameObject parent, PrimitiveType type, Material m, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);   // collider lives on the root
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private static Vector3[] Corners(float e) => new[]
        {
            new Vector3( e, e, e), new Vector3(-e, e, e), new Vector3( e,-e, e), new Vector3(-e,-e, e),
            new Vector3( e, e,-e), new Vector3(-e, e,-e), new Vector3( e,-e,-e), new Vector3(-e,-e,-e),
        };

        private static (Vector3 pos, Vector3 scale)[] FrameEdges(float cs)
        {
            float h = cs * 0.48f, t = cs * 0.06f, len = cs * 0.96f;
            return new[]
            {
                (new Vector3(0,  h,  h), new Vector3(len, t, t)),
                (new Vector3(0, -h,  h), new Vector3(len, t, t)),
                (new Vector3(0,  h, -h), new Vector3(len, t, t)),
                (new Vector3(0, -h, -h), new Vector3(len, t, t)),
            };
        }

        private static Material Mat(Color c, float metallic, float smooth, Color? emissive = null)
        {
            var m = new Material(Lit) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic"))  m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness", smooth);
            if (emissive.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emissive.Value);
            }
            // If an editor persister is set, save the material as an asset so the
            // prefab keeps a valid reference (no more magenta blocks).
            if (MaterialPersister != null)
                m = MaterialPersister(m, $"GMat_{_matCounter++}");
            return m;
        }
    }
}
