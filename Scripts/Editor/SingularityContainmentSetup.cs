// Assets/Scripts/VoxelEngine/Editor/SingularityContainmentSetup.cs
//
// Step 54 (Phase 5): CONTAINMENT SYSTEMS — exotic-matter storage done properly.
// Non-destructive authoring of:
//
//   • Item_Antimatter  — containment-class rare loot from BLACK HOLES
//   • Item_DarkMatter  — containment-class rare loot from QUASARS
//   • Containment Vault grid block — the only grid storage built to hold
//     containment-class items (plain cargo refuses them)
//   • Portable canisters — Antimatter Canister / Dark Matter Canister, the safe,
//     stackable way to carry exotic matter (required for the future Star Crafter
//     / World Engine)
//   • Research node "Exotic Containment" (tier 8) after Singularity Harvester
//   • Wires the harvester prefab's rare-drop item fields (only when null)
//
// Re-runnable. Idempotent. Existing authored balance is preserved; only missing
// content is created and broken links are re-wired.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Research;

namespace VoxelEngine.EditorTools
{
    public static class SingularityContainmentSetup
    {
        private const string ASSET_ROOT = "Assets/VoxelEngineAssets";
        private const string GRID_ROOT  = ASSET_ROOT + "/GridSystem";
        private const string ITEMS      = GRID_ROOT + "/Items";
        private const string PREFABS    = GRID_ROOT + "/Prefabs";
        private const string MATS       = PREFABS + "/Mats";
        private const string RECIPES    = GRID_ROOT + "/Recipes";
        private const string NODES      = ASSET_ROOT + "/Research/Nodes";

        public static void RunStep54()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 54 — Containment Systems started.");

            foreach (var f in new[] { GRID_ROOT, ITEMS, PREFABS, MATS, RECIPES }) EnsureFolder(f);

            // ── The exotic matter resources ────────────────────────
            var antimatter = GetOrCreateAsset<ResourceItem>(ASSET_ROOT + "/Items/Item_Antimatter.asset");
            antimatter.itemId = "item_antimatter";
            antimatter.displayName = "Antimatter";
            if (string.IsNullOrEmpty(antimatter.description))
                antimatter.description = "Exotic matter shed by black holes. Annihilates ordinary matter on contact — containment-class storage ONLY (Containment Vault, then portable canisters). Required with Dark Matter for the Star Crafter / World Engine.";
            antimatter.iconTint = new Color(0.98f, 0.25f, 0.65f);
            if (antimatter.maxStack <= 0) antimatter.maxStack = 50;
            if (antimatter.massPerUnit <= 0f) antimatter.massPerUnit = 2f;
            antimatter.requiresContainment = true;
            antimatter.cannotBeCarried = true;
            antimatter.category = "Resources";
            antimatter.subcategory = ResourceCategory.Misc;
            antimatter.fuelSeconds = 0f;
            EditorUtility.SetDirty(antimatter);

            var darkMatter = GetOrCreateAsset<ResourceItem>(ASSET_ROOT + "/Items/Item_DarkMatter.asset");
            darkMatter.itemId = "item_dark_matter";
            darkMatter.displayName = "Dark Matter";
            if (string.IsNullOrEmpty(darkMatter.description))
                darkMatter.description = "Invisible mass captured from quasar jets — it only reveals itself through gravity. Containment-class storage ONLY (Containment Vault, then portable canisters). Required with Antimatter for the Star Crafter / World Engine.";
            darkMatter.iconTint = new Color(0.42f, 0.22f, 0.88f);
            if (darkMatter.maxStack <= 0) darkMatter.maxStack = 50;
            if (darkMatter.massPerUnit <= 0f) darkMatter.massPerUnit = 3f;
            darkMatter.requiresContainment = true;
            darkMatter.cannotBeCarried = true;
            darkMatter.category = "Resources";
            darkMatter.subcategory = ResourceCategory.Misc;
            darkMatter.fuelSeconds = 0f;
            EditorUtility.SetDirty(darkMatter);

