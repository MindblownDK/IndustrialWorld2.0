// Assets/Scripts/VoxelEngine/Maritime/MaritimeMeshBuilder.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║   MARITIME MESH BUILDER v3 — realistic procedural models with     ║
//  ║   named animation pivots for MaritimeAnimator.                     ║
//  ║                                                                    ║
//  ║   Every spinning part is parented to a named empty GameObject      ║
//  ║   (SpinPivot, TurboSpin, Piston_N, GearRotor, GenRotor, HelmWheel)║
//  ║   so MaritimeAnimator can find and rotate them.                    ║
//  ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    public static class MaritimeMeshBuilder
    {
        public const int Version = 4;
        private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        public static System.Func<Material, string, Material> MaterialPersister;
        private static int _matCounter;
        private static readonly Vector3 V0 = Vector3.zero;

        // Material presets
        static Material MatC(Color c, float m, float s, Color? e = null)
        {
            var mat = new Material(Lit) { color = c };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", m);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", s);
            if (e.HasValue && mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", e.Value); }
            if (MaterialPersister != null) mat = MaterialPersister(mat, $"MMat_{_matCounter++}");
            return mat;
        }
        static Material Steel     => MatC(new Color(0.50f, 0.52f, 0.57f), 0.85f, 0.55f);
        static Material DarkSteel => MatC(new Color(0.28f, 0.29f, 0.33f), 0.85f, 0.45f);
        static Material CastIron  => MatC(new Color(0.35f, 0.34f, 0.36f), 0.80f, 0.35f);
        static Material Brass     => MatC(new Color(0.80f, 0.62f, 0.20f), 0.75f, 0.65f);
        static Material Bronze    => MatC(new Color(0.72f, 0.48f, 0.18f), 0.75f, 0.50f);
        static Material Copper    => MatC(new Color(0.72f, 0.45f, 0.20f), 0.70f, 0.55f);
        static Material Chrome    => MatC(new Color(0.88f, 0.89f, 0.91f), 0.94f, 0.88f);
        static Material Oak       => MatC(new Color(0.45f, 0.30f, 0.15f), 0.0f, 0.65f);
        static Material DarkOak   => MatC(new Color(0.30f, 0.20f, 0.10f), 0.0f, 0.60f);
        static Material Rubber    => MatC(new Color(0.08f, 0.08f, 0.09f), 0.0f, 0.40f);
        static Material Glow      => MatC(new Color(0.2f, 0.7f, 1f), 0f, 0.9f, e: new Color(0.1f, 0.5f, 0.8f));
        static Material GlowRed   => MatC(new Color(0.95f, 0.25f, 0.1f), 0f, 0.9f, e: new Color(0.8f, 0.15f, 0.05f));
        static Material GlowOrange=> MatC(new Color(0.95f, 0.55f, 0.1f), 0f, 0.9f, e: new Color(0.7f, 0.35f, 0.05f));

        public static void Build(GameObject root, string prefabName, GridSize size)
        {
            float cs = size.CellSize();
            string n = prefabName.ToLowerInvariant();

            while (root.transform.childCount > 0)
                Object.DestroyImmediate(root.transform.GetChild(0).gameObject);

            var marker = new GameObject($"__MaritimeMesh_v{Version}");
            marker.transform.SetParent(root.transform, false);
            marker.SetActive(false);

            if      (n.Contains("propeller_small"))     BuildPropeller(root, cs, 3, 0.15f, Bronze,  false);
            else if (n.Contains("propeller_large"))      BuildPropeller(root, cs, 4, 0.42f, Steel,   true);
            else if (n.Contains("epropeller"))           BuildEPropeller(root, cs);
            else if (n.Contains("engine_giant"))         BuildMGOEngine(root, cs);
            else if (n.Contains("engine_medium"))        BuildHFOEngine(root, cs);
            else if (n.Contains("engine_small"))         BuildCrudeEngine(root, cs);
            else if (n.Contains("turbocharger_large"))   BuildTurbo(root, cs, true);
            else if (n.Contains("turbocharger"))         BuildTurbo(root, cs, false);
            else if (n.Contains("gearbox"))              BuildGearbox(root, cs);
            else if (n.Contains("waterwheel"))           BuildWaterwheel(root, cs);
            else if (n.Contains("driveshaft"))           BuildDriveShaft(root, cs);
            else if (n.Contains("maritimegenerator"))    BuildGenerator(root, cs);
            else if (n.Contains("exhaust"))              BuildExhaustPipe(root, cs);
            else if (n.Contains("bilgepump"))            BuildBilgePump(root, cs);
            else if (n.Contains("helm"))                 BuildHelm(root, cs);
            else if (n.Contains("hull_balsa"))           BuildHull(root, cs, new Color(0.80f, 0.65f, 0.40f), 0f, 0.7f);
            else if (n.Contains("hull_iron"))            BuildHull(root, cs, new Color(0.45f, 0.47f, 0.52f), 0.85f, 0.5f, rivets: true);
            else if (n.Contains("hull_tar"))             BuildHull(root, cs, new Color(0.30f, 0.22f, 0.14f), 0f, 0.5f);
            else if (n.Contains("hull_untreated"))       BuildHull(root, cs, new Color(0.55f, 0.40f, 0.25f), 0f, 0.65f, planks: true);
            else                                          BuildHull(root, cs, new Color(0.5f, 0.5f, 0.5f), 0.5f, 0.4f);

            // Auto-attach animator if the block has animatable parts.
            if (root.GetComponent<MaritimeAnimator>() == null && n.ContainsAny(
                "propeller", "epropeller", "engine_", "turbocharger", "gearbox",
                "waterwheel", "maritimegenerator", "helm", "driveshaft"))
            {
                root.AddComponent<MaritimeAnimator>();
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  PROPELLER — hub + angled blades inside SpinPivot
        // ════════════════════════════════════════════════════════════════
        static void BuildPropeller(GameObject r, float cs, int blades, float bladeLen, Material bladeMat, bool heavy)
        {
            var spin = new GameObject("SpinPivot");
            spin.transform.SetParent(r.transform, false);
            spin.transform.localPosition = new Vector3(0, 0, cs * 0.15f);

            var hubMat = heavy ? DarkSteel : Bronze;
            var hub = Cyl(spin, hubMat, V0, cs * 0.14f, cs * 0.22f);
            hub.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Sphere(spin, hubMat, new Vector3(0, 0, cs * 0.18f), cs * 0.1f);

            float pitch = heavy ? 22f : 15f;
            for (int i = 0; i < blades; i++)
            {
                float angle = i * (360f / blades);
                // Per-blade pivot at hub center — fans blades around the hub.
                var bladePivot = new GameObject($"Blade_{i}");
                bladePivot.transform.SetParent(spin.transform, false);
                bladePivot.transform.localRotation = Quaternion.Euler(0, angle, 0);
                // Blade mesh offset from pivot so it sits at the rim.
                var blade = Box(bladePivot, bladeMat, new Vector3(bladeLen + cs * 0.1f, 0, 0),
                    new Vector3(bladeLen * 1.6f, cs * 0.05f, cs * 0.12f));
                blade.transform.localRotation = Quaternion.Euler(0, 0, pitch);
            }

            // Bossing / shaft housing behind the prop.
            Box(r, CastIron, new Vector3(0, 0, -cs * 0.25f), new Vector3(cs * 0.55f, cs * 0.55f, cs * 0.4f));
            Box(r, Steel, new Vector3(0, -cs * 0.35f, -cs * 0.2f), new Vector3(cs * 0.1f, cs * 0.3f, cs * 0.15f));
        }

        // ════════════════════════════════════════════════════════════════
        //  ELECTRIC PROPELLER — torpedo pod
        // ════════════════════════════════════════════════════════════════
        static void BuildEPropeller(GameObject r, float cs)
        {
            var spin = new GameObject("SpinPivot");
            spin.transform.SetParent(r.transform, false);
            spin.transform.localPosition = new Vector3(0, 0, cs * 0.3f);

            // Sleek pod housing.
            var pod = Cyl(r, Bronze, V0, cs * 0.32f, cs * 0.7f);
            pod.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // Armored conduit.
            Box(r, DarkSteel, new Vector3(0, 0, -cs * 0.42f), new Vector3(cs * 0.25f, cs * 0.25f, cs * 0.15f));

            var hub = Cyl(spin, Bronze, V0, cs * 0.1f, cs * 0.12f);
            hub.transform.localRotation = Quaternion.Euler(90, 0, 0);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                var bladePivot = new GameObject($"Blade_{i}");
                bladePivot.transform.SetParent(spin.transform, false);
                bladePivot.transform.localRotation = Quaternion.Euler(0, a, 0);
                var blade = Box(bladePivot, Bronze, new Vector3(cs * 0.2f, 0, 0),
                    new Vector3(cs * 0.3f, cs * 0.04f, cs * 0.08f));
                blade.transform.localRotation = Quaternion.Euler(0, 0, 12f);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  CRUDE ENGINE — small boiler + pistons
        // ════════════════════════════════════════════════════════════════
        static void BuildCrudeEngine(GameObject r, float cs)
        {
            Box(r, CastIron, new Vector3(0, -cs * 0.1f, 0), new Vector3(cs * 0.8f, cs * 0.55f, cs * 0.8f));
            var boiler = Cyl(r, Copper, new Vector3(0, cs * 0.2f, -cs * 0.15f), cs * 0.2f, cs * 0.4f);

            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1 : 1) * cs * 0.18f;
                var piston = Cyl(r, Brass, new Vector3(x, cs * 0.4f, cs * 0.18f), cs * 0.07f, cs * 0.25f);
                piston.name = $"Piston_{i}";
            }

            var crankPulley = Cyl(r, Steel, new Vector3(cs * 0.42f, -cs * 0.05f, 0), cs * 0.14f, cs * 0.05f);
            crankPulley.transform.localRotation = Quaternion.Euler(0, 0, 90);
            crankPulley.name = "CrankPulley";

            // Small exhaust stub.
            Cyl(r, DarkSteel, new Vector3(0, cs * 0.45f, -cs * 0.3f), cs * 0.06f, cs * 0.1f);
        }

        // ════════════════════════════════════════════════════════════════
        //  HEAVY FUEL OIL ENGINE — inline-4
        // ════════════════════════════════════════════════════════════════
        static void BuildHFOEngine(GameObject r, float cs)
        {
            Box(r, CastIron, V0, new Vector3(cs * 0.88f, cs * 0.5f, cs * 0.88f));
            // Oil sump.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.32f, 0), new Vector3(cs * 0.7f, cs * 0.15f, cs * 0.7f));

            for (int i = 0; i < 4; i++)
            {
                float z = (i - 1.5f) * cs * 0.2f;
                Cyl(r, DarkSteel, new Vector3(0, cs * 0.35f, z), cs * 0.07f, cs * 0.3f); // cylinder
                var piston = Cyl(r, Brass, new Vector3(0, cs * 0.48f, z), cs * 0.06f, cs * 0.08f);
                piston.name = $"Piston_{i}";
            }

            // Belt drive.
            var belt = Cyl(r, Rubber, new Vector3(cs * 0.46f, cs * 0.1f, 0), cs * 0.12f, cs * 0.06f);
            belt.transform.localRotation = Quaternion.Euler(0, 0, 90);
            belt.name = "CrankPulley";

            // Fuel rail.
            Box(r, Brass, new Vector3(0, cs * 0.45f, cs * 0.3f), new Vector3(cs * 0.06f, cs * 0.04f, cs * 0.4f));
            // Exhaust manifold.
            var mani = Cyl(r, Steel, new Vector3(0, cs * 0.5f, -cs * 0.3f), cs * 0.05f, cs * 0.5f);
            mani.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        // ════════════════════════════════════════════════════════════════
        //  MGO ENGINE — MASSIVE V12, multi-cell scale
        // ════════════════════════════════════════════════════════════════
        static void BuildMGOEngine(GameObject r, float cs)
        {
            // Scale up to fill ~2×2×2 cells so 4 turbos look proportional.
            float s = cs * 1.0f; // visual multiplier — the block occupies one cell
            // but we make it visually chunky with deep detail.

            // Massive crankcase.
            Box(r, DarkSteel, new Vector3(0, -s * 0.15f, 0), new Vector3(s * 0.96f, s * 0.5f, s * 0.96f));
            // Bedplate / base.
            Box(r, Steel, new Vector3(0, -s * 0.43f, 0), new Vector3(s * 0.98f, s * 0.08f, s * 0.98f));

            // Two banks of 6 cylinders (V12) in V formation.
            for (int bank = 0; bank < 2; bank++)
            {
                float tilt = bank == 0 ? 35f : -35f;
                float xOff = bank == 0 ? s * 0.1f : -s * 0.1f;
                for (int i = 0; i < 6; i++)
                {
                    float z = (i - 2.5f) * s * 0.14f;
                    var cyl = Cyl(r, CastIron, new Vector3(xOff, s * 0.25f, z), s * 0.06f, s * 0.35f);
                    cyl.transform.localRotation = Quaternion.Euler(0, 0, tilt);
                    // Piston cap (animated).
                    var piston = Cyl(r, Brass, new Vector3(
                        xOff + Mathf.Sin(tilt * Mathf.Deg2Rad) * s * 0.18f,
                        s * 0.45f, z), s * 0.05f, s * 0.06f);
                    piston.name = $"Piston_{bank * 6 + i}";
                    // Injector.
                    Sphere(r, GlowOrange, new Vector3(
                        xOff + Mathf.Sin(tilt * Mathf.Deg2Rad) * s * 0.2f,
                        s * 0.5f, z), s * 0.025f);
                }
            }

            // Massive common-rail fuel manifold (brass pipe running the length).
            var rail = Cyl(r, Brass, new Vector3(0, s * 0.52f, 0), s * 0.04f, s * 0.8f);
            rail.transform.localRotation = Quaternion.Euler(90, 0, 0);

            // Twin exhaust manifolds (turbo feeds).
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? 1 : -1) * s * 0.25f;
                var mani = Cyl(r, Steel, new Vector3(x, s * 0.55f, 0), s * 0.06f, s * 0.7f);
                mani.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }

            // Big crankshaft pulley (animated).
            var crank = Cyl(r, Steel, new Vector3(s * 0.48f, -s * 0.05f, s * 0.4f), s * 0.12f, s * 0.06f);
            crank.transform.localRotation = Quaternion.Euler(0, 0, 90);
            crank.name = "CrankPulley";

            // Cooling fins on the sides.
            for (int i = 0; i < 5; i++)
            {
                float z = (i - 2) * s * 0.16f;
                Box(r, Steel, new Vector3(s * 0.47f, -s * 0.1f, z), new Vector3(s * 0.03f, s * 0.25f, s * 0.1f));
                Box(r, Steel, new Vector3(-s * 0.47f, -s * 0.1f, z), new Vector3(s * 0.03f, s * 0.25f, s * 0.1f));
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  TURBOCHARGER — snail housing + spinning compressor
        // ════════════════════════════════════════════════════════════════
        static void BuildTurbo(GameObject r, float cs, bool large)
        {
            float s = large ? cs * 0.9f : cs * 0.5f;
            float housingR = large ? cs * 0.35f : cs * 0.22f;

            // Snail housing (scaled sphere).
            var housing = Sphere(r, Chrome, V0, housingR * 2f);
            housing.transform.localScale = new Vector3(housingR * 2f, housingR * 2f, housingR * 1.2f);

            // Spinning compressor wheel inside TurboSpin pivot.
            var spinPivot = new GameObject("TurboSpin");
            spinPivot.transform.SetParent(r.transform, false);
            spinPivot.transform.localPosition = V0;

            int blades = large ? 12 : 8;
            for (int i = 0; i < blades; i++)
            {
                float a = i * (360f / blades);
                // Per-blade pivot at center so blades fan out radially.
                var bladePivot = new GameObject($"CompBlade_{i}");
                bladePivot.transform.SetParent(spinPivot.transform, false);
                bladePivot.transform.localRotation = Quaternion.Euler(0, 0, a);
                // Curved compressor blade — thin rectangle offset from center.
                var blade = Box(bladePivot, Chrome, new Vector3(housingR * 0.45f, 0, 0),
                    new Vector3(housingR * 0.65f, housingR * 0.06f, housingR * 0.15f));
                blade.transform.localRotation = Quaternion.Euler(0, 35f, 0); // curve angle
            }
            // Hub.
            Cyl(spinPivot, DarkSteel, V0, housingR * 0.2f, housingR * 0.15f);

            // Glowing hot side (turbine — red when under load).
            var hot = Sphere(r, GlowRed, new Vector3(0, 0, -housingR * 0.6f), housingR * 0.5f);
            hot.transform.localScale = new Vector3(housingR, housingR, housingR * 0.6f);

            // Inlet pipe (air intake).
            var inlet = Cyl(r, Chrome, new Vector3(0, housingR * 0.8f, 0), housingR * 0.2f, housingR * 0.3f);
            // Outlet pipe (pressurized air to intake manifold).
            var outlet = Cyl(r, Chrome, new Vector3(housingR * 0.9f, 0, 0), housingR * 0.15f, housingR * 0.25f);
            outlet.transform.localRotation = Quaternion.Euler(0, 0, 90);

            // Oil feed line.
            Cyl(r, Copper, new Vector3(-housingR * 0.3f, housingR * 0.6f, 0), housingR * 0.04f, housingR * 0.4f);

            // Mounting bracket.
            Box(r, DarkSteel, new Vector3(0, -housingR * 0.8f, 0),
                new Vector3(large ? cs * 0.6f : cs * 0.4f, cs * 0.1f, large ? cs * 0.6f : cs * 0.4f));
        }

        // ════════════════════════════════════════════════════════════════
        //  GEARBOX
        // ════════════════════════════════════════════════════════════════
        static void BuildGearbox(GameObject r, float cs)
        {
            Box(r, CastIron, V0, new Vector3(cs * 0.88f, cs * 0.65f, cs * 0.88f));
            var rotor = new GameObject("GearRotor");
            rotor.transform.SetParent(r.transform, false);
            rotor.transform.localPosition = new Vector3(0, cs * 0.38f, 0);

            var gear = Cyl(rotor, Steel, V0, cs * 0.28f, cs * 0.06f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                Box(rotor, Steel, new Vector3(Mathf.Cos(a) * cs * 0.28f, 0, Mathf.Sin(a) * cs * 0.28f),
                    new Vector3(cs * 0.05f, cs * 0.06f, cs * 0.05f));
            }
            var inShaft = Cyl(r, Steel, new Vector3(0, 0, -cs * 0.46f), cs * 0.07f, cs * 0.12f);
            inShaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var outShaft = Cyl(r, Brass, new Vector3(0, 0, cs * 0.46f), cs * 0.07f, cs * 0.12f);
            outShaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        // ════════════════════════════════════════════════════════════════
        //  WATERWHEEL
        // ════════════════════════════════════════════════════════════════
        static void BuildWaterwheel(GameObject r, float cs)
        {
            var spin = new GameObject("SpinPivot");
            spin.transform.SetParent(r.transform, false);
            spin.transform.localPosition = V0;

            float wheelR = cs * 0.45f;
            var rim1 = Cyl(spin, CastIron, new Vector3(cs * 0.06f, 0, 0), wheelR, cs * 0.03f);
            rim1.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var rim2 = Cyl(spin, CastIron, new Vector3(-cs * 0.06f, 0, 0), wheelR, cs * 0.03f);
            rim2.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Cyl(spin, Steel, V0, cs * 0.1f, cs * 0.16f).transform.localRotation = Quaternion.Euler(0, 0, 90);

            int paddleCount = 10;
            for (int i = 0; i < paddleCount; i++)
            {
                float angle = i * (360f / paddleCount) * Mathf.Deg2Rad;
                var paddle = Box(spin, Oak,
                    new Vector3(0, Mathf.Sin(angle) * wheelR, Mathf.Cos(angle) * wheelR),
                    new Vector3(cs * 0.16f, cs * 0.04f, cs * 0.2f));
                paddle.transform.localRotation = Quaternion.Euler(i * (360f / paddleCount), 0, 0);
            }
            for (int i = 0; i < 6; i++)
            {
                var spoke = Box(spin, Steel, V0, new Vector3(cs * 0.14f, wheelR * 0.9f, cs * 0.02f));
                spoke.transform.localRotation = Quaternion.Euler(0, 0, i * 60f);
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  DRIVE SHAFT
        // ════════════════════════════════════════════════════════════════
        static void BuildDriveShaft(GameObject r, float cs)
        {
            // Static flanges at both ends.
            Cyl(r, Steel, new Vector3(0, 0, -cs * 0.42f), cs * 0.18f, cs * 0.05f).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cyl(r, Steel, new Vector3(0, 0, cs * 0.42f), cs * 0.18f, cs * 0.05f).transform.localRotation = Quaternion.Euler(90, 0, 0);

            // Spinning part: shaft + universal joint inside ShaftSpin pivot.
            var spin = new GameObject("ShaftSpin");
            spin.transform.SetParent(r.transform, false);
            var shaft = Cyl(spin, Chrome, V0, cs * 0.1f, cs * 0.78f);
            shaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
            // U-joint cross (visible when spinning).
            Box(spin, DarkSteel, V0, new Vector3(cs * 0.16f, cs * 0.03f, cs * 0.03f));
            Box(spin, DarkSteel, V0, new Vector3(cs * 0.03f, cs * 0.03f, cs * 0.16f));
            Sphere(spin, DarkSteel, V0, cs * 0.06f);
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATOR
        // ════════════════════════════════════════════════════════════════
        static void BuildGenerator(GameObject r, float cs)
        {
            Box(r, DarkSteel, new Vector3(0, -cs * 0.1f, 0), new Vector3(cs * 0.88f, cs * 0.55f, cs * 0.88f));
            var rotor = new GameObject("GenRotor");
            rotor.transform.SetParent(r.transform, false);
            rotor.transform.localPosition = new Vector3(0, cs * 0.28f, 0);

            for (int i = 0; i < 3; i++)
            {
                float z = (i - 1) * cs * 0.2f;
                Cyl(rotor, Copper, new Vector3(0, 0, z), cs * 0.1f, cs * 0.22f);
            }
            Box(r, Brass, new Vector3(cs * 0.28f, cs * 0.35f, cs * 0.28f), new Vector3(cs * 0.07f, cs * 0.14f, cs * 0.07f));
            Box(r, Brass, new Vector3(-cs * 0.28f, cs * 0.35f, cs * 0.28f), new Vector3(cs * 0.07f, cs * 0.14f, cs * 0.07f));
            var shaft = Cyl(r, Steel, new Vector3(0, 0, -cs * 0.46f), cs * 0.07f, cs * 0.1f);
            shaft.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Box(r, Glow, new Vector3(0, cs * 0.47f, -cs * 0.3f), new Vector3(cs * 0.25f, cs * 0.03f, cs * 0.03f));
        }

        // ════════════════════════════════════════════════════════════════
        //  EXHAUST PIPE
        // ════════════════════════════════════════════════════════════════
        static void BuildExhaustPipe(GameObject r, float cs)
        {
            Cyl(r, CastIron, new Vector3(0, cs * 0.1f, 0), cs * 0.13f, cs * 0.65f);
            Cyl(r, Steel, new Vector3(0, -cs * 0.28f, 0), cs * 0.22f, cs * 0.05f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                float y = cs * 0.1f + (i % 2) * cs * 0.12f;
                Box(r, Rubber, new Vector3(Mathf.Cos(a) * cs * 0.12f, y, Mathf.Sin(a) * cs * 0.12f),
                    new Vector3(cs * 0.03f, cs * 0.05f, cs * 0.03f));
            }
            Cyl(r, DarkSteel, new Vector3(0, cs * 0.4f, 0), cs * 0.14f, cs * 0.03f);
        }

        // ════════════════════════════════════════════════════════════════
        //  BILGE PUMP
        // ════════════════════════════════════════════════════════════════
        static void BuildBilgePump(GameObject r, float cs)
        {
            Box(r, DarkSteel, new Vector3(0, -cs * 0.15f, 0), new Vector3(cs * 0.82f, cs * 0.45f, cs * 0.82f));
            Cyl(r, CastIron, new Vector3(0, cs * 0.25f, 0), cs * 0.16f, cs * 0.28f);
            for (int i = 0; i < 4; i++)
            {
                var fin = Box(r, Steel, new Vector3(0, cs * 0.25f, 0), new Vector3(cs * 0.38f, cs * 0.03f, cs * 0.03f));
                fin.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
            }
            var outlet = Cyl(r, Copper, new Vector3(cs * 0.34f, cs * 0.1f, 0), cs * 0.07f, cs * 0.18f);
            outlet.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Sphere(r, Glow, new Vector3(0, cs * 0.4f, 0), cs * 0.04f);
        }

        // ════════════════════════════════════════════════════════════════
        //  HELM — ship's wheel
        // ════════════════════════════════════════════════════════════════
        static void BuildHelm(GameObject r, float cs)
        {
            float wheelR = cs * 0.38f;
            var wheelPivot = new GameObject("HelmWheel");
            wheelPivot.transform.SetParent(r.transform, false);
            wheelPivot.transform.localPosition = new Vector3(0, cs * 0.28f, 0);
            wheelPivot.transform.localRotation = Quaternion.Euler(0, 0, 90); // wheel faces sideways

            // Outer ring (using a thin cylinder as torus approximation).
            var ring = Cyl(wheelPivot, DarkOak, V0, wheelR, cs * 0.04f);
            ring.transform.localScale = new Vector3(wheelR * 2f, cs * 0.08f, wheelR * 2f * 0.2f);
            // Inner ring.
            Cyl(wheelPivot, Oak, V0, wheelR * 0.65f, cs * 0.03f).transform.localScale =
                new Vector3(wheelR * 1.3f, cs * 0.06f, wheelR * 1.3f * 0.2f);
            // Hub.
            Cyl(wheelPivot, Brass, V0, cs * 0.06f, cs * 0.1f);
            // 8 spokes.
            for (int i = 0; i < 8; i++)
            {
                var spoke = Box(wheelPivot, Oak, V0, new Vector3(wheelR * 1.7f, cs * 0.025f, cs * 0.025f));
                spoke.transform.localRotation = Quaternion.Euler(0, 0, i * 45f);
                // Handle knob.
                float a = i * 45f * Mathf.Deg2Rad;
                Sphere(wheelPivot, Brass, new Vector3(Mathf.Cos(a) * wheelR, Mathf.Sin(a) * wheelR, 0), cs * 0.035f);
            }

            // Pedestal.
            Cyl(r, DarkOak, new Vector3(0, -cs * 0.15f, 0), cs * 0.08f, cs * 0.45f);
            Box(r, DarkOak, new Vector3(0, -cs * 0.4f, 0), new Vector3(cs * 0.35f, cs * 0.08f, cs * 0.35f));
            // Compass binnacle.
            Box(r, Brass, new Vector3(0, cs * 0.28f, cs * 0.22f), new Vector3(cs * 0.1f, cs * 0.09f, cs * 0.07f));
        }

        // ════════════════════════════════════════════════════════════════
        //  HULL BLOCKS
        // ════════════════════════════════════════════════════════════════
        static void BuildHull(GameObject r, float cs, Color color, float metallic, float smooth,
            bool planks = false, bool rivets = false)
        {
            var mat = MatC(color, metallic, smooth);
            Box(r, mat, V0, new Vector3(cs * 0.98f, cs * 0.98f, cs * 0.98f));
            if (planks)
            {
                var dark = MatC(color * 0.7f, metallic, smooth);
                for (int i = 0; i < 4; i++)
                {
                    float y = (i - 1.5f) * cs * 0.24f;
                    Box(r, dark, new Vector3(0, y, cs * 0.49f), new Vector3(cs * 0.98f, cs * 0.02f, cs * 0.01f));
                }
            }
            if (rivets)
            {
                var rivetMat = MatC(new Color(0.6f, 0.62f, 0.65f), 0.9f, 0.6f);
                float[][] corners = {
                    new[] { cs * 0.35f, cs * 0.35f }, new[] { -cs * 0.35f, cs * 0.35f },
                    new[] { cs * 0.35f, -cs * 0.35f }, new[] { -cs * 0.35f, -cs * 0.35f },
                };
                foreach (var c in corners)
                {
                    Sphere(r, rivetMat, new Vector3(c[0], c[1], cs * 0.495f), cs * 0.04f);
                    Sphere(r, rivetMat, new Vector3(c[0], c[1], -cs * 0.495f), cs * 0.04f);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  PRIMITIVE HELPERS
        // ════════════════════════════════════════════════════════════════
        static GameObject Box(GameObject p, Material m, Vector3 pos, Vector3 scale)
            => Prim(p, PrimitiveType.Cube, m, pos, scale);
        static GameObject Sphere(GameObject p, Material m, Vector3 pos, float d)
            => Prim(p, PrimitiveType.Sphere, m, pos, new Vector3(d, d, d));
        static GameObject Cyl(GameObject p, Material m, Vector3 pos, float radius, float height)
            => Prim(p, PrimitiveType.Cylinder, m, pos, new Vector3(radius * 2f, height * 0.5f, radius * 2f));

        static GameObject Prim(GameObject parent, PrimitiveType type, Material m, Vector3 pos, Vector3 scale)
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
    }

    /// <summary>String extension for ContainsAny.</summary>
    internal static class StringExt
    {
        public static bool ContainsAny(this string s, params string[] needles)
        {
            foreach (var n in needles) if (s.Contains(n)) return true;
            return false;
        }
    }
}
