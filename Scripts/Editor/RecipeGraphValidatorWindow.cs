// Assets/Scripts/VoxelEngine/Editor/RecipeGraphValidatorWindow.cs
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Simulation;

namespace VoxelEngine.EditorTools
{
    /// <summary>
    /// Lightweight authoring validator for the production recipe graph. It scans
    /// hand-crafting, smelting, and machine recipes without mutating assets, then
    /// reports missing references, invalid counts, duplicate outputs, unreachable
    /// inputs, and dependency cycles.
    /// </summary>
    public sealed class RecipeGraphValidatorWindow : EditorWindow
    {
        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _info = new();
        private Vector2 _scroll;
        private string _lastReport = "Click Scan Project to validate all production recipes.";

        [MenuItem("Tools/Voxel Engine/Recipe Graph Validator")]
        public static void Open()
        {
            var window = GetWindow<RecipeGraphValidatorWindow>("Recipe Graph Validator");
            window.minSize = new Vector2(720f, 480f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Production Recipe Graph Validator", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Scans crafting, smelting, and machine recipes. No assets are modified.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan Project", GUILayout.Height(30f))) ScanProject();
                if (GUILayout.Button("Repair Missing Links", GUILayout.Height(30f), GUILayout.Width(160f)))
                {
                    RecipeGraphRepairUtility.RepairMissingRecipeLinks();
                    ScanProject();
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_lastReport)))
                {
                    if (GUILayout.Button("Copy Report", GUILayout.Height(30f), GUILayout.Width(120f)))
                        EditorGUIUtility.systemCopyBuffer = _lastReport;
                }
            }

            EditorGUILayout.Space(6f);
            DrawSummary();
            EditorGUILayout.Space(4f);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_lastReport, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"Errors: {_errors.Count}", _errors.Count > 0 ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(120f));
                GUILayout.Label($"Warnings: {_warnings.Count}", _warnings.Count > 0 ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(140f));
                GUILayout.Label($"Info: {_info.Count}", GUILayout.Width(100f));
            }
        }

        private void ScanProject()
        {
            _errors.Clear();
            _warnings.Clear();
            _info.Clear();

            var crafting = LoadAssets<RecipeDefinition>();
            var smelting = LoadAssets<SmeltingRecipe>();
            var machine = LoadAssets<MachineRecipe>();
            var resourceItems = LoadAssets<ItemDefinition>();

            ValidateCraftingRecipes(crafting);
            ValidateSmeltingRecipes(smelting);
            ValidateMachineRecipes(machine);
            ValidateOutputDuplicates(crafting, smelting, machine);
            ValidateReachability(crafting, smelting, machine, resourceItems);
            ValidateCycles(crafting, smelting, machine);

            _lastReport = BuildReport(crafting.Count, smelting.Count, machine.Count);
            Debug.Log($"[RecipeGraphValidator] Scan complete. Errors={_errors.Count}, Warnings={_warnings.Count}, Info={_info.Count}");
        }

        private static List<T> LoadAssets<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .Distinct()
                .OrderBy(asset => AssetDatabase.GetAssetPath(asset))
                .ToList();
        }

        private void ValidateCraftingRecipes(List<RecipeDefinition> recipes)
        {
            foreach (var recipe in recipes)
            {
                string path = AssetDatabase.GetAssetPath(recipe);
                if (recipe.outputItem == null) _errors.Add($"Crafting recipe has no output: {path}");
                if (recipe.outputCount <= 0) _errors.Add($"Crafting recipe output count must be > 0: {path}");
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                    _warnings.Add($"Crafting recipe has no inputs: {path}");
                else
                    ValidateIngredientArray(recipe.inputs, path);
            }
        }

        private void ValidateIngredientArray(RecipeIngredient[] inputs, string path)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                if (inputs[i].item == null) _errors.Add($"Missing crafting input #{i + 1}: {path}");
                if (inputs[i].count <= 0) _errors.Add($"Crafting input #{i + 1} count must be > 0: {path}");
            }
        }

        private void ValidateSmeltingRecipes(List<SmeltingRecipe> recipes)
        {
            foreach (var recipe in recipes)
            {
                string path = AssetDatabase.GetAssetPath(recipe);
                if (recipe.input == null) _errors.Add($"Smelting recipe has no input: {path}");
                if (recipe.inputCount <= 0) _errors.Add($"Smelting input count must be > 0: {path}");
                if (recipe.output == null) _errors.Add($"Smelting recipe has no output: {path}");
                if (recipe.outputCount <= 0) _errors.Add($"Smelting output count must be > 0: {path}");
                if (recipe.smeltSeconds <= 0f) _warnings.Add($"Smelting time should be positive: {path}");
            }
        }

        private void ValidateMachineRecipes(List<MachineRecipe> recipes)
        {
            foreach (var recipe in recipes)
            {
                string path = AssetDatabase.GetAssetPath(recipe);
                if (recipe.outputItem == null) _errors.Add($"Machine recipe has no output: {path}");
                if (recipe.outputCount <= 0) _errors.Add($"Machine output count must be > 0: {path}");
                if (recipe.inputs == null || recipe.inputs.Length == 0)
                    _warnings.Add($"Machine recipe has no inputs: {path}");
                else
                {
                    for (int i = 0; i < recipe.inputs.Length; i++)
                    {
                        if (recipe.inputs[i].item == null) _errors.Add($"Missing machine input #{i + 1}: {path}");
                        if (recipe.inputs[i].count <= 0) _errors.Add($"Machine input #{i + 1} count must be > 0: {path}");
                    }
                }
                if (recipe.processSeconds <= 0f) _warnings.Add($"Machine process time should be positive: {path}");
                if (recipe.byproductItem == null && recipe.byproductCount > 0)
                    _warnings.Add($"Machine recipe has byproduct count but no byproduct item: {path}");
            }
        }

        private void ValidateOutputDuplicates(
            List<RecipeDefinition> crafting,
            List<SmeltingRecipe> smelting,
            List<MachineRecipe> machine)
        {
            var byOutput = new Dictionary<ItemDefinition, List<string>>();
            void Add(ItemDefinition output, string label, Object asset)
            {
                if (output == null) return;
                if (!byOutput.TryGetValue(output, out var list))
                {
                    list = new List<string>();
                    byOutput.Add(output, list);
                }
                list.Add($"{label}: {AssetDatabase.GetAssetPath(asset)}");
            }

            foreach (var recipe in crafting) Add(recipe.outputItem, "Crafting", recipe);
            foreach (var recipe in smelting) Add(recipe.output, "Smelting", recipe);
            foreach (var recipe in machine) Add(recipe.outputItem, "Machine", recipe);

            foreach (var pair in byOutput.Where(pair => pair.Value.Count > 1))
            {
                string message = $"Multiple recipes output '{pair.Key.displayName}':\n  - {string.Join("\n  - ", pair.Value)}";
                if (LooksLikeIntentionalProgressionDuplicate(pair.Value))
                    _info.Add(message);
                else
                    _warnings.Add($"{message}\n  Verify this is intentional.");
            }
        }

        private static bool LooksLikeIntentionalProgressionDuplicate(List<string> recipeLabels)
        {
            if (recipeLabels == null || recipeLabels.Count <= 1) return false;
            bool hasMachine = recipeLabels.Any(label => label.StartsWith("Machine:", System.StringComparison.Ordinal));
            bool hasCrafting = recipeLabels.Any(label => label.StartsWith("Crafting:", System.StringComparison.Ordinal));
            if (hasMachine && hasCrafting) return true;

            bool hasRootRecipe = recipeLabels.Any(label => label.Contains("Assets/VoxelEngineAssets/Recipes/"));
            bool hasDomainRecipe = recipeLabels.Any(label =>
                label.Contains("Assets/VoxelEngineAssets/Fluids/Recipes/") ||
                label.Contains("Assets/VoxelEngineAssets/Industrial/Recipes/") ||
                label.Contains("Assets/VoxelEngineAssets/Research/Recipes/"));
            return hasRootRecipe && hasDomainRecipe;
        }

        private void ValidateReachability(
            List<RecipeDefinition> crafting,
            List<SmeltingRecipe> smelting,
            List<MachineRecipe> machine,
            List<ItemDefinition> allItems)
        {
            var outputs = new HashSet<ItemDefinition>();
            foreach (var recipe in crafting) if (recipe.outputItem != null) outputs.Add(recipe.outputItem);
            foreach (var recipe in smelting) if (recipe.output != null) outputs.Add(recipe.output);
            foreach (var recipe in machine) if (recipe.outputItem != null) outputs.Add(recipe.outputItem);

            var referencedInputs = new HashSet<ItemDefinition>();
            foreach (var recipe in crafting)
                if (recipe.inputs != null)
                    foreach (var input in recipe.inputs)
                        if (input.item != null) referencedInputs.Add(input.item);
            foreach (var recipe in smelting) if (recipe.input != null) referencedInputs.Add(recipe.input);
            foreach (var recipe in machine)
                if (recipe.inputs != null)
                    foreach (var input in recipe.inputs)
                        if (input.item != null) referencedInputs.Add(input.item);

            foreach (var input in referencedInputs.OrderBy(i => i.displayName))
            {
                bool looksRaw = input is ResourceItem || input is BlockItem || !outputs.Contains(input);
                if (!looksRaw && !allItems.Contains(input))
                    _warnings.Add($"Input '{input.displayName}' is referenced but was not found by asset scan.");
            }

            int craftableOutputCount = outputs.Count;
            int inputOnlyCount = referencedInputs.Count(item => !outputs.Contains(item));
            _info.Add($"Unique recipe outputs: {craftableOutputCount}");
            _info.Add($"Input-only items treated as raw/base resources: {inputOnlyCount}");
        }

        private void ValidateCycles(
            List<RecipeDefinition> crafting,
            List<SmeltingRecipe> smelting,
            List<MachineRecipe> machine)
        {
            var graph = new Dictionary<ItemDefinition, HashSet<ItemDefinition>>();
            void AddEdge(ItemDefinition output, ItemDefinition input)
            {
                if (output == null || input == null || output == input) return;
                if (!graph.TryGetValue(output, out var deps))
                {
                    deps = new HashSet<ItemDefinition>();
                    graph.Add(output, deps);
                }
                deps.Add(input);
            }

            foreach (var recipe in crafting)
                if (recipe.inputs != null)
                    foreach (var input in recipe.inputs) AddEdge(recipe.outputItem, input.item);
            foreach (var recipe in smelting) AddEdge(recipe.output, recipe.input);
            foreach (var recipe in machine)
                if (recipe.inputs != null)
                    foreach (var input in recipe.inputs) AddEdge(recipe.outputItem, input.item);

            var visiting = new HashSet<ItemDefinition>();
            var visited = new HashSet<ItemDefinition>();
            var stack = new Stack<ItemDefinition>();

            foreach (var node in graph.Keys.ToList())
                DetectCycle(node, graph, visiting, visited, stack);
        }

        private void DetectCycle(
            ItemDefinition node,
            Dictionary<ItemDefinition, HashSet<ItemDefinition>> graph,
            HashSet<ItemDefinition> visiting,
            HashSet<ItemDefinition> visited,
            Stack<ItemDefinition> stack)
        {
            if (node == null || visited.Contains(node)) return;
            if (!visiting.Add(node))
            {
                var cycle = stack.Reverse().Select(item => item.displayName).ToList();
                cycle.Add(node.displayName);
                _warnings.Add($"Potential recipe dependency cycle: {string.Join(" -> ", cycle)}");
                return;
            }

            stack.Push(node);
            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                    if (graph.ContainsKey(dep)) DetectCycle(dep, graph, visiting, visited, stack);
            }
            stack.Pop();
            visiting.Remove(node);
            visited.Add(node);
        }

        private string BuildReport(int craftingCount, int smeltingCount, int machineCount)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Production Recipe Graph Validation Report");
            builder.AppendLine($"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();
            builder.AppendLine($"Crafting recipes: {craftingCount}");
            builder.AppendLine($"Smelting recipes: {smeltingCount}");
            builder.AppendLine($"Machine recipes: {machineCount}");
            builder.AppendLine($"Errors: {_errors.Count}");
            builder.AppendLine($"Warnings: {_warnings.Count}");
            builder.AppendLine();
            AppendSection(builder, "Errors", _errors);
            AppendSection(builder, "Warnings", _warnings);
            AppendSection(builder, "Info", _info);
            return builder.ToString();
        }

        private static void AppendSection(StringBuilder builder, string title, List<string> entries)
        {
            builder.AppendLine($"## {title}");
            if (entries.Count == 0)
            {
                builder.AppendLine("- None");
                builder.AppendLine();
                return;
            }

            foreach (var entry in entries)
                builder.AppendLine($"- {entry.Replace("\n", "\n  ")}");
            builder.AppendLine();
        }
    }
}
#endif