            // ── Portable canisters (the safe way to carry it) ──────
            var circuit = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_Circuit.asset");
            var glass   = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_Glass.asset");

            var amCan = GetOrCreateAsset<ResourceItem>(ASSET_ROOT + "/Items/Item_AntimatterCanister.asset");
            amCan.itemId = "item_antimatter_canister";
            amCan.displayName = "Antimatter Canister";
            if (string.IsNullOrEmpty(amCan.description))
                amCan.description = "Portable magnetic bottle holding contained antimatter. The safe, stackable way to carry exotic matter off-grid. Required for the Star Crafter / World Engine.";
            amCan.iconTint = new Color(1.0f, 0.35f, 0.75f);
            if (amCan.maxStack <= 0) amCan.maxStack = 20;
            if (amCan.massPerUnit <= 0f) amCan.massPerUnit = 18f;
            amCan.isPressurizedCanister = true;
            amCan.category = "Components";
            amCan.subcategory = ResourceCategory.Component;
            EditorUtility.SetDirty(amCan);

            var dmCan = GetOrCreateAsset<ResourceItem>(ASSET_ROOT + "/Items/Item_DarkMatterCanister.asset");
            dmCan.itemId = "item_dark_matter_canister";
            dmCan.displayName = "Dark Matter Canister";
            if (string.IsNullOrEmpty(dmCan.description))
                dmCan.description = "Portable graviton bottle holding contained dark matter. The safe, stackable way to carry exotic matter off-grid. Required for the Star Crafter / World Engine.";
            dmCan.iconTint = new Color(0.55f, 0.30f, 0.95f);
            if (dmCan.maxStack <= 0) dmCan.maxStack = 20;
            if (dmCan.massPerUnit <= 0f) dmCan.massPerUnit = 26f;
            dmCan.isPressurizedCanister = true;
            dmCan.category = "Components";
            dmCan.subcategory = ResourceCategory.Component;
            EditorUtility.SetDirty(dmCan);

            // ── Containment Vault prefab (non-destructive) ─────────
            string vaultPrefabPath = PREFABS + "/Prefab_ContainmentVault.prefab";
            var vaultPrefab = GetOrCreatePrefab(vaultPrefabPath, "Prefab_ContainmentVault", (root) =>
            {
                var vault = root.GetComponent<GridContainmentVault>();
                if (vault == null) vault = root.AddComponent<GridContainmentVault>();

                vault.blockName = "Containment Vault";
                if (vault.slots <= 0) vault.slots = 24;
                if (vault.maxMassKg <= 0f) vault.maxMassKg = 120000f;
                // Grand-vault upgrade (9.13.0): the original step-54 generated vault was
                // 12 slots / 50 t — the new flagship is bigger. Bump only when the prefab
                // still carries those original generated values.
                if (vault.slots == 12) vault.slots = 24;
                if (Mathf.Abs(vault.maxMassKg - 50000f) < 1f) vault.maxMassKg = 120000f;

                // Containment field tuning (set only when at script defaults so designer
                // balance tweaks always survive re-runs).
                if (vault.basePowerDrawWatts <= 0f) vault.basePowerDrawWatts = 12000f;
                if (vault.wattsPerStoredUnit <= 0f) vault.wattsPerStoredUnit = 200f;
                if (vault.targetPressure <= 0f) vault.targetPressure = 70f;
                if (vault.stablePressureMin <= 0f) vault.stablePressureMin = 55f;
                if (vault.stablePressureMax <= 0f) vault.stablePressureMax = 85f;
                if (vault.criticalPressure <= 0f) vault.criticalPressure = 32f;
                if (vault.pressureResponsePerSec <= 0f) vault.pressureResponsePerSec = 14f;
                if (vault.pressureDecayPerSec <= 0f) vault.pressureDecayPerSec = 10f;
                if (vault.annihilatePerSecAtZero <= 0f) vault.annihilatePerSecAtZero = 0.25f;
                if (vault.discSpinDegPerSecond <= 0f) vault.discSpinDegPerSecond = 14f;
                if (vault.ringSpinDegPerSecond <= 0f) vault.ringSpinDegPerSecond = 9f;

                // Visuals: built when missing, UPGRADED when the prefab still carries the
                // original 9.12 step-54 generated visual (Hull present, no CoreDisc) or the
                // first 9.13 grand build (CoreDisc present, small pedestal) — 9.14.0 makes
                // both blocks bigger. Designer-customised geometry is never touched.
                bool hasOldVisual = root.transform.Find("Hull") != null && root.transform.Find("CoreDisc") == null;
                var pedestalT = root.transform.Find("Pedestal");
                bool hasSmallGrand = pedestalT != null && pedestalT.localScale.x < 1.95f;
                if ((root.transform.childCount == 0 && root.GetComponent<MeshFilter>() == null) || hasOldVisual || hasSmallGrand)
                {
                    var children = new System.Collections.Generic.List<Transform>();
                    foreach (Transform child in root.transform) children.Add(child);
                    foreach (var child in children) Object.DestroyImmediate(child.gameObject);
                    BuildVaultVisuals(root);
                }

                var bcol = root.GetComponent<BoxCollider>();
                if (bcol == null) bcol = root.AddComponent<BoxCollider>();
                bcol.size = Vector3.one * GridSizeExt.CellSize(GridSize.Large);
            });

