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
        // v16: rebuilt engine models — Crude Inline-4, HFO V8, MGO V12.
        // v18: straight exhaust pipe (no L, no ground supports) with an exhaust-gas
        //      tap port, oxygen intake ports + air filters on all engine tiers,
        //      drive shaft loses its floor feet (blocks almost never touch ground).
        // v19: shaft tips span the full cell and carry gold coupling rings
        //      (Port_ShaftIO_F/B) so collinear shafts physically TOUCH at the
        //      shared face and a held shaft snaps exactly in extension.
        public const int Version = 21;
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

        // Engine-grade finish materials (v16 engine models).
        static Material BlueGreenPaint => MatC(new Color(0.23f, 0.38f, 0.34f), 0.45f, 0.38f);  // faded, chipped blue-green cast-iron paint
        static Material YellowPaint    => MatC(new Color(0.58f, 0.47f, 0.16f), 0.40f, 0.36f);  // faded industrial yellow paint
        static Material AluminumSilver => MatC(new Color(0.72f, 0.74f, 0.76f), 0.85f, 0.62f);  // precision anodized aluminum
        static Material AnodizedRed    => MatC(new Color(0.45f, 0.07f, 0.06f), 0.80f, 0.55f);  // deep anodized red accents
        static Material HeatBlue       => MatC(new Color(0.30f, 0.38f, 0.62f), 0.75f, 0.45f);  // heat-discoloured blue steel
        static Material HeatOrange     => MatC(new Color(0.75f, 0.38f, 0.12f), 0.70f, 0.40f);  // heat-discoloured orange steel
        static Material LabelBlue      => MatC(new Color(0.45f, 0.75f, 0.95f), 0.20f, 0.60f, e: new Color(0.08f, 0.20f, 0.30f)); // light-blue service label
        static Material GlassPane      => GlassMat(new Color(0.70f, 0.85f, 0.90f, 0.28f));     // inspection window glass
        static Material QuartzPane     => GlassMat(new Color(0.78f, 0.88f, 0.90f, 0.20f));     // armoured quartz viewport

        /// <summary>Transparent pane material that works on URP (Lit _Surface) and Standard (_Mode).</summary>
        static Material GlassMat(Color c)
        {
            var mat = MatC(c, 0.10f, 0.90f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // URP: transparent
            if (mat.HasProperty("_Mode"))    mat.SetFloat("_Mode", 3f);    // Standard: transparent
            mat.renderQueue = 3000;
            return mat;
        }

        /// <summary>Box spanning two points — belts, hoses, hand-rails, struts.</summary>
        static GameObject Strut(GameObject parent, Material m, Vector3 a, Vector3 b, float width)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f) return Box(parent, m, a, Vector3.one * width);
            var go = Box(parent, m, (a + b) * 0.5f, new Vector3(width, width, len));
            go.transform.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            return go;
        }

        /// <summary>Invisible locator socket (standardized axes, no renderer) for
        /// alignment reference in the prefab hierarchy.</summary>
        static GameObject Socket(GameObject parent, string socketName, Vector3 pos)
        {
            var go = new GameObject(socketName);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            return go;
        }

        /// <summary>Turbo snapping socket — the container name MUST stay
        /// "Turbo attachment point N" (GridMaritimeEngine repositions/renames these
        /// at runtime and rescales the single child cube to the live socket size).</summary>
        static GameObject TurboSocket(GameObject parent, int slotIndex, Vector3 pos, float cubeSize)
        {
            var container = new GameObject($"Turbo attachment point {slotIndex}");
            container.transform.SetParent(parent.transform, false);
            container.transform.localPosition = pos;
            Prim(container, PrimitiveType.Cube, PortTurbo, V0, Vector3.one * cubeSize);
            return container;
        }

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
            else if (n.Contains("engine_giant"))         BuildMGOV12(root, cs);
            else if (n.Contains("engine_medium"))        BuildHFOV8(root, cs);
            else if (n.Contains("engine_small"))         BuildCrudeInline4(root, cs);
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
        //  TIER 1 — CRUDE INLINE-4 (v16)
        //  ~2×1×1 m weathered workhorse: chipped blue-green cast-iron
        //  paint, open-frame crankcase with a visible crankshaft, four
        //  open-air pistons, exposed pushrods + valve springs, and a
        //  grease-stained rear drive flange.
        //  Front = −Z (fuel/controls) · Rear = +Z (SAE drive flange).
        // ════════════════════════════════════════════════════════════════
        static void BuildCrudeInline4(GameObject r, float cs)
        {
            // ── Bedplate + open-frame crankcase ───────────────────────
            Box(r, DarkSteel, new Vector3(0, -cs * 0.42f, 0), new Vector3(cs * 0.80f, cs * 0.10f, cs * 0.88f));
            Box(r, CastIron, new Vector3(cs * 0.31f, -cs * 0.20f, 0), new Vector3(cs * 0.07f, cs * 0.34f, cs * 0.80f));
            Box(r, CastIron, new Vector3(-cs * 0.31f, -cs * 0.20f, 0), new Vector3(cs * 0.07f, cs * 0.34f, cs * 0.80f));
            // Main-bearing webs — the crank spins visibly between them.
            for (int i = 0; i < 5; i++)
            {
                float z = Mathf.Lerp(-cs * 0.32f, cs * 0.32f, i / 4f);
                Box(r, DarkSteel, new Vector3(0, -cs * 0.22f, z), new Vector3(cs * 0.58f, cs * 0.26f, cs * 0.045f));
            }

            // ── Crankshaft (open frame, rear) — spins about Z ─────────
            var crank = new GameObject("CrankPulley");
            crank.transform.SetParent(r.transform, false);
            crank.transform.localPosition = new Vector3(0, -cs * 0.20f, 0);
            float[] crankPinSides = { +1f, -1f, -1f, +1f }; // flat-plane throw pairs
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-cs * 0.30f, cs * 0.30f, i / 3f);
                var web = Cyl(crank, DarkSteel, new Vector3(0, 0, z), cs * 0.16f, cs * 0.03f);
                web.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Box(crank, Steel, new Vector3(0, crankPinSides[i] * cs * 0.055f, z),
                    new Vector3(cs * 0.10f, cs * 0.06f, cs * 0.025f));
            }
            var frontPulley = Cyl(crank, Steel, new Vector3(0, 0, -cs * 0.46f), cs * 0.10f, cs * 0.035f);
            frontPulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Cylinder block (faded, chipped blue-green paint) ──────
            Box(r, BlueGreenPaint, new Vector3(0, cs * 0.05f, 0), new Vector3(cs * 0.60f, cs * 0.22f, cs * 0.78f));
            // Paint chips / bare-metal patches.
            Box(r, DarkSteel, new Vector3(cs * 0.305f, cs * 0.10f, -cs * 0.18f), new Vector3(cs * 0.012f, cs * 0.07f, cs * 0.09f));
            Box(r, DarkSteel, new Vector3(cs * 0.305f, -cs * 0.02f, cs * 0.12f), new Vector3(cs * 0.012f, cs * 0.05f, cs * 0.06f));
            Box(r, DarkSteel, new Vector3(-cs * 0.305f, cs * 0.02f, cs * 0.26f), new Vector3(cs * 0.012f, cs * 0.06f, cs * 0.08f));

            // ── Four open-air cylinders + pistons (Piston_0..3) ───────
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-cs * 0.30f, cs * 0.30f, i / 3f);
                // Sleeve + domed head.
                Cyl(r, CastIron, new Vector3(0, cs * 0.235f, z), cs * 0.085f, cs * 0.13f);
                Sphere(r, BlueGreenPaint, new Vector3(0, cs * 0.315f, z), cs * 0.185f);
                // Piston pivot — MaritimeAnimator slides this along its bore.
                var piston = new GameObject($"Piston_{i}");
                piston.transform.SetParent(r.transform, false);
                piston.transform.localPosition = new Vector3(0, cs * 0.395f, z);
                Cyl(piston, Chrome, new Vector3(0, -cs * 0.045f, 0), cs * 0.020f, cs * 0.12f); // rod
                Cyl(piston, Brass, new Vector3(0, cs * 0.025f, 0), cs * 0.055f, cs * 0.07f);  // crown
                // Cross-head guide columns.
                Cyl(r, Chrome, new Vector3(cs * 0.095f, cs * 0.30f, z), cs * 0.012f, cs * 0.20f);
                Cyl(r, Chrome, new Vector3(-cs * 0.095f, cs * 0.30f, z), cs * 0.012f, cs * 0.20f);

                // Exposed pushrods + valve springs (right side).
                Cyl(r, Chrome, new Vector3(cs * 0.14f, cs * 0.26f, z - cs * 0.035f), cs * 0.010f, cs * 0.24f);
                Cyl(r, Chrome, new Vector3(cs * 0.14f, cs * 0.26f, z + cs * 0.035f), cs * 0.010f, cs * 0.24f);
                for (int k = 0; k < 3; k++)
                    Cyl(r, Copper, new Vector3(cs * 0.14f, cs * (0.345f + k * 0.035f), z), cs * 0.026f, cs * 0.008f);
            }
            // Rocker rail + arms above the pushrods.
            Box(r, BlueGreenPaint, new Vector3(cs * 0.14f, cs * 0.45f, 0), new Vector3(cs * 0.08f, cs * 0.04f, cs * 0.72f));
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-cs * 0.30f, cs * 0.30f, i / 3f);
                Box(r, Steel, new Vector3(cs * 0.13f, cs * 0.49f, z), new Vector3(cs * 0.11f, cs * 0.02f, cs * 0.07f));
            }

            // ── Exhaust manifold + vertical stack (left side) ─────────
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-cs * 0.30f, cs * 0.30f, i / 3f);
                Strut(r, CastIron, new Vector3(-cs * 0.12f, cs * 0.22f, z), new Vector3(-cs * 0.24f, cs * 0.14f, z), cs * 0.030f);
            }
            var collector = Cyl(r, CastIron, new Vector3(-cs * 0.24f, cs * 0.14f, 0), cs * 0.045f, cs * 0.62f);
            collector.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cyl(r, CastIron, new Vector3(-cs * 0.24f, cs * 0.36f, -cs * 0.28f), cs * 0.05f, cs * 0.35f);
            Box(r, Steel, new Vector3(-cs * 0.24f, cs * 0.555f, -cs * 0.28f), new Vector3(cs * 0.12f, cs * 0.015f, cs * 0.12f));
            Port(r, "Port_ExhaustOutput", PortExhaust, new Vector3(-cs * 0.24f, cs * 0.60f, -cs * 0.28f),
                new Vector3(cs * 0.11f, cs * 0.05f, cs * 0.11f), PrimitiveType.Cube, Vector3.up);

            // ── Air filter + oxygen intake (chrome — combustion air, v18) ──
            Cyl(r, Steel, new Vector3(cs * 0.34f, cs * 0.30f, cs * 0.24f), cs * 0.060f, cs * 0.10f);
            for (int k = 0; k < 3; k++)
                Cyl(r, AluminumSilver, new Vector3(cs * 0.34f, cs * (0.265f + k * 0.035f), cs * 0.24f), cs * 0.066f, cs * 0.012f);
            Strut(r, Chrome, new Vector3(cs * 0.34f, cs * 0.30f, cs * 0.24f), new Vector3(cs * 0.18f, cs * 0.40f, cs * 0.18f), cs * 0.02f);
            Port(r, "Port_OxygenInput", Chrome, new Vector3(cs * 0.44f, cs * 0.30f, cs * 0.24f),
                new Vector3(cs * 0.05f, cs * 0.10f, cs * 0.10f), PrimitiveType.Cube, Vector3.right);

            // ── Item intake hopper (right side) ───────────────────────
            Box(r, DarkSteel, new Vector3(cs * 0.36f, cs * 0.10f, -cs * 0.26f), new Vector3(cs * 0.14f, cs * 0.20f, cs * 0.24f));
            Box(r, Steel, new Vector3(cs * 0.36f, cs * 0.215f, -cs * 0.26f), new Vector3(cs * 0.16f, cs * 0.02f, cs * 0.26f));
            Port(r, "Port_ItemIntake", PortFuel, new Vector3(cs * 0.44f, cs * 0.10f, -cs * 0.26f),
                new Vector3(cs * 0.05f, cs * 0.12f, cs * 0.12f), PrimitiveType.Cube, Vector3.right);

            // ── Grease-stained rear drive flange + SAE output ─────────
            var flange = Cyl(r, Rubber, new Vector3(0, -cs * 0.20f, cs * 0.44f), cs * 0.15f, cs * 0.035f);
            flange.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Sphere(r, Steel, new Vector3(Mathf.Cos(a) * cs * 0.105f, -cs * 0.20f + Mathf.Sin(a) * cs * 0.105f, cs * 0.465f), cs * 0.025f);
            }
            var rearStub = Cyl(r, Steel, new Vector3(0, -cs * 0.20f, cs * 0.49f), cs * 0.06f, cs * 0.10f);
            rearStub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var shaftSpin = new GameObject("ShaftSpin");
            shaftSpin.transform.SetParent(r.transform, false);
            shaftSpin.transform.localPosition = new Vector3(0, -cs * 0.20f, cs * 0.55f);
            var shaftStub = Cyl(shaftSpin, Chrome, V0, cs * 0.055f, cs * 0.16f);
            shaftStub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(shaftSpin, DarkSteel, new Vector3(0, 0, -cs * 0.03f), new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.05f));
            Port(r, "Port_ShaftOutput", PortShaft, new Vector3(0, -cs * 0.20f, cs * 0.66f),
                new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.10f), PrimitiveType.Cylinder, Vector3.forward);

            // ── Turbo pad + snapping socket (top-right) ───────────────
            Box(r, CastIron, new Vector3(cs * 0.16f, cs * 0.255f, -cs * 0.06f), new Vector3(cs * 0.07f, cs * 0.07f, cs * 0.07f));
            Box(r, Steel, new Vector3(cs * 0.16f, cs * 0.30f, -cs * 0.06f), new Vector3(cs * 0.11f, cs * 0.02f, cs * 0.11f));
            TurboSocket(r, 0, new Vector3(cs * 0.16f, cs * 0.30f, -cs * 0.06f), cs * 0.14f);

            // ── Lube-oil lines ────────────────────────────────────────
            Strut(r, Copper, new Vector3(-cs * 0.29f, -cs * 0.04f, cs * 0.34f), new Vector3(-cs * 0.29f, -cs * 0.04f, -cs * 0.34f), cs * 0.015f);
            Strut(r, Copper, new Vector3(-cs * 0.29f, -cs * 0.04f, -cs * 0.34f), new Vector3(0, -cs * 0.10f, -cs * 0.40f), cs * 0.015f);

            // ── Invisible locator sockets (standard axes) ─────────────
            Socket(r, "Socket_CrankAxis", new Vector3(0, -cs * 0.20f, 0));
            Socket(r, "Socket_ShaftOutput", new Vector3(0, -cs * 0.20f, cs * 0.66f));
            Socket(r, "Socket_Turbo_0", new Vector3(cs * 0.16f, cs * 0.30f, -cs * 0.06f));
        }

        // ════════════════════════════════════════════════════════════════
        //  TIER 2 — HFO V8 (v16)
        //  ~4×2×2 m faded-yellow 90° V-block. Glass-paneled inspection
        //  windows on both flanks reveal the two cylinder banks and the
        //  crankshaft; a cast intake plenum sits in the valley; the HFO
        //  heating manifold carries steam-traced fuel filters. Geared
        //  output housing at the rear with a recessed heavy coupling.
        //  Front = −Z (accessory drive) · Rear = +Z (geared output).
        // ════════════════════════════════════════════════════════════════
        static void BuildHFOV8(GameObject r, float cs)
        {
            // ── Bedplate + crankcase + side skirts ────────────────────
            Box(r, DarkSteel, new Vector3(0, -cs * 0.36f, 0), new Vector3(cs * 0.84f, cs * 0.08f, cs * 1.64f));
            Box(r, YellowPaint, new Vector3(0, -cs * 0.16f, 0), new Vector3(cs * 0.74f, cs * 0.32f, cs * 1.52f));
            Box(r, DarkSteel, new Vector3(cs * 0.37f, -cs * 0.04f, 0), new Vector3(cs * 0.04f, cs * 0.20f, cs * 1.56f));
            Box(r, DarkSteel, new Vector3(-cs * 0.37f, -cs * 0.04f, 0), new Vector3(cs * 0.04f, cs * 0.20f, cs * 1.56f));
            // Paint wear patches.
            Box(r, DarkSteel, new Vector3(cs * 0.375f, -cs * 0.22f, cs * 0.40f), new Vector3(cs * 0.012f, cs * 0.10f, cs * 0.14f));
            Box(r, DarkSteel, new Vector3(-cs * 0.375f, -cs * 0.08f, -cs * 0.52f), new Vector3(cs * 0.012f, cs * 0.08f, cs * 0.10f));

            // ── Crankshaft — visible through the inspection windows ───
            var crank = new GameObject("CrankPulley");
            crank.transform.SetParent(r.transform, false);
            crank.transform.localPosition = new Vector3(0, -cs * 0.16f, 0);
            for (int i = 0; i < 8; i++)
            {
                float z = Mathf.Lerp(-cs * 0.66f, cs * 0.66f, i / 7f);
                var web = Cyl(crank, DarkSteel, new Vector3(0, 0, z), cs * 0.14f, cs * 0.025f);
                web.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var frontPulley = Cyl(crank, Steel, new Vector3(0, 0, -cs * 0.80f), cs * 0.11f, cs * 0.03f);
            frontPulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var damper = Cyl(crank, Rubber, new Vector3(0, 0, -cs * 0.76f), cs * 0.085f, cs * 0.04f);
            damper.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Two cylinder banks (4 pots per bank, 25° V tilt) ──────
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1f : -1f;
                float tilt = sign * 25f;
                var bank = Box(r, YellowPaint, new Vector3(sign * cs * 0.19f, cs * 0.02f, 0),
                    new Vector3(cs * 0.28f, cs * 0.24f, cs * 1.44f));
                bank.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                for (int i = 0; i < 4; i++)
                {
                    float z = Mathf.Lerp(-cs * 0.555f, cs * 0.555f, i / 3f);
                    var liner = Cyl(r, CastIron, new Vector3(sign * cs * 0.235f, cs * 0.10f, z), cs * 0.075f, cs * 0.20f);
                    liner.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    var head = Box(r, Steel, new Vector3(sign * cs * 0.30f, cs * 0.26f, z),
                        new Vector3(cs * 0.16f, cs * 0.08f, cs * 0.24f));
                    head.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                    // Piston pivot (tilted bore — animator slides along piston.up).
                    var piston = new GameObject($"Piston_{s * 4 + i}");
                    piston.transform.SetParent(r.transform, false);
                    piston.transform.localPosition = new Vector3(sign * cs * 0.235f, cs * 0.24f, z);
                    piston.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    Cyl(piston, Chrome, new Vector3(0, -cs * 0.05f, 0), cs * 0.022f, cs * 0.14f);
                    Cyl(piston, Brass, new Vector3(0, cs * 0.03f, 0), cs * 0.045f, cs * 0.07f);
                }

                // ── Glass-paneled inspection window (framed) ──────────
                Box(r, DarkSteel, new Vector3(sign * cs * 0.315f, cs * 0.235f, 0), new Vector3(cs * 0.03f, cs * 0.03f, cs * 1.02f));
                Box(r, DarkSteel, new Vector3(sign * cs * 0.315f, -cs * 0.035f, 0), new Vector3(cs * 0.03f, cs * 0.03f, cs * 1.02f));
                Box(r, DarkSteel, new Vector3(sign * cs * 0.315f, cs * 0.10f, cs * 0.51f), new Vector3(cs * 0.03f, cs * 0.30f, cs * 0.03f));
                Box(r, DarkSteel, new Vector3(sign * cs * 0.315f, cs * 0.10f, -cs * 0.51f), new Vector3(cs * 0.03f, cs * 0.30f, cs * 0.03f));
                Box(r, GlassPane, new Vector3(sign * cs * 0.30f, cs * 0.10f, 0), new Vector3(cs * 0.015f, cs * 0.24f, cs * 0.99f));
            }

            // ── Cast intake plenum in the valley ──────────────────────
            Box(r, CastIron, new Vector3(0, cs * 0.30f, 0), new Vector3(cs * 0.22f, cs * 0.14f, cs * 1.30f));
            var plenumTop = Cyl(r, CastIron, new Vector3(0, cs * 0.37f, 0), cs * 0.10f, cs * 1.28f);
            plenumTop.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Box(r, CastIron, new Vector3(0, cs * 0.44f, -cs * 0.30f), new Vector3(cs * 0.16f, cs * 0.06f, cs * 0.24f));

            // ── Geared output housing (rear) + recessed heavy coupling ─
            Box(r, CastIron, new Vector3(0, -cs * 0.10f, cs * 0.92f), new Vector3(cs * 0.56f, cs * 0.52f, cs * 0.30f));
            Sphere(r, CastIron, new Vector3(0, -cs * 0.10f, cs * 1.06f), cs * 0.36f);
            Box(r, Steel, new Vector3(cs * 0.24f, -cs * 0.10f, cs * 0.92f), new Vector3(cs * 0.06f, cs * 0.30f, cs * 0.20f));
            Box(r, Steel, new Vector3(-cs * 0.24f, -cs * 0.10f, cs * 0.92f), new Vector3(cs * 0.06f, cs * 0.30f, cs * 0.20f));
            var shaftSpin = new GameObject("ShaftSpin");
            shaftSpin.transform.SetParent(r.transform, false);
            shaftSpin.transform.localPosition = new Vector3(0, -cs * 0.10f, cs * 1.08f);
            var recessedFlange = Cyl(shaftSpin, DarkSteel, V0, cs * 0.16f, cs * 0.05f);
            recessedFlange.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var outStub = Cyl(shaftSpin, Steel, new Vector3(0, 0, cs * 0.06f), cs * 0.07f, cs * 0.12f);
            outStub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Port(r, "Port_ShaftOutput", PortShaft, new Vector3(0, -cs * 0.10f, cs * 1.18f),
                new Vector3(cs * 0.18f, cs * 0.18f, cs * 0.10f), PrimitiveType.Cylinder, Vector3.forward);

            // ── Exhaust output (top-right, insulated, heat-tinted) ────
            var collectorV8 = Cyl(r, Steel, new Vector3(cs * 0.34f, cs * 0.24f, 0), cs * 0.06f, cs * 1.30f);
            collectorV8.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-cs * 0.555f, cs * 0.555f, i / 3f);
                Strut(r, CastIron, new Vector3(cs * 0.24f, cs * 0.20f, z), new Vector3(cs * 0.34f, cs * 0.26f, z), cs * 0.035f);
            }
            Cyl(r, GlowOrange, new Vector3(cs * 0.34f, cs * 0.42f, cs * 0.45f), cs * 0.075f, cs * 0.12f);
            Cyl(r, HeatOrange, new Vector3(cs * 0.34f, cs * 0.52f, cs * 0.45f), cs * 0.075f, cs * 0.10f);
            Cyl(r, HeatBlue, new Vector3(cs * 0.34f, cs * 0.61f, cs * 0.45f), cs * 0.078f, cs * 0.08f);
            Cyl(r, Steel, new Vector3(cs * 0.34f, cs * 0.40f, cs * 0.45f), cs * 0.10f, cs * 0.06f);
            Cyl(r, Steel, new Vector3(cs * 0.34f, cs * 0.58f, cs * 0.45f), cs * 0.10f, cs * 0.06f);
            Port(r, "Port_ExhaustOutput", PortExhaust, new Vector3(cs * 0.34f, cs * 0.70f, cs * 0.45f),
                new Vector3(cs * 0.13f, cs * 0.05f, cs * 0.13f), PrimitiveType.Cube, Vector3.up);

            // ── HFO heating manifold + steam-traced fuel filters ──────
            var heatPipe = Cyl(r, Brass, new Vector3(-cs * 0.40f, cs * 0.02f, 0), cs * 0.03f, cs * 1.30f);
            heatPipe.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int f = 0; f < 2; f++)
            {
                float z = f == 0 ? -cs * 0.40f : cs * 0.40f;
                Cyl(r, Steel, new Vector3(-cs * 0.40f, -cs * 0.02f, z), cs * 0.06f, cs * 0.18f);
                for (int k = 0; k < 3; k++)
                    Cyl(r, Copper, new Vector3(-cs * 0.40f, cs * (-0.06f + k * 0.06f), z), cs * 0.075f, cs * 0.012f);
                Strut(r, Brass, new Vector3(-cs * 0.40f, cs * 0.02f, z), new Vector3(-cs * 0.40f, -cs * 0.06f, z), cs * 0.015f);
            }

            // ── Service ports: HFO intake, steam heat, coolant ────────
            Strut(r, Brass, new Vector3(-cs * 0.40f, -cs * 0.02f, -cs * 0.30f), new Vector3(-cs * 0.44f, -cs * 0.20f, -cs * 0.30f), cs * 0.03f);
            Port(r, "Port_FuelInput", PortFuel, new Vector3(-cs * 0.44f, -cs * 0.22f, -cs * 0.30f),
                new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.05f), PrimitiveType.Cube, Vector3.left);
            // (v21: residual steam-heat port REMOVED by request — exhaust is the
            //  one and only hot-gas hookup on the engines.)
            Strut(r, PortCoolant, new Vector3(cs * 0.44f, -cs * 0.18f, -cs * 0.42f), new Vector3(cs * 0.30f, -cs * 0.05f, -cs * 0.42f), cs * 0.025f);
            Port(r, "Port_CoolantInput", PortCoolant, new Vector3(cs * 0.44f, -cs * 0.18f, -cs * 0.42f),
                new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.05f), PrimitiveType.Cube, Vector3.right);

            // ── Air filter box + oxygen intake (chrome — combustion air, v18) ──
            Box(r, AluminumSilver, new Vector3(cs * 0.43f, cs * 0.08f, cs * 0.62f), new Vector3(cs * 0.10f, cs * 0.12f, cs * 0.16f));
            for (int k = 0; k < 3; k++)
                Box(r, DarkSteel, new Vector3(cs * 0.485f, cs * (0.025f + k * 0.035f), cs * 0.62f), new Vector3(cs * 0.012f, cs * 0.02f, cs * 0.14f));
            Strut(r, Chrome, new Vector3(cs * 0.40f, cs * 0.10f, cs * 0.55f), new Vector3(cs * 0.10f, cs * 0.40f, cs * 0.30f), cs * 0.025f);
            Port(r, "Port_OxygenInput", Chrome, new Vector3(cs * 0.50f, cs * 0.08f, cs * 0.62f),
                new Vector3(cs * 0.05f, cs * 0.10f, cs * 0.10f), PrimitiveType.Cube, Vector3.right);

            // ── Twin turbo pads + snapping sockets (valley service) ───
            Strut(r, Steel, new Vector3(cs * 0.34f, cs * 0.30f, -cs * 0.16f), new Vector3(cs * 0.58f, cs * 0.42f, -cs * 0.16f), cs * 0.06f);
            Strut(r, Steel, new Vector3(-cs * 0.34f, cs * 0.30f, -cs * 0.16f), new Vector3(-cs * 0.58f, cs * 0.42f, -cs * 0.16f), cs * 0.06f);
            Box(r, DarkSteel, new Vector3(cs * 0.58f, cs * 0.42f, -cs * 0.16f), new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.04f));
            Box(r, DarkSteel, new Vector3(-cs * 0.58f, cs * 0.42f, -cs * 0.16f), new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.04f));
            TurboSocket(r, 0, new Vector3(cs * 0.58f, cs * 0.42f, -cs * 0.16f), cs * 0.22f);
            TurboSocket(r, 1, new Vector3(-cs * 0.58f, cs * 0.42f, -cs * 0.16f), cs * 0.22f);

            // ── Lifting eyes + locator sockets ────────────────────────
            Box(r, Steel, new Vector3(cs * 0.20f, cs * 0.50f, cs * 0.55f), new Vector3(cs * 0.04f, cs * 0.06f, cs * 0.02f));
            Box(r, Steel, new Vector3(-cs * 0.20f, cs * 0.50f, cs * 0.55f), new Vector3(cs * 0.04f, cs * 0.06f, cs * 0.02f));
            Box(r, Steel, new Vector3(cs * 0.20f, cs * 0.50f, -cs * 0.55f), new Vector3(cs * 0.04f, cs * 0.06f, cs * 0.02f));
            Box(r, Steel, new Vector3(-cs * 0.20f, cs * 0.50f, -cs * 0.55f), new Vector3(cs * 0.04f, cs * 0.06f, cs * 0.02f));
            Socket(r, "Socket_CrankAxis", new Vector3(0, -cs * 0.16f, 0));
            Socket(r, "Socket_ShaftOutput", new Vector3(0, -cs * 0.10f, cs * 1.18f));
            Socket(r, "Socket_Turbo_0", new Vector3(cs * 0.58f, cs * 0.42f, -cs * 0.16f));
            Socket(r, "Socket_Turbo_1", new Vector3(-cs * 0.58f, cs * 0.42f, -cs * 0.16f));
        }

        // ════════════════════════════════════════════════════════════════
        //  TIER 3 — MGO V12 (v16)
        //  ~8×4×3 m flagship: precision anodized aluminum (deep red /
        //  silver), dry sump, electronic valve-train covers, four armored
        //  quartz viewing ports revealing both six-cylinder banks, gantry
        //  walkways + access ladders along the whole block, four turbo
        //  trunks on the central exhaust plenum, a belt-driven seawater
        //  pump off the front accessory drive, and a massive splined
        //  PTO shaft inside a bearing housing.
        //  Front = −Z (accessory belt + fuel rail) · Rear = +Z (splined PTO).
        // ════════════════════════════════════════════════════════════════
        static void BuildMGOV12(GameObject r, float cs)
        {
            // ── Bedplate + dry sump ───────────────────────────────────
            Box(r, DarkSteel, new Vector3(0, -cs * 0.72f, 0), new Vector3(cs * 1.28f, cs * 0.12f, cs * 3.28f));
            Box(r, AluminumSilver, new Vector3(0, -cs * 0.58f, 0), new Vector3(cs * 1.12f, cs * 0.18f, cs * 2.96f));
            for (int i = 0; i < 8; i++)
            {
                float z = Mathf.Lerp(-cs * 1.40f, cs * 1.40f, i / 7f);
                Sphere(r, Steel, new Vector3(cs * 0.54f, -cs * 0.49f, z), cs * 0.03f);
                Sphere(r, Steel, new Vector3(-cs * 0.54f, -cs * 0.49f, z), cs * 0.03f);
            }
            Sphere(r, Rubber, new Vector3(0, -cs * 0.68f, cs * 1.20f), cs * 0.04f); // sump drain

            // ── Crankcase + red anodized accent band ──────────────────
            Box(r, AluminumSilver, new Vector3(0, -cs * 0.26f, 0), new Vector3(cs * 1.06f, cs * 0.46f, cs * 3.02f));
            Box(r, AnodizedRed, new Vector3(0, -cs * 0.01f, 0), new Vector3(cs * 1.08f, cs * 0.05f, cs * 3.04f));

            // ── Crankshaft (webs behind the quartz ports) ─────────────
            var crank = new GameObject("CrankPulley");
            crank.transform.SetParent(r.transform, false);
            crank.transform.localPosition = new Vector3(0, -cs * 0.34f, 0);
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(-cs * 1.40f, cs * 1.40f, i / 5f);
                var web = Cyl(crank, DarkSteel, new Vector3(0, 0, z), cs * 0.20f, cs * 0.03f);
                web.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            var frontPulley = Cyl(crank, AluminumSilver, new Vector3(0, 0, -cs * 1.55f), cs * 0.13f, cs * 0.035f);
            frontPulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var damper = Cyl(crank, Rubber, new Vector3(0, 0, -cs * 1.50f), cs * 0.09f, cs * 0.05f);
            damper.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Two banks of six (28° V tilt), quartz viewing ports ───
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1f : -1f;
                float tilt = sign * 28f;
                var bank = Box(r, AluminumSilver, new Vector3(sign * cs * 0.30f, cs * 0.14f, 0),
                    new Vector3(cs * 0.34f, cs * 0.50f, cs * 2.92f));
                bank.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                for (int i = 0; i < 6; i++)
                {
                    float z = Mathf.Lerp(-cs * 1.30f, cs * 1.30f, i / 5f);
                    var liner = Cyl(r, CastIron, new Vector3(sign * cs * 0.355f, cs * 0.20f, z), cs * 0.095f, cs * 0.26f);
                    liner.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    var head = Box(r, AnodizedRed, new Vector3(sign * cs * 0.44f, cs * 0.40f, z),
                        new Vector3(cs * 0.22f, cs * 0.10f, cs * 0.34f));
                    head.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);

                    // Piston pivot (tilted bore — animator slides along piston.up).
                    var piston = new GameObject($"Piston_{s * 6 + i}");
                    piston.transform.SetParent(r.transform, false);
                    piston.transform.localPosition = new Vector3(sign * cs * 0.36f, cs * 0.18f, z);
                    piston.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    Cyl(piston, Chrome, new Vector3(0, -cs * 0.06f, 0), cs * 0.030f, cs * 0.22f);
                    Cyl(piston, Brass, new Vector3(0, cs * 0.06f, 0), cs * 0.055f, cs * 0.09f);
                }

                // Electronic valve-train cover + ribs + ECU connectors.
                var cover = Box(r, AnodizedRed, new Vector3(sign * cs * 0.50f, cs * 0.52f, 0),
                    new Vector3(cs * 0.20f, cs * 0.08f, cs * 2.96f));
                cover.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                for (int i = 0; i < 6; i++)
                {
                    float z = Mathf.Lerp(-cs * 1.30f, cs * 1.30f, i / 5f);
                    var rib = Box(r, AluminumSilver, new Vector3(sign * cs * 0.525f, cs * 0.57f, z),
                        new Vector3(cs * 0.22f, cs * 0.02f, cs * 0.06f));
                    rib.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
                }

                // Armored quartz viewing ports (2 per side).
                for (int wpos = 0; wpos < 2; wpos++)
                {
                    float wz = wpos == 0 ? -cs * 0.65f : cs * 0.65f;
                    Box(r, DarkSteel, new Vector3(sign * cs * 0.53f, cs * 0.39f, wz), new Vector3(cs * 0.03f, cs * 0.03f, cs * 0.66f));
                    Box(r, DarkSteel, new Vector3(sign * cs * 0.53f, cs * 0.09f, wz), new Vector3(cs * 0.03f, cs * 0.03f, cs * 0.66f));
                    Box(r, DarkSteel, new Vector3(sign * cs * 0.53f, cs * 0.24f, wz + cs * 0.33f), new Vector3(cs * 0.03f, cs * 0.30f, cs * 0.03f));
                    Box(r, DarkSteel, new Vector3(sign * cs * 0.53f, cs * 0.24f, wz - cs * 0.33f), new Vector3(cs * 0.03f, cs * 0.30f, cs * 0.03f));
                    Box(r, QuartzPane, new Vector3(sign * cs * 0.53f, cs * 0.24f, wz), new Vector3(cs * 0.015f, cs * 0.26f, cs * 0.60f));
                }
            }
            // ECU boxes in the valley.
            for (int i = 0; i < 3; i++)
            {
                float z = Mathf.Lerp(-cs * 0.90f, cs * 0.90f, i / 2f);
                Box(r, Rubber, new Vector3(0, cs * 0.60f, z), new Vector3(cs * 0.10f, cs * 0.06f, cs * 0.14f));
                Sphere(r, Glow, new Vector3(cs * 0.04f, cs * 0.635f, z), cs * 0.02f);
            }

            // ── Central exhaust plenum + four turbo trunks ────────────
            Box(r, DarkSteel, new Vector3(0, cs * 0.55f, 0), new Vector3(cs * 0.34f, cs * 0.16f, cs * 2.70f));
            var plenumRound = Cyl(r, DarkSteel, new Vector3(0, cs * 0.63f, 0), cs * 0.16f, cs * 2.68f);
            plenumRound.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Side trunks to pads 0/1 (2×2 grid on the plenum service plane).
            Strut(r, DarkSteel, new Vector3(cs * 0.20f, cs * 0.60f, -cs * 0.24f), new Vector3(cs * 1.18f, cs * 0.66f, -cs * 0.24f), cs * 0.10f);
            Strut(r, DarkSteel, new Vector3(-cs * 0.20f, cs * 0.60f, -cs * 0.24f), new Vector3(-cs * 1.18f, cs * 0.66f, -cs * 0.24f), cs * 0.10f);
            Strut(r, Steel, new Vector3(cs * 1.10f, cs * 0.61f, -cs * 0.24f), new Vector3(cs * 0.95f, cs * 0.30f, -cs * 0.24f), cs * 0.04f);
            Strut(r, Steel, new Vector3(-cs * 1.10f, cs * 0.61f, -cs * 0.24f), new Vector3(-cs * 0.95f, cs * 0.30f, -cs * 0.24f), cs * 0.04f);
            Box(r, Steel, new Vector3(cs * 1.24f, cs * 0.66f, -cs * 0.24f), new Vector3(cs * 0.06f, cs * 0.20f, cs * 0.16f));
            Box(r, Steel, new Vector3(-cs * 1.24f, cs * 0.66f, -cs * 0.24f), new Vector3(cs * 0.06f, cs * 0.20f, cs * 0.16f));
            TurboSocket(r, 0, new Vector3(cs * 1.24f, cs * 0.66f, -cs * 0.24f), cs * 0.30f);
            TurboSocket(r, 1, new Vector3(-cs * 1.24f, cs * 0.66f, -cs * 0.24f), cs * 0.30f);
            // Top + rear riser pads (2 and 3).
            Cyl(r, DarkSteel, new Vector3(0, cs * 0.78f, cs * 0.36f), cs * 0.10f, cs * 0.36f);
            Box(r, Steel, new Vector3(0, cs * 0.94f, cs * 0.36f), new Vector3(cs * 0.18f, cs * 0.05f, cs * 0.18f));
            TurboSocket(r, 2, new Vector3(0, cs * 0.94f, cs * 0.36f), cs * 0.30f);
            Cyl(r, DarkSteel, new Vector3(0, cs * 0.70f, -cs * 0.90f), cs * 0.10f, cs * 0.26f);
            Box(r, Steel, new Vector3(0, cs * 0.82f, -cs * 0.90f), new Vector3(cs * 0.18f, cs * 0.05f, cs * 0.18f));
            TurboSocket(r, 3, new Vector3(0, cs * 0.82f, -cs * 0.90f), cs * 0.30f);

            // ── Two exhaust collectors (front-top + rear-top) ─────────
            for (int e = 0; e < 2; e++)
            {
                float z = e == 0 ? -cs * 1.42f : cs * 1.42f;
                Cyl(r, DarkSteel, new Vector3(0, cs * 0.66f, z), cs * 0.12f, cs * 0.30f);
                Cyl(r, Steel, new Vector3(0, cs * 0.60f, z), cs * 0.15f, cs * 0.08f);
                Cyl(r, HeatBlue, new Vector3(0, cs * 0.72f, z), cs * 0.125f, cs * 0.06f);
                Cyl(r, HeatOrange, new Vector3(0, cs * 0.77f, z), cs * 0.12f, cs * 0.05f);
                Port(r, e == 0 ? "Port_ExhaustOutput_F" : "Port_ExhaustOutput_R", PortExhaust,
                    new Vector3(0, cs * 0.84f, z), new Vector3(cs * 0.16f, cs * 0.05f, cs * 0.16f),
                    PrimitiveType.Cube, Vector3.up);
            }

            // ── Gantry walkways + railings + access ladders ───────────
            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1f : -1f;
                Box(r, DarkSteel, new Vector3(sign * cs * 0.78f, cs * 0.44f, 0), new Vector3(cs * 0.22f, cs * 0.03f, cs * 3.00f));
                Box(r, Steel, new Vector3(sign * cs * 0.87f, cs * 0.60f, 0), new Vector3(cs * 0.02f, cs * 0.02f, cs * 2.95f));
                Box(r, Steel, new Vector3(sign * cs * 0.87f, cs * 0.52f, 0), new Vector3(cs * 0.02f, cs * 0.02f, cs * 2.95f));
                for (int i = 0; i < 7; i++)
                {
                    float z = Mathf.Lerp(-cs * 1.45f, cs * 1.45f, i / 6f);
                    Box(r, Steel, new Vector3(sign * cs * 0.87f, cs * 0.52f, z), new Vector3(cs * 0.02f, cs * 0.16f, cs * 0.02f));
                    if (i % 2 == 0)
                        Strut(r, Steel, new Vector3(sign * cs * 0.75f, cs * 0.43f, z), new Vector3(sign * cs * 0.55f, cs * 0.30f, z), cs * 0.025f);
                }
                // Access ladder at each end of the catwalk.
                float lz = s == 0 ? -cs * 1.45f : cs * 1.45f;
                Box(r, Steel, new Vector3(sign * cs * 0.83f, -cs * 0.05f, lz - cs * 0.07f), new Vector3(cs * 0.02f, cs * 1.00f, cs * 0.02f));
                Box(r, Steel, new Vector3(sign * cs * 0.83f, -cs * 0.05f, lz + cs * 0.07f), new Vector3(cs * 0.02f, cs * 1.00f, cs * 0.02f));
                for (int rung = 0; rung < 8; rung++)
                {
                    float y = Mathf.Lerp(-cs * 0.55f, cs * 0.40f, rung / 7f);
                    Box(r, Steel, new Vector3(sign * cs * 0.83f, y, lz), new Vector3(cs * 0.02f, cs * 0.015f, cs * 0.16f));
                }
            }

            // ── Sea chest + belt-driven seawater pump (bottom-right) ──
            Box(r, Steel, new Vector3(cs * 0.50f, -cs * 0.55f, -cs * 1.00f), new Vector3(cs * 0.26f, cs * 0.18f, cs * 0.24f));
            for (int i = 0; i < 3; i++)
                Box(r, Rubber, new Vector3(cs * 0.50f, -cs * 0.645f, cs * (-1.07f + i * 0.07f)), new Vector3(cs * 0.24f, cs * 0.02f, cs * 0.03f));
            Cyl(r, Bronze, new Vector3(cs * 0.42f, -cs * 0.55f, -cs * 1.25f), cs * 0.10f, cs * 0.14f);
            Sphere(r, Bronze, new Vector3(cs * 0.42f, -cs * 0.46f, -cs * 1.25f), cs * 0.16f);
            Strut(r, Copper, new Vector3(cs * 0.42f, -cs * 0.50f, -cs * 1.20f), new Vector3(cs * 0.50f, -cs * 0.50f, -cs * 1.05f), cs * 0.03f);
            // SeaPump — animated pulley on the front accessory belt.
            var seaPump = new GameObject("SeaPump");
            seaPump.transform.SetParent(r.transform, false);
            seaPump.transform.localPosition = new Vector3(cs * 0.42f, -cs * 0.62f, -cs * 1.58f);
            var pumpPulley = Cyl(seaPump, Chrome, V0, cs * 0.06f, cs * 0.025f);
            pumpPulley.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Strut(r, Copper, new Vector3(cs * 0.42f, -cs * 0.60f, -cs * 1.50f), new Vector3(cs * 0.42f, -cs * 0.56f, -cs * 1.30f), cs * 0.025f);
            // Accessory belt: crank pulley → SeaPump pulley.
            Strut(r, Rubber, new Vector3(0, -cs * 0.21f, -cs * 1.55f), new Vector3(cs * 0.42f, -cs * 0.56f, -cs * 1.58f), cs * 0.025f);
            Strut(r, Rubber, new Vector3(0, -cs * 0.47f, -cs * 1.55f), new Vector3(cs * 0.42f, -cs * 0.68f, -cs * 1.58f), cs * 0.025f);
            Strut(r, PortCoolant, new Vector3(cs * 0.56f, -cs * 0.56f, -cs * 0.80f), new Vector3(cs * 0.30f, -cs * 0.30f, -cs * 0.50f), cs * 0.035f);
            Port(r, "Port_CoolantInput", PortCoolant, new Vector3(cs * 0.58f, -cs * 0.60f, -cs * 0.72f),
                new Vector3(cs * 0.16f, cs * 0.16f, cs * 0.06f), PrimitiveType.Cube, Vector3.right);

            // ── Massive splined PTO (rear) + bearing housing ──────────
            Box(r, CastIron, new Vector3(0, -cs * 0.34f, cs * 1.55f), new Vector3(cs * 0.36f, cs * 0.30f, cs * 0.12f));
            Box(r, CastIron, new Vector3(0, -cs * 0.34f, cs * 1.74f), new Vector3(cs * 0.30f, cs * 0.26f, cs * 0.10f));
            Sphere(r, Rubber, new Vector3(0, -cs * 0.34f, cs * 1.80f), cs * 0.10f);
            var shaftSpin = new GameObject("ShaftSpin");
            shaftSpin.transform.SetParent(r.transform, false);
            shaftSpin.transform.localPosition = new Vector3(0, -cs * 0.34f, cs * 1.86f);
            var mainShaft = Cyl(shaftSpin, Chrome, V0, cs * 0.10f, cs * 0.30f);
            mainShaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                Box(shaftSpin, Chrome, new Vector3(Mathf.Cos(a) * cs * 0.10f, Mathf.Sin(a) * cs * 0.10f, 0),
                    new Vector3(cs * 0.02f, cs * 0.02f, cs * 0.28f));
            }
            Port(r, "Port_ShaftOutput", PortShaft, new Vector3(0, -cs * 0.34f, cs * 2.04f),
                new Vector3(cs * 0.22f, cs * 0.22f, cs * 0.12f), PrimitiveType.Cylinder, Vector3.forward);

            // ── MGO fuel rail (front-left, light-blue service label) ──
            var fuelRail = Cyl(r, Brass, new Vector3(-cs * 0.50f, cs * 0.30f, -cs * 0.35f), cs * 0.035f, cs * 2.20f);
            fuelRail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(-cs * 1.05f, cs * 1.05f, i / 5f);
                Strut(r, Brass, new Vector3(-cs * 0.50f, cs * 0.30f, z),
                    new Vector3(-cs * 0.42f, cs * 0.42f, z), cs * 0.012f);
            }
            Box(r, DarkSteel, new Vector3(-cs * 0.55f, cs * 0.42f, -cs * 0.90f), new Vector3(cs * 0.06f, cs * 0.11f, cs * 0.22f));
            Box(r, LabelBlue, new Vector3(-cs * 0.53f, cs * 0.42f, -cs * 0.90f), new Vector3(cs * 0.05f, cs * 0.09f, cs * 0.20f));
            Strut(r, Brass, new Vector3(-cs * 0.50f, -cs * 0.30f, -cs * 1.38f), new Vector3(-cs * 0.50f, cs * 0.26f, -cs * 1.05f), cs * 0.025f);
            Port(r, "Port_FuelInput", PortFuel, new Vector3(-cs * 0.50f, -cs * 0.30f, -cs * 1.42f),
                new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.06f), PrimitiveType.Cube, Vector3.left);

            // ── Twin air-filter housings + oxygen intake (chrome — combustion air, v18) ──
            Box(r, AluminumSilver, new Vector3(-cs * 0.50f, cs * 0.08f, -cs * 1.32f), new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.24f));
            for (int k = 0; k < 4; k++)
                Box(r, DarkSteel, new Vector3(-cs * 0.565f, cs * (0.015f + k * 0.04f), -cs * 1.32f), new Vector3(cs * 0.015f, cs * 0.022f, cs * 0.20f));
            Box(r, AluminumSilver, new Vector3(cs * 0.50f, cs * 0.08f, -cs * 1.32f), new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.24f));
            for (int k = 0; k < 4; k++)
                Box(r, DarkSteel, new Vector3(cs * 0.565f, cs * (0.015f + k * 0.04f), -cs * 1.32f), new Vector3(cs * 0.015f, cs * 0.022f, cs * 0.20f));
            Strut(r, Chrome, new Vector3(-cs * 0.50f, cs * 0.15f, -cs * 1.28f), new Vector3(-cs * 0.30f, cs * 0.52f, -cs * 0.40f), cs * 0.03f);
            Strut(r, Chrome, new Vector3(cs * 0.50f, cs * 0.15f, -cs * 1.28f), new Vector3(cs * 0.30f, cs * 0.52f, -cs * 0.40f), cs * 0.03f);
            Port(r, "Port_OxygenInput", Chrome, new Vector3(-cs * 0.50f, cs * 0.08f, -cs * 1.48f),
                new Vector3(cs * 0.12f, cs * 0.12f, cs * 0.05f), PrimitiveType.Cube, Vector3.left);

            // ── Invisible locator sockets (standard axes) ─────────────
            Socket(r, "Socket_CrankAxis", new Vector3(0, -cs * 0.34f, 0));
            Socket(r, "Socket_ShaftOutput", new Vector3(0, -cs * 0.34f, cs * 2.04f));
            Socket(r, "Socket_SeaPump", new Vector3(cs * 0.42f, -cs * 0.62f, -cs * 1.58f));
            Socket(r, "Socket_Turbo_0", new Vector3(cs * 1.24f, cs * 0.66f, -cs * 0.24f));
            Socket(r, "Socket_Turbo_1", new Vector3(-cs * 1.24f, cs * 0.66f, -cs * 0.24f));
            Socket(r, "Socket_Turbo_2", new Vector3(0, cs * 0.94f, cs * 0.36f));
            Socket(r, "Socket_Turbo_3", new Vector3(0, cs * 0.82f, -cs * 0.90f));
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

            Port(r, "Port_RotationInput", PortShaft, new Vector3(0, 0, -cs * 0.48f), new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.05f), PrimitiveType.Cube, Vector3.back);
            Port(r, "Port_RotationOutput_Straight", PortShaft, new Vector3(0, 0, cs * 0.48f), new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.05f), PrimitiveType.Cube, Vector3.forward);
            Port(r, "Port_RotationOutput_Up", PortShaft, new Vector3(0, cs * 0.48f, 0), new Vector3(cs * 0.13f, cs * 0.05f, cs * 0.13f), PrimitiveType.Cube, Vector3.up);
            Port(r, "Port_RotationOutput_Down", PortShaft, new Vector3(0, -cs * 0.48f, 0), new Vector3(cs * 0.13f, cs * 0.05f, cs * 0.13f), PrimitiveType.Cube, Vector3.down);
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
        // ════════════════════════════════════════════════════════════════
        //  DRIVE SHAFT — line shaft on two pillow-block bearings (v17 remake)
        //  Static: bearing pedestals, mounting feet, bolt circles.
        //  Spinning: shaft rod, spline section, keyway, U-joint yoke, collars.
        // ════════════════════════════════════════════════════════════════
        static void BuildDriveShaft(GameObject r, float cs)
        {
            // ── NO floor mounts / pedestals (v20) ──────────────────────
            // Drive shafts on a grid are always coupled port-to-port between
            // machines — never stood on the deck — so the module is a pure
            // floating shaft line: coupler flanges at the ends, spinning rod
            // through the middle, and NOTHING hanging off the axis to clip
            // through decks, hulls or neighbour blocks.
            // (v18 removed the ground feet; the pillow-block pedestals stayed
            //  and still read as "floor mounts" — v20 removes those too.)

            // ── End mounting flanges with bolt circles (static) ────────
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -cs * 0.455f : cs * 0.455f;
                var flange = Cyl(r, Steel, new Vector3(0, cs * 0.015f, z), cs * 0.16f, cs * 0.035f);
                flange.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                for (int bi = 0; bi < 6; bi++)
                {
                    float ang = bi * 60f * Mathf.Deg2Rad;
                    Sphere(r, DarkSteel,
                        new Vector3(Mathf.Cos(ang) * cs * 0.125f, cs * 0.015f + Mathf.Sin(ang) * cs * 0.125f,
                            z + (side == 0 ? -cs * 0.025f : cs * 0.025f)), cs * 0.030f);
                }
            }

            // ── Spinning assembly (driver/animator rotates "ShaftSpin") ─
            var spin = new GameObject("ShaftSpin");
            spin.transform.SetParent(r.transform, false);
            spin.transform.localPosition = new Vector3(0, cs * 0.015f, 0);

            // Main polished shaft — spans the FULL cell (v19): coaxial neighbours'
            // rods meet exactly at the shared cell face, so chained shafts touch.
            var shaft = Cyl(spin, Chrome, V0, cs * 0.055f, cs * 1.00f);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Splined mid-section: 8 ribs running the middle third of the rod.
            for (int si = 0; si < 8; si++)
            {
                float ang = si * 45f * Mathf.Deg2Rad;
                float sx = Mathf.Cos(ang) * cs * 0.058f;
                float sy = Mathf.Sin(ang) * cs * 0.058f;
                Box(spin, Steel, new Vector3(sx, sy, 0),
                    new Vector3(cs * 0.022f, cs * 0.022f, cs * 0.28f));
            }

            // Keyway bar along the shaft (reads rotation instantly).
            Box(spin, Brass, new Vector3(cs * 0.062f, 0f, 0f),
                new Vector3(cs * 0.020f, cs * 0.018f, cs * 0.72f));

            // Universal-joint yoke at the center (two crossing arms + hub).
            Box(spin, DarkSteel, V0, new Vector3(cs * 0.145f, cs * 0.028f, cs * 0.028f));
            Box(spin, DarkSteel, V0, new Vector3(cs * 0.028f, cs * 0.028f, cs * 0.145f));
            Sphere(spin, Bronze, V0, cs * 0.070f);

            // Clamp collars inboard of each end flange.
            for (int side = 0; side < 2; side++)
            {
                float z = side == 0 ? -cs * 0.375f : cs * 0.375f;
                var collar = Cyl(spin, DarkSteel, new Vector3(0, 0, z), cs * 0.090f, cs * 0.035f);
                collar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Sphere(spin, Steel, new Vector3(cs * 0.088f, 0, z), cs * 0.020f); // set screw
            }

            // ── Gold coupling rings at both rod tips (v19) ────────────
            // Chained shafts meet ring-to-ring at the shared cell face — visually a
            // bolted coupling. The named ports also let a HELD shaft snap exactly in
            // extension of a placed one (shaft snap targets Port_ShaftIO* prefixes).
            var ringF = Port(r, "Port_ShaftIO_F", PortShaft,
                new Vector3(0, cs * 0.015f, cs * 0.50f),
                new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.035f), PrimitiveType.Cylinder, Vector3.forward);
            var ringB = Port(r, "Port_ShaftIO_B", PortShaft,
                new Vector3(0, cs * 0.015f, -cs * 0.50f),
                new Vector3(cs * 0.13f, cs * 0.13f, cs * 0.035f), PrimitiveType.Cylinder, Vector3.back);

            Socket(r, "Socket_DriveCore", V0);
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATOR — heavy shaft-driven maritime dynamo
        // ════════════════════════════════════════════════════════════════
        // ════════════════════════════════════════════════════════════════
        //  GENERATOR — shaft-driven marine alternator (v17 remake)
        //  Layout: skid rails → stator barrel with cooling fins → rear fan
        //  cowl → OPEN front bell with a safety-yellow guard ring so the
        //  driveshaft input coupling is always visible (easy orientation).
        //  "GenRotor" + "ShaftSpin" pivots are physically driven by the
        //  MaritimeAnimator — names must not change.
        // ════════════════════════════════════════════════════════════════
        static void BuildGenerator(GameObject r, float cs)
        {
            float bodyY = cs * 0.02f;   // shaft centerline height

            // ── Skid frame: twin rail beams + cross feet ───────────────
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -cs * 0.26f : cs * 0.26f;
                Box(r, DarkSteel, new Vector3(x, -cs * 0.42f, 0), new Vector3(cs * 0.16f, cs * 0.10f, cs * 1.30f));
            }
            for (int fi = 0; fi < 2; fi++)
            {
                float z = fi == 0 ? -cs * 0.55f : cs * 0.55f;
                Box(r, DarkSteel, new Vector3(0, -cs * 0.46f, z), new Vector3(cs * 0.72f, cs * 0.06f, cs * 0.16f));
            }

            // ── Stator barrel (the big blue-grey drum) ─────────────────
            var barrel = Cyl(r, Steel, new Vector3(0, bodyY, cs * 0.10f), cs * 0.34f, cs * 0.85f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Barrel band rings.
            for (int ri = 0; ri < 3; ri++)
            {
                float z = cs * (-0.14f + ri * 0.24f);
                var band = Cyl(r, DarkSteel, new Vector3(0, bodyY, z), cs * 0.355f, cs * 0.03f);
                band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            // Cooling fins on top + sides, running fore-aft.
            for (int fi = 0; fi < 7; fi++)
            {
                float z = Mathf.Lerp(-cs * 0.20f, cs * 0.40f, fi / 6f);
                Box(r, AluminumSilver, new Vector3(0, bodyY + cs * 0.36f, z), new Vector3(cs * 0.50f, cs * 0.035f, cs * 0.05f));
            }
            // Brass stator frame ties.
            Box(r, Brass, new Vector3(cs * 0.36f, bodyY, cs * 0.10f), new Vector3(cs * 0.05f, cs * 0.22f, cs * 0.62f));
            Box(r, Brass, new Vector3(-cs * 0.36f, bodyY, cs * 0.10f), new Vector3(cs * 0.05f, cs * 0.22f, cs * 0.62f));

            // ── Rear bell + fan cowl with ventilation slots ────────────
            var rearBell = Cyl(r, CastIron, new Vector3(0, bodyY, cs * 0.60f), cs * 0.30f, cs * 0.14f);
            rearBell.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var cowl = Cyl(r, DarkSteel, new Vector3(0, bodyY, cs * 0.72f), cs * 0.26f, cs * 0.12f);
            cowl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int vi = 0; vi < 8; vi++)
            {
                float ang = vi * 45f * Mathf.Deg2Rad;
                Box(r, Rubber, new Vector3(Mathf.Cos(ang) * cs * 0.21f, bodyY + Mathf.Sin(ang) * cs * 0.21f, cs * 0.785f),
                    new Vector3(cs * 0.055f, cs * 0.055f, cs * 0.02f));
            }

            // ── Rotor (animated "GenRotor" — copper pole stacks) ───────
            var rotor = new GameObject("GenRotor");
            rotor.transform.SetParent(r.transform, false);
            rotor.transform.localPosition = new Vector3(0, bodyY, cs * 0.04f);
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-cs * 0.16f, cs * 0.20f, i / 3f);
                var coil = Cyl(rotor, Copper, new Vector3(0, 0, z), cs * 0.185f, cs * 0.44f);
                coil.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            Box(rotor, Steel, V0, new Vector3(cs * 0.16f, cs * 0.14f, cs * 0.60f));

            // ── Terminal box (top, toward the rear) ────────────────────
            Box(r, CastIron, new Vector3(0, bodyY + cs * 0.46f, cs * 0.42f), new Vector3(cs * 0.34f, cs * 0.16f, cs * 0.32f));
            Box(r, LabelBlue, new Vector3(0, bodyY + cs * 0.547f, cs * 0.42f), new Vector3(cs * 0.26f, cs * 0.012f, cs * 0.24f));
            // Three cable glands facing up.
            for (int gi = 0; gi < 3; gi++)
            {
                float gx = -cs * 0.10f + gi * cs * 0.10f;
                var gland = Cyl(r, Brass, new Vector3(gx, bodyY + cs * 0.545f, cs * 0.50f), cs * 0.028f, cs * 0.05f);
                gland.transform.localRotation = Quaternion.identity;
            }

            // ── OPEN FRONT BELL + guarded driveshaft input ─────────────
            // Aperture ring instead of a solid face: four frame rails around a
            // round hole so the spinning coupling is always visible from any angle.
            float fz = -cs * 0.52f; // front face z
            Box(r, CastIron, new Vector3(0, bodyY + cs * 0.28f, fz), new Vector3(cs * 0.56f, cs * 0.10f, cs * 0.10f)); // top rail
            Box(r, CastIron, new Vector3(0, bodyY - cs * 0.28f, fz), new Vector3(cs * 0.56f, cs * 0.10f, cs * 0.10f)); // bottom rail
            Box(r, CastIron, new Vector3(cs * 0.28f, bodyY, fz), new Vector3(cs * 0.10f, cs * 0.48f, cs * 0.10f));   // +x post
            Box(r, CastIron, new Vector3(-cs * 0.28f, bodyY, fz), new Vector3(cs * 0.10f, cs * 0.48f, cs * 0.10f));  // -x post
            // Safety-yellow guard ring around the spinning stub.
            var guardRing = Cyl(r, YellowPaint, new Vector3(0, bodyY, fz - cs * 0.045f), cs * 0.215f, cs * 0.035f);
            guardRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var guardInner = Cyl(r, Rubber, new Vector3(0, bodyY, fz - cs * 0.04f), cs * 0.165f, cs * 0.03f);
            guardInner.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Spinning input coupling — the OF CENTER visual anchor, animated.
            var shaftSpin = new GameObject("ShaftSpin");
            shaftSpin.transform.SetParent(r.transform, false);
            shaftSpin.transform.localPosition = new Vector3(0, bodyY, -cs * 0.60f);
            var coupling = Cyl(shaftSpin, Chrome, new Vector3(0, 0, cs * 0.06f), cs * 0.105f, cs * 0.10f);
            coupling.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Coupling bolt circle (4 bolt heads) — rotation is unmistakable.
            for (int bi = 0; bi < 4; bi++)
            {
                float ang = bi * 90f * Mathf.Deg2Rad;
                Sphere(shaftSpin, DarkSteel,
                    new Vector3(Mathf.Cos(ang) * cs * 0.078f, Mathf.Sin(ang) * cs * 0.078f, 0f), cs * 0.032f);
            }
            // Stub shaft reaching through the guard ring to the port.
            var stub = Cyl(shaftSpin, Chrome, new Vector3(0, 0, -cs * 0.16f), cs * 0.062f, cs * 0.26f);
            stub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Sphere(shaftSpin, Brass, new Vector3(cs * 0.055f, 0, -cs * 0.16f), cs * 0.018f); // key dot

            // The shaft-input port BEYOND the guard ring — gold, unmistakable.
            Port(r, "Port_ShaftInput", PortShaft, new Vector3(0, bodyY, -cs * 0.72f),
                new Vector3(cs * 0.15f, cs * 0.15f, cs * 0.05f));

            Socket(r, "Socket_StatorAxis", new Vector3(0, bodyY, cs * 0.10f));
        }

        // ════════════════════════════════════════════════════════════════
        //  EXHAUST PIPE — straight horizontal stack section (v18 remake)
        //  One clean run: bolted intake flange (−Z, engine side) → straight
        //  tube with weld rings + heat bands → outlet rim (+Z, smoke side).
        //  A chrome gas-tap flange on top (Port_ExhaustGasIO) lets gas pipes
        //  route exhaust gas away. No deck base, no ground supports.
        // ════════════════════════════════════════════════════════════════
        static void BuildExhaustPipe(GameObject r, float cs)
        {
            // ── Bolted intake flange (face that kisses the engine port) ──
            var flange = Cyl(r, DarkSteel, new Vector3(0, 0, -cs * 0.43f), cs * 0.20f, cs * 0.05f);
            flange.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (int bi = 0; bi < 6; bi++)
            {
                float ang = bi * 60f * Mathf.Deg2Rad;
                Sphere(r, Steel, new Vector3(Mathf.Cos(ang) * cs * 0.155f,
                    Mathf.Sin(ang) * cs * 0.155f, -cs * 0.455f), cs * 0.028f);
            }
            // Heat-discoloured intake lip right behind the flange.
            var lip = Cyl(r, HeatOrange, new Vector3(0, 0, -cs * 0.385f), cs * 0.135f, cs * 0.05f);
            lip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // The engine-side connection port (red = exhaust input).
            Port(r, "Port_ExhaustInput", PortExhaust, new Vector3(0, 0, -cs * 0.49f),
                new Vector3(cs * 0.14f, cs * 0.14f, cs * 0.05f), PrimitiveType.Cube, Vector3.back);

            // ── Straight main tube ────────────────────────────────────
            var tube = Cyl(r, CastIron, new Vector3(0, 0, 0), cs * 0.125f, cs * 0.80f);
            tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Weld rings along the run.
            for (int wi = 0; wi < 3; wi++)
            {
                float wz = cs * (-0.24f + wi * 0.24f);
                var ring = Cyl(r, DarkSteel, new Vector3(0, 0, wz), cs * 0.138f, cs * 0.022f);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            // Heat bands near the hot intake half (metal tempering colours).
            Cyl(r, HeatBlue,   new Vector3(0, 0, -cs * 0.30f), cs * 0.130f, cs * 0.030f)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cyl(r, HeatOrange, new Vector3(0, 0, -cs * 0.18f), cs * 0.128f, cs * 0.025f)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Exhaust-gas tap (chrome, top centre — GasPipe hookup) ─
            Cyl(r, Chrome, new Vector3(0, cs * 0.16f, cs * 0.05f), cs * 0.055f, cs * 0.09f);
            var tapRing = Cyl(r, DarkSteel, new Vector3(0, cs * 0.205f, cs * 0.05f), cs * 0.075f, cs * 0.025f);
            Sphere(r, Steel, new Vector3(cs * 0.045f, cs * 0.235f, cs * 0.05f), cs * 0.020f);
            Sphere(r, Steel, new Vector3(-cs * 0.045f, cs * 0.235f, cs * 0.05f), cs * 0.020f);
            Port(r, "Port_ExhaustGasIO", Chrome, new Vector3(0, cs * 0.26f, cs * 0.05f),
                new Vector3(cs * 0.10f, cs * 0.05f, cs * 0.10f), PrimitiveType.Cube, Vector3.up);

            // ── Outlet rim + dark throat (smoke exits here, +Z) ───────
            var rim = Cyl(r, DarkSteel, new Vector3(0, 0, cs * 0.43f), cs * 0.155f, cs * 0.05f);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cyl(r, Rubber, new Vector3(0, 0, cs * 0.455f), cs * 0.105f, cs * 0.03f)
                .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Socket(r, "Socket_StackTop", new Vector3(0, 0, cs * 0.52f));
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
                new Vector3(cs * 0.14f, cs * 0.04f, cs * 0.14f), PrimitiveType.Cube, Vector3.down);

            // ── Discharge outlet port (blue, on the side — connects to tanks) ──
            var outlet = Cyl(r, PortFuel, new Vector3(cs * 0.35f, -cs * 0.1f, 0), cs * 0.08f, cs * 0.15f);
            outlet.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Port(r, "Port_WaterOutlet", PortFuel, new Vector3(cs * 0.45f, -cs * 0.1f, 0),
                new Vector3(cs * 0.04f, cs * 0.12f, cs * 0.12f), PrimitiveType.Cube, Vector3.right);

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


        /// <summary>Create a named I/O port GameObject with a mesh primitive inside.
        /// The container is named (e.g. "Port_FuelInput") so you can select it in the
        /// prefab hierarchy and move it. The child mesh can be swapped cube↔cylinder
        /// by deleting and re-adding a different primitive in the editor.</summary>
        /// <param name="outward">Machine-local direction the attaching block connects
        /// FROM (e.g. Vector3.up for a top collector). The container's +Z AND a
        /// MaritimePortFacing tag are aligned to it, so snapping, ghost rotation and
        /// pipe arms read TRUE authored port orientation instead of guessing an axis
        /// from a position offset (which mis-aimed centre-line ports).</param>
        static GameObject Port(GameObject parent, string portName, Material m, Vector3 pos, Vector3 scale,
            PrimitiveType shape = PrimitiveType.Cube, Vector3 outward = default)
        {
            var container = new GameObject(portName);
            container.transform.SetParent(parent.transform, false);
            container.transform.localPosition = pos;
            if (outward.sqrMagnitude > 0.0001f)
            {
                Vector3 dir = outward.normalized;
                Vector3 guide = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                container.transform.localRotation = Quaternion.LookRotation(dir, guide);
                var facing = container.AddComponent<MaritimePortFacing>();
                facing.localOutward = dir;
            }
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
