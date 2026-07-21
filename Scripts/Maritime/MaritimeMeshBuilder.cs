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
        public const int Version = 15;
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
        // I/O port materials (distinct colors for connection cubes).
        static Material PortFuel   => MatC(new Color(0.15f, 0.45f, 0.95f), 0.3f, 0.6f, e: new Color(0.05f, 0.2f, 0.5f)); // blue = fuel/item input
        static Material PortExhaust=> MatC(new Color(0.85f, 0.15f, 0.1f), 0.3f, 0.6f, e: new Color(0.5f, 0.05f, 0.03f)); // red = exhaust output
        static Material PortShaft  => MatC(new Color(0.9f, 0.75f, 0.2f), 0.6f, 0.5f, e: new Color(0.3f, 0.2f, 0.02f));  // gold = shaft output
        static Material PortCoolant=> MatC(new Color(0.20f, 0.85f, 0.75f), 0.3f, 0.6f, e: new Color(0.05f, 0.4f, 0.35f)); // teal = coolant input
        static Material PortTurbo  => MatC(new Color(0.10f, 0.85f, 1.00f), 0.25f, 0.85f, e: new Color(0.02f, 0.35f, 0.50f)); // cyan = turbo attachment

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
            else if (n.Contains("rotationtransfer"))     BuildRotationTransfer(root, cs);
            else if (n.Contains("encasedchaindrive"))    BuildEncasedChainDrive(root, cs);
            else if (n.Contains("shippingcontainer"))    BuildShippingContainer(root, cs);
            else if (n.Contains("gearbox"))              BuildGearbox(root, cs);
            else if (n.Contains("waterwheel"))           BuildWaterwheel(root, cs);
            else if (n.Contains("driveshaft"))           BuildDriveShaft(root, cs);
            else if (n.Contains("maritimegenerator"))    BuildGenerator(root, cs);
            else if (n.Contains("exhaust"))              BuildExhaustPipe(root, cs);
            else if (n.Contains("marinewaterpump"))  BuildMarineWaterPump(root, cs);
            else if (n.Contains("bilgepump"))            BuildBilgePump(root, cs);
            else if (n.Contains("shipconsole"))           BuildShipConsole(root, cs);
            else if (n.Contains("helm"))                 BuildHelm(root, cs);
            else if (n.Contains("hull_balsa"))           BuildHull(root, cs, new Color(0.80f, 0.65f, 0.40f), 0f, 0.7f);
            else if (n.Contains("hull_iron"))            BuildHull(root, cs, new Color(0.45f, 0.47f, 0.52f), 0.85f, 0.5f, rivets: true);
            else if (n.Contains("hull_tar"))             BuildHull(root, cs, new Color(0.30f, 0.22f, 0.14f), 0f, 0.5f);
            else if (n.Contains("hull_untreated"))       BuildHull(root, cs, new Color(0.55f, 0.40f, 0.25f), 0f, 0.65f, planks: true);
            else                                          BuildHull(root, cs, new Color(0.5f, 0.5f, 0.5f), 0.5f, 0.4f);

            // Auto-attach animator if the block has animatable parts.
            if (root.GetComponent<MaritimeAnimator>() == null && n.ContainsAny(
                "propeller", "epropeller", "engine_", "turbocharger", "gearbox",
                "waterwheel", "maritimegenerator", "helm", "driveshaft",
                "rotationtransfer", "encasedchaindrive"))
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
            // Shaft input port (gold — where rotation comes in).
            Port(r, "Port_ShaftInput", PortShaft, new Vector3(0, 0, -cs * 0.42f), new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.04f));
            Port(r, "Rotation input point 0", PortShaft, new Vector3(0, 0, -cs * 0.50f), new Vector3(cs * 0.16f, cs * 0.16f, cs * 0.06f));
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
        //  CRUDE ENGINE — 1×1×1 starter engine, heavy cast-iron single block
        // ════════════════════════════════════════════════════════════════
        static void BuildCrudeEngine(GameObject r, float cs)
        {
            float w = cs * 0.92f;
            float h = cs * 0.92f;
            float l = cs * 0.92f;

            // Dense cast base.
            Box(r, CastIron, new Vector3(0, -h * 0.20f, 0), new Vector3(w, h * 0.58f, l));
            Box(r, DarkSteel, new Vector3(0, -h * 0.42f, 0), new Vector3(w * 1.02f, h * 0.12f, l * 1.02f));

            // Boiler + cylinder crown.
            var boiler = Cyl(r, Copper, new Vector3(0, h * 0.10f, -l * 0.10f), w * 0.20f, l * 0.42f);
            boiler.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, Brass, new Vector3(0, h * 0.32f, l * 0.04f), new Vector3(w * 0.42f, h * 0.16f, l * 0.30f));

            // Two exposed piston rods.
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1f : 1f) * w * 0.24f;
                var rod = Cyl(r, Brass, new Vector3(x, h * 0.36f, l * 0.18f), w * 0.07f, h * 0.28f);
                rod.name = $"Piston_{i}";
                Box(r, Steel, new Vector3(x, h * 0.22f, l * 0.28f), new Vector3(w * 0.10f, h * 0.10f, l * 0.10f));
            }

            // Flywheel + crank at the actual front power take-off.
            var crankPulley = Cyl(r, Steel, new Vector3(0, -h * 0.05f, l * 0.26f), w * 0.10f, l * 0.18f);
            crankPulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            crankPulley.name = "CrankPulley";
            var flywheel = Cyl(r, DarkSteel, new Vector3(0, -h * 0.02f, l * 0.48f), w * 0.24f, w * 0.06f);
            flywheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var flywheelRing = Cyl(r, Steel, new Vector3(0, -h * 0.02f, l * 0.48f), w * 0.30f, w * 0.03f);
            flywheelRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, Steel, new Vector3(0, -h * 0.02f, l * 0.48f), new Vector3(w * 0.42f, cs * 0.03f, cs * 0.05f));
            Box(r, Steel, new Vector3(0, -h * 0.02f, l * 0.48f), new Vector3(cs * 0.05f, w * 0.42f, cs * 0.03f));
            Box(r, DarkSteel, new Vector3(0, -h * 0.06f, l * 0.36f), new Vector3(w * 0.20f, h * 0.12f, l * 0.18f));

            // Fuel hatch + chimney.
            Box(r, DarkSteel, new Vector3(0, h * 0.04f, -l * 0.38f), new Vector3(w * 0.42f, h * 0.18f, l * 0.10f));
            var stack = Cyl(r, CastIron, new Vector3(0, h * 0.44f, -l * 0.24f), w * 0.08f, h * 0.30f);
            stack.transform.localRotation = Quaternion.identity;
            Box(r, Steel, new Vector3(0, h * 0.58f, -l * 0.24f), new Vector3(w * 0.20f, h * 0.04f, w * 0.20f));

            // ── I/O Ports ─────────────────────────────────────────────
            Port(r, "Port_FuelInput", PortFuel, new Vector3(-w * 0.44f, h * 0.02f, -l * 0.26f), new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.05f));
            Port(r, "Port_ExhaustOutput", PortExhaust, new Vector3(0, h * 0.68f, -l * 0.24f), new Vector3(cs * 0.11f, cs * 0.05f, cs * 0.11f));
            Port(r, "Port_ShaftOutput", PortShaft, new Vector3(0, -h * 0.02f, l * 0.60f), new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.10f), PrimitiveType.Cylinder);
            TurboAttachment(r, 0, cs, Vector3Int.right);
        }

        // ════════════════════════════════════════════════════════════════
        //  HEAVY FUEL OIL ENGINE — 4×3×2 large-grid industrial ship engine
        // ════════════════════════════════════════════════════════════════
        static void BuildHFOEngine(GameObject r, float cs)
        {
            float width = cs * 2.0f;
            float height = cs * 3.0f;
            float length = cs * 4.0f;
            float halfL = length * 0.5f;

            // Massive bedplate, sump, and side skirts.
            Box(r, DarkSteel, new Vector3(0, -height * 0.42f, 0), new Vector3(width * 1.02f, height * 0.18f, length * 1.02f));
            Box(r, CastIron, new Vector3(0, -height * 0.20f, 0), new Vector3(width * 0.94f, height * 0.36f, length * 0.92f));
            Box(r, DarkSteel, new Vector3(width * 0.48f, -height * 0.10f, 0), new Vector3(width * 0.08f, height * 0.26f, length * 0.94f));
            Box(r, DarkSteel, new Vector3(-width * 0.48f, -height * 0.10f, 0), new Vector3(width * 0.08f, height * 0.26f, length * 0.94f));

            // Long cylinder banks and access galleries.
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(-halfL * 0.72f, halfL * 0.72f, i / 5f);
                Box(r, CastIron, new Vector3(0, height * 0.08f, z), new Vector3(width * 0.70f, height * 0.20f, cs * 0.30f));
                var piston = Cyl(r, Brass, new Vector3(0, height * 0.34f, z), width * 0.055f, height * 0.16f);
                piston.name = $"Piston_{i}";
                Box(r, Steel, new Vector3(0, height * 0.47f, z), new Vector3(width * 0.16f, height * 0.05f, cs * 0.18f));
                Box(r, Steel, new Vector3(width * 0.28f, height * 0.22f, z), new Vector3(width * 0.10f, height * 0.36f, cs * 0.12f));
                Box(r, Steel, new Vector3(-width * 0.28f, height * 0.22f, z), new Vector3(width * 0.10f, height * 0.36f, cs * 0.12f));
            }

            // Top covers + catwalks.
            Box(r, Steel, new Vector3(0, height * 0.56f, 0), new Vector3(width * 0.76f, height * 0.10f, length * 0.86f));
            Box(r, DarkSteel, new Vector3(0, height * 0.84f, 0), new Vector3(width * 0.64f, height * 0.14f, length * 0.72f));
            Box(r, DarkSteel, new Vector3(width * 0.42f, height * 0.74f, 0), new Vector3(width * 0.12f, height * 0.06f, length * 0.84f));
            Box(r, DarkSteel, new Vector3(-width * 0.42f, height * 0.74f, 0), new Vector3(width * 0.12f, height * 0.06f, length * 0.84f));
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-halfL * 0.68f, halfL * 0.68f, i / 4f);
                Box(r, Brass, new Vector3(0, height * 0.74f, z), new Vector3(width * 0.44f, height * 0.03f, cs * 0.08f));
                Box(r, Steel, new Vector3(width * 0.46f, height * 0.96f, z), new Vector3(width * 0.03f, height * 0.10f, cs * 0.03f));
                Box(r, Steel, new Vector3(-width * 0.46f, height * 0.96f, z), new Vector3(width * 0.03f, height * 0.10f, cs * 0.03f));
            }
            Box(r, Steel, new Vector3(width * 0.46f, height * 1.02f, 0), new Vector3(width * 0.03f, height * 0.016f, length * 0.70f));
            Box(r, Steel, new Vector3(-width * 0.46f, height * 1.02f, 0), new Vector3(width * 0.03f, height * 0.016f, length * 0.70f));

            // Fuel rail and coolant gallery.
            var fuelRail = Cyl(r, Brass, new Vector3(width * 0.18f, height * 0.48f, 0), width * 0.040f, length * 0.86f);
            fuelRail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var coolantRail = Cyl(r, PortCoolant, new Vector3(-width * 0.22f, height * 0.44f, 0), width * 0.035f, length * 0.82f);
            coolantRail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Exhaust manifolds.
            var maniA = Cyl(r, Steel, new Vector3(width * 0.20f, height * 0.86f, 0), width * 0.055f, length * 0.82f);
            maniA.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var maniB = Cyl(r, Steel, new Vector3(-width * 0.20f, height * 0.86f, 0), width * 0.055f, length * 0.82f);
            maniB.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, Steel, new Vector3(0, height * 0.92f, -halfL * 0.18f), new Vector3(width * 0.66f, height * 0.08f, cs * 0.22f));

            // Front timing case + giant flywheel at the actual output face.
            Box(r, CastIron, new Vector3(0, 0, halfL * 0.44f), new Vector3(width * 0.84f, height * 0.48f, cs * 0.36f));
            var crankPulley = Cyl(r, Steel, new Vector3(0, -height * 0.02f, halfL * 0.26f), width * 0.11f, cs * 0.26f);
            crankPulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            crankPulley.name = "CrankPulley";
            var flywheel = Cyl(r, DarkSteel, new Vector3(0, -height * 0.02f, halfL * 0.52f), width * 0.24f, width * 0.08f);
            flywheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var flywheelRing = Cyl(r, Steel, new Vector3(0, -height * 0.02f, halfL * 0.52f), width * 0.30f, width * 0.04f);
            flywheelRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, Steel, new Vector3(0, -height * 0.02f, halfL * 0.52f), new Vector3(width * 0.52f, cs * 0.04f, cs * 0.07f));
            Box(r, Steel, new Vector3(0, -height * 0.02f, halfL * 0.52f), new Vector3(cs * 0.07f, width * 0.52f, cs * 0.04f));
            Box(r, DarkSteel, new Vector3(0, -height * 0.04f, halfL * 0.36f), new Vector3(width * 0.18f, height * 0.14f, cs * 0.24f));

            // ── I/O Ports ─────────────────────────────────────────────
            Port(r, "Port_FuelInput", PortFuel, new Vector3(width * 0.56f, -height * 0.02f, -halfL * 0.42f), new Vector3(cs * 0.16f, cs * 0.16f, cs * 0.05f));
            Port(r, "Port_ExhaustOutput", PortExhaust, new Vector3(0, height * 1.06f, -halfL * 0.24f), new Vector3(cs * 0.15f, cs * 0.05f, cs * 0.15f));
            Port(r, "Port_ShaftOutput", PortShaft, new Vector3(0, -height * 0.02f, halfL * 0.66f), new Vector3(cs * 0.18f, cs * 0.18f, cs * 0.12f), PrimitiveType.Cylinder);
            Port(r, "Port_CoolantInput", PortCoolant, new Vector3(-width * 0.58f, -height * 0.08f, -halfL * 0.34f), new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.05f));
            TurboAttachment(r, 0, cs, Vector3Int.right);
            TurboAttachment(r, 1, cs, Vector3Int.left);
        }

        // ════════════════════════════════════════════════════════════════
        //  MGO ENGINE — 6×5×3 large-grid colossal ship diesel
        // ════════════════════════════════════════════════════════════════
        static void BuildMGOEngine(GameObject r, float cs)
        {
            float width = cs * 3.0f;
            float height = cs * 5.0f;
            float length = cs * 6.0f;
            float halfL = length * 0.5f;

            // Huge bedplate and lower crankcase.
            Box(r, DarkSteel, new Vector3(0, -height * 0.44f, 0), new Vector3(width * 1.04f, height * 0.16f, length * 1.03f));
            Box(r, CastIron, new Vector3(0, -height * 0.26f, 0), new Vector3(width * 0.96f, height * 0.24f, length * 0.94f));
            Box(r, DarkSteel, new Vector3(0, -height * 0.06f, 0), new Vector3(width * 0.90f, height * 0.18f, length * 0.88f));
            Box(r, Steel, new Vector3(width * 0.48f, 0, 0), new Vector3(width * 0.08f, height * 0.44f, length * 0.90f));
            Box(r, Steel, new Vector3(-width * 0.48f, 0, 0), new Vector3(width * 0.08f, height * 0.44f, length * 0.90f));

            // Twin V banks with 12 visible power units.
            for (int bank = 0; bank < 2; bank++)
            {
                float sign = bank == 0 ? 1f : -1f;
                float xBase = sign * width * 0.18f;
                float bankTilt = sign > 0f ? 32f : -32f;
                for (int i = 0; i < 6; i++)
                {
                    float z = Mathf.Lerp(-halfL * 0.72f, halfL * 0.72f, i / 5f);
                    var liner = Cyl(r, CastIron, new Vector3(xBase, height * 0.18f, z), width * 0.050f, height * 0.34f);
                    liner.transform.localRotation = Quaternion.Euler(0f, 0f, bankTilt);
                    var head = Box(r, Steel, new Vector3(xBase + sign * width * 0.06f, height * 0.40f, z), new Vector3(width * 0.18f, height * 0.08f, cs * 0.24f));
                    head.transform.localRotation = Quaternion.Euler(0f, 0f, bankTilt);
                    var piston = Cyl(r, Brass, new Vector3(xBase + sign * width * 0.11f, height * 0.52f, z), width * 0.038f, height * 0.09f);
                    piston.name = $"Piston_{bank * 6 + i}";
                    Box(r, Brass, new Vector3(xBase + sign * width * 0.14f, height * 0.62f, z), new Vector3(width * 0.10f, height * 0.03f, cs * 0.12f));
                    Sphere(r, GlowOrange, new Vector3(xBase + sign * width * 0.16f, height * 0.68f, z), cs * 0.08f);
                }
            }

            // Upper covers, scavenging deck, catwalks, ladders, and side galleries.
            Box(r, Steel, new Vector3(0, height * 0.74f, 0), new Vector3(width * 0.82f, height * 0.10f, length * 0.90f));
            Box(r, DarkSteel, new Vector3(0, height * 1.10f, 0), new Vector3(width * 0.74f, height * 0.22f, length * 0.72f));
            Box(r, Steel, new Vector3(0, height * 1.34f, 0), new Vector3(width * 0.90f, height * 0.08f, length * 0.86f));
            Box(r, DarkSteel, new Vector3(width * 0.54f, height * 0.98f, 0), new Vector3(width * 0.10f, height * 0.06f, length * 0.88f));
            Box(r, DarkSteel, new Vector3(-width * 0.54f, height * 0.98f, 0), new Vector3(width * 0.10f, height * 0.06f, length * 0.88f));
            for (int i = 0; i < 7; i++)
            {
                float z = Mathf.Lerp(-halfL * 0.76f, halfL * 0.76f, i / 6f);
                Box(r, Brass, new Vector3(0, height * 0.98f, z), new Vector3(width * 0.62f, height * 0.025f, cs * 0.08f));
                Box(r, Steel, new Vector3(width * 0.62f, height * 0.40f, z), new Vector3(width * 0.03f, height * 0.72f, cs * 0.04f));
                Box(r, Steel, new Vector3(-width * 0.62f, height * 0.40f, z), new Vector3(width * 0.03f, height * 0.72f, cs * 0.04f));
            }
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(-halfL * 0.34f, halfL * 0.34f, i / 5f);
                Box(r, Steel, new Vector3(width * 0.50f, height * 1.48f, z), new Vector3(width * 0.025f, height * 0.12f, cs * 0.03f));
                Box(r, Steel, new Vector3(-width * 0.50f, height * 1.48f, z), new Vector3(width * 0.025f, height * 0.12f, cs * 0.03f));
            }
            Box(r, Steel, new Vector3(width * 0.50f, height * 1.56f, 0), new Vector3(width * 0.03f, height * 0.018f, length * 0.72f));
            Box(r, Steel, new Vector3(-width * 0.50f, height * 1.56f, 0), new Vector3(width * 0.03f, height * 0.018f, length * 0.72f));
            Box(r, Steel, new Vector3(width * 0.44f, height * 1.42f, 0), new Vector3(width * 0.03f, height * 0.018f, length * 0.72f));
            Box(r, Steel, new Vector3(-width * 0.44f, height * 1.42f, 0), new Vector3(width * 0.03f, height * 0.018f, length * 0.72f));

            // Massive fuel, coolant, and lube manifolds.
            var fuelRail = Cyl(r, Brass, new Vector3(0, height * 0.86f, 0), width * 0.035f, length * 0.92f);
            fuelRail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var coolantA = Cyl(r, PortCoolant, new Vector3(-width * 0.26f, height * 0.58f, 0), width * 0.030f, length * 0.90f);
            coolantA.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var coolantB = Cyl(r, PortCoolant, new Vector3(width * 0.26f, height * 0.58f, 0), width * 0.030f, length * 0.90f);
            coolantB.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var lubeGallery = Cyl(r, Copper, new Vector3(0, height * 0.22f, 0), width * 0.026f, length * 0.92f);
            lubeGallery.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Twin exhaust trunks and surge chamber.
            var maniL = Cyl(r, Steel, new Vector3(-width * 0.30f, height * 1.12f, 0), width * 0.055f, length * 0.88f);
            maniL.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var maniR = Cyl(r, Steel, new Vector3(width * 0.30f, height * 1.12f, 0), width * 0.055f, length * 0.88f);
            maniR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, DarkSteel, new Vector3(0, height * 1.20f, -halfL * 0.10f), new Vector3(width * 0.74f, height * 0.10f, cs * 0.34f));

            // Front timing house, massive centered flywheel, and PTO housing.
            Box(r, CastIron, new Vector3(0, height * 0.02f, halfL * 0.42f), new Vector3(width * 0.86f, height * 0.46f, cs * 0.52f));
            var crank = Cyl(r, Steel, new Vector3(0, height * 0.04f, halfL * 0.22f), width * 0.12f, cs * 0.34f);
            crank.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            crank.name = "CrankPulley";
            var flywheel = Cyl(r, DarkSteel, new Vector3(0, height * 0.04f, halfL * 0.58f), width * 0.28f, width * 0.10f);
            flywheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var flywheelRing = Cyl(r, Steel, new Vector3(0, height * 0.04f, halfL * 0.58f), width * 0.34f, width * 0.04f);
            flywheelRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, Steel, new Vector3(0, height * 0.04f, halfL * 0.58f), new Vector3(width * 0.60f, cs * 0.05f, cs * 0.08f));
            Box(r, Steel, new Vector3(0, height * 0.04f, halfL * 0.58f), new Vector3(cs * 0.08f, width * 0.60f, cs * 0.05f));
            Box(r, Steel, new Vector3(0, -height * 0.02f, halfL * 0.54f), new Vector3(width * 0.34f, height * 0.18f, cs * 0.16f));
            Box(r, DarkSteel, new Vector3(0, height * 0.42f, halfL * 0.56f), new Vector3(width * 0.30f, height * 0.18f, cs * 0.10f));
            Box(r, Steel, new Vector3(0, height * 0.04f, halfL * 0.42f), new Vector3(width * 0.14f, height * 0.10f, cs * 0.30f));

            // Turbo pads / service deck hints so separate turbo prefabs read naturally when mounted.
            Box(r, DarkSteel, new Vector3(width * 0.54f, height * 0.78f, -halfL * 0.18f), new Vector3(width * 0.16f, height * 0.12f, cs * 0.36f));
            Box(r, DarkSteel, new Vector3(-width * 0.54f, height * 0.78f, -halfL * 0.18f), new Vector3(width * 0.16f, height * 0.12f, cs * 0.36f));
            Box(r, DarkSteel, new Vector3(0, height * 1.34f, 0), new Vector3(width * 0.22f, height * 0.10f, cs * 0.48f));
            Box(r, DarkSteel, new Vector3(0, height * 0.66f, -halfL * 0.56f), new Vector3(width * 0.26f, height * 0.18f, cs * 0.18f));

            // ── I/O Ports ─────────────────────────────────────────────
            Port(r, "Port_FuelInput", PortFuel, new Vector3(width * 0.66f, -height * 0.02f, -halfL * 0.44f), new Vector3(cs * 0.18f, cs * 0.18f, cs * 0.06f));
            Port(r, "Port_ExhaustOutput_L", PortExhaust, new Vector3(width * 0.26f, height * 1.42f, -halfL * 0.18f), new Vector3(cs * 0.18f, cs * 0.06f, cs * 0.18f));
            Port(r, "Port_ExhaustOutput_R", PortExhaust, new Vector3(-width * 0.26f, height * 1.42f, -halfL * 0.18f), new Vector3(cs * 0.18f, cs * 0.06f, cs * 0.18f));
            Port(r, "Port_ShaftOutput", PortShaft, new Vector3(0, height * 0.04f, halfL * 0.72f), new Vector3(cs * 0.22f, cs * 0.22f, cs * 0.14f), PrimitiveType.Cylinder);
            Port(r, "Port_CoolantInput", PortCoolant, new Vector3(-width * 0.70f, -height * 0.04f, -halfL * 0.34f), new Vector3(cs * 0.16f, cs * 0.16f, cs * 0.06f));
            TurboAttachment(r, 0, cs, Vector3Int.right);
            TurboAttachment(r, 1, cs, Vector3Int.left);
            TurboAttachment(r, 2, cs, Vector3Int.up);
            TurboAttachment(r, 3, cs, new Vector3Int(0, 0, -1));
        }

        // ════════════════════════════════════════════════════════════════
        //  TURBOCHARGER — ship turbo, small and large industrial variants
        // ════════════════════════════════════════════════════════════════
        static void BuildTurbo(GameObject r, float cs, bool large)
        {
            float body = large ? cs * 1.45f : cs * 0.82f;
            float housingR = large ? cs * 0.42f : cs * 0.24f;
            float vertical = large ? cs * 0.95f : cs * 0.52f;

            // Base skid + mounting frame.
            Box(r, DarkSteel, new Vector3(0, -vertical * 0.42f, 0), new Vector3(body * 1.12f, cs * 0.10f, body * 0.82f));
            Box(r, Steel, new Vector3(-body * 0.22f, -vertical * 0.16f, 0), new Vector3(body * 0.16f, vertical * 0.42f, body * 0.20f));
            Box(r, Steel, new Vector3(body * 0.22f, -vertical * 0.16f, 0), new Vector3(body * 0.16f, vertical * 0.42f, body * 0.20f));

            // Compressor housing.
            var housing = Sphere(r, Chrome, new Vector3(0, 0, 0), housingR * 2f);
            housing.transform.localScale = new Vector3(housingR * 2.1f, housingR * 1.8f, housingR * 1.45f);
            Box(r, Steel, new Vector3(0, 0, -housingR * 0.18f), new Vector3(body * 0.22f, housingR * 1.2f, housingR * 0.55f));

            // Spinning compressor wheel.
            var spinPivot = new GameObject("TurboSpin");
            spinPivot.transform.SetParent(r.transform, false);
            spinPivot.transform.localPosition = new Vector3(housingR * 0.04f, 0, housingR * 0.08f);
            int blades = large ? 12 : 9;
            for (int i = 0; i < blades; i++)
            {
                float a = i * (360f / blades);
                var bladePivot = new GameObject($"CompBlade_{i}");
                bladePivot.transform.SetParent(spinPivot.transform, false);
                bladePivot.transform.localRotation = Quaternion.Euler(0f, 0f, a);
                var blade = Box(bladePivot, Chrome, new Vector3(housingR * 0.44f, 0, 0),
                    new Vector3(housingR * 0.65f, housingR * 0.06f, housingR * 0.14f));
                blade.transform.localRotation = Quaternion.Euler(0f, 38f, 0f);
            }
            Cyl(spinPivot, DarkSteel, V0, housingR * 0.18f, housingR * 0.16f);

            // Turbine / hot side.
            var hot = Sphere(r, GlowRed, new Vector3(0, 0, -housingR * 0.86f), housingR * 0.78f);
            hot.transform.localScale = new Vector3(housingR * 1.35f, housingR * 1.10f, housingR * 0.95f);
            Box(r, GlowRed, new Vector3(0, housingR * 0.14f, -housingR * 1.22f), new Vector3(housingR * 0.52f, housingR * 0.16f, housingR * 0.28f));

            // Inlet + compressor outlet.
            var inlet = Cyl(r, Chrome, new Vector3(0, housingR * 0.92f, housingR * 0.10f), housingR * 0.22f, housingR * 0.34f);
            inlet.transform.localRotation = Quaternion.identity;
            var outlet = Cyl(r, Chrome, new Vector3(housingR * 1.08f, housingR * 0.08f, housingR * 0.08f), housingR * 0.18f, housingR * 0.30f);
            outlet.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var exhaust = Cyl(r, CastIron, new Vector3(-housingR * 1.02f, -housingR * 0.02f, -housingR * 0.68f), housingR * 0.20f, housingR * 0.28f);
            exhaust.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Service lines.
            Cyl(r, Copper, new Vector3(-housingR * 0.42f, housingR * 0.64f, 0), housingR * 0.04f, housingR * 0.44f);
            Cyl(r, Copper, new Vector3(housingR * 0.36f, -housingR * 0.42f, 0), housingR * 0.04f, housingR * 0.34f).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Large turbo gets a surrounding frame and charge-air plenum.
            if (large)
            {
                Box(r, DarkSteel, new Vector3(0, housingR * 1.26f, 0), new Vector3(body * 0.84f, cs * 0.08f, body * 0.48f));
                Box(r, Steel, new Vector3(-body * 0.36f, housingR * 0.62f, 0), new Vector3(cs * 0.08f, vertical * 0.86f, cs * 0.08f));
                Box(r, Steel, new Vector3(body * 0.36f, housingR * 0.62f, 0), new Vector3(cs * 0.08f, vertical * 0.86f, cs * 0.08f));
                Box(r, GlowOrange, new Vector3(0, -housingR * 0.58f, housingR * 0.18f), new Vector3(body * 0.44f, cs * 0.06f, cs * 0.20f));
            }

            Port(r, "Port_BoostOutput", PortShaft, new Vector3(0, -vertical * 0.34f, housingR * 0.78f),
                new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.04f));
        }

        // ════════════════════════════════════════════════════════════════
        //  ROTATION TRANSFER — straight/up/down visual transfer casing
        // ════════════════════════════════════════════════════════════════
        static void BuildRotationTransfer(GameObject r, float cs)
        {
            Box(r, DarkSteel, V0, new Vector3(cs * 0.86f, cs * 0.50f, cs * 0.86f));
            Box(r, Steel, new Vector3(0, 0, -cs * 0.22f), new Vector3(cs * 0.34f, cs * 0.34f, cs * 0.48f));
            Box(r, Steel, new Vector3(0, cs * 0.22f, 0), new Vector3(cs * 0.34f, cs * 0.48f, cs * 0.34f));
            var bevel = Cyl(r, Brass, V0, cs * 0.18f, cs * 0.16f);
            bevel.name = "GearRotor";
            bevel.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            Port(r, "Port_RotationInput", PortShaft, new Vector3(0, 0, -cs * 0.48f), new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.05f));
            Port(r, "Port_RotationOutput_Straight", PortShaft, new Vector3(0, 0, cs * 0.48f), new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.05f));
            Port(r, "Port_RotationOutput_Up", PortShaft, new Vector3(0, cs * 0.48f, 0), new Vector3(cs * 0.13f, cs * 0.05f, cs * 0.13f));
            Port(r, "Port_RotationOutput_Down", PortShaft, new Vector3(0, -cs * 0.48f, 0), new Vector3(cs * 0.13f, cs * 0.05f, cs * 0.13f));
        }

        // ════════════════════════════════════════════════════════════════
        //  ENCASED CHAIN DRIVE — heavy marine reduction chain housing
        // ════════════════════════════════════════════════════════════════
        static void BuildEncasedChainDrive(GameObject r, float cs)
        {
            float width = cs * 1.15f;
            float height = cs * 0.78f;
            float length = cs * 2.40f;

            Box(r, DarkSteel, new Vector3(0, -height * 0.18f, 0), new Vector3(length, height * 0.42f, width));
            Box(r, Steel, new Vector3(0, height * 0.02f, 0), new Vector3(length * 0.94f, height * 0.22f, width * 0.78f));
            Box(r, DarkSteel, new Vector3(0, height * 0.26f, 0), new Vector3(length * 0.86f, height * 0.12f, width * 0.66f));

            for (int i = 0; i < 5; i++)
            {
                float x = Mathf.Lerp(-length * 0.34f, length * 0.34f, i / 4f);
                Box(r, Brass, new Vector3(x, height * 0.28f, width * 0.18f), new Vector3(cs * 0.10f, cs * 0.05f, cs * 0.18f));
                Box(r, Brass, new Vector3(x, -height * 0.24f, width * 0.18f), new Vector3(cs * 0.10f, cs * 0.05f, cs * 0.18f));
                Box(r, Brass, new Vector3(x, height * 0.28f, -width * 0.18f), new Vector3(cs * 0.10f, cs * 0.05f, cs * 0.18f));
                Box(r, Brass, new Vector3(x, -height * 0.24f, -width * 0.18f), new Vector3(cs * 0.10f, cs * 0.05f, cs * 0.18f));
            }

            var rotor = new GameObject("ChainRotor");
            rotor.transform.SetParent(r.transform, false);
            rotor.transform.localPosition = V0;
            var sprocketA = Cyl(rotor, Brass, new Vector3(-length * 0.34f, 0, 0), cs * 0.24f, cs * 0.12f);
            sprocketA.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var sprocketB = Cyl(rotor, Brass, new Vector3(length * 0.34f, 0, 0), cs * 0.24f, cs * 0.12f);
            sprocketB.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Box(rotor, Rubber, new Vector3(0, height * 0.12f, width * 0.22f), new Vector3(length * 0.70f, cs * 0.05f, cs * 0.06f));
            Box(rotor, Rubber, new Vector3(0, -height * 0.12f, width * 0.22f), new Vector3(length * 0.70f, cs * 0.05f, cs * 0.06f));
            Box(rotor, Rubber, new Vector3(0, height * 0.12f, -width * 0.22f), new Vector3(length * 0.70f, cs * 0.05f, cs * 0.06f));
            Box(rotor, Rubber, new Vector3(0, -height * 0.12f, -width * 0.22f), new Vector3(length * 0.70f, cs * 0.05f, cs * 0.06f));

            // Support feet and inspection cover.
            Box(r, Steel, new Vector3(-length * 0.28f, -height * 0.48f, 0), new Vector3(cs * 0.16f, height * 0.34f, cs * 0.24f));
            Box(r, Steel, new Vector3(length * 0.28f, -height * 0.48f, 0), new Vector3(cs * 0.16f, height * 0.34f, cs * 0.24f));
            Box(r, GlowOrange, new Vector3(0, height * 0.40f, 0), new Vector3(length * 0.32f, cs * 0.05f, cs * 0.16f));

            Port(r, "Port_RotationInput", PortShaft, new Vector3(-length * 0.56f, 0, 0), new Vector3(cs * 0.06f, cs * 0.16f, cs * 0.16f));
            Port(r, "Port_RotationOutput", PortShaft, new Vector3(length * 0.56f, 0, 0), new Vector3(cs * 0.06f, cs * 0.16f, cs * 0.16f));
            Port(r, "Propeller mount point 0", PortTurbo, new Vector3(0, 0, width * 0.54f), new Vector3(cs * 0.18f, cs * 0.18f, cs * 0.06f));
            Port(r, "Propeller mount point 1", PortTurbo, new Vector3(0, 0, -width * 0.54f), new Vector3(cs * 0.18f, cs * 0.18f, cs * 0.06f));
        }

        // ════════════════════════════════════════════════════════════════
        //  SHIPPING CONTAINER — real-world ribbed intermodal cargo block
        // ════════════════════════════════════════════════════════════════
        static void BuildShippingContainer(GameObject r, float cs)
        {
            var containerBlue = MatC(new Color(0.08f, 0.22f, 0.38f), 0.65f, 0.45f);
            var edgeYellow = MatC(new Color(0.95f, 0.72f, 0.18f), 0.55f, 0.50f, e: new Color(0.20f, 0.12f, 0.02f));

            Box(r, containerBlue, V0, new Vector3(cs * 0.96f, cs * 0.62f, cs * 0.96f));
            for (int i = 0; i < 7; i++)
            {
                float x = (i - 3) * cs * 0.13f;
                Box(r, DarkSteel, new Vector3(x, 0, cs * 0.49f), new Vector3(cs * 0.025f, cs * 0.58f, cs * 0.025f));
                Box(r, DarkSteel, new Vector3(x, 0, -cs * 0.49f), new Vector3(cs * 0.025f, cs * 0.58f, cs * 0.025f));
            }
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1 : 1) * cs * 0.49f;
                float y = (i % 2 == 0 ? -1 : 1) * cs * 0.32f;
                Box(r, edgeYellow, new Vector3(x, y, cs * 0.49f), new Vector3(cs * 0.07f, cs * 0.07f, cs * 0.07f));
                Box(r, edgeYellow, new Vector3(x, y, -cs * 0.49f), new Vector3(cs * 0.07f, cs * 0.07f, cs * 0.07f));
            }
            Box(r, DarkSteel, new Vector3(0, 0, cs * 0.505f), new Vector3(cs * 0.78f, cs * 0.03f, cs * 0.02f));
            Box(r, DarkSteel, new Vector3(0, 0, -cs * 0.505f), new Vector3(cs * 0.78f, cs * 0.03f, cs * 0.02f));
            Port(r, "Port_ItemAccess", PortFuel, new Vector3(0, cs * 0.34f, cs * 0.48f), new Vector3(cs * 0.16f, cs * 0.05f, cs * 0.08f));
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

            // ── I/O Port: Shaft Input/Output (gold) ──
            Port(r, "Port_ShaftIO", PortShaft, new Vector3(cs * 0.2f, 0, 0), new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.04f));
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
        //  GENERATOR — heavy shaft-driven maritime dynamo
        // ════════════════════════════════════════════════════════════════
        static void BuildGenerator(GameObject r, float cs)
        {
            float width = cs * 1.45f;
            float height = cs * 1.50f;
            float length = cs * 1.80f;

            Box(r, DarkSteel, new Vector3(0, -height * 0.28f, 0), new Vector3(width * 1.04f, height * 0.28f, length));
            Box(r, Steel, new Vector3(0, height * 0.10f, 0), new Vector3(width * 0.92f, height * 0.52f, length * 0.86f));
            Box(r, DarkSteel, new Vector3(0, height * 0.44f, 0), new Vector3(width * 0.80f, height * 0.18f, length * 0.72f));

            // Stator frame.
            Box(r, Brass, new Vector3(width * 0.34f, height * 0.18f, 0), new Vector3(width * 0.06f, height * 0.56f, length * 0.52f));
            Box(r, Brass, new Vector3(-width * 0.34f, height * 0.18f, 0), new Vector3(width * 0.06f, height * 0.56f, length * 0.52f));
            Box(r, Brass, new Vector3(0, height * 0.18f, length * 0.26f), new Vector3(width * 0.56f, height * 0.06f, cs * 0.08f));
            Box(r, Brass, new Vector3(0, height * 0.18f, -length * 0.26f), new Vector3(width * 0.56f, height * 0.06f, cs * 0.08f));

            var rotor = new GameObject("GenRotor");
            rotor.transform.SetParent(r.transform, false);
            rotor.transform.localPosition = new Vector3(0, height * 0.18f, 0);
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-length * 0.22f, length * 0.22f, i / 3f);
                var coil = Cyl(rotor, Copper, new Vector3(0, 0, z), cs * 0.18f, width * 0.40f);
                coil.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            Box(rotor, Steel, V0, new Vector3(width * 0.18f, cs * 0.14f, length * 0.64f));

            // Service box and cooling shroud.
            Box(r, CastIron, new Vector3(width * 0.44f, height * 0.26f, 0), new Vector3(width * 0.18f, height * 0.42f, length * 0.34f));
            Box(r, CastIron, new Vector3(0, height * 0.64f, 0), new Vector3(width * 0.70f, height * 0.10f, length * 0.62f));
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-length * 0.26f, length * 0.26f, i / 4f);
                Box(r, Steel, new Vector3(0, height * 0.74f, z), new Vector3(width * 0.56f, cs * 0.03f, cs * 0.06f));
            }
            Box(r, Glow, new Vector3(0, height * 0.50f, -length * 0.42f), new Vector3(width * 0.38f, cs * 0.05f, cs * 0.05f));

            // Shaft input and feet.
            var shaft = Cyl(r, Steel, new Vector3(0, 0, -length * 0.52f), cs * 0.10f, cs * 0.20f);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, DarkSteel, new Vector3(-width * 0.28f, -height * 0.56f, 0), new Vector3(cs * 0.18f, height * 0.30f, cs * 0.24f));
            Box(r, DarkSteel, new Vector3(width * 0.28f, -height * 0.56f, 0), new Vector3(cs * 0.18f, height * 0.30f, cs * 0.24f));

            Port(r, "Port_ShaftInput", PortShaft, new Vector3(0, 0, -length * 0.58f), new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.05f));
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
        // ════════════════════════════════════════════════════════════════
        //  MARINE WATER PUMP — industrial centrifugal pump with intake + outlet
        // ════════════════════════════════════════════════════════════════
        static void BuildMarineWaterPump(GameObject r, float cs)
        {
            // Base mounting plate.
            Box(r, DarkSteel, new Vector3(0, -cs * 0.4f, 0), new Vector3(cs * 0.85f, cs * 0.1f, cs * 0.85f));

            // Main pump housing — volute (snail-shell shape using a scaled sphere).
            var volute = Sphere(r, CastIron, new Vector3(0, -cs * 0.1f, 0), cs * 0.35f);
            volute.transform.localScale = new Vector3(cs * 0.7f, cs * 0.5f, cs * 0.7f);

            // Motor on top — cylindrical electric motor housing.
            var motor = Cyl(r, Steel, new Vector3(0, cs * 0.15f, 0), cs * 0.22f, cs * 0.4f);
            // Motor cooling fins.
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var fin = Box(r, Steel, new Vector3(Mathf.Cos(a) * cs * 0.22f, cs * 0.15f, Mathf.Sin(a) * cs * 0.22f),
                    new Vector3(cs * 0.04f, cs * 0.3f, cs * 0.1f));
                fin.transform.localRotation = Quaternion.Euler(0, i * 45f + 90, 0);
            }

            // Motor cap with junction box.
            Cyl(r, DarkSteel, new Vector3(0, cs * 0.36f, 0), cs * 0.18f, cs * 0.06f);
            Box(r, DarkSteel, new Vector3(cs * 0.12f, cs * 0.42f, 0), new Vector3(cs * 0.12f, cs * 0.08f, cs * 0.1f));

            // Impeller housing cover (brass disc on the front face).
            var cover = Cyl(r, Brass, new Vector3(0, -cs * 0.1f, cs * 0.3f), cs * 0.18f, cs * 0.04f);

            // ── Suction intake port (blue, at bottom — connects to water below) ──
            // Vertical pipe going down from the volute — this is what touches the water.
            var intake = Cyl(r, PortFuel, new Vector3(0, -cs * 0.35f, cs * 0.15f), cs * 0.1f, cs * 0.2f);
            Port(r, "Port_WaterIntake", PortFuel, new Vector3(0, -cs * 0.48f, cs * 0.15f),
                new Vector3(cs * 0.14f, cs * 0.04f, cs * 0.14f));

            // ── Discharge outlet port (blue, on the side — connects to tanks) ──
            var outlet = Cyl(r, PortFuel, new Vector3(cs * 0.35f, -cs * 0.1f, 0), cs * 0.08f, cs * 0.15f);
            outlet.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Port(r, "Port_WaterOutlet", PortFuel, new Vector3(cs * 0.45f, -cs * 0.1f, 0),
                new Vector3(cs * 0.04f, cs * 0.12f, cs * 0.12f));

            // Pressure gauge (small brass dial on top of volute).
            Sphere(r, Brass, new Vector3(-cs * 0.15f, cs * 0.05f, cs * 0.25f), cs * 0.06f);
            // Status indicator LED.
            Sphere(r, Glow, new Vector3(cs * 0.15f, cs * 0.05f, cs * 0.25f), cs * 0.04f);

            // Bolts on the base plate corners.
            var boltMat = Chrome;
            float[][] bolts = {
                new[] { cs * 0.3f, cs * 0.3f }, new[] { -cs * 0.3f, cs * 0.3f },
                new[] { cs * 0.3f, -cs * 0.3f }, new[] { -cs * 0.3f, -cs * 0.3f },
            };
            foreach (var b in bolts)
            {
                Sphere(r, boltMat, new Vector3(b[0], -cs * 0.34f, b[1]), cs * 0.03f);
            }
        }

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
        // ════════════════════════════════════════════════════════════════
        //  SHIP CONTROL CONSOLE — modern bridge console with throttle levers
        // ════════════════════════════════════════════════════════════════
        static void BuildShipConsole(GameObject r, float cs)
        {
            // Console base (angled dashboard).
            Box(r, DarkSteel, new Vector3(0, -cs * 0.3f, 0), new Vector3(cs * 0.8f, cs * 0.15f, cs * 0.5f));
            // Slanted console top.
            var top = Box(r, Steel, new Vector3(0, -cs * 0.05f, cs * 0.05f), new Vector3(cs * 0.75f, cs * 0.06f, cs * 0.4f));
            top.transform.localRotation = Quaternion.Euler(25f, 0, 0);

            // Radar screen (glowing blue).
            Box(r, Glow, new Vector3(-cs * 0.15f, cs * 0.1f, cs * 0.12f), new Vector3(cs * 0.2f, cs * 0.15f, cs * 0.02f));
            // Status displays.
            Box(r, Glow, new Vector3(cs * 0.15f, cs * 0.08f, cs * 0.1f), new Vector3(cs * 0.12f, cs * 0.08f, cs * 0.02f));
            Box(r, GlowRed, new Vector3(cs * 0.22f, cs * 0.08f, cs * 0.1f), new Vector3(cs * 0.04f, cs * 0.08f, cs * 0.02f));

            // Twin throttle levers (brass).
            for (int i = 0; i < 2; i++)
            {
                float x = (i == 0 ? -1 : 1) * cs * 0.08f;
                var leverBase = Cyl(r, Brass, new Vector3(x, -cs * 0.05f, cs * 0.2f), cs * 0.03f, cs * 0.04f);
                var lever = Box(r, Brass, new Vector3(x, cs * 0.1f, cs * 0.2f), new Vector3(cs * 0.03f, cs * 0.25f, cs * 0.03f));
                lever.transform.localRotation = Quaternion.Euler(20f, 0, 0);
                Sphere(r, Brass, new Vector3(x, cs * 0.22f, cs * 0.24f), cs * 0.035f); // knob
            }

            // Steering wheel (small, modern — unlike the big wooden helm).
            var wheelPivot = new GameObject("HelmWheel");
            wheelPivot.transform.SetParent(r.transform, false);
            wheelPivot.transform.localPosition = new Vector3(0, cs * 0.05f, cs * 0.18f);
            wheelPivot.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Cyl(wheelPivot, DarkSteel, V0, cs * 0.15f, cs * 0.02f).transform.localScale =
                new Vector3(cs * 0.3f, cs * 0.04f, cs * 0.3f * 0.15f);
            for (int i = 0; i < 3; i++)
            {
                var spoke = Box(wheelPivot, DarkSteel, V0, new Vector3(cs * 0.28f, cs * 0.02f, cs * 0.02f));
                spoke.transform.localRotation = Quaternion.Euler(0, 0, i * 60f);
            }
            Cyl(wheelPivot, Brass, V0, cs * 0.03f, cs * 0.05f); // hub

            // Captain's chair (behind the console).
            var chairPost = Cyl(r, DarkSteel, new Vector3(0, -cs * 0.15f, -cs * 0.3f), cs * 0.04f, cs * 0.35f);
            Box(r, DarkSteel, new Vector3(0, cs * 0.15f, -cs * 0.32f), new Vector3(cs * 0.25f, cs * 0.3f, cs * 0.04f)); // backrest
            Box(r, DarkSteel, new Vector3(0, -cs * 0.05f, -cs * 0.3f), new Vector3(cs * 0.25f, cs * 0.04f, cs * 0.25f)); // seat
        }

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


        static GameObject TurboAttachment(GameObject parent, int slotIndex, float cs, Vector3Int localOffset)
        {
            return Port(parent, $"Turbo attachment point {slotIndex}", PortTurbo,
                new Vector3(localOffset.x, localOffset.y, localOffset.z) * (cs * 0.52f),
                Vector3.one * cs * 0.14f);
        }

        /// <summary>Create a named I/O port GameObject with a mesh primitive inside.
        /// The container is named (e.g. "Port_FuelInput") so you can select it in the
        /// prefab hierarchy and move it. The child mesh can be swapped cube↔cylinder
        /// by deleting and re-adding a different primitive in the editor.</summary>
        static GameObject Port(GameObject parent, string portName, Material m, Vector3 pos, Vector3 scale,
            PrimitiveType shape = PrimitiveType.Cube)
        {
            var container = new GameObject(portName);
            container.transform.SetParent(parent.transform, false);
            container.transform.localPosition = pos;
            Prim(container, shape, m, V0, scale);
            return container;
        }

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