            // ── Vault item ─────────────────────────────────────────
            var vaultItem = GetOrCreateAsset<GridBlockItem>(ITEMS + "/GItem_ContainmentVault.asset");
            vaultItem.itemId = "gitem_containmentvault";
            vaultItem.displayName = "Containment Vault";
            if (string.IsNullOrEmpty(vaultItem.description))
                vaultItem.description = "Armoured storage built to hold containment-class matter. Plain cargo refuses antimatter and dark matter — this vault is the only grid storage that accepts them. Hazard-marked; keep it away from the engines.";
            vaultItem.iconTint = new Color(0.50f, 0.28f, 0.90f);
            if (vaultItem.maxStack <= 0) vaultItem.maxStack = 20;
            vaultItem.gridSize = GridSize.Large;
            vaultItem.blockPrefab = vaultPrefab;
            if (vaultItem.blockMass <= 0f) vaultItem.blockMass = 2400f;
            if (vaultItem.blockHP <= 0f) vaultItem.blockHP = 3600f;
            vaultItem.category = "Grid Blocks";
            EditorUtility.SetDirty(vaultItem);

            // ── Recipes (created fully when missing; inputs preserved when authored) ──
            var steelPlate = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_SteelPlate.asset");
            var advCircuit = LoadItem(ASSET_ROOT + "/Industrial/Items/Item_AdvCircuit.asset");
            var platinum   = LoadItem(ASSET_ROOT + "/Items/Item_Platinum.asset");

