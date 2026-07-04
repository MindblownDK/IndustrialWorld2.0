#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Editor-only Crest setup helper used by the Voxel Engine Setup Wizard.
    /// It is intentionally separate from the large wizard file so the action is easy to find.
    /// </summary>
    public static class CrestWaterSetupUtility
    {
        [MenuItem("Tools/Voxel Engine/Configure Crest Water Integration")]
        public static void Configure()
        {
            EnsurePanelSettingsFitMode();
            ConfigureSceneAmbientOnly();

            // v3.20.3 – HYBRID: Crest OceanRenderer clipped to voxel water + voxel mesh for lakes/rivers/oil
            var waterMat = ConfigureCrestWaterMaterial();
            // Import Crest ocean rig – we DO want OceanRenderer, but clipped
            ImportCrestMainSceneWaterRig();
            ConfigureCrestRuntimeInScene(waterMat);
            ConfigureVoxelCrestHelpers();
            ConfigureHybridVoxelBridge(waterMat);

            ConfigureExistingMaritimeWakeEmitters();

            AssetDatabase.SaveAssets();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

            EditorUtility.DisplayDialog("Voxel Engine - Crest Water v3.20",
                "Crest water integration configured for the active scene.\n\n" +
                "Configured:\n" +
                "• UI PanelSettings fit mode\n" +
                "• Shallow/clear Crest water material values\n" +
                "• Crest sample planes removed from the scene\n" +
                "• Generated voxel ocean/lake water re-enabled with procedural patch fallback\n" +
                "• Existing maritime grids with water-only Crest wake emitters\n" +
                "• Scene lighting and solar-system sun left untouched", "OK");
        }

        private static void EnsurePanelSettingsFitMode()
        {
            const string resourcesFolder = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            const string panelSettingsPath = "Assets/Resources/MenuPanelSettings.asset";
            var panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(panelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                panelSettings.name = "MenuPanelSettings";
                AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
            }

            panelSettings.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.Shrink;
            panelSettings.match = 0.5f;
            panelSettings.referenceDpi = 96;
            panelSettings.fallbackDpi = 96;
            EditorUtility.SetDirty(panelSettings);
        }

        private static void ConfigureSceneAmbientOnly()
        {
            // Do not create or modify a directional sun here. The solar-system
            // generator owns the real sun/light. We only keep ambient/fog from
            // washing terrain materials out to white after earlier setup passes.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.40f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.23f, 0.27f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);
            RenderSettings.fog = false;
        }

        private static void ConfigureCrestProceduralVoxelWaterMode()
        {
            // For spherical/procedural worlds we must not leave Crest's sample-scene
            // infinite ocean plane in the scene. Until we generate proper Crest clip
            // masks/patches from voxel water bodies, the correct visual source is the
            // procedural voxel water surface itself.
            RemoveExistingCrestRuntimeObjects();

            var bootstraps = Object.FindObjectsByType<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrap = bootstraps != null && bootstraps.Length > 0 ? bootstraps[0] : null;
            if (bootstrap == null)
            {
                var go = GameObject.Find("Liquid Visual Runtime") ?? new GameObject("Liquid Visual Runtime");
                bootstrap = go.GetComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
                if (bootstrap == null) bootstrap = go.AddComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
            }

            // Keep generated voxel/chunk water active as the authoritative visual
            // fallback while the procedural patch bridge stabilizes. The patch renderer
            // is also configured below, but old water must remain visible if patch
            // sampling misses a frame or a body has not streamed yet.
            bootstrap.renderVoxelLiquidSurfaces = true;
            bootstrap.rescheduleVisibleLiquidSurfaces = true;
            bootstrap.liquidRescheduleChunkRadius = 6;
            bootstrap.liquidRescheduleInterval = 0.35f;
            bootstrap.waterMaterialOverride = null;
            bootstrap.oilMaterialOverride = null;
            VoxelEngine.WaterSim.WaterMeshBuilder.RenderingEnabled = true;
            EnableExistingVoxelLiquidSurfaceObjects();
            ConfigureProceduralPatchRenderer();
            EditorUtility.SetDirty(bootstrap);
        }

        private static void ConfigureProceduralPatchRenderer()
        {
            // Recreate this object cleanly every time. Older scene saves may have
            // a disabled script or a MeshRenderer without a MeshFilter; starting
            // fresh avoids persistent bad component states.
            var existing = Object.FindObjectsByType<VoxelEngine.WaterSim.ProceduralWaterPatchRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing != null)
            {
                foreach (var r in existing)
                {
                    if (r != null && !string.IsNullOrEmpty(r.gameObject.scene.name))
                        Object.DestroyImmediate(r.gameObject);
                }
            }

            var go = new GameObject("Procedural Water Patch Renderer");
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.enabled = true;
            var renderer = go.AddComponent<VoxelEngine.WaterSim.ProceduralWaterPatchRenderer>();
            renderer.enabled = true;
            renderer.keepLastValidMesh = true;
            renderer.fastSphericalOceanPatch = true;
            renderer.keepLastValidMesh = true;
            renderer.searchRadius = 512f;
            renderer.tileSize = 24f;
            renderer.maxTilesPerAxis = 40;
            renderer.rebuildInterval = 0.75f;
            renderer.anchorMoveThreshold = 32f;
            renderer.shallowDepth = 2.5f;
            renderer.deepDepth = 28f;
            EditorUtility.SetDirty(renderer);
        }

        private static void ImportCrestMainSceneWaterRig()
        {
            const string prefabPath = "Assets/Liquid/Crest/Crest-Examples/Main/Scenes/Internal/MainSceneCore.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[CrestWaterSetup] MainSceneCore prefab was not found. Cannot copy the known-good Crest test-scene water rig.");
                return;
            }

            RemoveExistingCrestRuntimeObjects();

            GameObject contents = null;
            GameObject runtimeRoot = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(prefabPath);
                var oceanSource = FindChildByName(contents.transform, "Ocean");
                var wavesSource = FindChildByName(contents.transform, "Waves");

                runtimeRoot = new GameObject("Crest Water Runtime");
                Undo.RegisterCreatedObjectUndo(runtimeRoot, "Create Crest Water Runtime");

                if (oceanSource != null)
                {
                    var ocean = Object.Instantiate(oceanSource.gameObject);
                    ocean.name = "Ocean";
                    ocean.transform.SetParent(runtimeRoot.transform, worldPositionStays: true);
                    Undo.RegisterCreatedObjectUndo(ocean, "Create Crest Ocean From Main Scene");
                }
                else Debug.LogWarning("[CrestWaterSetup] Could not find Ocean object in MainSceneCore prefab.");

                if (wavesSource != null)
                {
                    var waves = Object.Instantiate(wavesSource.gameObject);
                    waves.name = "Waves";
                    waves.transform.SetParent(runtimeRoot.transform, worldPositionStays: true);
                    Undo.RegisterCreatedObjectUndo(waves, "Create Crest Waves From Main Scene");
                }
                else Debug.LogWarning("[CrestWaterSetup] Could not find Waves object in MainSceneCore prefab.");
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void RemoveExistingCrestRuntimeObjects()
        {
            string[] names = { "Crest Water Runtime", "Crest Ocean", "Crest Animated Waves", "Ocean", "Waves" };
            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in transforms)
            {
                if (t == null || string.IsNullOrEmpty(t.gameObject.scene.name)) continue;
                for (int i = 0; i < names.Length; i++)
                {
                    if (t.name == names[i])
                    {
                        Object.DestroyImmediate(t.gameObject);
                        break;
                    }
                }
            }

            var oceanType = FindType("Crest.OceanRenderer");
            if (oceanType != null)
            {
                var found = Resources.FindObjectsOfTypeAll(oceanType);
                if (found != null)
                {
                    foreach (var candidate in found)
                    {
                        var component = candidate as Component;
                        if (component == null || string.IsNullOrEmpty(component.gameObject.scene.name)) continue;
                        Object.DestroyImmediate(component.gameObject);
                    }
                }
            }
        }

        private static Material ConfigureCrestWaterMaterial()
        {
            string[] candidates =
            {
                "Assets/Liquid/Crest/Crest-Examples/Examples/Materials/Examples_Material_Ocean.mat",
                "Assets/Liquid/Crest/Crest/Materials/Ocean.mat",
                "Assets/Liquid/Crest/Crest-Examples/LakesAndRivers/Materials/LakesAndRivers_Material_Water.mat"
            };

            Material mat = null;
            foreach (var path in candidates)
            {
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) break;
            }

            if (mat == null)
            {
                Debug.LogWarning("[CrestWaterSetup] Crest water material was not found under Assets/Liquid/Crest. Import Crest first, then rerun Step 16.");
                return null;
            }

            if (mat.HasProperty("_Transparency")) mat.SetFloat("_Transparency", 1f);
            if (mat.HasProperty("_SubSurfaceShallowColour")) mat.SetFloat("_SubSurfaceShallowColour", 1f);
            if (mat.HasProperty("_SubSurfaceShallowCol")) mat.SetColor("_SubSurfaceShallowCol", new Color(0.42f, 0.78f, 0.72f, 1f));
            if (mat.HasProperty("_SubSurfaceShallowColShadow")) mat.SetColor("_SubSurfaceShallowColShadow", new Color(0.10f, 0.28f, 0.32f, 1f));
            if (mat.HasProperty("_SubSurfaceDepthMax")) mat.SetFloat("_SubSurfaceDepthMax", 7.5f);
            if (mat.HasProperty("_SubSurfaceDepthPower")) mat.SetFloat("_SubSurfaceDepthPower", 2.1f);
            if (mat.HasProperty("_DepthFogDensity")) mat.SetVector("_DepthFogDensity", new Vector4(0.10f, 0.08f, 0.055f, 1f));
            if (mat.HasProperty("_Diffuse")) mat.SetColor("_Diffuse", new Color(0.015f, 0.18f, 0.32f, 1f));
            if (mat.HasProperty("_DiffuseGrazing")) mat.SetColor("_DiffuseGrazing", new Color(0.11f, 0.36f, 0.42f, 1f));
            if (mat.HasProperty("_NormalsStrengthOverall")) mat.SetFloat("_NormalsStrengthOverall", 1f);
            if (mat.HasProperty("_ApplyNormalMapping")) mat.SetFloat("_ApplyNormalMapping", 1f);
            if (mat.HasProperty("_NormalsStrength")) mat.SetFloat("_NormalsStrength", 0.55f);
            if (mat.HasProperty("_NormalsScale")) mat.SetFloat("_NormalsScale", 32f);
            if (mat.HasProperty("_Flow")) mat.SetFloat("_Flow", 1f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ConfigureCrestRuntimeInScene(Material waterMaterial)
        {
            var oceanType = FindType("Crest.OceanRenderer");
            Component ocean = null;
            if (oceanType != null)
            {
                var found = Resources.FindObjectsOfTypeAll(oceanType);
                if (found != null)
                {
                    foreach (var candidate in found)
                    {
                        var component = candidate as Component;
                        if (component == null || string.IsNullOrEmpty(component.gameObject.scene.name)) continue;
                        ocean = component;
                        break;
                    }
                }

                if (ocean == null)
                {
                    var go = new GameObject("Crest Ocean");
                    ocean = go.AddComponent(oceanType);
                    Undo.RegisterCreatedObjectUndo(go, "Create Crest Ocean");
                }

                ConfigureSerializedCrestOcean(ocean, waterMaterial);
                ConfigureVoxelBinder(ocean);
            }
            else
            {
                Debug.LogWarning("[CrestWaterSetup] Crest.OceanRenderer type was not found. Crest may still be compiling or the imported package may be incomplete.");
            }

            var bootstraps = Object.FindObjectsByType<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrap = bootstraps != null && bootstraps.Length > 0 ? bootstraps[0] : null;
            if (bootstrap == null)
            {
                var go = GameObject.Find("Liquid Visual Runtime") ?? new GameObject("Liquid Visual Runtime");
                bootstrap = go.GetComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
                if (bootstrap == null) bootstrap = go.AddComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
            }

            // Crest should render the water visually. Do not assign Crest's ocean material
            // to voxel chunk water meshes; that makes the old voxel surface look flat/oily.
            bootstrap.renderVoxelLiquidSurfaces = false;
            bootstrap.waterMaterialOverride = null;
            EditorUtility.SetDirty(bootstrap);
            DisableExistingVoxelLiquidSurfaceObjects();
        }

        private static void EnableExistingVoxelLiquidSurfaceObjects()
        {
            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in transforms)
            {
                if (t != null && t.name == "LiquidSurface")
                    t.gameObject.SetActive(true);
            }
        }

        private static void DisableExistingVoxelLiquidSurfaceObjects()
        {
            var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in transforms)
            {
                if (t != null && t.name == "LiquidSurface")
                    t.gameObject.SetActive(false);
            }
        }

        private static void ConfigureSerializedCrestOcean(Component ocean, Material waterMaterial)
        {
            if (ocean == null) return;

            var so = new SerializedObject(ocean);
            SetBool(so, "_createDynamicWaveSim", true);
            SetBool(so, "_createFlowSim", true);
            SetBool(so, "_createSeaFloorDepthData", true);
            SetBool(so, "_createFoamSim", true);
            SetBool(so, "_heightQueries", true);
            SetBool(so, "_hideOceanTileGameObjects", true);
            SetNumber(so, "_lodDataResolution", 384f);
            SetNumber(so, "_geometryDownSampleFactor", 4f);
            SetNumber(so, "_lodCount", 7f);
            SetNumber(so, "_minScale", 4f);
            SetNumber(so, "_maxScale", 256f);

            if (waterMaterial != null)
            {
                var materialProperty = so.FindProperty("_material");
                if (materialProperty != null) materialProperty.objectReferenceValue = waterMaterial;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ocean);
        }

        private static void ConfigureVoxelBinder(Component ocean)
        {
            if (ocean == null) return;
            var binder = ocean.GetComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
            if (binder == null) binder = ocean.gameObject.AddComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
            binder.followNearestProceduralWater = true;
            binder.alignToPlanetSurface = true;
            binder.waterSearchRadius = 768f;
            binder.waterSearchSpacing = 24f;
            binder.waterHeightOffset = 0.08f;
            binder.smoothFollow = true;
            binder.forceOceanAlwaysOn = true;
            EditorUtility.SetDirty(binder);
        }

        private static void ConfigureVoxelCrestHelpers()
        {
            // Attach helper components to the Crest Ocean root
            var oceanType = FindType("Crest.OceanRenderer");
            Component ocean = null;
            if (oceanType != null)
            {
                var found = Resources.FindObjectsOfTypeAll(oceanType);
                foreach (var c in found)
                {
                    var comp = c as Component;
                    if (comp != null && !string.IsNullOrEmpty(comp.gameObject.scene.name))
                    { ocean = comp; break; }
                }
            }
            if (ocean == null) return;

            var go = ocean.gameObject;

            var depthProvider = go.GetComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>();
            if (depthProvider == null) depthProvider = go.AddComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>();
            depthProvider.sampleRadius = 64f;
            depthProvider.samplesPerAxis = 14;
            depthProvider.updateInterval = 0.25f;

            var foam = go.GetComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>();
            if (foam == null) foam = go.AddComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>();
            foam.scanRadius = 96f;
            foam.maxEmitters = 36;
            foam.emitterRadius = 1.9f;

            EditorUtility.SetDirty(go);
        }

        // v3.20.2 – bridge Crest material to voxel water mesh – NO ocean plane
        private static void ConfigureCrestVoxelMaterialBridge(Material waterMaterial)
        {
            // Remove any Crest ocean plane – we do NOT want a big water plane
            RemoveExistingCrestRuntimeObjects();

            var bootstraps = Object.FindObjectsByType<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrap = bootstraps != null && bootstraps.Length > 0 ? bootstraps[0] : null;
            if (bootstrap == null)
            {
                var go = GameObject.Find("Liquid Visual Runtime") ?? new GameObject("Liquid Visual Runtime");
                bootstrap = go.GetComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
                if (bootstrap == null) bootstrap = go.AddComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
            }

            // Enable voxel liquid surfaces – with Crest material override
            bootstrap.renderVoxelLiquidSurfaces = true;
            bootstrap.rescheduleVisibleLiquidSurfaces = true;
            bootstrap.liquidRescheduleChunkRadius = 4;
            bootstrap.liquidRescheduleInterval = 0.5f;
            bootstrap.meshBuildBudgetPerFrame = 2; // reduce lag
            bootstrap.waterMaterialOverride = waterMaterial;
            bootstrap.oilMaterialOverride = null;

            VoxelEngine.WaterSim.WaterMeshBuilder.RenderingEnabled = true;
            VoxelEngine.WaterSim.WaterMeshBuilder.SetMaterialOverrides(waterMaterial, null);

            EnableExistingVoxelLiquidSurfaceObjects();
            EditorUtility.SetDirty(bootstrap);

            // Add a lightweight binder (no ocean plane) just to feed Crest globals / foam
            var binderGO = GameObject.Find("CrestVoxelBridge") ?? new GameObject("CrestVoxelBridge");
            var binder = binderGO.GetComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
            if (binder == null) binder = binderGO.AddComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
            binder.disableCrestOceanPlane = true;
            binder.bridgeCrestMaterialToVoxelMesh = true;
            binder.autoConfigureCrestMaterial = true;
            EditorUtility.SetDirty(binderGO);

            // Depth + foam helpers on same GO
            var depth = binderGO.GetComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>() ?? binderGO.AddComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>();
            var foam = binderGO.GetComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>() ?? binderGO.AddComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>();
            EditorUtility.SetDirty(depth);
            EditorUtility.SetDirty(foam);

            Debug.Log("[CrestWaterSetup] ✓ Crest material bridged to voxel water – no ocean plane – v3.20.2");
        }

        // v3.20.3 HYBRID – Crest Ocean clipped + voxel mesh
        private static void ConfigureHybridVoxelBridge(Material waterMaterial)
        {
            var bootstraps = Object.FindObjectsByType<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrap = bootstraps != null && bootstraps.Length > 0 ? bootstraps[0] : null;
            if (bootstrap == null)
            {
                var go = GameObject.Find("Liquid Visual Runtime") ?? new GameObject("Liquid Visual Runtime");
                bootstrap = go.GetComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
                if (bootstrap == null) bootstrap = go.AddComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
            }

            // HYBRID: voxel mesh ON for lakes/rivers/oil puddles, Crest Ocean ON for big oceans (clipped)
            bootstrap.renderVoxelLiquidSurfaces = true;
            bootstrap.rescheduleVisibleLiquidSurfaces = true;
            bootstrap.liquidRescheduleChunkRadius = 3;
            bootstrap.liquidRescheduleInterval = 0.6f;
            bootstrap.meshBuildBudgetPerFrame = 2;
            bootstrap.waterMaterialOverride = waterMaterial; // Crest material on voxel mesh
            bootstrap.oilMaterialOverride = null;

            VoxelEngine.WaterSim.WaterMeshBuilder.RenderingEnabled = true;
            VoxelEngine.WaterSim.WaterMeshBuilder.SetMaterialOverrides(waterMaterial, null);
            EnableExistingVoxelLiquidSurfaceObjects();
            EditorUtility.SetDirty(bootstrap);

            // Find Crest Ocean that was just imported
            var oceanType = FindType("Crest.OceanRenderer");
            Component ocean = null;
            if (oceanType != null)
            {
                var found = Object.FindObjectsByType(oceanType, FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in found) { var comp = c as Component; if (comp != null && !string.IsNullOrEmpty(comp.gameObject.scene.name)) { ocean = comp; break; } }
            }
            if (ocean != null)
            {
                // Ensure clip surface is enabled in OceanRenderer
                var so = new SerializedObject(ocean);
                var clipProp = so.FindProperty("_createClipSurfaceData");
                if (clipProp != null) { clipProp.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }

                var go = ocean.gameObject;
                // Add clip surface provider – this REMOVES the big plane artifact
                var clip = go.GetComponent<VoxelEngine.WaterSim.VoxelCrestClipSurfaceProvider>();
                if (clip == null) clip = go.AddComponent<VoxelEngine.WaterSim.VoxelCrestClipSurfaceProvider>();
                clip.radius = 900f;
                clip.cellSize = 28f;
                clip.minDepthToInclude = 1.2f;
                clip.rebuildInterval = 0.45f;

                // Also keep depth + foam helpers
                var depth = go.GetComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>() ?? go.AddComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>();
                var foam = go.GetComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>() ?? go.AddComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>();

                // Configure binder to NOT disable ocean – we WANT ocean tiles, but clipped
                var binder = go.GetComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>() ?? go.AddComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
                binder.disableCrestOceanPlane = false; // allow Crest tiles
                binder.forceOceanAlwaysOn = true;
                binder.bridgeCrestMaterialToVoxelMesh = true;
                binder.alignToPlanetSurface = true;

                EditorUtility.SetDirty(go);
                Debug.Log("[CrestWaterSetup] ✓ HYBRID v3.20.3 – Crest Ocean clipped + voxel mesh – no unclipped plane");
            }

            // Oil second ocean
            ConfigureOilOcean(waterMaterial);
        }

        private static void ConfigureOilOcean(Material waterMaterial)
        {
            // Create a second Crest OceanRenderer for oil – disabled by default, enabled by CrestOilOceanController when oil is nearby
            var oceanType = FindType("Crest.OceanRenderer");
            if (oceanType == null) return;

            var existingOil = GameObject.Find("Crest Oil Ocean");
            if (existingOil == null)
            {
                var go = new GameObject("Crest Oil Ocean");
                var ocean = go.AddComponent(oceanType);
                // copy serialized settings from main ocean if present
                var mainOcean = Object.FindFirstObjectByType(oceanType) as Component;
                if (mainOcean != null && ocean != mainOcean)
                {
                    UnityEditor.EditorUtility.CopySerialized(mainOcean, ocean);
                }
                // attach oil controller
                var oilCtrl = go.AddComponent<VoxelEngine.WaterSim.CrestOilOceanController>();
                oilCtrl.oilMaterialOverride = null; // user can assign viscous oil mat
                Undo.RegisterCreatedObjectUndo(go, "Create Crest Oil Ocean");
            }
        }

        private static void ConfigureExistingMaritimeWakeEmitters()
        {
            var grids = Object.FindObjectsByType<VoxelEngine.GridSystem.GridEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var grid in grids)
            {
                if (grid == null) continue;
                if (grid.GetComponent<VoxelEngine.Maritime.MaritimePropulsionSystem>() == null) continue;

                var emitter = grid.GetComponent<VoxelEngine.Maritime.CrestMaritimeWakeEmitter>();
                if (emitter == null) emitter = grid.gameObject.AddComponent<VoxelEngine.Maritime.CrestMaritimeWakeEmitter>();
                emitter.requireWaterContact = true;
                emitter.maxInteractionProbes = Mathf.Clamp(emitter.maxInteractionProbes, 8, 64);
                emitter.wakeWeight = Mathf.Max(1.15f, emitter.wakeWeight);
                EditorUtility.SetDirty(emitter);
            }
        }

        private static System.Type FindType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            return null;
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            var p = so.FindProperty(name);
            if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value;
        }

        private static void SetNumber(SerializedObject so, string name, float value)
        {
            var p = so.FindProperty(name);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
            else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(value);
        }
    }
}
#endif
