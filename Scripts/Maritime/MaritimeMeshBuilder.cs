// Assets/Scripts/VoxelEngine/Maritime/MaritimeMeshBuilder.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║   MARITIME MESH BUILDER — bespoke procedural models for every     ║
//  ║   propulsion/power block. Each block looks like what it IS:        ║
//  ║   propellers have blades, the helm is a ship's wheel, engines      ║
//  ║   have pistons, the turbo has a snail housing, etc.                ║
//  ╚══════════════════════════════════════════════════════════════════╝
//
//  Same primitive-composition pattern as GridBlockMeshBuilder but with
//  maritime-specific geometry. Every model fills its grid cell (footprint ==
//  cellSize) so blocks tile seamlessly.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public static class MaritimeMeshBuilder
    {
        /// <summary>Mesh version — bump to force a rebuild of all maritime prefabs.</summary>
        public const int Version = 2;

        private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        /// <summary>Optional hook so editor tooling can persist generated materials as assets.</summary>
        public static System.Func<Material, string, Material> MaterialPersister;
        private static int _matCounter;

        private static readonly Vector3 V0 = Vector3.zero;

        // ── Material presets ──────────────────────────────────────────
        private static Material Steel    => Mat(new Color(0.50f, 0.52f, 0.57f), 0.85f, 0.55f);
        private static Material DarkSteel=> Mat(new Color(0.28f, 0.29f, 0.33f), 0.85f, 0.45f);
        private static Material CastIron => Mat(new Color(0.35f, 0.34f, 0.36f), 0.80f, 0.35f);
        private static Material Brass    => Mat(new Color(0.78f, 0.60f, 0.20f), 0.70f, 0.60f);
        private static Material Bronze   => Mat(new Color(0.72f, 0.48f, 0.18f), 0.75f, 0.50f);
        private static Material Copper   => Mat(new Color(0.72f, 0.45f, 0.20f), 0.70f, 0.55f);
        private static Material Chrome   => Mat(new Color(0.85f, 0.86f, 0.88f), 0.92f, 0.85f);
        private static Material Oak      => Mat(new Color(0.45f, 0.30f, 0.15f), 0.0f, 0.65f);
        private static Material DarkOak  => Mat(new Color(0.30f, 0.20f, 0.10f), 0.0f, 0.60f);
        private static Material Rubber   => Mat(new Color(0.08f, 0.08f, 0.09f), 0.0f, 0.40f);
        private static Material Glow     => Mat(new Color(0.2f, 0.7f, 1f), 0f, 0.9f, emissive: new Color(0.1f, 0.5f, 0.8f));
        private static Material GlowRed  => Mat(new Color(0.9f, 0.2f, 0.1f), 0f, 0.9f, emissive: new Color(0.7f, 0.1f, 0.05f));
        private static Material GlassBlue => Mat(new Color(0.4f, 0.6f, 0.85f, 0.6f), 0f, 0.9f);

        /// <summary>Build a maritime block mesh on the given root, identified by prefab name.</summary>
        public static void Build(GameObject root, string prefabName, GridSize size)
        {
            float cs = size.CellSize();
            string n = prefabName.ToLowerInvariant();

            // Clear any existing children (force-rebuild on version bump).
            while (root.transform.childCount > 0)
                Object.DestroyImmediate(root.transform.GetChild(0).gameObject);

            // Version marker child (invisible, just for detection).
            var marker = new GameObject($"__MaritimeMesh_v{Version}");
            marker.transform.SetParent(root.transform, false);
            marker.SetActive(false);

            if (n.Contains("propeller_small"))      BuildPropellerSmall(root, cs);
            else if (n.Contains("propeller_large"))  BuildPropellerLarge(root, cs);
            else if (n.Contains("epropeller"))       BuildEPropeller(root, cs);
            else if (n.Contains("engine_giant"))     BuildGiantDiesel(root, cs);
            else if (n.Contains("engine_medium"))    BuildMediumEngine(root, cs);
            else if (n.Contains("engine_small"))     BuildSmallEngine(root, cs);
            else if (n.Contains("turbocharger"))     BuildTurbocharger(root, cs);
            else if (n.Contains("gearbox"))          BuildGearbox(root, cs);
            else if (n.Contains("waterwheel"))       BuildWaterwheel(root, cs);
            else if (n.Contains("driveshaft"))       BuildDriveShaft(root, cs);
            else if (n.Contains("maritimegenerator"))BuildGenerator(root, cs);
            else if (n.Contains("exhaust"))          BuildExhaustPipe(root, cs);
            else if (n.Contains("bilgepump"))        BuildBilgePump(root, cs);
            else if (n.Contains("helm"))             BuildHelm(root, cs);
            else if (n.Contains("hull_balsa"))       BuildHull(root, cs, new Color(0.80f, 0.65f, 0.40f), 0f, 0.7f);
            else if (n.Contains("hull_iron"))        BuildHull(root, cs, new Color(0.45f, 0.47f, 0.52f), 0.85f, 0.5f, rivets: true);
            else if (n.Contains("hull_tar"))         BuildHull(root, cs, new Color(0.30f, 0.22f, 0.14f), 0f, 0.5f);
            else if (n.Contains("hull_untreated"))   BuildHull(root, cs, new Color(0.55f, 0.40f, 0.25f), 0f, 0.65f, planks: true);
            else                                    BuildHull(root, cs, new Color(0.5f, 0.5f, 0.5f), 0.5f, 0.4f);
        }

        // ════════════════════════════════════════════════════════════════
        //  PROPELLERS
        // ════════════════════════════════════════════════════════════════
        private static void BuildPropellerSmall(GameObject r, float cs)
        {
            // Central hub + 3 angled bronze blades. Points +Z (forward = push direction).
            var hub = Cyl(r, Bronze, new Vector3(0, 0, cs * 0.35f), cs * 0.15f, cs * 0.25f);
            hub.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Sphere(r, Bronze, new Vector3(0, 0, cs * 0.42f), cs * 0.12f); // nose cone

            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f;
                var blade = Box(r, Bronze, new Vector3(0, 0, cs * 0.2f), new Vector3(cs * 0.42f, cs * 0.08f, cs * 0.12f));
                blade.transform.localPosition = new Vector3(0, 0, cs * 0.15f);
                blade.transform.localRotation = Quaternion.Euler(15f, angle, 0f);
            }
            // Packing gland (base).
            Box(r, CastIron, new Vector3(0, 0, -cs * 0.2f), new Vector3(cs * 0.5f, cs * 0.5f, cs * 0.35f));
        }

        private static void BuildPropellerLarge(GameObject r, float cs)
        {
            // Heavy 4-blade steel propeller.
            var hub = Cyl(r, DarkSteel, new Vector3(0, 0, cs * 0.35f), cs * 0.2f, cs * 0.3f);
            hub.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Sphere(r, DarkSteel, new Vector3(0, 0, cs * 0.45f), cs * 0.18f);

            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var blade = Box(r, Steel, new Vector3(0, 0, cs * 0.2f), new Vector3(cs * 0.46f, cs * 0.1f, cs * 0.16f));
                blade.transform.localPosition = new Vector3(0, 0, cs * 0.15f);
                blade.transform.localRotation = Quaternion.Euler(20f, angle, 0f);
            }
            // Heavy mounting boss.
            Box(r, CastIron, new Vector3(0, 0, -cs * 0.25f), new Vector3(cs * 0.7f, cs * 0.7f, cs * 0.4f));
        }

        private static void BuildEPropeller(GameObject r, float cs)
        {
            // Torpedo pod: sleek housing + 3-blade prop.
            var pod = Cyl(r, Bronze, V0, cs * 0.35f, cs * 0.8f);
            pod.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // Armored power conduit.
            Box(r, DarkSteel, new Vector3(0, 0, -cs * 0.45f), new Vector3(cs * 0.3f, cs * 0.3f, cs * 0.15f));
            // Propeller blades at front.
            for (int i = 0; i < 3; i++)
            {
                float angle = i * 120f;
                var blade = Box(r, Bronze, new Vector3(0, 0, cs * 0.35f), new Vector3(cs * 0.35f, cs * 0.06f, cs * 0.1f));
                blade.transform.localRotation = Quaternion.Euler(12f, angle, 0f);
            }
            Sphere(r, Bronze, new Vector3(0, 0, cs * 0.42f), cs * 0.1f);
        }

        // ════════════════════════════════════════════════════════════════
        //  ENGINES
        // ════════════════════════════════════════════════════════════════
        private static void BuildSmallEngine(GameObject r, float cs)
        {
            // Compact block with brass pistons + copper boiler.
            Box(r, CastIron, new Vector3(0, -cs * 0.1f, 0), new Vector3(cs * 0.85f, cs * 0.6f, cs * 0.85f));
            // Copper boiler cylinder.
            var boiler = Cyl(r, Copper, new Vector3(0, cs * 0.2f, 0), cs * 0.25f, cs * 0.35f);
            // Two brass pistons.
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1 : 1) * cs * 0.2f;
                var piston = Cyl(r, Brass, new Vector3(x, cs * 0.35f, cs * 0.15f), cs * 0.08f, cs * 0.25f);
            }
            // Flywheel on side.
            var fly = Cyl(r, Steel, new Vector3(cs * 0.45f, -cs * 0.05f, 0), cs * 0.18f, cs * 0.06f);
            fly.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }

        private static void BuildMediumEngine(GameObject r, float cs)
        {
            // Inline-4 cast iron block.
            Box(r, CastIron, V0, new Vector3(cs * 0.9f, cs * 0.55f, cs * 0.9f));
            // 4 cylinders in a row.
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * cs * 0.2f;
                var cyl = Cyl(r, DarkSteel, new Vector3(x, cs * 0.35f, 0), cs * 0.08f, cs * 0.3f);
                // Piston cap.
                Cyl(r, Brass, new Vector3(x, cs * 0.5f, 0), cs * 0.07f, cs * 0.04f);
            }
            // Belt drive wheel.
            var belt = Cyl(r, Rubber, new Vector3(cs * 0.48f, cs * 0.1f, 0), cs * 0.15f, cs * 0.08f);
            belt.transform.localRotation = Quaternion.Euler(0, 0, 90);
            // Oil sump.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.35f, 0), new Vector3(cs * 0.7f, cs * 0.15f, cs * 0.7f));
        }

        private static void BuildGiantDiesel(GameObject r, float cs)
        {
            // Massive V-configuration block with manifold pipes.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.15f, 0), new Vector3(cs * 0.95f, cs * 0.5f, cs * 0.95f)); // crankcase

            // Two banks of cylinders in V formation.
            for (int bank = 0; bank < 2; bank++)
            {
                float tilt = bank == 0 ? 30f : -30f;
                float xOffset = bank == 0 ? cs * 0.12f : -cs * 0.12f;
                for (int i = 0; i < 3; i++)
                {
                    float z = (i - 1) * cs * 0.25f;
                    var cyl = Cyl(r, CastIron, new Vector3(xOffset, cs * 0.25f, z), cs * 0.1f, cs * 0.35f);
                    cyl.transform.localRotation = Quaternion.Euler(0, 0, tilt);
                    // Glow plug cap.
                    var cap = Cyl(r, Brass, new Vector3(xOffset + Mathf.Sin(tilt * Mathf.Deg2Rad) * cs * 0.2f, cs * 0.45f, z), cs * 0.07f, cs * 0.05f);
                }
            }

            // Massive steel exhaust manifold.
            var manifold = Cyl(r, Steel, new Vector3(0, cs * 0.5f, 0), cs * 0.12f, cs * 0.7f);
            manifold.transform.localRotation = Quaternion.Euler(90, 0, 0);

            // Fuel pump block.
            Box(r, Brass, new Vector3(cs * 0.35f, cs * 0.1f, -cs * 0.3f), new Vector3(cs * 0.12f, cs * 0.2f, cs * 0.15f));
            // Base mounting plate.
            Box(r, Steel, new Vector3(0, -cs * 0.45f, 0), new Vector3(cs * 0.98f, cs * 0.08f, cs * 0.98f));
        }

        // ════════════════════════════════════════════════════════════════
        //  TURBOCHARGER
        // ════════════════════════════════════════════════════════════════
        private static void BuildTurbocharger(GameObject r, float cs)
        {
            // Snail-shell housing (scaled sphere) + turbine inlet + outlet.
            var housing = Sphere(r, Chrome, new Vector3(0, 0, 0), cs * 0.35f);
            housing.transform.localScale = new Vector3(cs * 0.7f, cs * 0.7f, cs * 0.5f);
            // Glowing red core.
            Sphere(r, GlowRed, V0, cs * 0.12f);
            // Inlet pipe.
            var inlet = Cyl(r, Chrome, new Vector3(0, cs * 0.35f, 0), cs * 0.1f, cs * 0.2f);
            // Outlet pipe.
            var outlet = Cyl(r, Chrome, new Vector3(cs * 0.35f, 0, 0), cs * 0.1f, cs * 0.2f);
            outlet.transform.localRotation = Quaternion.Euler(0, 0, 90);
            // Mounting base.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.35f, 0), new Vector3(cs * 0.5f, cs * 0.15f, cs * 0.5f));
        }

        // ════════════════════════════════════════════════════════════════
        //  GEARBOX
        // ════════════════════════════════════════════════════════════════
        private static void BuildGearbox(GameObject r, float cs)
        {
            // Housing.
            Box(r, CastIron, V0, new Vector3(cs * 0.9f, cs * 0.7f, cs * 0.9f));
            // Large gear on top.
            var gear = Cyl(r, Steel, new Vector3(0, cs * 0.4f, 0), cs * 0.3f, cs * 0.08f);
            // Gear teeth.
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                var tooth = Box(r, Steel, new Vector3(Mathf.Cos(a) * cs * 0.3f, cs * 0.4f, Mathf.Sin(a) * cs * 0.3f), new Vector3(cs * 0.06f, cs * 0.06f, cs * 0.06f));
            }
            // Input shaft (-Z).
            var inShaft = Cyl(r, Steel, new Vector3(0, 0, -cs * 0.48f), cs * 0.08f, cs * 0.15f);
            inShaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // Output shaft (+Z).
            var outShaft = Cyl(r, Brass, new Vector3(0, 0, cs * 0.48f), cs * 0.08f, cs * 0.15f);
            outShaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        // ════════════════════════════════════════════════════════════════
        //  WATERWHEEL
        // ════════════════════════════════════════════════════════════════
        private static void BuildWaterwheel(GameObject r, float cs)
        {
            // Large wheel with paddles — oriented in the XZ plane (spins around X).
            float wheelR = cs * 0.45f;
            // Iron rim (two flattened cylinders).
            var rim1 = Cyl(r, CastIron, new Vector3(cs * 0.08f, 0, 0), wheelR, cs * 0.04f);
            rim1.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var rim2 = Cyl(r, CastIron, new Vector3(-cs * 0.08f, 0, 0), wheelR, cs * 0.04f);
            rim2.transform.localRotation = Quaternion.Euler(0, 0, 90);
            // Hub.
            var hub = Cyl(r, Steel, V0, cs * 0.12f, cs * 0.2f);
            hub.transform.localRotation = Quaternion.Euler(0, 0, 90);
            // Oak paddles around the rim.
            int paddleCount = 8;
            for (int i = 0; i < paddleCount; i++)
            {
                float angle = i * (360f / paddleCount) * Mathf.Deg2Rad;
                float px = 0;
                float py = Mathf.Sin(angle) * wheelR;
                float pz = Mathf.Cos(angle) * wheelR;
                var paddle = Box(r, Oak, new Vector3(px, py, pz), new Vector3(cs * 0.18f, cs * 0.04f, cs * 0.2f));
                paddle.transform.localRotation = Quaternion.Euler(0, i * (360f / paddleCount), 0);
            }
            // Spokes.
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var spoke = Box(r, Steel, V0, new Vector3(cs * 0.16f, wheelR * 0.9f, cs * 0.03f));
                spoke.transform.localRotation = Quaternion.Euler(0, 0, angle);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  DRIVE SHAFT
        // ════════════════════════════════════════════════════════════════
        private static void BuildDriveShaft(GameObject r, float cs)
        {
            // Long steel shaft running along Z.
            var shaft = Cyl(r, Chrome, V0, cs * 0.12f, cs * 0.9f);
            shaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // Coupling flanges at both ends.
            var f1 = Cyl(r, Steel, new Vector3(0, 0, -cs * 0.42f), cs * 0.2f, cs * 0.06f);
            f1.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var f2 = Cyl(r, Steel, new Vector3(0, 0, cs * 0.42f), cs * 0.2f, cs * 0.06f);
            f2.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // Universal joint detail.
            Sphere(r, DarkSteel, new Vector3(0, 0, 0), cs * 0.1f);
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATOR
        // ════════════════════════════════════════════════════════════════
        private static void BuildGenerator(GameObject r, float cs)
        {
            // Housing.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.1f, 0), new Vector3(cs * 0.9f, cs * 0.6f, cs * 0.9f));
            // Copper coil windings visible on top.
            for (int i = 0; i < 3; i++)
            {
                float z = (i - 1) * cs * 0.22f;
                var coil = Cyl(r, Copper, new Vector3(0, cs * 0.3f, z), cs * 0.12f, cs * 0.25f);
            }
            // Terminal posts.
            Box(r, Brass, new Vector3(cs * 0.3f, cs * 0.35f, cs * 0.3f), new Vector3(cs * 0.08f, cs * 0.15f, cs * 0.08f));
            Box(r, Brass, new Vector3(-cs * 0.3f, cs * 0.35f, cs * 0.3f), new Vector3(cs * 0.08f, cs * 0.15f, cs * 0.08f));
            // Input shaft.
            var shaft = Cyl(r, Steel, new Vector3(0, 0, -cs * 0.48f), cs * 0.08f, cs * 0.12f);
            shaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // Battery indicator strip.
            Box(r, Glow, new Vector3(0, cs * 0.48f, -cs * 0.3f), new Vector3(cs * 0.3f, cs * 0.04f, cs * 0.04f));
        }

        // ════════════════════════════════════════════════════════════════
        //  EXHAUST PIPE
        // ════════════════════════════════════════════════════════════════
        private static void BuildExhaustPipe(GameObject r, float cs)
        {
            // Vertical pipe with vent holes.
            var pipe = Cyl(r, CastIron, new Vector3(0, cs * 0.1f, 0), cs * 0.15f, cs * 0.7f);
            // Flange at base.
            var flange = Cyl(r, Steel, new Vector3(0, -cs * 0.3f, 0), cs * 0.25f, cs * 0.06f);
            // Vent holes (dark cubes around the pipe).
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                float y = cs * 0.1f + (i % 2) * cs * 0.15f;
                var hole = Box(r, Rubber, new Vector3(Mathf.Cos(angle) * cs * 0.14f, y, Mathf.Sin(angle) * cs * 0.14f), new Vector3(cs * 0.04f, cs * 0.06f, cs * 0.04f));
            }
            // Top cap.
            var cap = Cyl(r, DarkSteel, new Vector3(0, cs * 0.42f, 0), cs * 0.16f, cs * 0.04f);
        }

        // ════════════════════════════════════════════════════════════════
        //  BILGE PUMP
        // ════════════════════════════════════════════════════════════════
        private static void BuildBilgePump(GameObject r, float cs)
        {
            // Pump housing.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.15f, 0), new Vector3(cs * 0.85f, cs * 0.5f, cs * 0.85f));
            // Motor on top.
            var motor = Cyl(r, CastIron, new Vector3(0, cs * 0.25f, 0), cs * 0.18f, cs * 0.3f);
            // Cooling fins.
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var fin = Box(r, Steel, new Vector3(0, cs * 0.25f, 0), new Vector3(cs * 0.4f, cs * 0.04f, cs * 0.04f));
                fin.transform.localRotation = Quaternion.Euler(0, angle, 0);
            }
            // Outlet pipe.
            var outlet = Cyl(r, Copper, new Vector3(cs * 0.35f, cs * 0.1f, 0), cs * 0.08f, cs * 0.2f);
            outlet.transform.localRotation = Quaternion.Euler(0, 0, 90);
            // Status light.
            Sphere(r, Glow, new Vector3(0, cs * 0.42f, 0), cs * 0.05f);
        }

        // ════════════════════════════════════════════════════════════════
        //  HELM (Ship's Wheel)
        // ════════════════════════════════════════════════════════════════
        private static void BuildHelm(GameObject r, float cs)
        {
            // Ship's wheel: outer ring + spokes + handles + center hub + pedestal.
            float wheelR = cs * 0.4f;

            // Outer ring (flattened cylinder = torus approximation).
            var ring = Cyl(r, DarkOak, new Vector3(0, cs * 0.3f, 0), wheelR, cs * 0.05f);
            ring.transform.localRotation = Quaternion.Euler(0, 0, 90);
            ring.transform.localScale = new Vector3(wheelR * 2f, cs * 0.1f, wheelR * 2f * 0.15f);

            // Inner ring.
            var ring2 = Cyl(r, Oak, new Vector3(0, cs * 0.3f, 0), wheelR * 0.7f, cs * 0.04f);
            ring2.transform.localRotation = Quaternion.Euler(0, 0, 90);
            ring2.transform.localScale = new Vector3(wheelR * 1.4f, cs * 0.08f, wheelR * 1.4f * 0.15f);

            // Center hub.
            var hub = Cyl(r, Brass, new Vector3(0, cs * 0.3f, 0), cs * 0.08f, cs * 0.12f);
            hub.transform.localRotation = Quaternion.Euler(0, 0, 90);

            // Spokes (8).
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                var spoke = Box(r, Oak, new Vector3(0, cs * 0.3f, 0), new Vector3(wheelR * 1.7f, cs * 0.03f, cs * 0.03f));
                spoke.transform.localRotation = Quaternion.Euler(0, 0, angle);
            }

            // Handles (small knobs on the outer ring).
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * Mathf.Deg2Rad;
                float hx = Mathf.Cos(angle) * wheelR;
                float hy = cs * 0.3f + Mathf.Sin(angle) * wheelR;
                Sphere(r, Brass, new Vector3(hx, hy, 0), cs * 0.04f);
            }

            // Pedestal / steering column.
            var pedestal = Cyl(r, DarkOak, new Vector3(0, -cs * 0.15f, 0), cs * 0.1f, cs * 0.5f);
            // Base plate.
            Box(r, DarkOak, new Vector3(0, -cs * 0.42f, 0), new Vector3(cs * 0.4f, cs * 0.08f, cs * 0.4f));
            // Compass binnacle.
            Box(r, Brass, new Vector3(0, cs * 0.3f, cs * 0.25f), new Vector3(cs * 0.12f, cs * 0.1f, cs * 0.08f));
        }

        // ════════════════════════════════════════════════════════════════
        //  HULL BLOCKS
        // ════════════════════════════════════════════════════════════════
        private static void BuildHull(GameObject r, float cs, Color color, float metallic, float smooth,
            bool planks = false, bool rivets = false)
        {
            var mat = Mat(color, metallic, smooth);
            Box(r, mat, V0, new Vector3(cs * 0.98f, cs * 0.98f, cs * 0.98f));

            if (planks)
            {
                // Horizontal plank lines.
                var dark = Mat(color * 0.7f, metallic, smooth);
                for (int i = 0; i < 4; i++)
                {
                    float y = (i - 1.5f) * cs * 0.24f;
                    Box(r, dark, new Vector3(0, y, cs * 0.49f), new Vector3(cs * 0.98f, cs * 0.02f, cs * 0.01f));
                }
            }

            if (rivets)
            {
                // Steel rivets at corners.
                var rivetMat = Mat(new Color(0.6f, 0.62f, 0.65f), 0.9f, 0.6f);
                float[][] corners = {
                    new[] { cs * 0.35f, cs * 0.35f },
                    new[] { -cs * 0.35f, cs * 0.35f },
                    new[] { cs * 0.35f, -cs * 0.35f },
                    new[] { -cs * 0.35f, -cs * 0.35f },
                };
                foreach (var c in corners)
                {
                    Sphere(r, rivetMat, new Vector3(c[0], c[1], cs * 0.495f), cs * 0.04f);
                    Sphere(r, rivetMat, new Vector3(c[0], c[1], -cs * 0.495f), cs * 0.04f);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  PRIMITIVE HELPERS (same pattern as GridBlockMeshBuilder)
        // ════════════════════════════════════════════════════════════════
        private static GameObject Box(GameObject parent, Material m, Vector3 pos, Vector3 scale)
            => Prim(parent, PrimitiveType.Cube, m, pos, scale);

        private static GameObject Sphere(GameObject parent, Material m, Vector3 pos, float d)
            => Prim(parent, PrimitiveType.Sphere, m, pos, new Vector3(d, d, d));

        private static GameObject Cyl(GameObject parent, Material m, Vector3 pos, float radius, float height)
            => Prim(parent, PrimitiveType.Cylinder, m, pos, new Vector3(radius * 2f, height * 0.5f, radius * 2f));

        private static GameObject Prim(GameObject parent, PrimitiveType type, Material m, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private static Material Mat(Color c, float metallic, float smooth, Color? emissive = null)
        {
            var m = new Material(Lit) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (emissive.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emissive.Value);
            }
            if (MaterialPersister != null)
                m = MaterialPersister(m, $"MMat_{_matCounter++}");
            return m;
        }
    }
}