            var vaultRecipe = GetOrCreateAsset<RecipeDefinition>(RECIPES + "/Recipe_GContainmentVault.asset");
            vaultRecipe.displayName = "Containment Vault";
            vaultRecipe.outputItem = vaultItem;
            vaultRecipe.outputCount = 1;
            vaultRecipe.requiredStation = StationTier.Assembler;
            if (vaultRecipe.craftSeconds <= 0f) vaultRecipe.craftSeconds = 40f;
            vaultRecipe.unlockedByDefault = false;
            if (vaultRecipe.inputs == null || vaultRecipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                if (steelPlate != null) inputs.Add(new RecipeIngredient { item = steelPlate, count = 30 });
                if (glass != null)      inputs.Add(new RecipeIngredient { item = glass, count = 8 });
                if (advCircuit != null) inputs.Add(new RecipeIngredient { item = advCircuit, count = 4 });
                if (platinum != null)   inputs.Add(new RecipeIngredient { item = platinum, count = 4 });
                vaultRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(vaultRecipe);

            var amCanRecipe = GetOrCreateAsset<RecipeDefinition>(RECIPES + "/Recipe_AntimatterCanister.asset");
            amCanRecipe.displayName = "Antimatter Canister";
            amCanRecipe.outputItem = amCan;
            amCanRecipe.outputCount = 1;
            amCanRecipe.requiredStation = StationTier.Assembler;
            if (amCanRecipe.craftSeconds <= 0f) amCanRecipe.craftSeconds = 20f;
            amCanRecipe.unlockedByDefault = false;
            if (amCanRecipe.inputs == null || amCanRecipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                inputs.Add(new RecipeIngredient { item = antimatter, count = 8 });
                if (circuit != null) inputs.Add(new RecipeIngredient { item = circuit, count = 1 });
                if (glass != null)   inputs.Add(new RecipeIngredient { item = glass, count = 2 });
                amCanRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(amCanRecipe);

            var dmCanRecipe = GetOrCreateAsset<RecipeDefinition>(RECIPES + "/Recipe_DarkMatterCanister.asset");
            dmCanRecipe.displayName = "Dark Matter Canister";
            dmCanRecipe.outputItem = dmCan;
            dmCanRecipe.outputCount = 1;
            dmCanRecipe.requiredStation = StationTier.Assembler;
            if (dmCanRecipe.craftSeconds <= 0f) dmCanRecipe.craftSeconds = 20f;
            dmCanRecipe.unlockedByDefault = false;
            if (dmCanRecipe.inputs == null || dmCanRecipe.inputs.Length == 0)
            {
                var inputs = new List<RecipeIngredient>();
                inputs.Add(new RecipeIngredient { item = darkMatter, count = 8 });
                if (circuit != null) inputs.Add(new RecipeIngredient { item = circuit, count = 1 });
                if (glass != null)   inputs.Add(new RecipeIngredient { item = glass, count = 2 });
                dmCanRecipe.inputs = inputs.ToArray();
            }
            EditorUtility.SetDirty(dmCanRecipe);

            var recipeRegistry = AssetDatabase.LoadAssetAtPath<RecipeRegistry>(ASSET_ROOT + "/RecipeRegistry.asset");
            if (recipeRegistry != null)
            {
                foreach (var r in new[] { vaultRecipe, amCanRecipe, dmCanRecipe })
                {
                    if (!recipeRegistry.recipes.Contains(r))
                    {
                        recipeRegistry.recipes.Add(r);
                        EditorUtility.SetDirty(recipeRegistry);
                    }
                }
            }

            // ── Research: "Exotic Containment" after Singularity Harvester ──
            var sciT2 = LoadItem(ASSET_ROOT + "/Items/Item_ScienceT2.asset");
            var sciT3 = LoadItem(ASSET_ROOT + "/Items/Item_ScienceT3.asset");
            var tree = AssetDatabase.LoadAssetAtPath<ResearchTree>(ASSET_ROOT + "/Research/ResearchTree.asset");
            if (tree != null)
            {
                var node = FindNode(tree, "res_containment");
                if (node == null)
                {
                    node = ScriptableObject.CreateInstance<ResearchNode>();
                    node.nodeId = "res_containment";
                    node.displayName = "Exotic Containment";
                    node.description = "Unlocks the Containment Vault and the portable canisters that safely carry antimatter and dark matter. Plain cargo cannot hold exotic matter — everything from here on needs containment-grade engineering.";
                    node.category = ResearchCategory.Environment;
                    node.subCategory = ResearchSubCategory.Building;
                    node.tier = 8;
                    node.column = 7;
                    node.iconTint = new Color(0.50f, 0.28f, 0.90f);
                    node.researchSeconds = 900f;
                    node.cost = new[]
                    {
                        new ResearchNode.ScienceCost { pack = sciT3 as ScienceItem, count = 80 },
                        new ResearchNode.ScienceCost { pack = sciT2 as ScienceItem, count = 100 },
                    };
                    var harvester = FindNode(tree, "res_singularityharvester");
                    if (harvester != null) node.prerequisites = new[] { harvester };
                    AssetDatabase.CreateAsset(node, NODES + "/res_containment.asset");
                    tree.nodes.Add(node);
                }
                node.unlocksRecipes = new[] { vaultRecipe, amCanRecipe, dmCanRecipe };
                EditorUtility.SetDirty(node);
                EditorUtility.SetDirty(tree);
                foreach (var r in node.unlocksRecipes)
                    if (r != null) ResearchRecipeLinker.Register("res_containment", r);
            }

            // ── Wire the harvester prefab's exotic-drop fields (only when null) ──
            WireHarvesterExotics(antimatter, darkMatter);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Containment Systems",
                "Containment Systems wired (non-destructive):\n\n" +
                "• Resources: Antimatter (black holes) + Dark Matter (quasars) — containment-class\n" +
                "• Containment Vault: the only grid storage that accepts exotic matter (plain cargo refuses it)\n" +
                "• Portable canisters: Antimatter Canister / Dark Matter Canister (Assembler) — the safe way to carry it\n" +
                "• Research: Exotic Containment (tier 8) after Singularity Harvester\n" +
                "• The Singularity Harvester now sheds antimatter at black holes and dark matter at quasars\n\n" +
                "Flow: harvester → vault → inventory → craft canisters → future Star Crafter / World Engine.",
                "OK");
        }

