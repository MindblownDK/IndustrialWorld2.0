// Assets/Scripts/VoxelEngine/Exploration/RuinChest.cs
//
// Loot container found in ruins of dead civilization.
// Visual: rusted, overgrown, damaged version of real player blocks.
// Contains: components, fuel, and — most importantly — damaged blueprint data cores.

using UnityEngine;
using VoxelEngine.Items;
using System.Collections.Generic;

namespace VoxelEngine.Exploration
{
    [RequireComponent(typeof(Collider))]
    public class RuinChest : MonoBehaviour
    {
        [Header("Ruin Chest")]
        public string ruinName = "Collapsed Warehouse";
        public bool isLooted = false;
        public float respawnSeconds = 1800f; // 30 min respawn for ruins
        private float _respawnTimer;

        [Header("Loot")]
        [Tooltip("Possible component items to drop")]
        public ItemDefinition[] possibleComponents;
        [Tooltip("Fuel items")]
        public ItemDefinition[] possibleFuel;
        [Tooltip("Blueprint cores that can be found here")]
        public BlueprintDataCoreItem[] possibleBlueprints;

        [Tooltip("Min/max components to spawn")]
        public int minComponents = 2;
        public int maxComponents = 5;

        private void Update()
        {
            if (!isLooted) return;
            _respawnTimer += Time.deltaTime;
            if (_respawnTimer >= respawnSeconds)
            {
                isLooted = false;
                _respawnTimer = 0f;
            }
        }

        // Called by PlayerInteractionTool RMB
        public bool TryOpen(Inventory inventory)
        {
            if (isLooted)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Ruin Empty", "Already looted — respawns in 30 min", null, Color.gray);
                return false;
            }

            if (inventory == null) return false;

            // Roll loot
            int compCount = Random.Range(minComponents, maxComponents + 1);
            for (int i = 0; i < compCount; i++)
            {
                if (possibleComponents == null || possibleComponents.Length == 0) break;
                var item = possibleComponents[Random.Range(0, possibleComponents.Length)];
                if (item == null) continue;
                int amount = Random.Range(1, 4);
                inventory.Add(item, amount);
            }

            // Fuel
            if (possibleFuel != null && possibleFuel.Length > 0 && Random.value < 0.6f)
            {
                var fuel = possibleFuel[Random.Range(0, possibleFuel.Length)];
                if (fuel != null) inventory.Add(fuel, Random.Range(1, 3));
            }

            // Blueprint — 35% chance, or guaranteed if first time
            if (possibleBlueprints != null && possibleBlueprints.Length > 0)
            {
                bool shouldDropBlueprint = Random.value < 0.35f || !HasAnyBlueprintUnlocked();
                if (shouldDropBlueprint)
                {
                    var bp = possibleBlueprints[Random.Range(0, possibleBlueprints.Length)];
                    if (bp != null)
                    {
                        inventory.Add(bp, 1);
                        VoxelEngine.UI.BuildFeedbackHud.Show("Blueprint Core Found!", bp.targetDisplayName, bp.icon, new Color(0.45f, 0.85f, 1f));
                        Debug.Log($"[Ruin] Player found blueprint {bp.name} -> {bp.targetRecipeAssetName} in {ruinName}");
                    }
                }
            }

            isLooted = true;
            _respawnTimer = 0f;

            VoxelEngine.UI.BuildFeedbackHud.Show($"Looted {ruinName}", $"Found components + fuel", null, new Color(0.95f, 0.72f, 0.25f));
            return true;
        }

        private bool HasAnyBlueprintUnlocked()
        {
            if (possibleBlueprints == null) return true;
            BlueprintUnlockManager.EnsureInstance();
            var mgr = BlueprintUnlockManager.Instance;
            if (mgr == null) return false;
            foreach (var bp in possibleBlueprints)
            {
                if (bp == null) continue;
                string assetName = bp.targetRecipe != null ? bp.targetRecipe.name : bp.targetRecipeAssetName;
                if (!mgr.IsUnlocked(assetName)) return false;
            }
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.35f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 2f);
        }
    }
}
