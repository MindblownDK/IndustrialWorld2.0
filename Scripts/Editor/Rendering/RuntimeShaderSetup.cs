#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Authors the Resources material anchors required by every shader resolved through
    /// Shader.Find at runtime. A shader that is visible in the Editor can still be stripped
    /// from a player build when no build-reachable material references it; these anchors make
    /// that dependency explicit without changing any gameplay material or balance value.
    /// </summary>
    public static class RuntimeShaderSetup
    {
        public const string RuntimeRoot = "Assets/Resources/VoxelEngineRuntime";
        public const string AnchorRoot = RuntimeRoot + "/ShaderAnchors";
        public const string TerrainMaterialPath = RuntimeRoot + "/VoxelTerrainRuntime.mat";
        public const string WaterMaterialPath = RuntimeRoot + "/VoxelWaterRuntime.mat";

        private const string TerrainSourcePath = "Assets/VoxelEngineAssets/VoxelTerrain.mat";
        private const string RenderingShaderRoot = "Assets/Scripts/Rendering";
        private const string AdvancedWaterShaderPath = RenderingShaderRoot + "/VoxelWaterURP.shader";
        private const string FallbackWaterShaderPath = RenderingShaderRoot + "/VoxelWater.shader";

        private static readonly string[] SetupOwnedWaterMaterialPaths =
        {
            "Assets/VoxelEngineAssets/Fluids/Prefabs/Mat_NativeSphericalWater.mat",
            "Assets/VoxelEngineAssets/Fluids/Prefabs/Mat_NativeCrudeOil.mat"
        };

        private static readonly string[] BuiltInShaderNames =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Unlit/Color",
            "Unlit/Texture",
            "Unlit/Transparent",
            "Particles/Standard Unlit",
            "Standard"
        };

        public static void RunStep103()
        {
            EnsureFolder(RuntimeRoot);
            EnsureFolder(AnchorRoot);

            int created = 0;
            int repaired = 0;
            int preserved = 0;
            var missing = new List<string>();
            var notes = new List<string>();

            Shader terrainShader = FindSupportedShader(
                "VoxelEngine/VoxelTerrainEnhanced",
                "VoxelEngine/VoxelTerrainURP");

            // Load project water shaders directly so setup does not depend on Shader.Find
            // discovering an asset before its first material exists. Prefer the full shader;
            // the simpler in-house shader is a supported, build-safe fallback.
            Shader advancedWaterShader = AssetDatabase.LoadAssetAtPath<Shader>(AdvancedWaterShaderPath);
            Shader fallbackWaterShader = AssetDatabase.LoadAssetAtPath<Shader>(FallbackWaterShaderPath);
            Shader waterShader = IsSupported(advancedWaterShader)
                ? advancedWaterShader
                : IsSupported(fallbackWaterShader) ? fallbackWaterShader : null;

            if (terrainShader == null) missing.Add("VoxelEngine/VoxelTerrainEnhanced or VoxelEngine/VoxelTerrainURP");
            if (waterShader == null)
                missing.Add("VoxelEngine/VoxelWaterURP or VoxelEngine/VoxelWater");
            else if (waterShader != advancedWaterShader)
                notes.Add("VoxelEngine/VoxelWaterURP is unsupported on the active graphics API; using VoxelEngine/VoxelWater as the safe fallback.");

            if (terrainShader != null)
            {
                Material terrainSource = AssetDatabase.LoadAssetAtPath<Material>(TerrainSourcePath);
                EnsureMaterial(TerrainMaterialPath, terrainShader, terrainSource,
                    ref created, ref repaired, ref preserved);
            }

            if (waterShader != null)
            {
                EnsureMaterial(WaterMaterialPath, waterShader, null,
                    ref created, ref repaired, ref preserved);
                RepairSetupOwnedWaterMaterials(waterShader,
                    ref repaired, ref preserved);
            }

            foreach (Shader shader in FindProjectRuntimeShaders())
            {
                if (!IsSupported(shader))
                {
                    // The water pair has an explicit project-owned fallback in both directions.
                    // Do not keep the unselected unsupported member anchored in Resources,
                    // because that would force a broken shader into an otherwise safe player.
                    bool unselectedWaterShader =
                        (shader == advancedWaterShader || shader == fallbackWaterShader) &&
                        waterShader != null && shader != waterShader;
                    if (unselectedWaterShader)
                    {
                        RemoveGeneratedAnchor(shader.name, ref repaired);
                        continue;
                    }

                    // Validation below reports every other unsupported runtime shader and blocks
                    // the build. Creating an anchor cannot make a shader compiler error safe.
                    continue;
                }

                EnsureMaterial(AnchorPath(shader.name), shader, null,
                    ref created, ref repaired, ref preserved);
            }

            for (int i = 0; i < BuiltInShaderNames.Length; i++)
            {
                string shaderName = BuiltInShaderNames[i];
                Shader shader = FindSupportedShader(shaderName);
                if (shader == null)
                {
                    // Some legacy fallbacks are intentionally unavailable under a pure URP
                    // project. An unavailable fallback is harmless while the URP shaders exist.
                    Debug.Log($"[Setup 103] Optional fallback shader '{shaderName}' is unavailable or unsupported; skipped.");
                    continue;
                }

                EnsureMaterial(AnchorPath(shaderName), shader, null,
                    ref created, ref repaired, ref preserved);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!ValidateForBuild(out string validationError))
                missing.Add(validationError);

            string result =
                $"Created {created}, repaired {repaired}, preserved {preserved} runtime shader material link(s).";
            string noteText = notes.Count > 0
                ? "\n\nNotes:\n" + string.Join("\n", notes)
                : string.Empty;

            if (missing.Count > 0)
            {
                string detail = string.Join("\n", missing);
                Debug.LogError("[Setup 103] Runtime shader inclusion is incomplete:\n" + detail + noteText);
                EditorUtility.DisplayDialog(
                    "Voxel Engine - Runtime Shader Inclusion",
                    result + "\n\nThe setup is incomplete:\n" + detail + noteText,
                    "OK");
                return;
            }

            Debug.Log("[Setup 103] " + result +
                      " Terrain, water and every project runtime shader now have build-reachable Resources materials." +
                      noteText);
            EditorUtility.DisplayDialog(
                "Voxel Engine - Runtime Shader Inclusion",
                result +
                "\n\nTerrain, water, atmosphere, weather, space, damage and portal shaders are now anchored for standalone builds." +
                "\n\nExisting material properties were preserved. Re-running is safe." +
                noteText,
                "OK");
        }

        /// <summary>Used by the build guard. Never mutates assets.</summary>
        public static bool ValidateForBuild(out string error)
        {
            var problems = new List<string>();

            ValidateMaterial(TerrainMaterialPath, "terrain", problems,
                "VoxelEngine/VoxelTerrainEnhanced", "VoxelEngine/VoxelTerrainURP");
            ValidateMaterial(WaterMaterialPath, "water", problems,
                "VoxelEngine/VoxelWaterURP", "VoxelEngine/VoxelWater");

            Material waterMaterial = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            string selectedWaterShaderName =
                waterMaterial != null && waterMaterial.shader != null
                    ? waterMaterial.shader.name
                    : string.Empty;

            foreach (Shader shader in FindProjectRuntimeShaders())
            {
                string path = AnchorPath(shader.name);
                Material anchor = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (!IsSupported(shader))
                {
                    bool unselectedWaterShader =
                        IsProjectWaterShader(shader.name) &&
                        !string.IsNullOrEmpty(selectedWaterShaderName) &&
                        shader.name != selectedWaterShaderName;
                    if (unselectedWaterShader)
                    {
                        if (anchor != null)
                            problems.Add($"unsupported unselected water anchor '{path}' must be removed by rerunning setup step 103");
                        continue;
                    }

                    problems.Add($"project runtime shader '{shader.name}' is unsupported on the active graphics API");
                    continue;
                }

                if (anchor == null)
                    problems.Add($"missing shader anchor '{path}'");
                else if (anchor.shader != shader)
                    problems.Add($"wrong shader link on '{path}'");
            }

            if (problems.Count == 0)
            {
                error = string.Empty;
                return true;
            }

            error = string.Join("\n", problems);
            return false;
        }

        private static void ValidateMaterial(
            string path,
            string role,
            List<string> problems,
            params string[] allowedShaderNames)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                problems.Add($"missing {role} runtime material '{path}'");
                return;
            }

            if (material.shader == null || !material.shader.isSupported)
            {
                problems.Add($"{role} runtime material '{path}' has a missing or unsupported shader");
                return;
            }

            bool allowed = false;
            for (int i = 0; i < allowedShaderNames.Length; i++)
            {
                if (material.shader.name != allowedShaderNames[i]) continue;
                allowed = true;
                break;
            }

            if (!allowed)
                problems.Add($"{role} runtime material '{path}' uses unexpected shader '{material.shader.name}'");
        }

        private static IEnumerable<Shader> FindProjectRuntimeShaders()
        {
            var found = new List<Shader>();
            string[] guids = AssetDatabase.FindAssets("t:Shader", new[] { RenderingShaderRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || !shader.name.StartsWith("VoxelEngine/", StringComparison.Ordinal))
                    continue;
                found.Add(shader);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        private static void RepairSetupOwnedWaterMaterials(
            Shader shader,
            ref int repaired,
            ref int preserved)
        {
            for (int i = 0; i < SetupOwnedWaterMaterialPaths.Length; i++)
            {
                string path = SetupOwnedWaterMaterialPaths[i];
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) continue;

                if (material.shader == shader)
                {
                    preserved++;
                    continue;
                }

                string currentName = material.shader != null ? material.shader.name : string.Empty;
                bool knownGeneratedFallback =
                    string.IsNullOrEmpty(currentName) ||
                    currentName == "Hidden/InternalErrorShader" ||
                    currentName == "VoxelEngine/VoxelWaterURP" ||
                    currentName == "VoxelEngine/VoxelWater" ||
                    currentName == "Universal Render Pipeline/Lit" ||
                    currentName == "Standard";

                // An unknown custom shader can be a designer override. Preserve it. Only
                // upgrade setup-owned materials that still carry a recognized fallback.
                if (!knownGeneratedFallback)
                {
                    preserved++;
                    Debug.Log($"[Setup 103] Preserved custom shader '{currentName}' on '{path}'.");
                    continue;
                }

                material.shader = shader;
                material.SetOverrideTag("RenderType", "Transparent");
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_Cull"))
                    material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                material.renderQueue = 3000;

                if (path.EndsWith("Mat_NativeCrudeOil.mat", StringComparison.Ordinal))
                    VoxelEngine.WaterSim.LiquidVisualProfile.CrudeOil.ApplyTo(material);
                else
                    VoxelEngine.WaterSim.LiquidVisualProfile.Water.ApplyTo(material);

                EditorUtility.SetDirty(material);
                repaired++;
                Debug.Log($"[Setup 103] Upgraded setup-owned liquid material '{path}' -> '{shader.name}'.");
            }
        }

        private static void EnsureMaterial(
            string path,
            Shader shader,
            Material template,
            ref int created,
            ref int repaired,
            ref int preserved)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = template != null ? new Material(template) : new Material(shader);
                material.name = Path.GetFileNameWithoutExtension(path);
                if (material.shader != shader) material.shader = shader;
                AssetDatabase.CreateAsset(material, path);
                created++;
                Debug.Log($"[Setup 103] Created '{path}' with shader '{shader.name}'.");
                return;
            }

            if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
                repaired++;
                Debug.Log($"[Setup 103] Repaired shader link on '{path}' -> '{shader.name}'.");
                return;
            }

            preserved++;
        }

        private static bool IsProjectWaterShader(string shaderName)
            => shaderName == "VoxelEngine/VoxelWaterURP" ||
               shaderName == "VoxelEngine/VoxelWater";

        private static void RemoveGeneratedAnchor(string shaderName, ref int repaired)
        {
            string path = AnchorPath(shaderName);
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null) return;

            if (AssetDatabase.DeleteAsset(path))
            {
                repaired++;
                Debug.Log($"[Setup 103] Removed unsupported generated shader anchor '{path}'.");
            }
        }

        private static bool IsSupported(Shader shader)
            => shader != null && shader.isSupported;

        private static Shader FindSupportedShader(params string[] shaderNames)
        {
            for (int i = 0; i < shaderNames.Length; i++)
            {
                Shader shader = Shader.Find(shaderNames[i]);
                if (IsSupported(shader)) return shader;
            }
            return null;
        }

        private static string AnchorPath(string shaderName)
            => AnchorRoot + "/Anchor_" + SafeFileName(shaderName) + ".mat";

        private static string SafeFileName(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') chars[i] = '_';
            }
            return new string(chars);
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }

    /// <summary>
    /// Refuses a player build that would reproduce the magenta-shader failure. The repair
    /// remains an explicit, non-destructive Voxel Engine Setup step; the build check only
    /// explains what is missing and never edits assets during a build.
    /// </summary>
    public sealed class RuntimeShaderBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (RuntimeShaderSetup.ValidateForBuild(out string error)) return;

            throw new BuildFailedException(
                "Runtime shader inclusion is incomplete. Open Tools -> Voxel Engine -> " +
                "Voxel Engine Setup and run step 103 before building.\n\n" + error);
        }
    }
}
#endif
