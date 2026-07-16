// Assets/Scripts/VoxelEngine/UI/RecipeBrowserUI.cs
//
// Lightweight production dependency browser. This is the first runtime view over
// the validated recipe graph: select an output and inspect what makes it, what it
// consumes, and which recipes use it next.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Crafting;
using VoxelEngine.Items;
using VoxelEngine.Simulation;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RecipeBrowserUI
    {
        private sealed class RecipeEntry
        {
            public string Kind;
            public string Name;
            public ItemDefinition Output;
            public int OutputCount;
            public readonly List<(ItemDefinition item, int count)> Inputs = new();
            public float Seconds;
        }

        private static string _search = string.Empty;
        private static string _selectedOutputKey;
        private static System.Action _refreshCurrentDetails;

        public static VisualElement BuildPanel(RecipeRegistry registry)
        {
            var panel = T.MachinePanel();
            panel.style.left = new StyleLength(new Length(34f, LengthUnit.Percent));
            panel.style.right = 12;
            panel.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            panel.style.maxWidth = new StyleLength(new Length(62f, LengthUnit.Percent));

            var entries = BuildEntries(registry);
            if (string.IsNullOrEmpty(_selectedOutputKey) && entries.Count > 0) _selectedOutputKey = OutputKey(entries[0].Output);

            panel.Add(Header());
            panel.Add(T.AccentDivider(T.AccentGold));

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            body.style.marginTop = 8;
            panel.Add(body);

            var detailsHost = new VisualElement();
            detailsHost.style.flexGrow = 1;
            void RefreshDetails()
            {
                detailsHost.Clear();
                detailsHost.Add(BuildDetails(entries));
            }
            _refreshCurrentDetails = RefreshDetails;

            body.Add(BuildRecipeList(entries, RefreshDetails));
            body.Add(detailsHost);
            RefreshDetails();
            return panel;
        }

        private static VisualElement Header()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.Add(T.IconBadge("⌁", T.AccentGold));
            var title = T.Title("Recipe Browser");
            title.style.flexGrow = 1;
            row.Add(title);
            var (pill, _) = T.StatusPill("GRAPH", T.AccentGold);
            row.Add(pill);
            return row;
        }

        private static VisualElement BuildRecipeList(List<RecipeEntry> entries, System.Action refreshDetails)
        {
            var left = new VisualElement();
            left.style.width = new StyleLength(new Length(38f, LengthUnit.Percent));
            left.style.minWidth = 220;
            left.style.marginRight = 10;

            var search = new TextField { value = _search ?? string.Empty };
            search.style.marginBottom = 8;
            left.Add(search);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            T.StyleScroller(scroll);
            left.Add(scroll);

            void PopulateList()
            {
                scroll.Clear();
                string query = (_search ?? string.Empty).Trim().ToLowerInvariant();
                var filtered = entries.Where(entry =>
                        string.IsNullOrEmpty(query)
                        || (entry.Output != null && entry.Output.displayName.ToLowerInvariant().Contains(query))
                        || entry.Name.ToLowerInvariant().Contains(query)
                        || entry.Kind.ToLowerInvariant().Contains(query))
                    .Where(entry => entry.Output != null)
                    .GroupBy(entry => OutputKey(entry.Output))
                    .Select(group => new { Key = group.Key, Output = group.First().Output, Count = group.Count(), Kinds = string.Join(", ", group.Select(e => e.Kind).Distinct()) })
                    .OrderBy(group => group.Output.displayName)
                    .ToList();

                foreach (var group in filtered)
                    scroll.Add(RecipeButton(group.Key, group.Output, group.Count, group.Kinds, () =>
                    {
                        refreshDetails?.Invoke();
                        PopulateList();
                    }));

                if (filtered.Count == 0)
                    scroll.Add(T.Muted("No recipes match the current search."));
            }

            search.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                PopulateList();
            });
            PopulateList();
            return left;
        }

        private static VisualElement RecipeButton(string outputKey, ItemDefinition output, int recipeCount, string kinds, System.Action refreshDetails)
        {
            bool selected = !string.IsNullOrEmpty(outputKey) && outputKey == _selectedOutputKey;
            var card = T.Card();
            card.style.marginBottom = 5;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = new StyleColor(selected ? T.AccentGold : T.BorderDim);

            var name = new Label(output != null ? output.displayName : "Unknown Output");
            name.style.color = new StyleColor(selected ? T.TextAccent : T.TextPrimary);
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(name);

            var meta = new Label(recipeCount == 1 ? kinds : $"{recipeCount} ways · {kinds}");
            meta.style.color = new StyleColor(T.TextMuted);
            meta.style.fontSize = 9;
            meta.style.whiteSpace = WhiteSpace.Normal;
            card.Add(meta);

            card.RegisterCallback<ClickEvent>(_ =>
            {
                _selectedOutputKey = outputKey;
                refreshDetails?.Invoke();
            });
            return card;
        }

        private static VisualElement BuildDetails(List<RecipeEntry> entries)
        {
            var right = new ScrollView(ScrollViewMode.Vertical);
            right.style.flexGrow = 1;
            T.StyleScroller(right);

            if (string.IsNullOrEmpty(_selectedOutputKey))
            {
                right.Add(T.Muted("Select a recipe to inspect its dependency chain."));
                return right;
            }

            var selectedItem = entries.FirstOrDefault(entry => OutputKey(entry.Output) == _selectedOutputKey)?.Output;
            string selectedName = selectedItem != null ? selectedItem.displayName : _selectedOutputKey;
            right.Add(T.Subtitle(selectedName));
            right.Add(T.Spacer(4));

            var makers = entries.Where(entry => OutputKey(entry.Output) == _selectedOutputKey).ToList();
            right.Add(SectionTitle("Made By", T.AccentGreen));
            foreach (var maker in makers)
                right.Add(RecipeDetailCard(maker));

            var usedBy = entries.Where(entry => entry.Inputs.Any(input => OutputKey(input.item) == _selectedOutputKey)).ToList();
            right.Add(T.Spacer(8));
            right.Add(SectionTitle("Used By", T.AccentCyan));
            if (usedBy.Count == 0) right.Add(T.Muted("No known recipe consumes this item."));
            foreach (var use in usedBy)
                right.Add(RecipeDetailCard(use, compact: true));

            right.Add(T.Spacer(8));
            right.Add(SectionTitle("Immediate Inputs", T.AccentOrange));
            var inputItems = makers.SelectMany(m => m.Inputs).Where(i => i.item != null).ToList();
            if (inputItems.Count == 0) right.Add(T.Muted("This recipe has no listed inputs."));
            foreach (var input in inputItems)
                right.Add(InputLine(input.item, input.count, entries.Any(e => OutputKey(e.Output) == OutputKey(input.item))));

            right.Add(T.Spacer(8));
            right.Add(SectionTitle("Dependency Chain", T.AccentPurple));
            right.Add(BuildDependencyTree(_selectedOutputKey, entries));

            return right;
        }

        private static VisualElement SectionTitle(string text, Color accent)
        {
            var label = new Label(text.ToUpperInvariant());
            label.style.color = new StyleColor(accent);
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 1.2f;
            label.style.marginTop = 6;
            label.style.marginBottom = 4;
            return label;
        }

        private static VisualElement RecipeDetailCard(RecipeEntry entry, bool compact = false)
        {
            var card = T.Card();
            card.style.marginBottom = 5;
            card.style.paddingTop = compact ? 7 : 10;
            card.style.paddingBottom = compact ? 7 : 10;

            var title = new Label($"{entry.Kind}: {entry.Name}");
            title.style.color = new StyleColor(T.TextPrimary);
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(title);

            var output = new Label($"Output: {entry.OutputCount}x {(entry.Output != null ? entry.Output.displayName : "?")} · {entry.Seconds:0.#}s");
            output.style.color = new StyleColor(T.TextSecondary);
            output.style.fontSize = 10;
            card.Add(output);

            if (!compact)
            {
                var inputs = entry.Inputs.Count == 0
                    ? "Inputs: —"
                    : "Inputs: " + string.Join(" + ", entry.Inputs.Select(i => $"{i.count}x {(i.item != null ? i.item.displayName : "?")}"));
                var inputLabel = new Label(inputs);
                inputLabel.style.color = new StyleColor(T.TextMuted);
                inputLabel.style.fontSize = 10;
                inputLabel.style.whiteSpace = WhiteSpace.Normal;
                card.Add(inputLabel);
            }

            return card;
        }

        private static VisualElement BuildDependencyTree(string outputKey, List<RecipeEntry> entries)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            var visited = new HashSet<string>();
            AddDependencyNode(card, outputKey, entries, depth: 0, visited);
            return card;
        }

        private static void AddDependencyNode(VisualElement parent, string outputKey, List<RecipeEntry> entries, int depth, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(outputKey) || depth > 4) return;
            var recipe = entries
                .Where(entry => OutputKey(entry.Output) == outputKey)
                .OrderBy(entry => entry.Inputs.Count)
                .ThenBy(entry => entry.Seconds)
                .FirstOrDefault();

            string indent = new string(' ', depth * 2);
            if (recipe == null)
            {
                var raw = new Label($"{indent}• {outputKey}  (raw / no recipe)");
                raw.style.color = new StyleColor(T.TextMuted);
                raw.style.fontSize = 10;
                parent.Add(raw);
                return;
            }

            string key = OutputKey(recipe.Output);
            var line = new Label($"{indent}• {recipe.Output.displayName}  ←  {recipe.Kind}");
            line.style.color = new StyleColor(depth == 0 ? T.TextPrimary : T.TextSecondary);
            line.style.fontSize = depth == 0 ? 11 : 10;
            line.style.unityFontStyleAndWeight = depth == 0 ? FontStyle.Bold : FontStyle.Normal;
            parent.Add(line);

            if (!visited.Add(key))
            {
                var cycle = new Label($"{indent}  ↳ already shown");
                cycle.style.color = new StyleColor(T.TextMuted);
                cycle.style.fontSize = 9;
                parent.Add(cycle);
                return;
            }

            foreach (var input in recipe.Inputs.Where(input => input.item != null))
                AddDependencyNode(parent, OutputKey(input.item), entries, depth + 1, visited);
        }

        private static VisualElement InputLine(ItemDefinition item, int count, bool craftable)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var tag = new Label(craftable ? "CRAFT" : "RAW");
            tag.style.width = 48;
            tag.style.fontSize = 8;
            tag.style.unityFontStyleAndWeight = FontStyle.Bold;
            tag.style.color = new StyleColor(craftable ? T.AccentGreen : T.TextMuted);
            row.Add(tag);

            var label = new Label($"{count}x {item.displayName}");
            label.style.flexGrow = 1;
            label.style.fontSize = 10;
            label.style.color = new StyleColor(T.TextSecondary);
            row.Add(label);

            if (craftable)
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    _selectedOutputKey = OutputKey(item);
                    _refreshCurrentDetails?.Invoke();
                });
            return row;
        }

        private static string RecipeKind(StationTier station)
        {
            return station switch
            {
                StationTier.None => "Hand Crafting",
                StationTier.Assembler => "Assembler Station",
                StationTier.CraftingBench => "Crafting Bench",
                _ => station.ToString()
            };
        }

        private static string MachineKind(MachineRecipeType type)
        {
            return type == MachineRecipeType.Assembling ? "AI-assembler" : type.ToString();
        }

        private static string OutputKey(ItemDefinition item)
        {
            if (item == null) return string.Empty;
            // Display name is intentional here: older setup passes produced duplicate
            // assets with different itemIds but the same player-facing item. The
            // browser groups those as one production target.
            if (!string.IsNullOrWhiteSpace(item.displayName)) return item.displayName.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(item.itemId)) return item.itemId.Trim().ToLowerInvariant();
            return item.name.Trim().ToLowerInvariant();
        }

        private static List<RecipeEntry> BuildEntries(RecipeRegistry registry)
        {
            var entries = new List<RecipeEntry>();

            if (registry != null && registry.recipes != null)
            {
                foreach (var recipe in registry.recipes)
                {
                    if (recipe == null || recipe.outputItem == null) continue;
                    var entry = new RecipeEntry
                    {
                        Kind = RecipeKind(recipe.requiredStation),
                        Name = recipe.GetName(),
                        Output = recipe.outputItem,
                        OutputCount = recipe.outputCount,
                        Seconds = recipe.craftSeconds
                    };
                    if (recipe.inputs != null)
                        foreach (var input in recipe.inputs)
                            if (input.item != null && input.count > 0) entry.Inputs.Add((input.item, input.count));
                    entries.Add(entry);
                }
            }

            foreach (var recipe in Resources.FindObjectsOfTypeAll<MachineRecipe>())
            {
                if (recipe == null || recipe.outputItem == null) continue;
                var entry = new RecipeEntry
                {
                    Kind = MachineKind(recipe.recipeType),
                    Name = recipe.GetName(),
                    Output = recipe.outputItem,
                    OutputCount = recipe.outputCount,
                    Seconds = recipe.processSeconds
                };
                if (recipe.inputs != null)
                    foreach (var input in recipe.inputs)
                        if (input.item != null && input.count > 0) entry.Inputs.Add((input.item, input.count));
                entries.Add(entry);
            }

            foreach (var recipe in Resources.FindObjectsOfTypeAll<SmeltingRecipe>())
            {
                if (recipe == null || recipe.output == null) continue;
                var entry = new RecipeEntry
                {
                    Kind = "Smelting",
                    Name = recipe.name,
                    Output = recipe.output,
                    OutputCount = recipe.outputCount,
                    Seconds = recipe.smeltSeconds
                };
                if (recipe.input != null && recipe.inputCount > 0) entry.Inputs.Add((recipe.input, recipe.inputCount));
                entries.Add(entry);
            }

            return entries
                .Where(entry => entry.Output != null)
                .GroupBy(EntryKey)
                .Select(group => group.First())
                .ToList();
        }

        private static string EntryKey(RecipeEntry entry)
        {
            string inputs = string.Join("+", entry.Inputs
                .Where(input => input.item != null)
                .OrderBy(input => OutputKey(input.item))
                .Select(input => $"{OutputKey(input.item)}:{input.count}"));
            return $"{entry.Kind}|{entry.Name}|{OutputKey(entry.Output)}|{entry.OutputCount}|{inputs}";
        }
    }
}
