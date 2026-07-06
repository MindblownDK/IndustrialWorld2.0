// Assets/Scripts/Editor/WindPowerContentBuilder.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  STEP 15 — MODULAR WIND POWER CONTENT (one-click, idempotent)   ║
// ║                                                                  ║
// ║  Generates the COMPLETE modular wind turbine content pack:      ║
// ║                                                                  ║
// ║   T-Series (horizontal axis, 3 tiers — number = wing span):     ║
// ║     • T90  - 2 MW    Tower / Nacelle / Gearbox / Generator /    ║
// ║     • T150 - 6 MW    Hub / Blade ×3 — placed part by part,      ║
// ║     • T236 - 15 MW   parts snap onto the tower automatically.   ║
// ║                                                                  ║
// ║   Vertical Series (cheaper, smaller):                           ║
// ║     • Small Vertical Turbine   Rotor → Blades                   ║
// ║     • Large Vertical Turbine   Rotor → Blades                   ║
// ║                                                                  ║
// ║   Offshore: 3 monopole foundations (one per T-series tier).     ║
// ║                                                                  ║
// ║   Everything is created AND linked: prefabs → block items →     ║
// ║   assembler recipes → research nodes. Zero manual wiring.       ║
// ║   Legacy (pre-4.0) windmill assets are removed automatically.   ║
// ╚══════════════════════════════════════════════════════════════════╝

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Power.Wind;

namespace VoxelEngine.EditorTools
{
    public static class WindPowerContentBuilder
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string WIND_ROOT  = ASSET_ROOT + "/WindPower";
        private const string PREFABS    = WIND_ROOT + "/Prefabs";
        private const string MATERIALS  = WIND_ROOT + "/Materials";
        private const string ITEMS      = WIND_ROOT + "/Items";
        private const string RECIPES    = WIND_ROOT + "/Recipes";
        private const string NODES      = ASSET_ROOT + "/Research/Nodes";

        // Shared materials (built once per run)
        private static Material _matShell, _matDark, _matAccent, _matCopper, _matBlade, _matPort, _matMono, _matHub, _matTipMark, _matSnapMark;

