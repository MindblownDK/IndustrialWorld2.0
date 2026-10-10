// Assets/Scripts/Editor/Items/StationInteractionSetup.cs
// Non-destructive repair for Crafting Bench / Assembler placed-prefab links.
// Existing custom prefab links and authored geometry are preserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VoxelEngine.EditorTools
{
    public static class StationInteractionSetup
    {
        private const string BenchId = "block_craftingbench";
        private const string AssemblerId = "block_assembler";
        private const string BenchPrefabPath = "Assets/VoxelEngineAssets/StationPrefabs/CraftingBench.prefab";
        private const string AssemblerPrefabPath = "Assets/VoxelEngineAssets/StationPrefabs/Assembler.prefab";

        /// <summary>
        /// Repair one station prefab without replacing its model, materials, colliders,
        /// queue settings, or other authored components. Blank display names are filled
        /// from the item definition. With repairTier enabled, the item's expected tier
        /// is restored; otherwise any existing non-default tier remains untouched.
        /// </summary>
        public static bool EnsurePrefabInteraction(
            GameObject prefab,
            VoxelEngine.Crafting.StationTier expectedTier,
            string expectedDisplayName,
            bool repairTier)
        {
            if (prefab == null) return false;
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return false;

            GameObject contents = null;
            bool changed = false;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null) return false;

                var station = contents.GetComponent<VoxelEngine.Crafting.CraftingStation>();
                if (station == null)
                {
                    // Reuse a nested station rather than adding a duplicate queue/
                    // crafting component to the root. Only add one when the prefab
                    // has no station component anywhere in its hierarchy.
                    station = contents.GetComponentInChildren<VoxelEngine.Crafting.CraftingStation>(true);
                    if (station == null)
                    {
                        station = contents.AddComponent<VoxelEngine.Crafting.CraftingStation>();
                        station.tier = expectedTier;
                        station.displayName = expectedDisplayName;
                        changed = true;
                    }
                }

                if ((repairTier || station.tier == VoxelEngine.Crafting.StationTier.None)
                    && station.tier != expectedTier)
                {
                    station.tier = expectedTier;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(station.displayName))
                {
                    station.displayName = expectedDisplayName;
                    changed = true;
                }

                if (repairTier)
                {
                    var stations = contents.GetComponentsInChildren<VoxelEngine.Crafting.CraftingStation>(true);
                    for (int i = 0; i < stations.Length; i++)
                    {
                        var nestedStation = stations[i];
                        if (nestedStation == null) continue;
                        bool nestedChanged = false;
                        if (nestedStation.tier != expectedTier)
                        {
                            nestedStation.tier = expectedTier;
                            nestedChanged = true;
                        }
                        if (string.IsNullOrWhiteSpace(nestedStation.displayName))
                        {
                            nestedStation.displayName = expectedDisplayName;
                            nestedChanged = true;
                        }
                        if (!nestedChanged) continue;
                        EditorUtility.SetDirty(nestedStation);
                        changed = true;
                    }
                }

                if (changed)
                {
                    EditorUtility.SetDirty(station);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StationInteractionSetup] Could not repair '{path}': {ex.Message}");
                return false;
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }

            return changed;
        }

        public static void Run()
        {
            var warnings = new List<string>();
            int found = 0;
            int linksRepaired = 0;
            int componentsRepaired = 0;
            int visualsFound = 0;

            string[] guids = AssetDatabase.FindAssets("t:BlockItem");
            foreach (string guid in guids)
            {
                string itemPath = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<VoxelEngine.Items.BlockItem>(itemPath);
                if (item == null) continue;

                string id = (item.itemId ?? string.Empty).Trim();
                bool isBench = string.Equals(id, BenchId, StringComparison.OrdinalIgnoreCase);
                bool isAssembler = string.Equals(id, AssemblerId, StringComparison.OrdinalIgnoreCase);
                if (!isBench && !isAssembler) continue;

                found++;
                var tier = isBench
                    ? VoxelEngine.Crafting.StationTier.CraftingBench
                    : VoxelEngine.Crafting.StationTier.Assembler;
                string displayName = isBench ? "Crafting Bench" : "Assembler";

                if (item.placedPrefab == null)
                {
                    string fallbackPath = isBench ? BenchPrefabPath : AssemblerPrefabPath;
                    var fallback = AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPath);
                    if (fallback != null)
                    {
                        item.placedPrefab = fallback;
                        EditorUtility.SetDirty(item);
                        linksRepaired++;
                    }
                    else
                    {
                        warnings.Add($"{itemPath}: {id} has no placed prefab, and the standard prefab is missing ({fallbackPath}).");
                        continue;
                    }
                }

                string prefabPath = AssetDatabase.GetAssetPath(item.placedPrefab);
                componentsRepaired += EnsurePrefabInteraction(item.placedPrefab, tier, displayName, repairTier: true) ? 1 : 0;
                InspectPrefab(item.placedPrefab, itemPath, warnings, ref visualsFound);

                if (string.IsNullOrEmpty(prefabPath))
                    warnings.Add($"{itemPath}: placed prefab is not a saved prefab asset; interaction component was not changed.");
            }

            if (linksRepaired > 0) AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string summary = $"Station interaction repair complete. Matching items: {found}; missing links repaired: {linksRepaired}; prefab interaction components/fields repaired: {componentsRepaired}; prefabs with measurable visible bounds: {visualsFound}; warnings: {warnings.Count}.";
            if (warnings.Count > 0)
                Debug.LogWarning("[StationInteractionSetup] " + summary + "\n  " + string.Join("\n  ", warnings));
            else
                Debug.Log("[StationInteractionSetup] " + summary);

            EditorUtility.DisplayDialog(
                "Station Interaction Repair",
                summary + (warnings.Count > 0
                    ? "\n\nSee the Unity Console for the exact item/prefab paths and visual audit warnings. Existing custom models and non-empty links were preserved."
                    : "\n\nExisting custom models and non-empty links were preserved."),
                "OK");
        }

        private static void InspectPrefab(GameObject prefab, string itemPath, List<string> warnings, ref int visualsFound)
        {
            if (prefab == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null) return;

                bool hasVisibleRenderer = false;
                bool hasAnyCollider = false;
                var renderers = contents.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i];
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    hasVisibleRenderer = true;
                    break;
                }

                var colliders = contents.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var collider = colliders[i];
                    if (collider != null && collider.enabled && !collider.isTrigger)
                    {
                        hasAnyCollider = true;
                        break;
                    }
                }

                bool hasRendererBounds = TryGetRendererBounds(contents.transform, renderers, out Bounds localBounds);
                bool measurableGeometry = hasRendererBounds && localBounds.size.sqrMagnitude > 0.000001f;
                if (!hasVisibleRenderer)
                    warnings.Add($"{itemPath}: '{path}' has no enabled Renderer; its placed model cannot be made visible by the interaction repair.");
                else if (!measurableGeometry)
                    warnings.Add($"{itemPath}: '{path}' has enabled Renderer components but no measurable bounds; check for a missing or empty mesh on the source prefab.");
                else
                    visualsFound++;

                if (!hasAnyCollider)
                    warnings.Add($"{itemPath}: '{path}' has no enabled solid Collider; BuildSystem adds a fallback station collider at placement, fitted to visible renderer bounds when available.");

                if (measurableGeometry)
                {
                    Vector3 center = localBounds.center;
                    float maxOffset = Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.z), Mathf.Abs(localBounds.min.y));
                    if (maxOffset > 0.15f)
                        warnings.Add($"{itemPath}: '{path}' renderer bounds are offset from the prefab pivot (center {center.ToString("F2")}, bottom {localBounds.min.y:F2} m). The source prefab is unchanged; BuildSystem anchors station render bounds on decks at runtime.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"{itemPath}: could not inspect '{path}': {ex.Message}");
            }
            finally
            {
                if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool TryGetRendererBounds(Transform root, Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            if (root == null || renderers == null) return false;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;

                Bounds local = renderer.localBounds;
                Vector3 c = local.center;
                Vector3 e = local.extents;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = c + new Vector3(
                        (corner & 1) == 0 ? -e.x : e.x,
                        (corner & 2) == 0 ? -e.y : e.y,
                        (corner & 4) == 0 ? -e.z : e.z);
                    Vector3 rootPoint = root.InverseTransformPoint(renderer.transform.TransformPoint(point));
                    if (!any)
                    {
                        bounds = new Bounds(rootPoint, Vector3.zero);
                        any = true;
                    }
                    else bounds.Encapsulate(rootPoint);
                }
            }
            return any;
        }
    }
}
