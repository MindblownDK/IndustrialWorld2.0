// Assets/Scripts/VoxelEngine/Editor/DebugSpawnerWindow.cs
//
// Developer tool — quickly fill the player's inventory with whole categories of
// items for in-editor playtesting. Open via Tools ▸ Debug (Spawner).
//
// Requires Play Mode (there must be a live Inventory in the scene). Each button
// scans the project's ItemDefinition assets, filters by category, and adds a few
// of each to the active player's inventory.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.GridSystem;

namespace VoxelEngine.EditorTools
{
    public class DebugSpawnerWindow : EditorWindow
    {
        private int _countPerItem = 5;
        private Vector2 _scroll;

        [MenuItem("Tools/Debug (Spawner)")]
        public static void Open()
        {
            var w = GetWindow<DebugSpawnerWindow>("Debug Spawner");
            w.minSize = new Vector2(340, 360);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("🧪 Debug Item Spawner", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Spawns items straight into the active player's Inventory for testing.\n" +
                "Enter Play Mode first — a live Inventory must exist in the scene.",
                Application.isPlaying ? MessageType.Info : MessageType.Warning);

            _countPerItem = Mathf.Max(1, EditorGUILayout.IntField("Count per item", _countPerItem));

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Categories", EditorStyles.miniBoldLabel);

                if (GUILayout.Button("🚀  Spawn All GRID Blocks", GUILayout.Height(38)))
                    SpawnAllGrid();

                if (GUILayout.Button("⚓  Spawn All MARITIME Blocks", GUILayout.Height(38)))
                    SpawnAllMaritime();

                if (GUILayout.Button("⚡  Spawn All POWER Blocks", GUILayout.Height(34)))
                    SpawnByCategory("Power");

                if (GUILayout.Button("🧱  Spawn All BUILDING Blocks", GUILayout.Height(34)))
                    SpawnBuilding();

                if (GUILayout.Button("📦  Spawn All STORAGE Blocks", GUILayout.Height(34)))
                    SpawnByCategory("Storage");

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Extras", EditorStyles.miniBoldLabel);

                if (GUILayout.Button("🏭  Spawn All INDUSTRIAL / Stations"))
                    SpawnByCategories("Industrial", "Stations", "Oil", "Electronics");

                if (GUILayout.Button("🌾  Spawn All FARMING / Food"))
                    SpawnByCategory("Farming");

                if (GUILayout.Button("☢️  Spawn All NUCLEAR / Gas"))
                    SpawnByCategories("Nuclear", "Gas");

                if (GUILayout.Button("🛠️  Spawn All TOOLS"))
                    SpawnByCategory("Tools");

                EditorGUILayout.Space(10);
                if (GUILayout.Button("✨  Spawn EVERYTHING", GUILayout.Height(30)))
                    SpawnAll();

                EditorGUILayout.EndScrollView();
            }
        }

        // ── spawn helpers ─────────────────────────────────────────────────────
        private static Inventory FindInventory()
        {
            var inv = Object.FindAnyObjectByType<Inventory>();
            if (inv == null) EditorUtility.DisplayDialog("Debug Spawner",
                "No Inventory found in the scene. Enter Play Mode and spawn the player first.", "OK");
            return inv;
        }

        private static IEnumerable<ItemDefinition> AllItems()
            => Resources.FindObjectsOfTypeAll<ItemDefinition>()
                        .Where(i => i != null && !string.IsNullOrEmpty(i.itemId));

        private void Give(IEnumerable<ItemDefinition> items, string label)
        {
            var inv = FindInventory();
            if (inv == null) return;

            int n = 0;
            foreach (var item in items.Distinct())
            {
                inv.Add(item, _countPerItem);
                n++;
            }
            Debug.Log($"[DebugSpawner] Spawned {n} {label} item type(s) × {_countPerItem}.");
        }

        private void SpawnAllGrid()
            => Give(Resources.FindObjectsOfTypeAll<GridBlockItem>().Where(i => i != null), "GRID");

        // Maritime blocks are GridBlockItems with category "Maritime".
        // We search GridBlockItem (not ItemDefinition) because they may not be
        // loaded in memory via Resources.FindObjectsOfTypeAll<ItemDefinition>.
        private void SpawnAllMaritime()
            => Give(Resources.FindObjectsOfTypeAll<GridBlockItem>()
                    .Where(i => i != null && string.Equals(i.category, "Maritime", System.StringComparison.OrdinalIgnoreCase)),
                    "MARITIME");

        private void SpawnByCategory(string category)
            => Give(AllItems().Where(i => string.Equals(i.category, category, System.StringComparison.OrdinalIgnoreCase)), category);

        private void SpawnByCategories(params string[] categories)
        {
            var set = new HashSet<string>(categories, System.StringComparer.OrdinalIgnoreCase);
            Give(AllItems().Where(i => i.category != null && set.Contains(i.category)), string.Join("/", categories));
        }

        // Building covers both the legacy "Building" category and tiered build tokens.
        private void SpawnBuilding()
            => Give(AllItems().Where(i =>
                   string.Equals(i.category, "Building", System.StringComparison.OrdinalIgnoreCase) ||
                   (i is VoxelEngine.Building.Tiered.BuildToken)),
                   "BUILDING");

        private void SpawnAll() => Give(AllItems(), "ALL");
    }
}
#endif