        // ════════════════════════════════════════════════════════════════
        //  ENTRY POINT
        // ════════════════════════════════════════════════════════════════
        public static void BuildAll()
        {
            // -- Dependencies (Step 10 industrial pack) --
            string ind = ASSET_ROOT + "/Industrial/Items";
            var steelPlate = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_SteelPlate.asset");
            var ironPlate  = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_IronPlate.asset");
            var copperWire = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_CopperWire.asset");
            var ironGear   = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_IronGear.asset");
            var plastic    = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_Plastic.asset");
            var circuit    = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_Circuit.asset");
            var advCircuit = Load<VoxelEngine.Items.ResourceItem>($"{ind}/Item_AdvCircuit.asset");
            if (steelPlate == null || circuit == null)
            {
                EditorUtility.DisplayDialog("Voxel Engine", "Run Step 10 (Industrial Content) first.", "OK");
                return;
            }

            foreach (var f in new[] { WIND_ROOT, PREFABS, MATERIALS, ITEMS, RECIPES }) EnsureFolder(f);

            RemoveLegacyAssets();
            BuildMaterials();

            var registry = Load<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            var tree     = Load<VoxelEngine.Research.ResearchTree>($"{ASSET_ROOT}/Research/ResearchTree.asset");
            if (registry != null) registry.recipes.RemoveAll(r => r == null);
            if (tree != null) tree.nodes.RemoveAll(n => n == null);

            // ── Tier definitions ────────────────────────────────────────
            //  (id, name, watts, towerH, rotorD, bladeLen, scale accents)
            var t90  = new HawtSpec {
                id = "t90",  label = "T90 - 2 MW",   watts = 2_000_000f,
                towerH = 40f, baseR = 1.5f, topR = 0.95f, rotorD = 44f, bladeLen = 20f,
                nacelle = new Vector3(2.4f, 2.1f, 5.2f), hubR = 0.95f
            };
            var t150 = new HawtSpec {
                id = "t150", label = "T150 - 6 MW",  watts = 6_000_000f,
                towerH = 65f, baseR = 2.2f, topR = 1.35f, rotorD = 72f, bladeLen = 33f,
                nacelle = new Vector3(3.4f, 3.0f, 7.6f), hubR = 1.35f
            };
            var t236 = new HawtSpec {
                id = "t236", label = "T236 - 15 MW", watts = 15_000_000f,
                towerH = 95f, baseR = 3.1f, topR = 1.9f, rotorD = 110f, bladeLen = 51f,
                nacelle = new Vector3(4.8f, 4.2f, 10.8f), hubR = 1.9f
            };

            var recipesByTier = new Dictionary<string, List<VoxelEngine.Crafting.RecipeDefinition>>();

            foreach (var spec in new[] { t90, t150, t236 })
            {
                var list = new List<VoxelEngine.Crafting.RecipeDefinition>();
                float m = spec.watts / 2_000_000f;   // cost multiplier vs T90
                int M(int baseCount) => Mathf.Max(1, Mathf.RoundToInt(baseCount * Mathf.Pow(m, 0.85f)));

                // 1) Prefabs
                var towerPrefab = BuildTowerPrefab(spec);
                var nacPrefab   = BuildNacellePrefab(spec);
                var gearPrefab  = BuildGearboxPrefab(spec);
                var genPrefab   = BuildGeneratorPrefab(spec);
                var hubPrefab   = BuildHubPrefab(spec);
                var bladePrefab = BuildBladePrefab(spec);

                // 2) Items
                string L = spec.label;
                var iTower = MakeBlock($"Block_{spec.id}_Tower", $"{L} Tower", towerPrefab,
                    $"Tubular steel tower for the {L} turbine. Place first — every other part snaps onto it. Power port at the base (marked square).", 400);
                var iNac   = MakeBlock($"Block_{spec.id}_Nacelle", $"{L} Nacelle", nacPrefab,
                    $"Machine housing for the {L} turbine. Mounts on top of the tower.", 300);
                var iGear  = MakeBlock($"Block_{spec.id}_Gearbox", $"{L} Gearbox", gearPrefab,
                    $"Planetary drivetrain for the {L} turbine. Installs inside the nacelle. Wears fastest under load.", 250);
                var iGen   = MakeBlock($"Block_{spec.id}_Generator", $"{L} Generator", genPrefab,
                    $"High-output generator for the {L} turbine. Installs inside the nacelle.", 250);
                var iHub   = MakeBlock($"Block_{spec.id}_Hub", $"{L} Hub", hubPrefab,
                    $"Rotor hub and spinner for the {L} turbine. Mounts on the nacelle — blades bolt onto it.", 250);
                var iBlade = MakeBlock($"Block_{spec.id}_Blade", $"{L} Blade", bladePrefab,
                    $"Aerodynamic composite blade for the {L} turbine. Three are required per rotor.", 200);

                // 3) Recipes — the bigger the turbine, the steeper the bill.
                list.Add(MakeRecipe($"Recipe_{spec.id}_Tower", $"{L} Tower", iTower, 14f,
                    (steelPlate, M(20)), (ironPlate, M(10))));
                list.Add(MakeRecipe($"Recipe_{spec.id}_Nacelle", $"{L} Nacelle", iNac, 12f,
                    (steelPlate, M(12)), (ironPlate, M(6)), (circuit, M(2))));
                list.Add(MakeRecipe($"Recipe_{spec.id}_Gearbox", $"{L} Gearbox", iGear, 12f,
                    (steelPlate, M(8)), (ironGear, M(12)),
                    spec.id == "t236" ? (advCircuit, M(1)) : (circuit, M(1))));
                list.Add(MakeRecipe($"Recipe_{spec.id}_Generator", $"{L} Generator", iGen, 12f,
                    (steelPlate, M(6)), (copperWire, M(20)),
                    spec.id == "t90" ? (circuit, M(3)) : (advCircuit, M(2))));
                list.Add(MakeRecipe($"Recipe_{spec.id}_Hub", $"{L} Hub", iHub, 10f,
                    (steelPlate, M(8)), (ironGear, M(6))));
                list.Add(MakeRecipe($"Recipe_{spec.id}_Blade", $"{L} Blade", iBlade, 10f,
                    (steelPlate, M(4)), (plastic, M(6))));

                recipesByTier[spec.id] = list;
            }

            // ── Vertical turbines ───────────────────────────────────────
            var vSmall = new VawtSpec {
                id = "vsmall", label = "Small Vertical Turbine", watts = 90_000f,
                drumR = 0.75f, drumH = 1.3f, mastH = 2.2f, cageH = 3.4f, cageR = 1.5f
            };
            var vLarge = new VawtSpec {
                id = "vlarge", label = "Large Vertical Turbine", watts = 350_000f,
                drumR = 1.25f, drumH = 2.0f, mastH = 3.4f, cageH = 5.6f, cageR = 2.5f
            };

            foreach (var spec in new[] { vSmall, vLarge })
            {
                bool large = spec.id == "vlarge";
                var list = new List<VoxelEngine.Crafting.RecipeDefinition>();

                var rotorPrefab = BuildVerticalRotorPrefab(spec);
                var bladePrefab = BuildVerticalBladePrefab(spec);

                var iRotor = MakeBlock($"Block_{spec.id}_Rotor", $"{spec.label} Rotor", rotorPrefab,
                    $"Base unit of the {spec.label.ToLower()} — generator drum and mast. Place first; power port at the base (marked square).", 200);
                var iBlade = MakeBlock($"Block_{spec.id}_Blades", $"{spec.label} Blades", bladePrefab,
                    $"Helical blade cage for the {spec.label.ToLower()}. Snaps on top of the rotor and spins with the wind.", 150);

                list.Add(MakeRecipe($"Recipe_{spec.id}_Rotor", $"{spec.label} Rotor", iRotor, 8f,
                    (steelPlate, large ? 14 : 6), (copperWire, large ? 20 : 8), (circuit, large ? 2 : 1)));
                list.Add(MakeRecipe($"Recipe_{spec.id}_Blades", $"{spec.label} Blades", iBlade, 6f,
                    (steelPlate, large ? 8 : 4), (plastic, large ? 10 : 4)));

                recipesByTier[spec.id] = list;
            }

            // ── Monopoles (offshore foundations) ────────────────────────
            var monoRecipes = new List<VoxelEngine.Crafting.RecipeDefinition>();
            foreach (var (spec, cost) in new[] { (t90, 15), (t150, 30), (t236, 60) })
            {
                var monoPrefab = BuildMonopolePrefab(spec);
                var iMono = MakeBlock($"Block_{spec.id}_Monopole", $"{spec.label} Monopole", monoPrefab,
                    $"Offshore foundation for the {spec.label} turbine. Drives deep into the seafloor — place in water, then build the tower on its platform.", 500);
                monoRecipes.Add(MakeRecipe($"Recipe_{spec.id}_Monopole", $"{spec.label} Monopole", iMono, 16f,
                    (steelPlate, cost), (ironPlate, cost / 2)));
            }

            // ── Research ────────────────────────────────────────────────
            if (tree != null)
            {
                var sciT1 = Load<VoxelEngine.Items.ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT1.asset");
                var sciT2 = Load<VoxelEngine.Items.ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT2.asset");
                var sciT3 = Load<VoxelEngine.Items.ScienceItem>($"{ASSET_ROOT}/Items/Item_ScienceT3.asset");

                var small = new List<VoxelEngine.Crafting.RecipeDefinition>();
                small.AddRange(recipesByTier["t90"]);
                small.AddRange(recipesByTier["vsmall"]);

                var medium = new List<VoxelEngine.Crafting.RecipeDefinition>();
                medium.AddRange(recipesByTier["t150"]);
                medium.AddRange(recipesByTier["vlarge"]);

                var offshore = new List<VoxelEngine.Crafting.RecipeDefinition>();
                offshore.AddRange(recipesByTier["t236"]);
                offshore.AddRange(monoRecipes);

                var n1 = MakeNode(tree, "res_wind_1", "Small Wind Systems",
                    "Unlocks the T90 - 2 MW turbine (Tower, Nacelle, Gearbox, Generator, Hub, Blades) and the Small Vertical Turbine (Rotor, Blades).",
                    1, 10, 40f, Costs((sciT1, 15)), small.ToArray(), null);
                var n2 = MakeNode(tree, "res_wind_2", "Medium Wind Systems",
                    "Unlocks the T150 - 6 MW turbine (Tower, Nacelle, Gearbox, Generator, Hub, Blades) and the Large Vertical Turbine (Rotor, Blades).",
                    2, 10, 80f, Costs((sciT1, 25), (sciT2, 15)), medium.ToArray(),
                    new[] { n1 });
                MakeNode(tree, "res_wind_3", "Off-Shore Wind Systems",
                    "Unlocks the flagship T236 - 15 MW turbine and monopole foundations for all three T-Series tiers — build wind farms at sea.",
                    3, 10, 140f, Costs((sciT2, 30), (sciT3, 20)), offshore.ToArray(),
                    new[] { n2 });

                EditorUtility.SetDirty(tree);
            }

            if (registry != null) EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Step 15",
                "Modular Wind Power content verified!\n\n" +
                "NON-DESTRUCTIVE PASS:\n" +
                "• Missing prefabs / items / recipes / research → created\n" +
                "• Existing assets → your tweaks kept, missing scripts re-added\n" +
                "• All links verified: prefab → item → recipe → research → registry\n\n" +
                "T90 / T150 / T236 (6 parts each) · 2 Vertical Turbines · 3 Monopoles.\n" +
                "Tip: delete a prefab asset and re-run Step 15 to regenerate its visuals.", "OK");
        }

        // ════════════════════════════════════════════════════════════════
        //  SPECS
        // ════════════════════════════════════════════════════════════════
        private class HawtSpec
        {
            public string id, label;
            public float watts, towerH, baseR, topR, rotorD, bladeLen, hubR;
            public Vector3 nacelle;
        }

        private class VawtSpec
        {
            public string id, label;
            public float watts, drumR, drumH, mastH, cageH, cageR;
        }

        // ════════════════════════════════════════════════════════════════
        //  MATERIALS — clean metallic, single accent
        // ════════════════════════════════════════════════════════════════
        private static void BuildMaterials()
        {
            _matShell  = MakeMat("Mat_TurbineShell",  new Color(0.87f, 0.89f, 0.91f), 0.70f, 0.72f);
            _matDark   = MakeMat("Mat_TurbineDark",   new Color(0.16f, 0.18f, 0.21f), 0.60f, 0.48f);
            _matAccent = MakeMat("Mat_TurbineAccent", new Color(0.16f, 0.40f, 0.72f), 0.55f, 0.60f);
            _matCopper = MakeMat("Mat_TurbineCopper", new Color(0.72f, 0.44f, 0.22f), 0.90f, 0.62f);
            _matBlade  = MakeMat("Mat_TurbineBlade",  new Color(0.94f, 0.95f, 0.97f), 0.12f, 0.68f);
            _matTipMark= MakeMat("Mat_TurbineTipMark",new Color(0.80f, 0.16f, 0.14f), 0.10f, 0.55f);
            _matHub    = MakeMat("Mat_TurbineHub",    new Color(0.78f, 0.80f, 0.84f), 0.80f, 0.70f);
            _matMono   = MakeMat("Mat_MonopoleSteel", new Color(0.52f, 0.55f, 0.59f), 0.75f, 0.45f);
            _matSnapMark = MakeMat("Mat_SnapMarker",  new Color(0.95f, 0.80f, 0.10f), 0.10f, 0.45f);
            if (!_matSnapMark.IsKeywordEnabled("_EMISSION"))
            {
                _matSnapMark.EnableKeyword("_EMISSION");
                _matSnapMark.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                _matSnapMark.SetColor("_EmissionColor", new Color(0.85f, 0.70f, 0.05f) * 1.1f);
                EditorUtility.SetDirty(_matSnapMark);
            }
            _matPort   = MakeMat("Mat_PowerPort",     new Color(0.05f, 0.09f, 0.08f), 0.30f, 0.40f);
            if (!_matPort.IsKeywordEnabled("_EMISSION"))
            {
                _matPort.EnableKeyword("_EMISSION");
                _matPort.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                _matPort.SetColor("_EmissionColor", new Color(0.10f, 0.95f, 0.65f) * 2.2f);
                EditorUtility.SetDirty(_matPort);
            }
        }

        private static Material MakeMat(string name, Color c, float metallic, float smooth)
        {
            // Non-destructive: existing materials keep any colour/PBR tweaks you
            // made in the inspector — values are only written on first creation.
            string path = $"{MATERIALS}/{name}.mat";
            var m = Load<Material>(path);
            if (m != null) return m;

            m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(m, path);
            m.color = c;
            if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ════════════════════════════════════════════════════════════════
        //  PREFAB BUILDERS — T-SERIES
        // ════════════════════════════════════════════════════════════════
        private static GameObject BuildTowerPrefab(HawtSpec s)
        {
            // Script pass — idempotent: adds missing components, initialises ONLY
            // freshly-added ones. Existing components keep every inspector tweak.
            void EnsureScripts(GameObject root)
            {
                EnsureComponent<VoxelEngine.Power.PowerGenerator>(root, gen =>
                {
                    gen.wattsPerSecond = 0f;
                    gen.connectRadius = 7f;
                });
                EnsureComponent<WindTurbinePart>(root, part =>
                {
                    part.kind = WindTurbinePartKind.Tower;
                    part.tierId = s.id;
                });
                EnsureComponent<WindTurbineController>(root, c =>
                {
                    c.tierId = s.id;
                    c.displayName = s.label;
                    c.vertical = false;
                    c.ratedPowerWatts = s.watts;
                    c.rotorDiameter = s.rotorD;
                    c.hubHeight = s.towerH + s.nacelle.y * 0.5f;
                    c.bladeCount = 3;
                    c.yawPivotLocal      = new Vector3(0f, s.towerH, 0f);
                    c.nacelleSocket      = new Vector3(0f, s.nacelle.y * 0.5f, -s.nacelle.z * 0.12f);
                    c.gearboxSocket      = new Vector3(0f, s.nacelle.y * 0.42f, -s.nacelle.z * 0.05f);
                    c.generatorSocket    = new Vector3(0f, s.nacelle.y * 0.42f, -s.nacelle.z * 0.32f);
                    c.hubSocket          = new Vector3(0f, s.nacelle.y * 0.5f, s.nacelle.z * 0.52f);
                    c.bladeMountRadius   = s.hubR * 0.85f;
                    c.repairPlateCost    = s.id == "t236" ? 12 : (s.id == "t150" ? 8 : 4);
                });
                if (root.GetComponent<Collider>() == null)
                {
                    var col = root.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, s.towerH * 0.5f, 0f);
                    col.size = new Vector3(s.baseR * 2f, s.towerH, s.baseR * 2f);
                }
            }

            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Tower.prefab", $"Turbine_{s.id}_Tower", root =>
            {
                EnsureScripts(root);

                // Visuals — tapered tube in 4 segments + flange rings + base door + accent stripe.
                int segs = 4;
                for (int i = 0; i < segs; i++)
                {
                    float t0 = i / (float)segs, t1 = (i + 1) / (float)segs;
                    float r  = Mathf.Lerp(s.baseR, s.topR, (t0 + t1) * 0.5f);
                    float h  = s.towerH / segs;
                    var seg = Prim(PrimitiveType.Cylinder, root.transform, $"TowerSeg{i}",
                        new Vector3(0f, (t0 * s.towerH) + h * 0.5f, 0f),
                        new Vector3(r * 2f, h * 0.5f, r * 2f), _matShell);
                    Object.DestroyImmediate(seg.GetComponent<Collider>());

                    if (i < segs - 1)
                    {
                        float fr = Mathf.Lerp(s.baseR, s.topR, t1) + 0.07f;
                        var flange = Prim(PrimitiveType.Cylinder, root.transform, $"Flange{i}",
                            new Vector3(0f, t1 * s.towerH, 0f),
                            new Vector3(fr * 2f, 0.09f, fr * 2f), _matDark);
                        Object.DestroyImmediate(flange.GetComponent<Collider>());
                    }
                }

                // Base collar + accent stripe
                var collar = Prim(PrimitiveType.Cylinder, root.transform, "BaseCollar",
                    new Vector3(0f, 0.35f, 0f), new Vector3(s.baseR * 2.25f, 0.35f, s.baseR * 2.25f), _matDark);
                Object.DestroyImmediate(collar.GetComponent<Collider>());
                var stripe = Prim(PrimitiveType.Cylinder, root.transform, "AccentStripe",
                    new Vector3(0f, 1.9f, 0f), new Vector3(s.baseR * 2.02f, 0.22f, s.baseR * 2.02f), _matAccent);
                Object.DestroyImmediate(stripe.GetComponent<Collider>());

                // Service door
                var door = Prim(PrimitiveType.Cube, root.transform, "ServiceDoor",
                    new Vector3(0f, 1.5f, s.baseR * 0.96f), new Vector3(0.9f, 1.9f, 0.16f), _matDark);
                Object.DestroyImmediate(door.GetComponent<Collider>());

                // POWER PORT — glowing square marker on the base front.
                BuildPortMarker(root.transform, new Vector3(0f, 0.9f, s.baseR * 1.02f));
            }, EnsureScripts);
        }

        private static GameObject BuildNacellePrefab(HawtSpec s)
        {
            Vector3 nc = s.nacelle;

            // ── Roof lid subtree — hinge PIVOT at the FRONT (rotor side). ──
            //    "RoofLid" is the parent: EVERYTHING under it swings with the
            //    lid, so restyle the plate / stripe / hinges (or add your own
            //    children) freely in the prefab. Swing angle & speed live on
            //    the NacelleRoofLid component (default 75°).
            void BuildRoofLid(GameObject root)
            {
                var lid = new GameObject("RoofLid");
                lid.transform.SetParent(root.transform, false);
                lid.transform.localPosition = new Vector3(0f, nc.y * 0.5f, nc.z * 0.5f);   // front hinge line
                lid.AddComponent<NacelleRoofLid>();   // openAngle=75, easeSpeed=3.5 defaults

                // Plate spans backwards from the hinge so pitching up tips it over the rotor side.
                var lidPlate = Prim(PrimitiveType.Cube, lid.transform, "LidPlate",
                    new Vector3(0f, nc.y * 0.05f, -nc.z * 0.5f), new Vector3(nc.x * 1.02f, nc.y * 0.10f, nc.z * 1.02f), _matShell);
                Object.DestroyImmediate(lidPlate.GetComponent<Collider>());
                var lidStripe = Prim(PrimitiveType.Cube, lid.transform, "LidStripe",
                    new Vector3(0f, nc.y * 0.11f, -nc.z * 0.5f), new Vector3(nc.x * 0.9f, nc.y * 0.02f, nc.z * 0.14f), _matAccent);
                Object.DestroyImmediate(lidStripe.GetComponent<Collider>());
                for (int i = 0; i < 2; i++)
                {
                    var hinge = Prim(PrimitiveType.Cylinder, lid.transform, $"Hinge{i}",
                        new Vector3((i == 0 ? -1f : 1f) * nc.x * 0.32f, 0f, 0f), new Vector3(0.10f, nc.x * 0.07f, 0.10f), _matDark);
                    hinge.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // along hinge (X) axis
                    Object.DestroyImmediate(hinge.GetComponent<Collider>());
                }
            }

            // ── Yellow alignment markers — flat pads on the machine bed showing
            //    exactly where the Gearbox / Generator snap. The controller reads
            //    these transforms as the REAL sockets, so nudging a marker in the
            //    prefab moves the actual snap point with it. ──
            void BuildSnapMarkers(GameObject root)
            {
                void Marker(string name, Vector3 localPos, float foot)
                {
                    var m = Prim(PrimitiveType.Cube, root.transform, name,
                        localPos, new Vector3(foot, nc.y * 0.015f, foot), _matSnapMark);
                    Object.DestroyImmediate(m.GetComponent<Collider>());
                }
                // Pads sit just above the machine bed (bed top ≈ -0.31 * y).
                float padY = -nc.y * 0.30f;
                Marker("SnapMarker_Gearbox",   new Vector3(0f, padY,  nc.z * 0.07f), nc.y * 0.62f);
                Marker("SnapMarker_Generator", new Vector3(0f, padY, -nc.z * 0.20f), nc.y * 0.60f);
            }

            void EnsureScripts(GameObject root)
            {
                EnsureComponent<WindTurbinePart>(root, part =>
                {
                    part.kind = WindTurbinePartKind.Nacelle;
                    part.tierId = s.id;
                });
                if (root.GetComponent<Collider>() == null)
                {
                    var col = root.AddComponent<BoxCollider>();
                    col.size = nc;
                }

                // Upgrade pass for EXISTING nacelles:
                //  • old side-hinged lid (no NacelleRoofLid script) → replaced with
                //    the new front-hinged rig. A lid you already customised (has
                //    the script) is left completely untouched.
                var oldLid = root.transform.Find("RoofLid");
                if (oldLid != null && oldLid.GetComponent<NacelleRoofLid>() == null)
                {
                    Object.DestroyImmediate(oldLid.gameObject);
                    oldLid = null;
                }
                if (oldLid == null) BuildRoofLid(root);

                //  • snap markers added if missing (never duplicated / overwritten).
                if (root.transform.Find("SnapMarker_Gearbox") == null &&
                    root.transform.Find("SnapMarker_Generator") == null)
                    BuildSnapMarkers(root);
            }

            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Nacelle.prefab", $"Turbine_{s.id}_Nacelle", root =>
            {
                EnsureScripts(root);

                Vector3 n = s.nacelle;
                // Shell — open-top box: floor + two flanks + front/back walls, so the
                // machinery bay is genuinely visible when the roof lid swings open.
                var floor = Prim(PrimitiveType.Cube, root.transform, "Floor",
                    new Vector3(0f, -n.y * 0.44f, 0f), new Vector3(n.x, n.y * 0.12f, n.z), _matShell);
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                foreach (float side in new[] { -1f, 1f })
                {
                    var wall = Prim(PrimitiveType.Cube, root.transform, side > 0 ? "WallR" : "WallL",
                        new Vector3(side * n.x * 0.44f, 0f, 0f), new Vector3(n.x * 0.12f, n.y, n.z), _matShell);
                    Object.DestroyImmediate(wall.GetComponent<Collider>());
                }
                var wallF = Prim(PrimitiveType.Cube, root.transform, "WallFront",
                    new Vector3(0f, 0f, n.z * 0.47f), new Vector3(n.x, n.y, n.z * 0.06f), _matShell);
                Object.DestroyImmediate(wallF.GetComponent<Collider>());
                var wallB = Prim(PrimitiveType.Cube, root.transform, "WallBack",
                    new Vector3(0f, 0f, -n.z * 0.47f), new Vector3(n.x, n.y, n.z * 0.06f), _matShell);
                Object.DestroyImmediate(wallB.GetComponent<Collider>());

                // Interior deck detail (dark machinery bed the parts sit on)
                var bed = Prim(PrimitiveType.Cube, root.transform, "MachineBed",
                    new Vector3(0f, -n.y * 0.34f, 0f), new Vector3(n.x * 0.72f, n.y * 0.06f, n.z * 0.78f), _matDark);
                Object.DestroyImmediate(bed.GetComponent<Collider>());

                // (RoofLid + yellow snap markers are created by EnsureScripts above,
                //  so fresh builds and repaired prefabs share one code path.)

                // Cooling vents (dark insets on both flanks)
                for (int i = 0; i < 3; i++)
                {
                    float z = -n.z * 0.28f + i * n.z * 0.22f;
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var vent = Prim(PrimitiveType.Cube, root.transform, $"Vent{i}{(side > 0 ? "R" : "L")}",
                            new Vector3(side * n.x * 0.505f, n.y * 0.08f, z),
                            new Vector3(0.04f, n.y * 0.42f, n.z * 0.13f), _matDark);
                        Object.DestroyImmediate(vent.GetComponent<Collider>());
                    }
                }

                // Rotor-side collar (where the hub docks)
                var collar = Prim(PrimitiveType.Cylinder, root.transform, "RotorCollar",
                    new Vector3(0f, 0f, n.z * 0.5f), new Vector3(s.hubR * 1.9f, 0.18f, s.hubR * 1.9f), _matDark);
                collar.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Object.DestroyImmediate(collar.GetComponent<Collider>());

                // Tail sensor mast (anemometry)
                var mast = Prim(PrimitiveType.Cylinder, root.transform, "SensorMast",
                    new Vector3(n.x * 0.2f, n.y * 0.75f, -n.z * 0.42f), new Vector3(0.06f, n.y * 0.35f, 0.06f), _matDark);
                Object.DestroyImmediate(mast.GetComponent<Collider>());

            }, EnsureScripts);
        }

        /// <summary>Shared idempotent script pass for simple parts (part marker + box collider).</summary>
        private static System.Action<GameObject> PartEnsure(WindTurbinePartKind kind, string tierId,
            Vector3 colCenter, Vector3 colSize)
        {
            return root =>
            {
                EnsureComponent<WindTurbinePart>(root, part =>
                {
                    part.kind = kind;
                    part.tierId = tierId;
                });
                if (root.GetComponent<Collider>() == null)
                {
                    var col = root.AddComponent<BoxCollider>();
                    col.center = colCenter;
                    col.size = colSize;
                }
            };
        }

        private static GameObject BuildGearboxPrefab(HawtSpec s)
        {
            float k = s.nacelle.y;   // scale driver
            var ensure = PartEnsure(WindTurbinePartKind.Gearbox, s.id,
                Vector3.zero, new Vector3(k * 0.6f, k * 0.55f, k * 0.9f));
            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Gearbox.prefab", $"Turbine_{s.id}_Gearbox", root =>
            {
                ensure(root);

                var body = Prim(PrimitiveType.Cube, root.transform, "Housing",
                    Vector3.zero, new Vector3(k * 0.55f, k * 0.5f, k * 0.75f), _matDark);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                // Planetary stage rings
                for (int i = 0; i < 3; i++)
                {
                    float r = k * (0.34f - i * 0.05f);
                    var ring = Prim(PrimitiveType.Cylinder, root.transform, $"Stage{i}",
                        new Vector3(0f, 0f, k * (0.42f - i * 0.16f)), new Vector3(r * 2f, k * 0.05f, r * 2f), _matHub);
                    ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Object.DestroyImmediate(ring.GetComponent<Collider>());
                }
                // Cooling ribs on top
                for (int i = 0; i < 4; i++)
                {
                    var rib = Prim(PrimitiveType.Cube, root.transform, $"Rib{i}",
                        new Vector3(-k * 0.18f + i * k * 0.12f, k * 0.28f, 0f),
                        new Vector3(k * 0.04f, k * 0.10f, k * 0.6f), _matHub);
                    Object.DestroyImmediate(rib.GetComponent<Collider>());
                }
            }, ensure);
        }

        private static GameObject BuildGeneratorPrefab(HawtSpec s)
        {
            float k = s.nacelle.y;
            var ensure = PartEnsure(WindTurbinePartKind.Generator, s.id,
                Vector3.zero, new Vector3(k * 0.62f, k * 0.62f, k * 0.75f));
            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Generator.prefab", $"Turbine_{s.id}_Generator", root =>
            {
                ensure(root);

                var body = Prim(PrimitiveType.Cylinder, root.transform, "Stator",
                    Vector3.zero, new Vector3(k * 0.55f, k * 0.35f, k * 0.55f), _matShell);
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                // Copper winding rings
                for (int i = -1; i <= 1; i++)
                {
                    var coil = Prim(PrimitiveType.Cylinder, root.transform, $"Coil{i + 1}",
                        new Vector3(0f, 0f, i * k * 0.20f), new Vector3(k * 0.58f, k * 0.045f, k * 0.58f), _matCopper);
                    coil.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Object.DestroyImmediate(coil.GetComponent<Collider>());
                }
                // Terminal box
                var tbox = Prim(PrimitiveType.Cube, root.transform, "TerminalBox",
                    new Vector3(0f, k * 0.32f, 0f), new Vector3(k * 0.2f, k * 0.14f, k * 0.24f), _matDark);
                Object.DestroyImmediate(tbox.GetComponent<Collider>());

            }, ensure);
        }

        private static GameObject BuildHubPrefab(HawtSpec s)
        {
            void EnsureScripts(GameObject root)
            {
                EnsureComponent<WindTurbinePart>(root, part =>
                {
                    part.kind = WindTurbinePartKind.Hub;
                    part.tierId = s.id;
                });
                if (root.GetComponent<Collider>() == null)
                {
                    var col = root.AddComponent<SphereCollider>();
                    col.radius = s.hubR * 1.15f;
                }
            }

            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Hub.prefab", $"Turbine_{s.id}_Hub", root =>
            {
                EnsureScripts(root);

                float r = s.hubR;
                // Spinner nose (sphere + forward cone-ish cap)
                var core = Prim(PrimitiveType.Sphere, root.transform, "HubCore",
                    Vector3.zero, new Vector3(r * 2f, r * 2f, r * 2.2f), _matHub);
                Object.DestroyImmediate(core.GetComponent<Collider>());
                var nose = Prim(PrimitiveType.Sphere, root.transform, "Nose",
                    new Vector3(0f, 0f, r * 0.9f), new Vector3(r * 1.2f, r * 1.2f, r * 1.5f), _matShell);
                Object.DestroyImmediate(nose.GetComponent<Collider>());

                // Blade mount collars — 120° apart around Z.
                for (int i = 0; i < 3; i++)
                {
                    Quaternion q = Quaternion.Euler(0f, 0f, i * 120f);
                    var mount = Prim(PrimitiveType.Cylinder, root.transform, $"BladeMount{i}",
                        q * new Vector3(0f, r * 0.92f, 0f), new Vector3(r * 0.62f, r * 0.22f, r * 0.62f), _matDark);
                    mount.transform.localRotation = q;
                    Object.DestroyImmediate(mount.GetComponent<Collider>());
                }

            }, EnsureScripts);
        }

        private static GameObject BuildBladePrefab(HawtSpec s)
        {
            float lenC = s.bladeLen;
            float chordC = Mathf.Max(0.55f, s.hubR * 0.72f);
            var ensure = PartEnsure(WindTurbinePartKind.Blade, s.id,
                new Vector3(lenC * 0.02f, lenC * 0.5f, 0f), new Vector3(chordC * 1.1f, lenC, chordC * 1.7f));
            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Blade.prefab", $"Turbine_{s.id}_Blade", root =>
            {
                ensure(root);

                float len = s.bladeLen;
                float rootChord = Mathf.Max(0.55f, s.hubR * 0.72f);

                // ── Root: bolt collar + cylindrical shank easing into the airfoil ──
                var collar = Prim(PrimitiveType.Cylinder, root.transform, "RootCollar",
                    new Vector3(0f, len * 0.015f, 0f), new Vector3(rootChord * 1.12f, len * 0.015f, rootChord * 1.12f), _matDark);
                Object.DestroyImmediate(collar.GetComponent<Collider>());
                var shank = Prim(PrimitiveType.Cylinder, root.transform, "RootShank",
                    new Vector3(0f, len * 0.055f, 0f), new Vector3(rootChord * 0.95f, len * 0.035f, rootChord * 0.95f), _matBlade);
                Object.DestroyImmediate(shank.GetComponent<Collider>());

                // ── Airfoil body: many thin overlapping slices → smooth taper,
                //    aerodynamic twist AND a gentle downwind prebend curve.
                //    Slices are flattened cylinders (elliptical cross-section),
                //    which reads as a real composite blade instead of boxes. ──
                int slices = 12;
                for (int i = 0; i < slices; i++)
                {
                    float t0 = 0.09f + (i / (float)slices) * 0.91f;
                    float t1 = 0.09f + ((i + 1) / (float)slices) * 0.91f;
                    float mid = (t0 + t1) * 0.5f;

                    // Chord swells to max at ~30% span then tapers to a fine tip.
                    float swell = Mathf.Sin(Mathf.Clamp01((mid - 0.05f) / 0.30f) * Mathf.PI * 0.5f);
                    float taper = Mathf.Pow(1f - Mathf.Clamp01((mid - 0.30f) / 0.70f), 1.25f);
                    float chord = rootChord * Mathf.Lerp(0.9f, 1.55f, swell) * Mathf.Lerp(0.16f, 1f, taper);
                    float thick = chord * Mathf.Lerp(0.34f, 0.14f, mid);

                    // Twist washes out from ~20° at the root to ~1° at the tip.
                    float twist = Mathf.Lerp(20f, 1f, Mathf.Pow(mid, 0.65f));
                    // Prebend: tip curves gently downwind (local +X here; the 120°
                    // slot rotation orients it correctly on the hub).
                    float bend = Mathf.Pow(mid, 2.1f) * len * 0.055f;

                    var slice = Prim(PrimitiveType.Cylinder, root.transform, $"Foil{i:00}",
                        new Vector3(bend, mid * len, 0f),
                        new Vector3(thick, (t1 - t0) * len * 0.62f, chord), _matBlade);
                    slice.transform.localRotation = Quaternion.Euler(0f, twist, Mathf.Pow(mid, 2.1f) * 6f);
                    Object.DestroyImmediate(slice.GetComponent<Collider>());
                }

                // Trailing-edge spine — thin dark strip that sells the airfoil silhouette.
                for (int i = 0; i < 4; i++)
                {
                    float mid = 0.18f + i * 0.20f;
                    float taper = Mathf.Pow(1f - Mathf.Clamp01((mid - 0.30f) / 0.70f), 1.25f);
                    float swell = Mathf.Sin(Mathf.Clamp01((mid - 0.05f) / 0.30f) * Mathf.PI * 0.5f);
                    float chord = rootChord * Mathf.Lerp(0.9f, 1.55f, swell) * Mathf.Lerp(0.16f, 1f, taper);
                    float bend = Mathf.Pow(mid, 2.1f) * len * 0.055f;
                    var spine = Prim(PrimitiveType.Cube, root.transform, $"Spine{i}",
                        new Vector3(bend, mid * len, -chord * 0.48f),
                        new Vector3(chord * 0.05f, len * 0.185f, chord * 0.06f), _matDark);
                    spine.transform.localRotation = Quaternion.Euler(0f, Mathf.Lerp(20f, 1f, Mathf.Pow(mid, 0.65f)), 0f);
                    Object.DestroyImmediate(spine.GetComponent<Collider>());
                }

                // Fine swept tip + red aviation marker band.
                float tipBend = len * 0.055f;
                var tipCone = Prim(PrimitiveType.Cylinder, root.transform, "TipCone",
                    new Vector3(tipBend, len * 0.985f, 0f),
                    new Vector3(rootChord * 0.07f, len * 0.022f, rootChord * 0.22f), _matBlade);
                tipCone.transform.localRotation = Quaternion.Euler(0f, 1f, 8f);
                Object.DestroyImmediate(tipCone.GetComponent<Collider>());
                var marker = Prim(PrimitiveType.Cylinder, root.transform, "TipMarker",
                    new Vector3(tipBend * 0.88f, len * 0.945f, 0f),
                    new Vector3(rootChord * 0.12f, len * 0.012f, rootChord * 0.34f), _matTipMark);
                marker.transform.localRotation = Quaternion.Euler(0f, 2f, 7f);
                Object.DestroyImmediate(marker.GetComponent<Collider>());

            }, ensure);
        }

        // ════════════════════════════════════════════════════════════════
        //  PREFAB BUILDERS — VERTICAL SERIES
        // ════════════════════════════════════════════════════════════════
        private static GameObject BuildVerticalRotorPrefab(VawtSpec s)
        {
            void EnsureScripts(GameObject root)
            {
                EnsureComponent<VoxelEngine.Power.PowerGenerator>(root, gen =>
                {
                    gen.wattsPerSecond = 0f;
                    gen.connectRadius = 5f;
                });
                EnsureComponent<WindTurbinePart>(root, part =>
                {
                    part.kind = WindTurbinePartKind.VerticalRotor;
                    part.tierId = s.id;
                });
                EnsureComponent<WindTurbineController>(root, c =>
                {
                    c.tierId = s.id;
                    c.displayName = s.label;
                    c.vertical = true;
                    c.ratedPowerWatts = s.watts;
                    c.rotorDiameter = s.cageR * 2f;
                    c.hubHeight = s.drumH + s.mastH + s.cageH * 0.5f;
                    c.bladeCount = 1;                                   // one blade-cage item
                    c.yawPivotLocal = new Vector3(0f, s.drumH + s.mastH, 0f);
                    c.verticalBladeSocket = Vector3.zero;
                    c.repairPlateCost = s.id == "vlarge" ? 4 : 2;
                });
                if (root.GetComponent<Collider>() == null)
                {
                    var col = root.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, (s.drumH + s.mastH) * 0.5f, 0f);
                    col.size = new Vector3(s.drumR * 2f, s.drumH + s.mastH, s.drumR * 2f);
                }
            }

            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Rotor.prefab", $"Turbine_{s.id}_Rotor", root =>
            {
                EnsureScripts(root);

                // Generator drum
                var drum = Prim(PrimitiveType.Cylinder, root.transform, "GeneratorDrum",
                    new Vector3(0f, s.drumH * 0.5f, 0f), new Vector3(s.drumR * 2f, s.drumH * 0.5f, s.drumR * 2f), _matShell);
                Object.DestroyImmediate(drum.GetComponent<Collider>());
                var band = Prim(PrimitiveType.Cylinder, root.transform, "AccentBand",
                    new Vector3(0f, s.drumH * 0.72f, 0f), new Vector3(s.drumR * 2.04f, s.drumH * 0.08f, s.drumR * 2.04f), _matAccent);
                Object.DestroyImmediate(band.GetComponent<Collider>());
                var baseRing = Prim(PrimitiveType.Cylinder, root.transform, "BasePlate",
                    new Vector3(0f, 0.06f, 0f), new Vector3(s.drumR * 2.5f, 0.06f, s.drumR * 2.5f), _matDark);
                Object.DestroyImmediate(baseRing.GetComponent<Collider>());

                // Mast up to the blade socket
                var mast = Prim(PrimitiveType.Cylinder, root.transform, "Mast",
                    new Vector3(0f, s.drumH + s.mastH * 0.5f, 0f), new Vector3(s.drumR * 0.5f, s.mastH * 0.5f, s.drumR * 0.5f), _matShell);
                Object.DestroyImmediate(mast.GetComponent<Collider>());

                // POWER PORT — marked square on the drum.
                BuildPortMarker(root.transform, new Vector3(0f, s.drumH * 0.45f, s.drumR * 1.01f));
            }, EnsureScripts);
        }

        private static GameObject BuildVerticalBladePrefab(VawtSpec s)
        {
            var ensure = PartEnsure(WindTurbinePartKind.VerticalBlade, s.id,
                new Vector3(0f, s.cageH * 0.5f, 0f), new Vector3(s.cageR * 2.2f, s.cageH, s.cageR * 2.2f));
            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Blades.prefab", $"Turbine_{s.id}_Blades", root =>
            {
                ensure(root);

                // Central shaft
                var shaft = Prim(PrimitiveType.Cylinder, root.transform, "Shaft",
                    new Vector3(0f, s.cageH * 0.5f, 0f), new Vector3(s.drumR * 0.42f, s.cageH * 0.5f, s.drumR * 0.42f), _matHub);
                Object.DestroyImmediate(shaft.GetComponent<Collider>());

                // Helical cage — 3 blades, each 5 stacked, progressively rotated slats.
                int blades = 3, steps = 5;
                for (int b = 0; b < blades; b++)
                {
                    float baseAngle = b * (360f / blades);
                    for (int i = 0; i < steps; i++)
                    {
                        float t = (i + 0.5f) / steps;
                        float angle = baseAngle + t * 70f;   // helix twist
                        Quaternion q = Quaternion.Euler(0f, angle, 0f);
                        var slat = Prim(PrimitiveType.Cube, root.transform, $"B{b}S{i}",
                            q * new Vector3(s.cageR, t * s.cageH, 0f),
                            new Vector3(0.10f * s.cageR + 0.04f, s.cageH / steps * 1.06f, s.cageR * 0.55f), _matBlade);
                        slat.transform.localRotation = q * Quaternion.Euler(0f, 18f, 0f);
                        Object.DestroyImmediate(slat.GetComponent<Collider>());
                    }
                    // Support arms top + bottom
                    foreach (float h in new[] { s.cageH * 0.06f, s.cageH * 0.94f })
                    {
                        Quaternion q = Quaternion.Euler(0f, baseAngle + (h > s.cageH * 0.5f ? 70f : 0f), 0f);
                        var arm = Prim(PrimitiveType.Cube, root.transform, $"Arm{b}{(h > 1f ? "T" : "B")}",
                            q * new Vector3(s.cageR * 0.5f, h, 0f),
                            new Vector3(s.cageR, 0.07f, 0.10f), _matDark);
                        arm.transform.localRotation = q;
                        Object.DestroyImmediate(arm.GetComponent<Collider>());
                    }
                }

            }, ensure);
        }

        // ════════════════════════════════════════════════════════════════
        //  PREFAB BUILDER — MONOPOLE
        // ════════════════════════════════════════════════════════════════
        private static GameObject BuildMonopolePrefab(HawtSpec s)
        {
            float rC = s.baseR * 1.15f;
            float depthC = 18f + s.towerH * 0.15f;
            const float deckC = 3.5f;
            void EnsureScripts(GameObject root)
            {
                if (root.GetComponent<Collider>() == null)
                {
                    var col = root.AddComponent<BoxCollider>();
                    col.center = new Vector3(0f, (deckC - depthC) * 0.5f, 0f);
                    col.size = new Vector3(rC * 2.2f, deckC + depthC, rC * 2.2f);
                }
            }

            return EnsurePrefab($"{PREFABS}/Turbine_{s.id}_Monopole.prefab", $"Turbine_{s.id}_Monopole", root =>
            {
                EnsureScripts(root);

                float r = s.baseR * 1.15f;
                float depth = 18f + s.towerH * 0.15f;   // below waterline
                float deck = 3.5f;                       // above waterline

                // Pile — extends deep under the placement point.
                var pile = Prim(PrimitiveType.Cylinder, root.transform, "Pile",
                    new Vector3(0f, -depth * 0.5f + deck * 0.5f, 0f), new Vector3(r * 2f, (depth + deck) * 0.5f, r * 2f), _matMono);
                Object.DestroyImmediate(pile.GetComponent<Collider>());

                // Splash-zone safety band
                var band = Prim(PrimitiveType.Cylinder, root.transform, "SafetyBand",
                    new Vector3(0f, deck * 0.25f, 0f), new Vector3(r * 2.06f, deck * 0.18f, r * 2.06f), _matAccent);
                Object.DestroyImmediate(band.GetComponent<Collider>());

                // Work platform with railing posts — the tower sits on this.
                var deckPlate = Prim(PrimitiveType.Cylinder, root.transform, "Deck",
                    new Vector3(0f, deck, 0f), new Vector3(r * 3.0f, 0.12f, r * 3.0f), _matDark);
                Object.DestroyImmediate(deckPlate.GetComponent<Collider>());
                for (int i = 0; i < 8; i++)
                {
                    Quaternion q = Quaternion.Euler(0f, i * 45f, 0f);
                    var post = Prim(PrimitiveType.Cylinder, root.transform, $"Rail{i}",
                        q * new Vector3(r * 1.42f, deck + 0.55f, 0f), new Vector3(0.07f, 0.55f, 0.07f), _matMono);
                    Object.DestroyImmediate(post.GetComponent<Collider>());
                }

            }, EnsureScripts);
        }

        // ════════════════════════════════════════════════════════════════
        //  SHARED HELPERS
        // ════════════════════════════════════════════════════════════════

        /// <summary>Glowing square marker showing where the power line connects.</summary>
        private static void BuildPortMarker(Transform parent, Vector3 localPos)
        {
            var frame = Prim(PrimitiveType.Cube, parent, "PowerPortFrame",
                localPos, new Vector3(0.72f, 0.72f, 0.10f), _matDark);
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            var square = Prim(PrimitiveType.Cube, parent, "PowerPort",
                localPos + new Vector3(0f, 0f, 0.03f), new Vector3(0.52f, 0.52f, 0.09f), _matPort);
            Object.DestroyImmediate(square.GetComponent<Collider>());
        }

        private static GameObject Prim(PrimitiveType type, Transform parent, string name,
            Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// NON-DESTRUCTIVE prefab pass:
        ///   • Prefab missing            → build it fresh (geometry + scripts).
        ///   • Prefab exists             → keep ALL visuals and serialized values;
        ///     only strip broken script references and run <paramref name="ensure"/>,
        ///     which adds any missing components (never overwriting existing ones).
        /// Delete a prefab manually if you want its visuals regenerated.
        /// </summary>
        private static GameObject EnsurePrefab(string path, string name,
            System.Action<GameObject> buildFresh, System.Action<GameObject> ensure)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing == null)
            {
                var root = new GameObject(name);
                try
                {
                    buildFresh(root);
                    return PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { Object.DestroyImmediate(root); }
            }

            // Additive repair pass on the existing prefab.
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                    if (t != null) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                ensure?.Invoke(contents);
                return PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WindPowerContentBuilder] Could not repair '{path}' additively ({ex.Message}). Leaving it untouched.");
                return existing;
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>GetComponent-or-add. Runs <paramref name="onAdded"/> ONLY for a
        /// freshly added component — existing components keep every tweaked value.</summary>
        private static T EnsureComponent<T>(GameObject go, System.Action<T> onAdded = null) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c == null)
            {
                c = go.AddComponent<T>();
                onAdded?.Invoke(c);
            }
            return c;
        }

        /// <summary>
        /// NON-DESTRUCTIVE item pass. New asset → fully initialised. Existing
        /// asset → every tuned value (name, health, stack size, icon…) is kept;
        /// only broken/missing LINKS are repaired: itemId (needed by save files)
        /// and placedPrefab (item → prefab).
        /// </summary>
        private static VoxelEngine.Items.BlockItem MakeBlock(string assetName, string display,
            GameObject prefab, string description, int health)
        {
            string path = $"{ITEMS}/{assetName}.asset";
            var b = Load<VoxelEngine.Items.BlockItem>(path);
            bool fresh = b == null;
            if (fresh)
            {
                b = ScriptableObject.CreateInstance<VoxelEngine.Items.BlockItem>();
                AssetDatabase.CreateAsset(b, path);
                b.displayName = display;
                b.description = description;
                b.iconTint = new Color(0.80f, 0.84f, 0.90f);
                b.maxStack = 10;
                b.massPerUnit = 25f;
                b.gridSize = Vector3Int.one;
                b.allowStacking = true;      // parts may occupy space near the tower
                b.blockHealth = health;
                b.miningTier = 1;
                b.category = "Power";
            }

            // Always-verified links (harmless when already correct):
            if (string.IsNullOrEmpty(b.itemId)) b.itemId = assetName.ToLower();
            if (b.placedPrefab == null && prefab != null) b.placedPrefab = prefab;
            EditorUtility.SetDirty(b);
            return b;
        }

        /// <summary>
        /// NON-DESTRUCTIVE recipe pass. New asset → fully initialised. Existing
        /// asset → tuned costs / craft time / station are kept; only broken links
        /// are repaired: outputItem, dead ingredient references, and registry
        /// membership (recipe → registry).
        /// </summary>
        private static VoxelEngine.Crafting.RecipeDefinition MakeRecipe(string assetName, string display,
            VoxelEngine.Items.ItemDefinition output, float seconds,
            params (VoxelEngine.Items.ItemDefinition item, int count)[] inputs)
        {
            string path = $"{RECIPES}/{assetName}.asset";
            var r = Load<VoxelEngine.Crafting.RecipeDefinition>(path);
            bool fresh = r == null;
            if (fresh)
            {
                r = ScriptableObject.CreateInstance<VoxelEngine.Crafting.RecipeDefinition>();
                AssetDatabase.CreateAsset(r, path);
                r.displayName = display;
                r.outputCount = 1;
                r.requiredStation = VoxelEngine.Crafting.StationTier.Assembler;
                r.craftSeconds = seconds;
                r.unlockedByDefault = false;
            }

            // Link repair — output must point at the right item.
            if (r.outputItem == null) r.outputItem = output;

            // Ingredients: write defaults on fresh assets; on existing assets only
            // replace the list if it is empty or contains dead (null-item) entries.
            bool inputsBroken = r.inputs == null || r.inputs.Length == 0;
            if (!inputsBroken)
                foreach (var ing in r.inputs)
                    if (ing.item == null) { inputsBroken = true; break; }
            if (fresh || inputsBroken)
            {
                var valid = new List<VoxelEngine.Crafting.RecipeIngredient>();
                foreach (var (item, count) in inputs)
                    if (item != null && count > 0)
                        valid.Add(new VoxelEngine.Crafting.RecipeIngredient { item = item, count = count });
                r.inputs = valid.ToArray();
            }
            EditorUtility.SetDirty(r);

            var registry = Load<VoxelEngine.Crafting.RecipeRegistry>($"{ASSET_ROOT}/RecipeRegistry.asset");
            if (registry != null && !registry.recipes.Contains(r)) registry.recipes.Add(r);
            return r;
        }

        private static VoxelEngine.Research.ResearchNode.ScienceCost[] Costs(
            params (VoxelEngine.Items.ScienceItem pack, int count)[] costs)
        {
            var valid = new List<VoxelEngine.Research.ResearchNode.ScienceCost>();
            foreach (var (pack, count) in costs)
                if (pack != null)
                    valid.Add(new VoxelEngine.Research.ResearchNode.ScienceCost { pack = pack, count = count });
            return valid.ToArray();
        }

        private static VoxelEngine.Research.ResearchNode MakeNode(VoxelEngine.Research.ResearchTree tree,
            string id, string display, string desc, int tier, int column, float seconds,
            VoxelEngine.Research.ResearchNode.ScienceCost[] cost,
            VoxelEngine.Crafting.RecipeDefinition[] unlocks,
            VoxelEngine.Research.ResearchNode[] prereqs)
        {
            // NON-DESTRUCTIVE node pass: fresh nodes are fully authored; existing
            // nodes keep tuned names/costs/tier placement. Links are ALWAYS
            // verified: dead unlock entries are dropped, missing wind recipes are
            // appended (union merge), prerequisites are repaired if broken, and
            // tree membership is guaranteed.
            string path = $"{NODES}/{id}.asset";
            var n = Load<VoxelEngine.Research.ResearchNode>(path);
            bool fresh = n == null;
            if (fresh)
            {
                n = ScriptableObject.CreateInstance<VoxelEngine.Research.ResearchNode>();
                AssetDatabase.CreateAsset(n, path);
                n.displayName = display;
                n.description = desc;
                n.category = VoxelEngine.Research.ResearchCategory.Environment;
                n.subCategory = VoxelEngine.Research.ResearchSubCategory.Power;
                n.tier = tier;
                n.column = column;
                n.researchSeconds = seconds;
                n.cost = cost;
            }

            if (string.IsNullOrEmpty(n.nodeId) || n.nodeId != id) n.nodeId = id;
            if (n.cost == null || n.cost.Length == 0) n.cost = cost;

            // Unlock links: keep valid existing entries, drop nulls, append ours.
            var merged = new List<VoxelEngine.Crafting.RecipeDefinition>();
            if (n.unlocksRecipes != null)
                foreach (var r in n.unlocksRecipes)
                    if (r != null && !merged.Contains(r)) merged.Add(r);
            foreach (var r in unlocks)
                if (r != null && !merged.Contains(r)) merged.Add(r);
            n.unlocksRecipes = merged.ToArray();

            // Prerequisite links: repair only when empty or containing dead refs.
            bool prereqBroken = n.prerequisites == null || n.prerequisites.Length == 0;
            if (!prereqBroken)
                foreach (var p in n.prerequisites)
                    if (p == null) { prereqBroken = true; break; }
            if (prereqBroken)
                n.prerequisites = prereqs ?? new VoxelEngine.Research.ResearchNode[0];

            EditorUtility.SetDirty(n);
            if (tree != null && !tree.nodes.Contains(n)) tree.nodes.Add(n);
            return n;
        }

        // ════════════════════════════════════════════════════════════════
        //  LEGACY CLEANUP — pre-4.0 windmill content
        // ════════════════════════════════════════════════════════════════
        private static void RemoveLegacyAssets()
        {
            string[] legacy =
            {
                // Prefabs
                $"{PREFABS}/WindmillMonopole.prefab",
                $"{PREFABS}/StandardWindmill_Small.prefab",
                $"{PREFABS}/StandardWindmill_Medium.prefab",
                $"{PREFABS}/StandardWindmill_Large.prefab",
                $"{PREFABS}/HelixSmall.prefab",
                $"{PREFABS}/HelixLarge.prefab",
                $"{PREFABS}/Mat_Monopole.mat",
                $"{PREFABS}/Mat_SWind_Small.mat",
                $"{PREFABS}/Mat_SWind_Medium.mat",
                $"{PREFABS}/Mat_SWind_Large.mat",
                // Items
                $"{ITEMS}/Block_WindmillMonopole.asset",
                $"{ITEMS}/Block_SWind_Small.asset",
                $"{ITEMS}/Block_SWind_Medium.asset",
                $"{ITEMS}/Block_SWind_Large.asset",
                $"{ITEMS}/Block_HWind_Small.asset",
                $"{ITEMS}/Block_HWind_Large.asset",
                $"{ITEMS}/Item_Wind_Tower.asset",
                $"{ITEMS}/Item_Wind_Nacelle.asset",
                $"{ITEMS}/Item_Wind_Gearbox.asset",
                $"{ITEMS}/Item_Wind_Generator.asset",
                $"{ITEMS}/Item_Wind_Hub.asset",
                $"{ITEMS}/Item_Wind_Blade.asset",
                $"{ITEMS}/Item_HelixGen_Small.asset",
                $"{ITEMS}/Item_HelixGen_Large.asset",
                $"{ITEMS}/Item_HelixWing_Small.asset",
                $"{ITEMS}/Item_HelixWing_Large.asset",
                // Recipes
                $"{RECIPES}/Recipe_WindMono.asset",
                $"{RECIPES}/Recipe_SWindSmall.asset",
                $"{RECIPES}/Recipe_SWindMed.asset",
                $"{RECIPES}/Recipe_SWindLarge.asset",
                $"{RECIPES}/Recipe_HWindSmall.asset",
                $"{RECIPES}/Recipe_HWindLarge.asset",
                $"{RECIPES}/Recipe_Wind_Tower.asset",
                $"{RECIPES}/Recipe_Wind_Nacelle.asset",
                $"{RECIPES}/Recipe_Wind_Gearbox.asset",
                $"{RECIPES}/Recipe_Wind_Generator.asset",
                $"{RECIPES}/Recipe_Wind_Hub.asset",
                $"{RECIPES}/Recipe_Wind_Blade.asset",
                $"{RECIPES}/Recipe_HelixGen_Small.asset",
                $"{RECIPES}/Recipe_HelixGen_Large.asset",
                $"{RECIPES}/Recipe_HelixWing_Small.asset",
                $"{RECIPES}/Recipe_HelixWing_Large.asset",
            };
            int removed = 0;
            foreach (var p in legacy)
                if (AssetDatabase.LoadMainAssetAtPath(p) != null && AssetDatabase.DeleteAsset(p))
                    removed++;
            if (removed > 0)
                Debug.Log($"[WindPowerContentBuilder] Removed {removed} legacy wind asset(s).");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                var leaf = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
    }
}
#endif
