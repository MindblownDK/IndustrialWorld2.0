#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Editor-only Crest setup helper used by the Voxel Engine Setup Wizard.
    /// v3.12.0 – GENUINE CREST BRIDGE:
    ///   • OceanRenderer is KEPT ALIVE (LOD cascade driver)
    ///   • Crest's own infinite ocean tiles are hidden at runtime by the binder
    ///   • Voxel water chunks are painted with the real Crest/Ocean material
    ///     and each carries a VoxelCrestChunkBinder so waves / foam / flow show up
    /// </summary>
    public static class CrestWaterSetupUtility
    {
        [MenuItem("Tools/Voxel Engine/Configure Crest Water Integration")]
        public static void Configure()
        {
            try
            {
                EnsurePanelSettingsFitMode();
                ConfigureSceneAmbientOnly();

                // v3.12.0 – GENUINE CREST BRIDGE – use the real Crest/Ocean material.
                // Fall back to a stylized voxel material only if Crest is missing.
                var waterMat = ConfigureCrestWaterMaterial();
                if (waterMat == null)
                {
                    waterMat = CreateFallbackWaterMaterial();
                    Debug.LogWarning("[CrestWaterSetup] Crest material not found – using fallback URP Lit water.");
                }

                // v3.12.0 – DO NOT DESTROY OCEAN RENDERER. It is the LOD driver
                // that powers Crest waves / foam / flow. The runtime binder will
                // simply hide its infinite ocean tiles for us.
                EnsureCrestOceanRendererInScene(waterMat);

                ConfigureCrestVoxelMaterialBridge(waterMat);

                ConfigureExistingMaritimeWakeEmitters();

                AssetDatabase.SaveAssets();
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                if (scene.IsValid() && scene.isLoaded)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

                EditorUtility.DisplayDialog("Voxel Engine - Crest Water v3.12.0",
                    "Genuine Crest bridge configured.\n\n" +
                    "Configured:\n" +
                    "• UI PanelSettings fit mode\n" +
                    "• Real Crest/Ocean material assigned to voxel water\n" +
                    "• OceanRenderer KEPT ALIVE (LOD cascade driver)\n" +
                    "• Crest built-in ocean tiles hidden at runtime\n" +
                    "• VoxelCrestChunkBinder auto-attached per water chunk\n" +
                    "• WaterMeshBuilder.RenderingEnabled = true\n" +
                    "• Maritime wake emitters updated",
                    "OK");

                Debug.Log("[CrestWaterSetup] ✓ v3.12.0 – Genuine Crest bridge: OceanRenderer alive, tiles hidden, voxel mesh uses Crest/Ocean shader.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CrestWaterSetup] Configure FAILED: " + ex);
                EditorUtility.DisplayDialog("Crest Water Setup FAILED", ex.Message + "\n\nSee console for full stack trace.\n\nThe setup attempted to nuke Crest planes safely – check that Crest URP package is imported at Assets/Liquid/Crest/", "OK");
            }
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
            // generator owns the real sun/light.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.40f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.23f, 0.27f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.10f, 0.12f, 1f);
            RenderSettings.fog = false;
        }

        // v3.20.9 safe nuke – replaces all previous Nuke / RemoveExisting versions
        private static void SafeNukeAllCrest()
        {
            try
            {
                RemoveExistingCrestRuntimeObjects();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CrestWaterSetup] SafeNuke warning: " + e.Message);
            }
        }

        private static void RemoveExistingCrestRuntimeObjects()
        {
            string[] exactNames = {
                "Crest Water Runtime", "Crest Ocean", "Crest Animated Waves",
                "Ocean", "Waves", "Crest Oil Ocean", "CrestVoxelBridge",
                "Procedural Water Patch Renderer", "VoxelCrestClipSurfaceProvider"
            };

            var objectsToDestroy = new System.Collections.Generic.HashSet<GameObject>();

            // First pass: collect scene objects by name only. Do not destroy while iterating
            // Unity's object list; Crest editor callbacks can mutate the hierarchy during teardown.
            try
            {
                var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var t in transforms)
                {
                    if (!IsLiveSceneObject(t)) continue;

                    string objectName = t.name;
                    bool shouldDestroy = objectName.StartsWith("Ocean ", StringComparison.Ordinal) ||
                                         objectName.StartsWith("Crest ", StringComparison.Ordinal);

                    if (!shouldDestroy)
                    {
                        for (int i = 0; i < exactNames.Length; i++)
                        {
                            if (objectName == exactNames[i])
                            {
                                shouldDestroy = true;
                                break;
                            }
                        }
                    }

                    if (shouldDestroy)
                        objectsToDestroy.Add(t.gameObject);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CrestWaterSetup] Crest scene-name scan skipped: " + e.Message);
            }

            // Second pass: collect known Crest runtime Components by concrete Component type only.
            // This intentionally does NOT scan every Crest.* type. Crest.LodDataMgr is a plain C#
            // manager class, not a UnityEngine.Object, and passing it to Unity object-finders causes:
            // "FindAllObjectsOfType: The type has to be derived from UnityEngine.Object. Type is LodDataMgr".
            AddSceneComponentOwners(objectsToDestroy,
                "Crest.OceanRenderer",
                "Crest.OceanChunkRenderer",
                "Crest.OceanPlanarReflection");

            DestroyCollectedSceneObjects(objectsToDestroy);
        }

        private static void AddSceneComponentOwners(System.Collections.Generic.HashSet<GameObject> results, params string[] fullTypeNames)
        {
            if (results == null || fullTypeNames == null) return;

            for (int i = 0; i < fullTypeNames.Length; i++)
            {
                var type = FindTypeSafe(fullTypeNames[i]);
                if (!IsSceneComponentType(type))
                    continue;

                UnityEngine.Object[] found = SafeFindObjectsByType(type);
                for (int j = 0; j < found.Length; j++)
                {
                    var component = found[j] as Component;
                    if (!IsLiveSceneObject(component)) continue;
                    results.Add(component.gameObject);
                }
            }
        }

        private static void DestroyCollectedSceneObjects(System.Collections.Generic.HashSet<GameObject> objectsToDestroy)
        {
            if (objectsToDestroy == null || objectsToDestroy.Count == 0) return;

            var ordered = new System.Collections.Generic.List<GameObject>(objectsToDestroy);
            ordered.Sort((a, b) => GetHierarchyDepth(b).CompareTo(GetHierarchyDepth(a))); // children first

            for (int i = 0; i < ordered.Count; i++)
            {
                var go = ordered[i];
                if (!IsLiveSceneObject(go)) continue;

                try
                {
                    Undo.DestroyObjectImmediate(go);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CrestWaterSetup] Could not destroy Crest runtime object '{go.name}': {e.Message}");
                }
            }
        }

        private static int GetHierarchyDepth(GameObject go)
        {
            if (go == null) return 0;
            int depth = 0;
            var current = go.transform;
            while (current != null && current.parent != null)
            {
                depth++;
                current = current.parent;
            }
            return depth;
        }

        private static Material ConfigureCrestWaterMaterial()
        {
            // v3.12.0 – Prefer the REAL Crest ocean material. Crest's shader
            // samples LOD cascades (animated waves, foam, flow, shadow,
            // sea-floor depth) that OceanRenderer populates each frame. The
            // per-chunk VoxelCrestChunkBinder attaches an MPB with _LD_SliceIndex
            // so each voxel chunk becomes a valid Crest LOD tile.
            var crestMat = LoadOrCopyCrestOceanMaterial();
            if (crestMat != null)
            {
                Debug.Log("[CrestWaterSetup] ✓ Using genuine Crest ocean material: " + crestMat.name);
                return crestMat;
            }

            Debug.LogWarning("[CrestWaterSetup] Crest ocean material not found under Assets/Liquid/Crest – using stylized voxel water shader as fallback.");
            var fallback = CreateOrUpdateVoxelWaterVisualMaterial();
            return fallback != null ? fallback : CreateFallbackWaterMaterial();
        }

        /// <summary>
        /// Loads the shipped Crest Ocean.mat, copies it to Resources/ so the
        /// runtime WaterMeshBuilder can find it, and returns the runtime copy.
        /// </summary>
        private static Material LoadOrCopyCrestOceanMaterial()
        {
            const string crestOceanMatPath = "Assets/Liquid/Crest/Crest/Materials/Ocean.mat";
            var source = AssetDatabase.LoadAssetAtPath<Material>(crestOceanMatPath);
            if (source == null)
            {
                // Fallback – search the project for any material using Crest/Ocean shader.
                var crestShader = Shader.Find("Crest/Ocean");
                if (crestShader == null) return null;
                string[] guids = AssetDatabase.FindAssets("t:Material");
                for (int i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat != null && mat.shader == crestShader) { source = mat; break; }
                }
                if (source == null) return null;
            }

            const string resourcesFolder = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            const string bridgeMatPath = "Assets/Resources/CrestOcean_VoxelBridge.mat";
            var bridge = AssetDatabase.LoadAssetAtPath<Material>(bridgeMatPath);
            if (bridge == null)
            {
                bridge = new Material(source) { name = "CrestOcean_VoxelBridge" };
                AssetDatabase.CreateAsset(bridge, bridgeMatPath);
            }
            else
            {
                EditorUtility.CopySerialized(source, bridge);
                bridge.name = "CrestOcean_VoxelBridge";
                EditorUtility.SetDirty(bridge);
            }
            AssetDatabase.SaveAssets();
            return bridge;
        }

        /// <summary>
        /// v3.12.0 – ensures a Crest OceanRenderer exists in the scene and uses
        /// the shared bridge material. If one already exists, its material is
        /// updated in place.
        /// </summary>
        private static void EnsureCrestOceanRendererInScene(Material waterMaterial)
        {
            var oceanType = FindTypeSafe("Crest.OceanRenderer");
            if (oceanType == null)
            {
                Debug.LogWarning("[CrestWaterSetup] Crest.OceanRenderer type not found. Import Crest under Assets/Liquid/Crest.");
                return;
            }

            var existing = UnityEngine.Object.FindFirstObjectByType(oceanType) as Component;
            if (existing == null)
            {
                var go = new GameObject("Crest Ocean");
                existing = go.AddComponent(oceanType) as Component;
                Undo.RegisterCreatedObjectUndo(go, "Create Crest Ocean");
            }
            if (existing == null) return;

            ConfigureSerializedCrestOcean(existing, waterMaterial);

            var t = existing.transform;
            var world = UnityEngine.Object.FindFirstObjectByType<VoxelEngine.Core.VoxelWorld>();
            if (world != null)
            {
                float seaY = world.flatSeaLevel * VoxelEngine.Core.VoxelConstants.VOXEL_SIZE;
                t.position = new Vector3(t.position.x, seaY, t.position.z);
            }
        }

        private static Material CreateOrUpdateVoxelWaterVisualMaterial()
        {
            try
            {
                const string folder = "Assets/Resources";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Resources");

                const string path = "Assets/Resources/CrestOcean_VoxelBridge.mat";
                var shader = Shader.Find("VoxelEngine/VoxelWaterURP")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard");
                if (shader == null) return null;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader) { name = "CrestOcean_VoxelBridge" };
                    AssetDatabase.CreateAsset(mat, path);
                }
                else if (mat.shader != shader)
                {
                    mat.shader = shader;
                }

                ConfigureVoxelWaterMaterial(mat);
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
                return mat;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CrestWaterSetup] Voxel water visual material setup failed: " + e.Message);
                return null;
            }
        }

        private static void ConfigureVoxelWaterMaterial(Material mat)
        {
            if (mat == null) return;

            ConfigureTransparent(mat);
            SetColorIfPresent(mat, "_ShallowColor", new Color(0.10f, 0.70f, 0.92f, 0.94f));
            SetColorIfPresent(mat, "_DeepColor", new Color(0.005f, 0.08f, 0.24f, 0.98f));
            SetColorIfPresent(mat, "_FoamColor", new Color(0.92f, 0.98f, 1.00f, 0.90f));
            SetColorIfPresent(mat, "_BaseColor", new Color(0.08f, 0.52f, 0.82f, 0.88f));
            SetColorIfPresent(mat, "_Color", new Color(0.08f, 0.52f, 0.82f, 0.88f));

            SetFloatIfPresent(mat, "_DeepWaveAmplitude", 0.82f);
            SetFloatIfPresent(mat, "_DeepWaveFrequency", 0.22f);
            SetFloatIfPresent(mat, "_DeepWaveSpeed", 0.55f);
            SetFloatIfPresent(mat, "_SecondaryWaveAmplitude", 0.32f);
            SetFloatIfPresent(mat, "_SecondaryWaveFrequency", 0.47f);
            SetFloatIfPresent(mat, "_SecondaryWaveSpeed", 0.91f);
            SetFloatIfPresent(mat, "_ShallowWaveAmplitude", 0.15f);
            SetFloatIfPresent(mat, "_ShallowWaveFrequency", 1.65f);
            SetFloatIfPresent(mat, "_ShallowWaveSpeed", 1.8f);
            SetFloatIfPresent(mat, "_WaveChop", 0.28f);
            SetFloatIfPresent(mat, "_PlanetWaveBlend", 1.0f);
            SetFloatIfPresent(mat, "_TideStrength", 0.22f);
            SetFloatIfPresent(mat, "_ShoreBlendDistance", 2.5f);
            SetFloatIfPresent(mat, "_NormalScale", 1.65f);
            SetFloatIfPresent(mat, "_Gloss", 0.96f);
            SetFloatIfPresent(mat, "_FresnelPower", 3.2f);
            SetFloatIfPresent(mat, "_RefractionStrength", 0.032f);
            SetFloatIfPresent(mat, "_CausticsIntensity", 0.30f);
            SetFloatIfPresent(mat, "_DepthFade", 2.5f);
            SetFloatIfPresent(mat, "_ShoreOpaqueDepth", 1.5f);
            SetFloatIfPresent(mat, "_ShoreFoamWidth", 2.0f);
            SetFloatIfPresent(mat, "_ShoreFoamIntensity", 1.2f);
            SetFloatIfPresent(mat, "_SSSIntensity", 0.38f);
            SetFloatIfPresent(mat, "_FlowNormalStrength", 1.0f);
            SetFloatIfPresent(mat, "_FlowFoamStrength", 0.8f);
        }

        private static void ConfigureTransparent(Material mat)
        {
            if (mat == null) return;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            SetFloatIfPresent(mat, "_Surface", 1f);
            SetFloatIfPresent(mat, "_Blend", 0f);
            SetFloatIfPresent(mat, "_ZWrite", 0f);
            SetFloatIfPresent(mat, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            SetFloatIfPresent(mat, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            SetFloatIfPresent(mat, "_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        private static void SetColorIfPresent(Material mat, string property, Color value)
        {
            if (mat != null && mat.HasProperty(property)) mat.SetColor(property, value);
        }

        private static void SetFloatIfPresent(Material mat, string property, float value)
        {
            if (mat != null && mat.HasProperty(property)) mat.SetFloat(property, value);
        }

        private static Material CreateFallbackWaterMaterial()
        {
            try
            {
                var shader = Shader.Find("VoxelEngine/VoxelWaterURP")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard");
                var mat = new Material(shader);
                mat.name = "VoxelCrest_Fallback_Water";
                // Mark as transparent-ish
                mat.SetOverrideTag("RenderType", "Transparent");
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", new Color(0.08f, 0.52f, 0.82f, 0.88f));
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 1f);
                // try to save as asset so WaterMeshBuilder can find it
                string folder = "Assets/Resources";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "Resources");
                string path = "Assets/Resources/CrestOcean_VoxelBridge.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(mat, path);
                    AssetDatabase.SaveAssets();
                    return AssetDatabase.LoadAssetAtPath<Material>(path);
                }
                else
                {
                    EditorUtility.CopySerialized(mat, existing);
                    EditorUtility.SetDirty(existing);
                    UnityEngine.Object.DestroyImmediate(mat);
                    return existing;
                }
            }
            catch
            {
                return null;
            }
        }

        // v3.20.2 – bridge visible water material to voxel water mesh – NO ocean plane
        // v3.20.9 – hardened null checks
        private static void ConfigureCrestVoxelMaterialBridge(Material waterMaterial)
        {
            if (waterMaterial == null)
            {
                waterMaterial = CreateFallbackWaterMaterial();
            }

            // v3.12.0 – DO NOT nuke Crest here. OceanRenderer must stay alive
            // so its LOD cascades keep populating the shader globals; the runtime
            // CrestVoxelWaterBinder hides the visible ocean tiles instead.

            var bootstraps = UnityEngine.Object.FindObjectsByType<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrap = bootstraps != null && bootstraps.Length > 0 ? bootstraps[0] : null;
            if (bootstrap == null)
            {
                var go = GameObject.Find("Liquid Visual Runtime") ?? new GameObject("Liquid Visual Runtime");
                bootstrap = go.GetComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
                if (bootstrap == null) bootstrap = go.AddComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
            }

            // Enable voxel liquid surfaces – with visible water material override
            bootstrap.renderVoxelLiquidSurfaces = true;
            bootstrap.rescheduleVisibleLiquidSurfaces = true;
            bootstrap.liquidRescheduleChunkRadius = 4;
            bootstrap.liquidRescheduleInterval = 0.5f;
            bootstrap.meshBuildBudgetPerFrame = 2; // reduce lag
            bootstrap.waterMaterialOverride = waterMaterial;
            bootstrap.oilMaterialOverride = null;

            VoxelEngine.WaterSim.WaterMeshBuilder.RenderingEnabled = true;
            if (waterMaterial != null)
                VoxelEngine.WaterSim.WaterMeshBuilder.SetMaterialOverrides(waterMaterial, null);

            EnableExistingVoxelLiquidSurfaceObjects();
            EditorUtility.SetDirty(bootstrap);

            // Add a lightweight binder (no ocean plane) just to feed Crest globals / foam
            var binderGO = GameObject.Find("CrestVoxelBridge");
            if (binderGO == null)
            {
                binderGO = new GameObject("CrestVoxelBridge");
                Undo.RegisterCreatedObjectUndo(binderGO, "Create CrestVoxelBridge");
            }

            var binder = binderGO.GetComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
            if (binder == null) binder = binderGO.AddComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
            binder.hideCrestOceanTiles = true;
            binder.bridgeCrestMaterialToVoxelMesh = true;
            binder.autoConfigureCrestMaterial = true;
            binder.followNearestProceduralWater = true;
            binder.alignToPlanetSurface = true;
            binder.forceOceanAlwaysOn = true;
            EditorUtility.SetDirty(binderGO);

            // Depth + foam helpers on same GO
            var depth = binderGO.GetComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>();
            if (depth == null) depth = binderGO.AddComponent<VoxelEngine.WaterSim.VoxelCrestSeaFloorDepthProvider>();
            var foam = binderGO.GetComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>();
            if (foam == null) foam = binderGO.AddComponent<VoxelEngine.WaterSim.VoxelCrestBlockFoamEmitter>();
            EditorUtility.SetDirty(depth);
            EditorUtility.SetDirty(foam);

            Debug.Log("[CrestWaterSetup] ✓ visible water material bridged to voxel water – no ocean plane – v3.20.9");
        }

        // ---------------------------------------------------------------------
        // Legacy / optional paths – kept for reference but NOT called by default Configure()
        // They are hardened with type safety to prevent LodDataMgr crash
        // ---------------------------------------------------------------------

        private static void ConfigureCrestProceduralVoxelWaterMode()
        {
            SafeNukeAllCrest();

            var bootstraps = UnityEngine.Object.FindObjectsByType<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var bootstrap = bootstraps != null && bootstraps.Length > 0 ? bootstraps[0] : null;
            if (bootstrap == null)
            {
                var go = GameObject.Find("Liquid Visual Runtime") ?? new GameObject("Liquid Visual Runtime");
                bootstrap = go.GetComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
                if (bootstrap == null) bootstrap = go.AddComponent<VoxelEngine.WaterSim.PlanetWaterRendererBootstrap>();
            }

            bootstrap.renderVoxelLiquidSurfaces = true;
            bootstrap.rescheduleVisibleLiquidSurfaces = true;
            bootstrap.liquidRescheduleChunkRadius = 6;
            bootstrap.liquidRescheduleInterval = 0.35f;
            bootstrap.waterMaterialOverride = null;
            bootstrap.oilMaterialOverride = null;
            VoxelEngine.WaterSim.WaterMeshBuilder.RenderingEnabled = true;
            EnableExistingVoxelLiquidSurfaceObjects();
            EditorUtility.SetDirty(bootstrap);
        }

        private static void ConfigureProceduralPatchRenderer()
        {
            // Left intentionally disabled in v3.20.9 – patch renderer causes second ocean plane
            // If you need it, uncomment below
            /*
            var existing = UnityEngine.Object.FindObjectsByType<VoxelEngine.WaterSim.ProceduralWaterPatchRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing != null)
            {
                foreach (var r in existing)
                {
                    if (r != null && !string.IsNullOrEmpty(r.gameObject.scene.name))
                        UnityEngine.Object.DestroyImmediate(r.gameObject);
                }
            }
            var go = new GameObject("Procedural Water Patch Renderer");
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.enabled = false; // disabled by default – Crest voxel mesh is authoritative
            var renderer = go.AddComponent<VoxelEngine.WaterSim.ProceduralWaterPatchRenderer>();
            renderer.enabled = false;
            */
        }

        // --- The following methods are LEGACY – not used in NO-PLANE mode ---
        // They are kept compile-safe with type guards to avoid LodDataMgr crash

        private static void ConfigureCrestRuntimeInScene(Material waterMaterial)
        {
            var oceanType = FindTypeSafe("Crest.OceanRenderer");
            if (!IsUnityObjectType(oceanType)) { Debug.LogWarning("[CrestWaterSetup] OceanRenderer type not found or not UnityEngine.Object"); return; }

            Component ocean = null;
            try
            {
                var found = SafeFindObjectsByType(oceanType);
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
                    ocean = go.AddComponent(oceanType) as Component;
                    Undo.RegisterCreatedObjectUndo(go, "Create Crest Ocean");
                }
                if (ocean != null)
                {
                    ConfigureSerializedCrestOcean(ocean, waterMaterial);
                    ConfigureVoxelBinder(ocean);
                }
            }
            catch (Exception e) { Debug.LogWarning("[CrestWaterSetup] ConfigureCrestRuntimeInScene failed: " + e.Message); }
        }

        // v3.20.3 HYBRID – kept but NOT auto-called – guarded
        private static void ConfigureHybridVoxelBridge(Material waterMaterial)
        {
            var oceanType = FindTypeSafe("Crest.OceanRenderer");
            if (!IsUnityObjectType(oceanType))
            {
                Debug.LogWarning("[CrestWaterSetup] Hybrid bridge skipped – OceanRenderer type invalid");
                return;
            }

            // ... (hybrid code omitted for brevity in NO-PLANE build – kept safe)
            Debug.LogWarning("[CrestWaterSetup] ConfigureHybridVoxelBridge is legacy – use ConfigureCrestVoxelMaterialBridge instead");
        }

        private static void ConfigureOilOcean(Material waterMaterial)
        {
            var oceanType = FindTypeSafe("Crest.OceanRenderer");
            if (!IsUnityObjectType(oceanType)) return;

            try
            {
                var existingOil = GameObject.Find("Crest Oil Ocean");
                if (existingOil == null)
                {
                    var go = new GameObject("Crest Oil Ocean");
                    var ocean = go.AddComponent(oceanType) as Component;
                    // copy serialized settings from main ocean if present
                    var mains = SafeFindObjectsByType(oceanType);
                    Component mainOcean = null;
                    if (mains != null && mains.Length > 0) mainOcean = mains[0] as Component;
                    if (mainOcean != null && ocean != null && ocean != mainOcean)
                    {
                        try { UnityEditor.EditorUtility.CopySerialized(mainOcean, ocean); } catch { }
                    }
                    var oilCtrl = go.AddComponent<VoxelEngine.WaterSim.CrestOilOceanController>();
                    if (oilCtrl != null) oilCtrl.oilMaterialOverride = null;
                    Undo.RegisterCreatedObjectUndo(go, "Create Crest Oil Ocean");
                    // Immediately destroy – NO PLANE policy v3.20.9
                    UnityEngine.Object.DestroyImmediate(go);
                    Debug.Log("[CrestWaterSetup] Oil Ocean created then nuked – NO PLANE policy");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CrestWaterSetup] ConfigureOilOcean failed: " + e.Message);
            }
        }

        // ---------------------------------------------------------------------

        private static void ConfigureExistingMaritimeWakeEmitters()
        {
            try
            {
                var grids = UnityEngine.Object.FindObjectsByType<VoxelEngine.GridSystem.GridEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var grid in grids)
                {
                    if (grid == null) continue;
                    if (grid.GetComponent<VoxelEngine.Maritime.MaritimePropulsionSystem>() == null) continue;

                    var emitter = grid.GetComponent<VoxelEngine.Maritime.CrestMaritimeWakeEmitter>();
                    if (emitter == null) emitter = grid.gameObject.AddComponent<VoxelEngine.Maritime.CrestMaritimeWakeEmitter>();
                    if (emitter != null)
                    {
                        emitter.requireWaterContact = true;
                        emitter.maxInteractionProbes = Mathf.Clamp(emitter.maxInteractionProbes, 8, 64);
                        emitter.wakeWeight = Mathf.Max(1.15f, emitter.wakeWeight);
                        EditorUtility.SetDirty(emitter);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CrestWaterSetup] Wake emitter configure failed: " + e.Message);
            }
        }

        // ---------------- SAFE TYPE HELPERS – v3.20.9 ----------------

        private static System.Type FindTypeSafe(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            try
            {
                foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type type = null;
                    try { type = assembly.GetType(fullName, false); } catch { }
                    if (type != null) return type;
                }
            }
            catch { }
            return null;
        }

        private static bool IsUnityObjectType(System.Type t)
        {
            return t != null && typeof(UnityEngine.Object).IsAssignableFrom(t);
        }

        private static bool IsSceneComponentType(System.Type t)
        {
            return t != null && typeof(Component).IsAssignableFrom(t);
        }

        private static bool IsLiveSceneObject(UnityEngine.Object obj)
        {
            if (obj == null) return false;

            var component = obj as Component;
            if (component != null)
                return component.gameObject != null && component.gameObject.scene.IsValid();

            var go = obj as GameObject;
            return go != null && go.scene.IsValid();
        }

        private static UnityEngine.Object[] SafeFindObjectsByType(System.Type t)
        {
            if (!IsUnityObjectType(t)) return Array.Empty<UnityEngine.Object>();

            try
            {
                // Unity 6000+
                var method = typeof(UnityEngine.Object).GetMethod("FindObjectsByType", new[] { typeof(Type), typeof(FindObjectsInactive), typeof(FindObjectsSortMode) });
                if (method != null)
                    return (UnityEngine.Object[])method.Invoke(null, new object[] { t, FindObjectsInactive.Include, FindObjectsSortMode.None }) ?? Array.Empty<UnityEngine.Object>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CrestWaterSetup] Unity object scan skipped for '{t.FullName}': {e.Message}");
                return Array.Empty<UnityEngine.Object>();
            }

            try
            {
                return Resources.FindObjectsOfTypeAll(t) ?? Array.Empty<UnityEngine.Object>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CrestWaterSetup] Resource object scan skipped for '{t.FullName}': {e.Message}");
                return Array.Empty<UnityEngine.Object>();
            }
        }

        // Legacy FindType – kept for other code paths
        private static System.Type FindType(string fullName)
        {
            return FindTypeSafe(fullName);
        }

        // ---------------------------------------------------------------------

        private static void EnableExistingVoxelLiquidSurfaceObjects()
        {
            try
            {
                var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var t in transforms)
                {
                    if (t != null && t.name == "LiquidSurface")
                        t.gameObject.SetActive(true);
                }
            }
            catch { }
        }

        private static void DisableExistingVoxelLiquidSurfaceObjects()
        {
            try
            {
                var transforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var t in transforms)
                {
                    if (t != null && t.name == "LiquidSurface")
                        t.gameObject.SetActive(false);
                }
            }
            catch { }
        }

        private static void ConfigureSerializedCrestOcean(Component ocean, Material waterMaterial)
        {
            if (ocean == null) return;
            try
            {
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
            catch (Exception e)
            {
                Debug.LogWarning("[CrestWaterSetup] ConfigureSerializedCrestOcean failed: " + e.Message);
            }
        }

        private static void ConfigureVoxelBinder(Component ocean)
        {
            if (ocean == null) return;
            try
            {
                var binder = ocean.GetComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
                if (binder == null) binder = ocean.gameObject.AddComponent<VoxelEngine.WaterSim.CrestVoxelWaterBinder>();
                binder.followNearestProceduralWater = true;
                binder.alignToPlanetSurface = true;
                binder.waterSearchRadius = 768f;
                binder.waterSearchSpacing = 24f;
                binder.waterHeightOffset = 0.08f;
                binder.smoothFollow = true;
                binder.forceOceanAlwaysOn = true;
                binder.hideCrestOceanTiles = true; // v3.12.0 – hide built-in Crest tiles but keep OceanRenderer alive
                binder.bridgeCrestMaterialToVoxelMesh = true;
                EditorUtility.SetDirty(binder);
            }
            catch { }
        }

        private static void SetBool(SerializedObject so, string name, bool value)
        {
            try
            {
                var p = so.FindProperty(name);
                if (p != null && p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value;
            }
            catch { }
        }

        private static void SetNumber(SerializedObject so, string name, float value)
        {
            try
            {
                var p = so.FindProperty(name);
                if (p == null) return;
                if (p.propertyType == SerializedPropertyType.Float) p.floatValue = value;
                else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(value);
            }
            catch { }
        }

        // Legacy import helpers – kept but not auto-called
        private static void ImportCrestMainSceneWaterRig() { /* intentionally no-op in NO-PLANE mode – see git history for original */ }
        private static Transform FindChildByName(Transform root, string name) { if (root == null) return null; if (root.name == name) return root; for (int i = 0; i < root.childCount; i++) { var found = FindChildByName(root.GetChild(i), name); if (found != null) return found; } return null; }
        private static void ConfigureVoxelCrestHelpers() { /* legacy – helpers now added in ConfigureCrestVoxelMaterialBridge */ }
    }
}
#endif