        /// <summary>Connect the harvester prefab's exotic-item fields (never overwrites authored ones).</summary>
        private static void WireHarvesterExotics(ItemDefinition antimatter, ItemDefinition darkMatter)
        {
            string prefabPath = PREFABS + "/Prefab_SingularityHarvester.prefab";
            if (AssetDatabase.LoadMainAssetAtPath(prefabPath) == null)
            {
                Debug.LogWarning("[SingularityContainmentSetup] Singularity Harvester prefab not found — run Step 53 first.");
                return;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                var harvester = root.GetComponent<GridSingularityHarvester>();
                if (harvester == null)
                {
                    Debug.LogWarning("[SingularityContainmentSetup] Singularity Harvester prefab has no GridSingularityHarvester component.");
                    return;
                }
                bool changed = false;
                if (harvester.antimatterItem == null) { harvester.antimatterItem = antimatter; changed = true; }
                if (harvester.darkMatterItem == null) { harvester.darkMatterItem = darkMatter; changed = true; }
                if (harvester.antimatterDropChance <= 0f) { harvester.antimatterDropChance = 0.10f; changed = true; }
                if (harvester.darkMatterDropChance <= 0f) { harvester.darkMatterDropChance = 0.08f; changed = true; }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Vault visuals (prefab content only) — the GRAND vault ──
        // Full-cell (2.5 m) containment monument: pedestal, four field pylons,
        // a central column holding the contained black hole (lensed horizon +
        // spinning accretion disc), two counter-rotating containment rings,
        // hazard stripes and a live status light bar driven by the vault script.
        private static void BuildVaultVisuals(GameObject root)
        {
            Material hullMat    = MakeColoredMat("Mat_VaultHull", new Color(0.10f, 0.105f, 0.13f), emissive: false, metallic: 0.75f);
            Material plateMat   = MakeColoredMat("Mat_VaultPlates", new Color(0.16f, 0.17f, 0.20f), emissive: false, metallic: 0.8f);
            Material stripeMat  = MakeColoredMat("Mat_VaultStripe", new Color(0.95f, 0.62f, 0.10f), emissive: true, metallic: 0.4f);
            Material ringMat    = MakeColoredMat("Mat_VaultRing", new Color(0.62f, 0.30f, 0.95f), emissive: true, metallic: 0.5f);
            Material pylonMat   = MakeColoredMat("Mat_VaultPylon", new Color(0.13f, 0.14f, 0.17f), emissive: false, metallic: 0.7f);
            Material pylonTip   = MakeColoredMat("Mat_VaultPylonTip", new Color(0.55f, 0.30f, 0.95f), emissive: true, metallic: 0.5f);
            Material statusMat  = MakeColoredMat("Mat_VaultStatus", new Color(0.25f, 0.9f, 0.45f), emissive: true, metallic: 0.3f);

            // Contained singularity shaders (shipped with the Phase 5 code).
            Material horizonMat = MakeShaderMat("Mat_VaultHorizon", "VoxelEngine/SingularityHorizon");
            Material discMat = MakeShaderMat("Mat_VaultCoreDisc", "VoxelEngine/BlackHoleAccretionDisc");
            if (discMat != null)
            {
                discMat.SetColor("_CoreColor", new Color(1.0f, 0.90f, 0.76f));
                discMat.SetColor("_MidColor", new Color(0.95f, 0.45f, 0.16f));
                discMat.SetColor("_OuterColor", new Color(0.55f, 0.10f, 0.06f));
                discMat.SetFloat("_Brightness", 1.35f);
            }

            // ── 9.14.0 GRAND build: fills the full 2.5 m cell ──
            // Pedestal.
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localScale = new Vector3(2.00f, 0.22f, 2.00f);
            pedestal.transform.localPosition = new Vector3(0f, -1.10f, 0f);
            pedestal.GetComponent<Renderer>().sharedMaterial = hullMat;
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());

            // Top cap.
            var topCap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            topCap.name = "TopCap";
            topCap.transform.SetParent(root.transform, false);
            topCap.transform.localScale = new Vector3(1.88f, 0.16f, 1.88f);
            topCap.transform.localPosition = new Vector3(0f, 1.12f, 0f);
            topCap.GetComponent<Renderer>().sharedMaterial = plateMat;
            Object.DestroyImmediate(topCap.GetComponent<Collider>());

            // Four field pylons with glowing tips.
            foreach (var corner in new[] { new Vector3(-1.00f, 0f, -1.00f), new Vector3(1.00f, 0f, -1.00f),
                                           new Vector3(-1.00f, 0f, 1.00f), new Vector3(1.00f, 0f, 1.00f) })
            {
                var pylon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pylon.name = "FieldPylon";
                pylon.transform.SetParent(root.transform, false);
                pylon.transform.localScale = new Vector3(0.18f, 2.40f, 0.18f);
                pylon.transform.localPosition = corner;
                pylon.GetComponent<Renderer>().sharedMaterial = pylonMat;
                Object.DestroyImmediate(pylon.GetComponent<Collider>());

                var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tip.name = "PylonTip";
                tip.transform.SetParent(root.transform, false);
                tip.transform.localScale = Vector3.one * 0.15f;
                tip.transform.localPosition = corner + new Vector3(0f, 1.26f, 0f);
                tip.GetComponent<Renderer>().sharedMaterial = pylonTip;
                Object.DestroyImmediate(tip.GetComponent<Collider>());
            }

            // Central containment column.
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            column.name = "CoreColumn";
            column.transform.SetParent(root.transform, false);
            column.transform.localScale = new Vector3(0.90f, 0.55f, 0.90f);
            column.GetComponent<Renderer>().sharedMaterial = hullMat;
            Object.DestroyImmediate(column.GetComponent<Collider>());

            // The contained event horizon.
            var horizon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            horizon.name = "CoreHorizon";
            horizon.transform.SetParent(root.transform, false);
            horizon.transform.localScale = Vector3.one * 0.70f;
            horizon.GetComponent<Renderer>().sharedMaterial = horizonMat;
            Object.DestroyImmediate(horizon.GetComponent<Collider>());

            // Accretion disc — spins at runtime via GridContainmentVault.
            var disc = new GameObject("CoreDisc");
            disc.transform.SetParent(root.transform, false);
            disc.transform.localRotation = Quaternion.Euler(72f, 0f, 0f);
            disc.transform.localScale = Vector3.one * 1.60f;
            var discMF = disc.AddComponent<MeshFilter>();
            discMF.sharedMesh = CreateDiscAnnulus(48);
            var discMR = disc.AddComponent<MeshRenderer>();
            discMR.sharedMaterial = discMat;
            discMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            discMR.receiveShadows = false;

            // Counter-rotating containment rings (runtime-driven).
            var ringA = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringA.name = "ContainmentRingA";
            ringA.transform.SetParent(root.transform, false);
            ringA.transform.localScale = new Vector3(2.20f, 0.05f, 2.20f);
            ringA.transform.localRotation = Quaternion.Euler(78f, 0f, 0f);
            ringA.GetComponent<Renderer>().sharedMaterial = ringMat;
            Object.DestroyImmediate(ringA.GetComponent<Collider>());

            var ringB = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ringB.name = "ContainmentRingB";
            ringB.transform.SetParent(root.transform, false);
            ringB.transform.localScale = new Vector3(2.42f, 0.03f, 2.42f);
            ringB.GetComponent<Renderer>().sharedMaterial = ringMat;
            Object.DestroyImmediate(ringB.GetComponent<Collider>());

            // Hazard stripes on the pedestal front.
            for (int i = 0; i < 4; i++)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = "HazardStripe";
                stripe.transform.SetParent(root.transform, false);
                stripe.transform.localScale = new Vector3(0.28f, 0.03f, 1.72f);
                stripe.transform.localPosition = new Vector3(-0.66f + i * 0.44f, -0.98f, 0f);
                stripe.GetComponent<Renderer>().sharedMaterial = stripeMat;
                Object.DestroyImmediate(stripe.GetComponent<Collider>());
            }

            // Live status light bar (colour driven by the vault script at runtime).
            var status = GameObject.CreatePrimitive(PrimitiveType.Cube);
            status.name = "StatusLight";
            status.transform.SetParent(root.transform, false);
            status.transform.localScale = new Vector3(1.44f, 0.07f, 0.07f);
            status.transform.localPosition = new Vector3(0f, -0.97f, 0.96f);
            status.GetComponent<Renderer>().sharedMaterial = statusMat;
            Object.DestroyImmediate(status.GetComponent<Collider>());
        }

