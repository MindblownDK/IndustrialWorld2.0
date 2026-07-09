// Assets/Scripts/VoxelEngine/Editor/FactoryPrefabVisualsBuilder.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Adds procedural visual meshes to Factory and HV prefabs created by Step 17.
    /// Run from: Tools ▸ Voxel Engine ▸ Add Factory Prefab Visuals
    /// Non-destructive: checks for existing children before adding.
    /// </summary>
    public static class FactoryPrefabVisualsBuilder
    {
        private const string ROOT     = "Assets/VoxelEngineAssets";
        private const string FAC      = ROOT + "/Factory/Prefabs";
        private const string HV       = ROOT + "/HighVoltage/Prefabs";
        private const string FAC_MATS = FAC + "/Materials";
        private const string HV_MATS  = HV + "/Materials";

        [MenuItem("Tools/Voxel Engine/Add Factory Prefab Visuals")]
        public static void BuildAll()
        {
            int count = 0;

            // Ensure material subfolders exist.
            EnsureFolder(FAC);
            EnsureFolder(HV);
            EnsureFolder(FAC_MATS);
            EnsureFolder(HV_MATS);

            // ═══ CONVEYOR BELTS ═══
            count += BuildConveyor("ConveyorBelt_Basic",   new Color(0.15f, 0.16f, 0.18f));
            count += BuildConveyor("ConveyorBelt_Fast",    new Color(0.20f, 0.22f, 0.26f));
            count += BuildConveyor("ConveyorBelt_Express", new Color(0.25f, 0.28f, 0.32f));

            // ═══ CONVEYOR CORNERS & SLOPES ═══
            count += BuildConveyorCorner("ConveyorBelt_Basic_Corner",   new Color(0.15f, 0.16f, 0.18f));
            count += BuildConveyorCorner("ConveyorBelt_Fast_Corner",    new Color(0.20f, 0.22f, 0.26f));
            count += BuildConveyorCorner("ConveyorBelt_Express_Corner", new Color(0.25f, 0.28f, 0.32f));
            count += BuildConveyorRamp("ConveyorBelt_Basic_RampUp",     new Color(0.15f, 0.16f, 0.18f), true);
            count += BuildConveyorRamp("ConveyorBelt_Basic_RampDown",   new Color(0.15f, 0.16f, 0.18f), false);

            // ═══ FUNNEL ═══
            count += BuildFunnel();

            // ═══ CONVEYOR CHUTE ═══
            count += BuildChute();

            // ═══ MACHINES ═══
            count += BuildMachine("Crusher",        new Color(0.55f, 0.40f, 0.30f), new Color(0.85f, 0.55f, 0.20f));
            count += BuildMachine("Assembler_Mk1",   new Color(0.20f, 0.50f, 0.85f), new Color(0.22f, 0.78f, 0.42f));
            count += BuildMachine("Assembler_Mk2",   new Color(0.25f, 0.55f, 0.90f), new Color(0.18f, 0.72f, 0.88f));
            count += BuildMachine("Assembler_Mk3",   new Color(0.30f, 0.60f, 0.95f), new Color(0.58f, 0.30f, 0.84f));

            // ═══ LIGHTS ═══
            count += BuildGridLight();
            count += BuildLEDStrip();

            // ═══ HV INFRASTRUCTURE ═══
            count += BuildPowerPole();
            count += BuildSubstation();
            count += BuildHVTower();
            count += BuildStepUpTransformer();
            count += BuildStepDownTransformer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Factory Prefab Visuals",
                "Done! Added visuals to " + count + " prefabs.\n\n" +
                "Check the prefabs in:\n" +
                "  " + FAC + "\n  " + HV, "OK");
        }

        // ─── Helpers ─────────────────────────────────────────────────

        private static Material MakeMat(string folder, string name, Color c)
        {
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = name };
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            EnsureFolder(folder);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        private static void AddChild(GameObject parent, PrimitiveType type, string childName,
            Vector3 pos, Vector3 scale, Color color, string matFolder,
            Quaternion rot = default, bool hasRot = false)
        {
            var mat = MakeMat(matFolder, "Mat_" + childName, color);

            var existing = parent.transform.Find(childName);
            if (existing != null)
            {
                // Child already exists — just ensure the material is in the correct folder.
                var renderer = existing.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = mat;
                return;
            }

            var go = GameObject.CreatePrimitive(type);
            go.name = childName;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (hasRot) go.transform.localRotation = rot;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            // Recursively ensure parent exists first.
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent); // recurse up
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static GameObject LoadPrefab(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null) return null;
            try { return PrefabUtility.LoadPrefabContents(path); }
            catch { return null; }
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ─── Conveyor Belt ───────────────────────────────────────────

        private static int BuildConveyor(string name, Color beltColor)
        {
            string path = FAC + "/" + name + ".prefab";
            var root = LoadPrefab(path);
            if (root == null) { Debug.LogWarning("[FactoryVisuals] Prefab not found: " + path); return 0; }
            string mf = FAC_MATS;

            AddChild(root, PrimitiveType.Cube, "BeltSurface", new Vector3(0, 0.48f, 0), new Vector3(0.85f, 0.04f, 0.95f), beltColor, mf);
            AddChild(root, PrimitiveType.Cube, "RailLeft",    new Vector3(-0.45f, 0.50f, 0), new Vector3(0.06f, 0.08f, 0.95f), new Color(0.35f, 0.38f, 0.42f), mf);
            AddChild(root, PrimitiveType.Cube, "RailRight",   new Vector3(0.45f, 0.50f, 0),  new Vector3(0.06f, 0.08f, 0.95f), new Color(0.35f, 0.38f, 0.42f), mf);
            AddChild(root, PrimitiveType.Cylinder, "RollerF", new Vector3(0, 0.46f, 0.45f),  new Vector3(0.08f, 0.42f, 0.08f), new Color(0.50f, 0.52f, 0.55f), mf, Quaternion.Euler(0, 0, 90), true);
            AddChild(root, PrimitiveType.Cylinder, "RollerB", new Vector3(0, 0.46f, -0.45f), new Vector3(0.08f, 0.42f, 0.08f), new Color(0.50f, 0.52f, 0.55f), mf, Quaternion.Euler(0, 0, 90), true);
            AddChild(root, PrimitiveType.Cube, "LegFL", new Vector3(-0.40f, 0.22f, 0.40f),  new Vector3(0.06f, 0.44f, 0.06f), new Color(0.30f, 0.32f, 0.35f), mf);
            AddChild(root, PrimitiveType.Cube, "LegFR", new Vector3(0.40f, 0.22f, 0.40f),   new Vector3(0.06f, 0.44f, 0.06f), new Color(0.30f, 0.32f, 0.35f), mf);
            AddChild(root, PrimitiveType.Cube, "LegBL", new Vector3(-0.40f, 0.22f, -0.40f), new Vector3(0.06f, 0.44f, 0.06f), new Color(0.30f, 0.32f, 0.35f), mf);
            AddChild(root, PrimitiveType.Cube, "LegBR", new Vector3(0.40f, 0.22f, -0.40f),  new Vector3(0.06f, 0.44f, 0.06f), new Color(0.30f, 0.32f, 0.35f), mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for " + name);
            return 1;
        }

        // ─── Conveyor Chute ──────────────────────────────────────────

        private static int BuildChute()
        {
            string path = FAC + "/ConveyorChute.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;

            AddChild(root, PrimitiveType.Cube, "ChuteBody",    Vector3.zero, new Vector3(0.6f, 1.0f, 0.6f),    new Color(0.30f, 0.33f, 0.38f), mf);
            AddChild(root, PrimitiveType.Cube, "ChuteChannel", Vector3.zero, new Vector3(0.45f, 0.95f, 0.45f), new Color(0.12f, 0.13f, 0.16f), mf);
            AddChild(root, PrimitiveType.Cube, "RimTop",  new Vector3(0, 0.52f, 0),  new Vector3(0.65f, 0.04f, 0.65f), new Color(0.40f, 0.42f, 0.46f), mf);
            AddChild(root, PrimitiveType.Cube, "RimBot",  new Vector3(0, -0.52f, 0), new Vector3(0.65f, 0.04f, 0.65f), new Color(0.40f, 0.42f, 0.46f), mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for ConveyorChute");
            return 1;
        }

        // ─── Machine (Crusher / Assembler) ───────────────────────────

        private static int BuildMachine(string name, Color bodyColor, Color accentColor)
        {
            string path = FAC + "/" + name + ".prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;
            Color dark = new Color(bodyColor.r * 0.8f, bodyColor.g * 0.8f, bodyColor.b * 0.8f);
            Color darker = new Color(bodyColor.r * 0.6f, bodyColor.g * 0.6f, bodyColor.b * 0.6f);

            AddChild(root, PrimitiveType.Cube,   "MachineBody",  new Vector3(0, 0.5f, 0),      new Vector3(1.2f, 1.0f, 1.2f),  bodyColor, mf);
            AddChild(root, PrimitiveType.Cube,   "InputHopper",  new Vector3(0, 1.15f, 0),     new Vector3(0.6f, 0.3f, 0.6f),  dark, mf);
            AddChild(root, PrimitiveType.Cube,   "OutputPort",   new Vector3(0, 0.2f, 0.62f),  new Vector3(0.4f, 0.3f, 0.04f), accentColor, mf);
            AddChild(root, PrimitiveType.Sphere,  "StatusLED",   new Vector3(0.4f, 0.9f, 0.62f), new Vector3(0.1f, 0.1f, 0.1f), new Color(0.22f, 0.78f, 0.42f), mf);
            AddChild(root, PrimitiveType.Cube,   "BasePlate",    new Vector3(0, 0.02f, 0),      new Vector3(1.3f, 0.04f, 1.3f), new Color(0.25f, 0.27f, 0.30f), mf);
            AddChild(root, PrimitiveType.Cube,   "VentL",        new Vector3(-0.62f, 0.5f, 0),  new Vector3(0.02f, 0.5f, 0.8f), darker, mf);
            AddChild(root, PrimitiveType.Cube,   "VentR",        new Vector3(0.62f, 0.5f, 0),   new Vector3(0.02f, 0.5f, 0.8f), darker, mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for " + name);
            return 1;
        }

        // ─── Grid Light ──────────────────────────────────────────────

        private static int BuildGridLight()
        {
            string path = FAC + "/GridLight.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;

            AddChild(root, PrimitiveType.Cube,   "Housing", new Vector3(0, 0.15f, 0),      new Vector3(0.3f, 0.12f, 0.3f),  new Color(0.35f, 0.38f, 0.42f), mf);
            AddChild(root, PrimitiveType.Sphere,  "Lens",   new Vector3(0, 0.22f, 0.12f),  new Vector3(0.18f, 0.18f, 0.08f), new Color(1f, 0.95f, 0.7f), mf);
            AddChild(root, PrimitiveType.Cube,   "Bracket", new Vector3(0, 0.05f, -0.12f), new Vector3(0.15f, 0.10f, 0.06f), new Color(0.30f, 0.32f, 0.35f), mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for GridLight");
            return 1;
        }

        // ─── LED Strip ───────────────────────────────────────────────

        private static int BuildLEDStrip()
        {
            string path = FAC + "/LEDStrip.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;

            AddChild(root, PrimitiveType.Cube, "StripBody", Vector3.zero, new Vector3(1f, 0.02f, 0.04f), new Color(0.18f, 0.72f, 0.88f), mf);
            for (int i = 0; i < 5; i++)
                AddChild(root, PrimitiveType.Sphere, "LED_" + i,
                    new Vector3(-0.4f + i * 0.2f, 0.02f, 0),
                    new Vector3(0.03f, 0.03f, 0.03f),
                    new Color(0.18f, 0.85f, 1f), mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for LEDStrip");
            return 1;
        }

        // ─── Power Pole ─────────────────────────────────────────────

        private static int BuildPowerPole()
        {
            string path = HV + "/PowerPole.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = HV_MATS;
            Color wood = new Color(0.45f, 0.40f, 0.35f);

            AddChild(root, PrimitiveType.Cylinder, "Shaft",    new Vector3(0, 1.5f, 0), new Vector3(0.12f, 1.5f, 0.12f), wood, mf);
            AddChild(root, PrimitiveType.Cube,     "CrossArm", new Vector3(0, 3f, 0),   new Vector3(1.2f, 0.08f, 0.08f), wood, mf);
            AddChild(root, PrimitiveType.Cylinder, "BasePlate", new Vector3(0, 0.02f, 0), new Vector3(0.5f, 0.02f, 0.5f), new Color(0.35f, 0.33f, 0.30f), mf);
            for (int i = 0; i < 6; i++)
            {
                float angle = (360f / 6f) * i * Mathf.Deg2Rad;
                AddChild(root, PrimitiveType.Sphere, "Conn_" + i,
                    new Vector3(Mathf.Cos(angle) * 0.4f, 3.1f, Mathf.Sin(angle) * 0.4f),
                    new Vector3(0.08f, 0.08f, 0.08f),
                    new Color(0.22f, 0.78f, 0.42f), mf);
            }

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for PowerPole");
            return 1;
        }

        // ─── Electrical Substation ──────────────────────────────────

        private static int BuildSubstation()
        {
            string path = HV + "/ElectricalSubstation.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = HV_MATS;
            Color metal = new Color(0.40f, 0.42f, 0.46f);

            AddChild(root, PrimitiveType.Cube, "Body",      new Vector3(0, 2f, 0), new Vector3(2f, 3f, 1.5f),    metal, mf);
            AddChild(root, PrimitiveType.Cube, "Foundation", Vector3.zero,           new Vector3(3f, 0.3f, 2f),   new Color(0.55f, 0.53f, 0.50f), mf);
            AddChild(root, PrimitiveType.Cube, "Stripe",     new Vector3(0, 2.75f, 0.76f), new Vector3(1.8f, 0.15f, 0.01f), new Color(0.92f, 0.60f, 0.12f), mf);
            AddChild(root, PrimitiveType.Cylinder, "InsL",   new Vector3(-1.5f, 5f, 0), new Vector3(0.15f, 0.6f, 0.15f), new Color(0.85f, 0.82f, 0.75f), mf);
            AddChild(root, PrimitiveType.Cylinder, "InsR",   new Vector3(1.5f, 5f, 0),  new Vector3(0.15f, 0.6f, 0.15f), new Color(0.85f, 0.82f, 0.75f), mf);
            for (int i = 0; i < 4; i++)
            {
                float y = 1.2f + i * 0.6f;
                AddChild(root, PrimitiveType.Cube, "FinL_" + i, new Vector3(-1.1f, y, 0), new Vector3(0.08f, 0.5f, 1.3f), metal, mf);
                AddChild(root, PrimitiveType.Cube, "FinR_" + i, new Vector3(1.1f, y, 0),  new Vector3(0.08f, 0.5f, 1.3f), metal, mf);
            }

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for Substation");
            return 1;
        }

        // ─── HV Transmission Tower ──────────────────────────────────

        private static int BuildHVTower()
        {
            string path = HV + "/HighVoltagePole.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = HV_MATS;
            Color steel = new Color(0.52f, 0.54f, 0.56f);
            float h = 12f, bw = 1.5f, tw = 0.6f;

            // Four tapered legs.
            Vector3[] baseC = new Vector3[] { new Vector3(-bw,0,-bw), new Vector3(bw,0,-bw), new Vector3(bw,0,bw), new Vector3(-bw,0,bw) };
            Vector3[] topC  = new Vector3[] { new Vector3(-tw,h,-tw), new Vector3(tw,h,-tw), new Vector3(tw,h,tw), new Vector3(-tw,h,tw) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 mid = (baseC[i] + topC[i]) * 0.5f;
                float len = Vector3.Distance(baseC[i], topC[i]);
                Vector3 dir = (topC[i] - baseC[i]).normalized;
                AddChild(root, PrimitiveType.Cube, "Leg_" + i,
                    mid, new Vector3(0.10f, 0.10f, len), steel, mf,
                    Quaternion.LookRotation(dir), true);
            }

            // Cross-arms at two levels.
            for (int arm = 0; arm < 2; arm++)
            {
                float armY = h - 0.5f - arm * 1.8f;
                AddChild(root, PrimitiveType.Cube, "Arm_" + arm,
                    new Vector3(0, armY, 0), new Vector3(7f, 0.08f, 0.08f), steel, mf);
                AddChild(root, PrimitiveType.Cube, "BrL_" + arm,
                    new Vector3(-2.5f, armY - 0.5f, 0), new Vector3(0.04f, 0.04f, 1.5f), steel, mf,
                    Quaternion.Euler(0, 0, 35f), true);
                AddChild(root, PrimitiveType.Cube, "BrR_" + arm,
                    new Vector3(2.5f, armY - 0.5f, 0), new Vector3(0.04f, 0.04f, 1.5f), steel, mf,
                    Quaternion.Euler(0, 0, -35f), true);
            }

            // X-braces.
            for (int b = 0; b < 3; b++)
            {
                float by = 2f + b * 3f;
                AddChild(root, PrimitiveType.Cube, "XB_" + b,
                    new Vector3(0, by, 0), new Vector3(0.04f, 0.04f, 2.5f), steel, mf,
                    Quaternion.Euler(0, 0, 45f), true);
            }

            // Lightning rod + peak.
            AddChild(root, PrimitiveType.Cylinder, "L Rod", new Vector3(0, h + 0.75f, 0), new Vector3(0.04f, 0.75f, 0.04f), new Color(0.60f, 0.62f, 0.64f), mf);
            AddChild(root, PrimitiveType.Sphere, "Peak",    new Vector3(0, h + 1.5f, 0),  new Vector3(0.12f, 0.12f, 0.12f), new Color(0.60f, 0.62f, 0.64f), mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for HV Tower");
            return 1;
        }

        // ─── Step-Up Transformer (BLUE) ─────────────────────────────

        private static int BuildStepUpTransformer()
        {
            string path = HV + "/StepUpTransformer.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = HV_MATS;
            Color tank = new Color(0.55f, 0.56f, 0.52f);
            Color blue = new Color(0.15f, 0.45f, 0.85f);
            Color ceramic = new Color(0.70f, 0.60f, 0.45f);

            AddChild(root, PrimitiveType.Cube, "Foundation", Vector3.zero,                new Vector3(10f, 0.3f, 6f),    new Color(0.62f, 0.60f, 0.58f), mf);
            AddChild(root, PrimitiveType.Cube, "TankMain",   new Vector3(0, 2f, 0),       new Vector3(4f, 3.5f, 3f),     tank, mf);
            AddChild(root, PrimitiveType.Cube, "TankSec",    new Vector3(3.5f, 1.3f, 0),  new Vector3(2.5f, 2.2f, 2f),   tank, mf);
            AddChild(root, PrimitiveType.Cube, "Sign",       new Vector3(0, 4.5f, 1.55f), new Vector3(2.5f, 0.6f, 0.05f), blue, mf);
            AddChild(root, PrimitiveType.Cube, "Arrow1",     new Vector3(-0.4f, 4.5f, 1.58f), new Vector3(0.15f, 0.4f, 0.02f), blue, mf);
            AddChild(root, PrimitiveType.Cube, "Arrow2",     new Vector3(0.4f, 4.5f, 1.58f),  new Vector3(0.15f, 0.4f, 0.02f), blue, mf);
            AddChild(root, PrimitiveType.Cube, "Cabinet",    new Vector3(-4f, 1f, 0),     new Vector3(1.2f, 1.8f, 0.8f), tank, mf);

            for (int i = 0; i < 5; i++)
            {
                float z = -1.2f + i * 0.6f;
                AddChild(root, PrimitiveType.Cube, "RadL_" + i, new Vector3(-2.3f, 1.5f, z), new Vector3(0.08f, 2.5f, 0.45f), tank, mf);
                AddChild(root, PrimitiveType.Cube, "RadR_" + i, new Vector3(2.3f, 1.5f, z),  new Vector3(0.08f, 2.5f, 0.45f), tank, mf);
            }
            for (int i = 0; i < 3; i++)
            {
                float x = -1.5f + i * 1.5f;
                AddChild(root, PrimitiveType.Cylinder, "HVB_" + i, new Vector3(x, 4.75f, 0),     new Vector3(0.22f, 1f, 0.22f),   ceramic, mf);
                AddChild(root, PrimitiveType.Cylinder, "LVB_" + i, new Vector3(x, 4.35f, -1.2f), new Vector3(0.18f, 0.6f, 0.18f), new Color(0.60f, 0.50f, 0.35f), mf);
            }

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for Step-Up Transformer");
            return 1;
        }

        // ─── Conveyor Corner (90° turn) ─────────────────────────────

        private static int BuildConveyorCorner(string name, Color beltColor)
        {
            string path = FAC + "/" + name + ".prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;
            Color rail = new Color(0.35f, 0.38f, 0.42f);
            Color leg = new Color(0.30f, 0.32f, 0.35f);

            // Corner belt surface (L-shaped from 2 boxes).
            AddChild(root, PrimitiveType.Cube, "BeltA",
                new Vector3(0, 0.48f, -0.25f), new Vector3(0.85f, 0.04f, 0.5f), beltColor, mf);
            AddChild(root, PrimitiveType.Cube, "BeltB",
                new Vector3(0.25f, 0.48f, 0), new Vector3(0.5f, 0.04f, 0.85f), beltColor, mf);

            // Corner rails.
            AddChild(root, PrimitiveType.Cube, "RailOuterL",
                new Vector3(-0.45f, 0.50f, -0.25f), new Vector3(0.06f, 0.08f, 0.5f), rail, mf);
            AddChild(root, PrimitiveType.Cube, "RailOuterB",
                new Vector3(0.25f, 0.50f, -0.45f), new Vector3(0.5f, 0.08f, 0.06f), rail, mf);
            AddChild(root, PrimitiveType.Cube, "RailInnerL",
                new Vector3(0.45f, 0.50f, 0.25f), new Vector3(0.06f, 0.08f, 0.5f), rail, mf);
            AddChild(root, PrimitiveType.Cube, "RailInnerB",
                new Vector3(-0.25f, 0.50f, 0.45f), new Vector3(0.5f, 0.08f, 0.06f), rail, mf);

            // 4 legs.
            AddChild(root, PrimitiveType.Cube, "LegFL", new Vector3(-0.40f, 0.22f, 0.40f),  new Vector3(0.06f, 0.44f, 0.06f), leg, mf);
            AddChild(root, PrimitiveType.Cube, "LegFR", new Vector3(0.40f, 0.22f, 0.40f),   new Vector3(0.06f, 0.44f, 0.06f), leg, mf);
            AddChild(root, PrimitiveType.Cube, "LegBL", new Vector3(-0.40f, 0.22f, -0.40f), new Vector3(0.06f, 0.44f, 0.06f), leg, mf);
            AddChild(root, PrimitiveType.Cube, "LegBR", new Vector3(0.40f, 0.22f, -0.40f),  new Vector3(0.06f, 0.44f, 0.06f), leg, mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for " + name);
            return 1;
        }

        // ─── Conveyor Ramp (slope up/down) ──────────────────────────

        private static int BuildConveyorRamp(string name, Color beltColor, bool rampUp)
        {
            string path = FAC + "/" + name + ".prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;
            Color rail = new Color(0.35f, 0.38f, 0.42f);
            Color leg = new Color(0.30f, 0.32f, 0.35f);

            // Angled belt surface.
            float angle = rampUp ? -26.5f : 26.5f;
            float yOff = rampUp ? 0.25f : 0.25f;
            AddChild(root, PrimitiveType.Cube, "BeltSurface",
                new Vector3(0, 0.48f + yOff, 0), new Vector3(0.85f, 0.04f, 1.12f), beltColor, mf,
                Quaternion.Euler(angle, 0, 0), true);

            // Side rails (angled).
            AddChild(root, PrimitiveType.Cube, "RailL",
                new Vector3(-0.45f, 0.50f + yOff, 0), new Vector3(0.06f, 0.08f, 1.12f), rail, mf,
                Quaternion.Euler(angle, 0, 0), true);
            AddChild(root, PrimitiveType.Cube, "RailR",
                new Vector3(0.45f, 0.50f + yOff, 0), new Vector3(0.06f, 0.08f, 1.12f), rail, mf,
                Quaternion.Euler(angle, 0, 0), true);

            // Support legs (taller on the high side).
            float legHigh = rampUp ? 0.70f : 0.22f;
            float legLow  = rampUp ? 0.22f : 0.70f;
            AddChild(root, PrimitiveType.Cube, "LegFL", new Vector3(-0.40f, legHigh * 0.5f, 0.40f),  new Vector3(0.06f, legHigh, 0.06f), leg, mf);
            AddChild(root, PrimitiveType.Cube, "LegFR", new Vector3(0.40f, legHigh * 0.5f, 0.40f),   new Vector3(0.06f, legHigh, 0.06f), leg, mf);
            AddChild(root, PrimitiveType.Cube, "LegBL", new Vector3(-0.40f, legLow * 0.5f, -0.40f),  new Vector3(0.06f, legLow, 0.06f),  leg, mf);
            AddChild(root, PrimitiveType.Cube, "LegBR", new Vector3(0.40f, legLow * 0.5f, -0.40f),   new Vector3(0.06f, legLow, 0.06f),  leg, mf);

            // Cross brace for structural support.
            AddChild(root, PrimitiveType.Cube, "CrossBrace",
                new Vector3(0, 0.25f, 0), new Vector3(0.80f, 0.04f, 0.04f), leg, mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for " + name);
            return 1;
        }

        // ─── Funnel (Import/Export item transfer) ───────────────────

        private static int BuildFunnel()
        {
            string path = FAC + "/Funnel.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = FAC_MATS;
            Color body = new Color(0.42f, 0.45f, 0.50f);
            Color hopper = new Color(0.50f, 0.53f, 0.58f);
            Color accent = new Color(0.22f, 0.78f, 0.42f);

            // Main body (tapered box — wider at top, narrower at bottom).
            AddChild(root, PrimitiveType.Cube, "FunnelBody",
                new Vector3(0, 0.4f, 0), new Vector3(0.8f, 0.6f, 0.8f), body, mf);

            // Top hopper opening (wider rim).
            AddChild(root, PrimitiveType.Cube, "HopperRim",
                new Vector3(0, 0.72f, 0), new Vector3(0.9f, 0.04f, 0.9f), hopper, mf);

            // Bottom output nozzle (narrower).
            AddChild(root, PrimitiveType.Cube, "OutputNozzle",
                new Vector3(0, 0.08f, 0), new Vector3(0.35f, 0.16f, 0.35f), body, mf);

            // Status LED (shows mode: green = import, amber = export).
            AddChild(root, PrimitiveType.Sphere, "StatusLED",
                new Vector3(0.35f, 0.55f, 0.42f), new Vector3(0.08f, 0.08f, 0.08f), accent, mf);

            // Direction arrow (front face indicator).
            AddChild(root, PrimitiveType.Cube, "DirectionArrow",
                new Vector3(0, 0.4f, 0.42f), new Vector3(0.25f, 0.20f, 0.02f), accent, mf);

            // Side mounting brackets.
            AddChild(root, PrimitiveType.Cube, "BracketL",
                new Vector3(-0.42f, 0.4f, 0), new Vector3(0.04f, 0.30f, 0.40f), new Color(0.35f, 0.38f, 0.42f), mf);
            AddChild(root, PrimitiveType.Cube, "BracketR",
                new Vector3(0.42f, 0.4f, 0), new Vector3(0.04f, 0.30f, 0.40f), new Color(0.35f, 0.38f, 0.42f), mf);

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for Funnel");
            return 1;
        }

        // ─── Step-Down Transformer (AMBER) ──────────────────────────

        private static int BuildStepDownTransformer()
        {
            string path = HV + "/StepDownTransformer.prefab";
            var root = LoadPrefab(path);
            if (root == null) return 0;
            string mf = HV_MATS;
            Color tank = new Color(0.50f, 0.52f, 0.48f);
            Color amber = new Color(0.92f, 0.60f, 0.12f);
            Color ceramic = new Color(0.72f, 0.62f, 0.42f);

            AddChild(root, PrimitiveType.Cube, "Foundation",  Vector3.zero,                new Vector3(12f, 0.3f, 7f),    new Color(0.60f, 0.58f, 0.55f), mf);
            AddChild(root, PrimitiveType.Cube, "TankPrimary", new Vector3(0, 2.2f, 0),     new Vector3(5f, 4f, 3.5f),     tank, mf);
            AddChild(root, PrimitiveType.Cube, "RegTank",     new Vector3(-4f, 1.5f, 0),   new Vector3(2f, 2.5f, 2.2f),   tank, mf);
            AddChild(root, PrimitiveType.Cube, "Sign",        new Vector3(0, 5f, 1.8f),    new Vector3(3f, 0.7f, 0.05f),  amber, mf);
            AddChild(root, PrimitiveType.Cube, "Arrow1",      new Vector3(-0.4f, 5f, 1.83f), new Vector3(0.15f, 0.4f, 0.02f), amber, mf);
            AddChild(root, PrimitiveType.Cube, "Arrow2",      new Vector3(0.4f, 5f, 1.83f),  new Vector3(0.15f, 0.4f, 0.02f), amber, mf);
            AddChild(root, PrimitiveType.Cube, "Building",    new Vector3(-4.5f, 1.2f, -2.5f), new Vector3(2f, 2.2f, 1.8f), new Color(0.60f, 0.58f, 0.55f), mf);
            AddChild(root, PrimitiveType.Cylinder, "ArrL",    new Vector3(-4.5f, 2.5f, 0), new Vector3(0.2f, 2f, 0.2f), new Color(0.38f, 0.40f, 0.42f), mf);
            AddChild(root, PrimitiveType.Cylinder, "ArrR",    new Vector3(4.5f, 2.5f, 0),  new Vector3(0.2f, 2f, 0.2f), new Color(0.38f, 0.40f, 0.42f), mf);

            for (int i = 0; i < 7; i++)
            {
                float z = -1.5f + i * 0.5f;
                AddChild(root, PrimitiveType.Cube, "RadL_" + i, new Vector3(-2.8f, 1.8f, z), new Vector3(0.08f, 3f, 0.38f), tank, mf);
                AddChild(root, PrimitiveType.Cube, "RadR_" + i, new Vector3(2.8f, 1.8f, z),  new Vector3(0.08f, 3f, 0.38f), tank, mf);
            }
            for (int i = 0; i < 3; i++)
            {
                float xH = 1f + i * 1.5f;
                float xL = -1.5f + i * 1.2f;
                AddChild(root, PrimitiveType.Cylinder, "HVB_" + i, new Vector3(xH, 5.45f, 0),     new Vector3(0.25f, 1.25f, 0.25f), ceramic, mf);
                AddChild(root, PrimitiveType.Cylinder, "LVB_" + i, new Vector3(xL, 4.7f, -1.4f),  new Vector3(0.16f, 0.5f, 0.16f),  new Color(0.58f, 0.48f, 0.32f), mf);
            }
            for (int i = 0; i < 6; i++)
            {
                Color sc = (i % 2 == 0) ? new Color(0.12f, 0.12f, 0.12f) : amber;
                AddChild(root, PrimitiveType.Cube, "Warn_" + i,
                    new Vector3(-5f + i * 2f, 0.16f, 3.5f), new Vector3(0.8f, 0.02f, 0.3f), sc, mf);
            }

            SavePrefab(root, path);
            Debug.Log("[FactoryVisuals] Built visuals for Step-Down Transformer");
            return 1;
        }
    }
}
#endif
