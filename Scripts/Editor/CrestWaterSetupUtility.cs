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
            var waterMaterial = ConfigureCrestWaterMaterial();
            ConfigureCrestRuntimeInScene(waterMaterial);
            ConfigureExistingMaritimeWakeEmitters();

            AssetDatabase.SaveAssets();
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (scene.IsValid() && scene.isLoaded)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

            EditorUtility.DisplayDialog("Voxel Engine - Crest Water",
                "Crest water integration configured for the active scene.\n\n" +
                "Configured:\n" +
                "• UI PanelSettings fit mode\n" +
                "• Shallow/clear Crest water material values\n" +
                "• Crest ocean dynamic waves + flow flags where available\n" +
                "• Planet water bootstrap material override\n" +
                "• Existing maritime grids with water-only Crest wake emitters", "OK");
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

        private static Material ConfigureCrestWaterMaterial()
        {
            string[] candidates =
            {
                "Assets/Liquid/Crest/Crest-Examples/LakesAndRivers/Materials/LakesAndRivers_Material_Water.mat",
                "Assets/Liquid/Crest/Crest-Examples/Examples/Materials/Examples_Material_Ocean.mat",
                "Assets/Liquid/Crest/Crest/Materials/Ocean.mat"
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
            if (mat.HasProperty("_DepthFogDensity")) mat.SetVector("_DepthFogDensity", new Vector4(0.13f, 0.10f, 0.08f, 1f));
            if (mat.HasProperty("_Diffuse")) mat.SetColor("_Diffuse", new Color(0.015f, 0.12f, 0.22f, 1f));
            if (mat.HasProperty("_DiffuseGrazing")) mat.SetColor("_DiffuseGrazing", new Color(0.08f, 0.25f, 0.30f, 1f));
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

            if (waterMaterial != null)
            {
                bootstrap.waterMaterialOverride = waterMaterial;
                EditorUtility.SetDirty(bootstrap);
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