        // Flat annulus with polar UVs for the BlackHoleAccretionDisc shader (x = radius, y = angle).
        private static Mesh CreateDiscAnnulus(int segments)
        {
            const float InnerFraction = 0.30f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                verts.Add(new Vector3(ca * InnerFraction, 0f, sa * InnerFraction));
                verts.Add(new Vector3(ca, 0f, sa));
                uvs.Add(new Vector2(0f, i / (float)segments));
                uvs.Add(new Vector2(1f, i / (float)segments));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(b); tris.Add(d);
                tris.Add(a); tris.Add(d); tris.Add(c);
            }

            var mesh = new Mesh { name = "VaultCoreDiscMesh" };
            mesh.vertices = verts.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ── Helpers ────────────────────────────────────────────────
        private static ItemDefinition LoadItem(string path)
            => AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);

        private static ResearchNode FindNode(ResearchTree tree, string id)
        {
            if (tree == null || tree.nodes == null) return null;
            for (int i = 0; i < tree.nodes.Count; i++)
                if (tree.nodes[i] != null && tree.nodes[i].nodeId == id) return tree.nodes[i];
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
            var leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static T GetOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static Material MakeShaderMat(string name, string shaderName)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[SingularityContainmentSetup] Shader '{shaderName}' not found — using URP/Lit fallback.");
                shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
            var mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material MakeColoredMat(string name, Color c, bool emissive, float metallic)
        {
            string path = MATS + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.5f);
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 1.6f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static GameObject GetOrCreatePrefab(string path, string name, System.Action<GameObject> onUpdate)
        {
            GameObject root = null;
            bool loadedPrefabContents = false;
            try
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    try
                    {
                        root = PrefabUtility.LoadPrefabContents(path);
                        loadedPrefabContents = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[SingularityContainmentSetup] Could not load prefab contents at '{path}'. " +
                                         $"The asset will be recreated. Unity said: {ex.Message}");
                        AssetDatabase.DeleteAsset(path);
                    }
                }
                if (root == null) root = new GameObject(name);

                onUpdate?.Invoke(root);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (root != null)
                {
                    if (loadedPrefabContents) PrefabUtility.UnloadPrefabContents(root);
                    else Object.DestroyImmediate(root);
                }
            }
        }
    }
}
#endif
