// Assets/Scripts/VoxelEngine/UI/RecipeBrowserUI.cs
//
// Lightweight production dependency browser. This is the first runtime view over
// the validated recipe graph: select an output and inspect what makes it, what it
// consumes, and which recipes use it next.

using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private enum ChainPreference { Auto, AIAssembler, AssemblerStation }
        private enum RecipeListFilter { All, Hand, Station, AIAssembler, Smelting }
        private const string PrefSelectedOutput = "IndustrialWorld.RecipeBrowser.SelectedOutput";
        private const string PrefChainDepth = "IndustrialWorld.RecipeBrowser.ChainDepth";
        private const string PrefShowRaw = "IndustrialWorld.RecipeBrowser.ShowRaw";
        private const string PrefPreference = "IndustrialWorld.RecipeBrowser.Preference";
        private const string PrefPlanBatches = "IndustrialWorld.RecipeBrowser.PlanBatches";
        private const string PrefTargetPerMinute = "IndustrialWorld.RecipeBrowser.TargetPerMinute";
        private const string PrefListFilter = "IndustrialWorld.RecipeBrowser.ListFilter";
        private static bool _settingsLoaded;
        private static int _chainDepth = 4;
        private static bool _showRawInputs = true;
        private static ChainPreference _chainPreference = ChainPreference.Auto;
        private static RecipeListFilter _listFilter = RecipeListFilter.All;
        private static int _planBatches = 1;
        private static int _targetPerMinute = 60;
        private static System.Action _refreshCurrentDetails;

        public static VisualElement BuildPanel(RecipeRegistry registry)
        {
            var panel = T.MachinePanel();
            panel.style.left = new StyleLength(new Length(34f, LengthUnit.Percent));
            panel.style.right = 12;
            panel.style.width = new StyleLength(new Length(54f, LengthUnit.Percent));
            panel.style.maxWidth = new StyleLength(new Length(62f, LengthUnit.Percent));

            EnsureSettingsLoaded();
            var entries = BuildEntries(registry);
            if (string.IsNullOrEmpty(_selectedOutputKey) && entries.Count > 0) _selectedOutputKey = OutputKey(entries[0].Output);

            panel.Add(Header());
            panel.Add(T.AccentDivider(ProductionPanelThemeState.Accent));

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

        private static void EnsureSettingsLoaded()
        {
            if (_settingsLoaded) return;
            _settingsLoaded = true;
            _selectedOutputKey = PlayerPrefs.GetString(PrefSelectedOutput, _selectedOutputKey ?? string.Empty);
            _chainDepth = Mathf.Clamp(PlayerPrefs.GetInt(PrefChainDepth, _chainDepth), 1, 8);
            _showRawInputs = PlayerPrefs.GetInt(PrefShowRaw, _showRawInputs ? 1 : 0) != 0;
            int pref = PlayerPrefs.GetInt(PrefPreference, (int)_chainPreference);
            _chainPreference = System.Enum.IsDefined(typeof(ChainPreference), pref) ? (ChainPreference)pref : ChainPreference.Auto;
            _planBatches = Mathf.Clamp(PlayerPrefs.GetInt(PrefPlanBatches, _planBatches), 1, 999);
            _targetPerMinute = Mathf.Clamp(PlayerPrefs.GetInt(PrefTargetPerMinute, _targetPerMinute), 1, 9999);
            int listFilter = PlayerPrefs.GetInt(PrefListFilter, (int)_listFilter);
            _listFilter = System.Enum.IsDefined(typeof(RecipeListFilter), listFilter) ? (RecipeListFilter)listFilter : RecipeListFilter.All;
        }

        private static void SaveSettings()
        {
            PlayerPrefs.SetString(PrefSelectedOutput, _selectedOutputKey ?? string.Empty);
            PlayerPrefs.SetInt(PrefChainDepth, _chainDepth);
            PlayerPrefs.SetInt(PrefShowRaw, _showRawInputs ? 1 : 0);
            PlayerPrefs.SetInt(PrefPreference, (int)_chainPreference);
            PlayerPrefs.SetInt(PrefPlanBatches, _planBatches);
            PlayerPrefs.SetInt(PrefTargetPerMinute, _targetPerMinute);
            PlayerPrefs.SetInt(PrefListFilter, (int)_listFilter);
            PlayerPrefs.Save();
        }

        private static VisualElement Header()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.Add(T.IconBadge("R", ProductionPanelThemeState.Accent));
            var title = T.Title("Recipe Browser");
            title.style.flexGrow = 1;
            row.Add(title);
            row.Add(T.SmallButton($"Theme: {ProductionPanelThemeState.Label}", () =>
            {
                ProductionPanelThemeState.Next();
                GameUIController.Instance?.RequestRefresh();
            }, ProductionPanelThemeState.Accent));
            var (pill, _) = T.StatusPill("GRAPH", ProductionPanelThemeState.Accent);
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

            var filterBar = new VisualElement();
            filterBar.style.flexDirection = FlexDirection.Row;
            filterBar.style.flexWrap = Wrap.Wrap;
            filterBar.style.marginBottom = 8;
            left.Add(filterBar);

            var resultLabel = new Label();
            resultLabel.style.color = new StyleColor(T.TextMuted);
            resultLabel.style.fontSize = 9;
            resultLabel.style.marginBottom = 5;
            left.Add(resultLabel);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            T.StyleScroller(scroll);
            left.Add(scroll);

            void RefreshFilterBar()
            {
                filterBar.Clear();
                AddFilterButton(filterBar, RecipeListFilter.All, "All", PopulateList);
                AddFilterButton(filterBar, RecipeListFilter.Hand, "Hand", PopulateList);
                AddFilterButton(filterBar, RecipeListFilter.Station, "Station", PopulateList);
                AddFilterButton(filterBar, RecipeListFilter.AIAssembler, "AI", PopulateList);
                AddFilterButton(filterBar, RecipeListFilter.Smelting, "Smelt", PopulateList);
                if (!string.IsNullOrEmpty(_search))
                    filterBar.Add(T.SmallButton("Clear", () => { _search = string.Empty; search.value = string.Empty; PopulateList(); }, T.TextMuted));
            }

            void PopulateList()
            {
                scroll.Clear();
                string query = (_search ?? string.Empty).Trim().ToLowerInvariant();
                var filteredEntries = entries.Where(entry => MatchesFilter(entry))
                    .Where(entry =>
                        string.IsNullOrEmpty(query)
                        || (entry.Output != null && entry.Output.displayName.ToLowerInvariant().Contains(query))
                        || entry.Name.ToLowerInvariant().Contains(query)
                        || entry.Kind.ToLowerInvariant().Contains(query))
                    .Where(entry => entry.Output != null)
                    .ToList();

                var filtered = filteredEntries
                    .GroupBy(entry => OutputKey(entry.Output))
                    .Select(group => new { Key = group.Key, Output = group.First().Output, Count = group.Count(), Kinds = string.Join(", ", group.Select(e => e.Kind).Distinct()) })
                    .OrderBy(group => group.Output.displayName)
                    .ToList();

                resultLabel.text = $"{filtered.Count} item{(filtered.Count == 1 ? "" : "s")} · {filteredEntries.Count} recipe method{(filteredEntries.Count == 1 ? "" : "s")}";
                RefreshFilterBar();

                foreach (var group in filtered)
                    scroll.Add(RecipeButton(group.Key, group.Output, group.Count, group.Kinds, () =>
                    {
                        refreshDetails?.Invoke();
                        PopulateList();
                    }));

                if (filtered.Count == 0)
                    scroll.Add(T.Muted("No recipes match the current search/filter."));
            }

            search.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                PopulateList();
            });
            PopulateList();
            return left;
        }

        private static void AddFilterButton(VisualElement parent, RecipeListFilter filter, string label, System.Action refresh)
        {
            bool selected = _listFilter == filter;
            parent.Add(T.SmallButton(label, () =>
            {
                _listFilter = filter;
                SaveSettings();
                refresh?.Invoke();
            }, selected ? ProductionPanelThemeState.Accent : T.TextMuted));
        }

        private static bool MatchesFilter(RecipeEntry entry)
        {
            return _listFilter switch
            {
                RecipeListFilter.Hand => entry.Kind == "Hand Crafting",
                RecipeListFilter.Station => entry.Kind == "Crafting Bench" || entry.Kind == "Assembler Station",
                RecipeListFilter.AIAssembler => entry.Kind == "AI-assembler",
                RecipeListFilter.Smelting => entry.Kind == "Smelting",
                _ => true
            };
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
                SaveSettings();
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
            if (makers.Count > 0) right.Add(PinButton(makers[0]));

            right.Add(SectionTitle("Made By", T.AccentGreen));
            foreach (var maker in makers)
                right.Add(RecipeDetailCard(maker));

            if (makers.Count > 1)
            {
                right.Add(T.Spacer(8));
                right.Add(SectionTitle("Method Comparison", T.AccentGold));
                right.Add(BuildMethodComparison(makers));
            }

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

            right.Add(T.Spacer(8));
            right.Add(SectionTitle("Material Summary", T.AccentGold));
            right.Add(BuildMaterialSummary(_selectedOutputKey, entries));

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

        private static VisualElement BuildMethodComparison(List<RecipeEntry> makers)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = new StyleColor(T.AccentGold);

            var help = new Label($"Target: {_targetPerMinute}/min · Click Prefer to steer Dependency Chain + Material Summary.");
            help.style.color = new StyleColor(T.TextMuted);
            help.style.fontSize = 9;
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.marginBottom = 6;
            card.Add(help);

            foreach (var maker in makers.OrderByDescending(m => OutputPerMinute(m)).ThenBy(m => m.Kind))
                card.Add(MethodRow(maker));

            return card;
        }

        private static VisualElement MethodRow(RecipeEntry maker)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 5;
            row.style.paddingTop = 5;
            row.style.paddingBottom = 5;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.035f));
            T.Radius(row, 6);

            var method = new Label(maker.Kind);
            method.style.width = 118;
            method.style.color = new StyleColor(T.TextPrimary);
            method.style.fontSize = 10;
            method.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(method);

            float rate = OutputPerMinute(maker);
            int machines = rate > 0.001f ? Mathf.Max(1, Mathf.CeilToInt(_targetPerMinute / rate)) : 0;

            var stats = new Label(rate > 0f ? $"{rate:0.#}/min each · {machines} needed" : "Instant/manual method");
            stats.style.flexGrow = 1;
            stats.style.color = new StyleColor(T.TextSecondary);
            stats.style.fontSize = 10;
            row.Add(stats);

            var prefer = PreferenceForKind(maker.Kind);
            bool active = prefer.HasValue && _chainPreference == prefer.Value;
            if (prefer.HasValue)
            {
                row.Add(T.SmallButton(active ? "Preferred" : "Prefer", () =>
                {
                    _chainPreference = prefer.Value;
                    SaveSettings();
                    _refreshCurrentDetails?.Invoke();
                }, active ? T.AccentGreen : T.AccentGold));
            }

            return row;
        }

        private static float OutputPerMinute(RecipeEntry maker)
        {
            if (maker == null || maker.OutputCount <= 0 || maker.Seconds <= 0f) return 0f;
            return maker.OutputCount * 60f / Mathf.Max(0.01f, maker.Seconds);
        }

        private static ChainPreference? PreferenceForKind(string kind)
        {
            return kind switch
            {
                "AI-assembler" => ChainPreference.AIAssembler,
                "Assembler Station" => ChainPreference.AssemblerStation,
                _ => null
            };
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

        private static string BuildMethodsText(string selectedName, List<RecipeEntry> makers)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Recipe Methods: {selectedName}");
            foreach (var maker in makers)
            {
                builder.AppendLine($"- {maker.Kind}: {maker.Name}");
                builder.AppendLine($"  Output: {maker.OutputCount}x {(maker.Output != null ? maker.Output.displayName : "?")} · {maker.Seconds:0.#}s");
                string inputs = maker.Inputs.Count == 0
                    ? "—"
                    : string.Join(" + ", maker.Inputs.Select(input => $"{input.count}x {(input.item != null ? input.item.displayName : "?")}"));
                builder.AppendLine($"  Inputs: {inputs}");
            }
            return builder.ToString();
        }

        private static string BuildChainText(string outputKey, List<RecipeEntry> entries)
        {
            var builder = new StringBuilder();
            var selected = entries.FirstOrDefault(entry => OutputKey(entry.Output) == outputKey)?.Output;
            builder.AppendLine($"Dependency Chain: {(selected != null ? selected.displayName : outputKey)}");
            builder.AppendLine($"Preference: {_chainPreference}");
            builder.AppendLine($"Depth: {_chainDepth}");
            BuildChainTextRecursive(builder, outputKey, entries, 0, new HashSet<string>());
            return builder.ToString();
        }

        private static void BuildChainTextRecursive(StringBuilder builder, string outputKey, List<RecipeEntry> entries, int depth, HashSet<string> path)
        {
            if (string.IsNullOrEmpty(outputKey) || depth > _chainDepth) return;
            var recipe = SelectPreferredRecipe(entries
                .Where(entry => OutputKey(entry.Output) == outputKey)
                .OrderBy(entry => entry.Inputs.Count)
                .ThenBy(entry => entry.Seconds)
                .ToList());
            string indent = new string(' ', depth * 2);
            if (recipe == null)
            {
                if (_showRawInputs) builder.AppendLine($"{indent}- {outputKey} (RAW)");
                return;
            }

            string key = OutputKey(recipe.Output);
            builder.AppendLine($"{indent}- {recipe.Output.displayName} <- {recipe.Kind} ({recipe.OutputCount}x, {recipe.Seconds:0.#}s)");
            if (!path.Add(key))
            {
                builder.AppendLine($"{indent}  already shown");
                return;
            }

            var nextPath = new HashSet<string>(path);
            foreach (var input in recipe.Inputs.Where(input => input.item != null))
                BuildChainTextRecursive(builder, OutputKey(input.item), entries, depth + 1, nextPath);
        }

        private sealed class MaterialNeed
        {
            public ItemDefinition Item;
            public int Count;
            public bool Raw;
        }

        private static VisualElement BuildMaterialSummary(string outputKey, List<RecipeEntry> entries)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;
            card.style.borderLeftWidth = 4;
            card.style.borderLeftColor = new StyleColor(T.AccentGold);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            card.Add(header);

            var title = new Label("Material Summary");
            title.style.flexGrow = 1;
            title.style.color = new StyleColor(T.AccentGold);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);

            var summaryBody = new VisualElement();
            Button batchButton = null;
            Label targetLabel = null;

            Dictionary<string, MaterialNeed> BuildSummary()
            {
                var summary = new Dictionary<string, MaterialNeed>();
                BuildMaterialSummaryRecursive(outputKey, entries, requiredCount: _planBatches, depth: 0, summary, new HashSet<string>());
                return summary;
            }

            void RefreshSummaryOnly()
            {
                summaryBody.Clear();
                batchButton.text = $"{_planBatches} batch{(_planBatches == 1 ? "" : "es")}";
                var summary = BuildSummary();
                targetLabel.text = $"Target: {_targetPerMinute}/min";
                if (summary.Count == 0)
                {
                    summaryBody.Add(T.Muted("No base materials found for the selected path."));
                    return;
                }

                var machineEstimate = BuildMachineEstimate(outputKey, entries);
                if (machineEstimate != null) summaryBody.Add(machineEstimate);

                var top = new Label($"Approximate base requirements for {_planBatches} selected output batch{(_planBatches == 1 ? "" : "es")}.");
                top.style.color = new StyleColor(T.TextMuted);
                top.style.fontSize = 9;
                top.style.whiteSpace = WhiteSpace.Normal;
                top.style.marginBottom = 8;
                summaryBody.Add(top);

                foreach (var need in summary.Values.OrderByDescending(n => n.Raw).ThenBy(n => n.Item != null ? n.Item.displayName : string.Empty))
                    summaryBody.Add(MaterialLine(need));
            }

            header.Add(T.SmallButton("−", () =>
            {
                _planBatches = Mathf.Max(1, _planBatches - 1);
                SaveSettings();
                RefreshSummaryOnly();
            }, T.TextMuted));
            batchButton = T.SmallButton($"{_planBatches} batch{(_planBatches == 1 ? "" : "es")}", () =>
            {
                _planBatches = 1;
                SaveSettings();
                RefreshSummaryOnly();
            }, T.AccentGold);
            header.Add(batchButton);
            header.Add(T.SmallButton("+", () =>
            {
                _planBatches = Mathf.Min(999, _planBatches + 1);
                SaveSettings();
                RefreshSummaryOnly();
            }, T.TextMuted));
            header.Add(T.SmallButton("Copy Plan", () =>
            {
                GUIUtility.systemCopyBuffer = BuildPlanText(outputKey, entries, BuildSummary());
            }, T.AccentGreen));

            var targetRow = new VisualElement();
            targetRow.style.flexDirection = FlexDirection.Row;
            targetRow.style.alignItems = Align.Center;
            targetRow.style.marginBottom = 8;
            targetLabel = new Label();
            targetLabel.style.flexGrow = 1;
            targetLabel.style.fontSize = 10;
            targetLabel.style.color = new StyleColor(T.TextSecondary);
            targetRow.Add(targetLabel);
            targetRow.Add(T.SmallButton("−", () =>
            {
                _targetPerMinute = Mathf.Max(1, _targetPerMinute - 10);
                SaveSettings();
                RefreshSummaryOnly();
            }, T.TextMuted));
            targetRow.Add(T.SmallButton("+", () =>
            {
                _targetPerMinute = Mathf.Min(9999, _targetPerMinute + 10);
                SaveSettings();
                RefreshSummaryOnly();
            }, T.TextMuted));
            card.Add(targetRow);

            card.Add(summaryBody);
            RefreshSummaryOnly();
            return card;
        }

        private static VisualElement BuildMachineEstimate(string outputKey, List<RecipeEntry> entries)
        {
            var recipe = SelectPreferredRecipe(entries
                .Where(entry => OutputKey(entry.Output) == outputKey)
                .OrderBy(entry => entry.Inputs.Count)
                .ThenBy(entry => entry.Seconds)
                .ToList());
            if (recipe == null || recipe.Seconds <= 0f || recipe.OutputCount <= 0) return null;

            float perMachinePerMinute = recipe.OutputCount * 60f / Mathf.Max(0.01f, recipe.Seconds);
            int machines = Mathf.Max(1, Mathf.CeilToInt(_targetPerMinute / perMachinePerMinute));
            var card = T.Card();
            card.style.marginBottom = 8;
            card.style.paddingTop = 7;
            card.style.paddingBottom = 7;
            card.style.borderLeftWidth = 3;
            card.style.borderLeftColor = new StyleColor(T.AccentCyan);

            var label = new Label($"Planner: {machines} × {recipe.Kind} for ~{_targetPerMinute}/min  ({perMachinePerMinute:0.#}/min each)");
            label.style.color = new StyleColor(T.TextPrimary);
            label.style.fontSize = 10;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(label);
            return card;
        }

        private static string BuildPlanText(string outputKey, List<RecipeEntry> entries, Dictionary<string, MaterialNeed> summary)
        {
            var selected = entries.FirstOrDefault(entry => OutputKey(entry.Output) == outputKey)?.Output;
            var builder = new StringBuilder();
            builder.AppendLine($"Production Plan: {(selected != null ? selected.displayName : outputKey)}");
            builder.AppendLine($"Batches: {_planBatches}");
            builder.AppendLine($"Target/min: {_targetPerMinute}");
            builder.AppendLine($"Preference: {_chainPreference}");
            builder.AppendLine($"Depth: {_chainDepth}");
            builder.AppendLine("Materials:");
            foreach (var need in summary.Values.OrderByDescending(n => n.Raw).ThenBy(n => n.Item != null ? n.Item.displayName : string.Empty))
                builder.AppendLine($"- {need.Count}x {(need.Item != null ? need.Item.displayName : "Unknown")} ({(need.Raw ? "RAW" : "ITEM")})");
            return builder.ToString();
        }

        private static VisualElement MaterialLine(MaterialNeed need)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var tag = new Label(need.Raw ? "RAW" : "ITEM");
            tag.style.width = 44;
            tag.style.fontSize = 8;
            tag.style.unityFontStyleAndWeight = FontStyle.Bold;
            tag.style.color = new StyleColor(need.Raw ? T.TextMuted : T.AccentCyan);
            row.Add(tag);

            var swatch = new VisualElement();
            swatch.style.width = 9;
            swatch.style.height = 9;
            swatch.style.marginRight = 7;
            swatch.style.backgroundColor = new StyleColor(need.Item != null ? need.Item.iconTint : T.TextMuted);
            T.Radius(swatch, 5);
            row.Add(swatch);

            var label = new Label($"{need.Count}x {(need.Item != null ? need.Item.displayName : "Unknown")}");
            label.style.flexGrow = 1;
            label.style.color = new StyleColor(T.TextSecondary);
            label.style.fontSize = 10;
            row.Add(label);
            return row;
        }

        private static void BuildMaterialSummaryRecursive(
            string outputKey,
            List<RecipeEntry> entries,
            int requiredCount,
            int depth,
            Dictionary<string, MaterialNeed> summary,
            HashSet<string> path)
        {
            if (string.IsNullOrEmpty(outputKey) || requiredCount <= 0 || depth > _chainDepth + 1) return;
            var candidates = entries
                .Where(entry => OutputKey(entry.Output) == outputKey)
                .OrderBy(entry => entry.Inputs.Count)
                .ThenBy(entry => entry.Seconds)
                .ToList();
            var recipe = SelectPreferredRecipe(candidates);
            if (recipe == null || recipe.Inputs.Count == 0 || path.Contains(outputKey))
            {
                var item = entries.FirstOrDefault(entry => OutputKey(entry.Output) == outputKey)?.Output;
                AddNeed(summary, outputKey, item, requiredCount, raw: true);
                return;
            }

            var nextPath = new HashSet<string>(path) { outputKey };
            int batches = Mathf.Max(1, Mathf.CeilToInt(requiredCount / (float)Mathf.Max(1, recipe.OutputCount)));
            foreach (var input in recipe.Inputs.Where(input => input.item != null && input.count > 0))
            {
                string inputKey = OutputKey(input.item);
                bool craftable = entries.Any(entry => OutputKey(entry.Output) == inputKey);
                int needed = input.count * batches;
                if (craftable)
                    BuildMaterialSummaryRecursive(inputKey, entries, needed, depth + 1, summary, nextPath);
                else
                    AddNeed(summary, inputKey, input.item, needed, raw: true);
            }
        }

        private static void AddNeed(Dictionary<string, MaterialNeed> summary, string key, ItemDefinition item, int count, bool raw)
        {
            if (string.IsNullOrEmpty(key) || count <= 0) return;
            if (!summary.TryGetValue(key, out var need))
            {
                need = new MaterialNeed { Item = item, Raw = raw };
                summary[key] = need;
            }
            need.Count += count;
            need.Raw &= raw;
        }

        private static VisualElement BuildDependencyTree(string outputKey, List<RecipeEntry> entries)
        {
            var card = T.Card();
            card.style.marginBottom = 6;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 10;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            var title = new Label("Dependency Chain");
            title.style.flexGrow = 1;
            title.style.color = new StyleColor(T.AccentPurple);
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);
            header.Add(T.SmallButton("Copy Chain", () =>
            {
                GUIUtility.systemCopyBuffer = BuildChainText(outputKey, entries);
            }, T.AccentGreen));

            var tree = new VisualElement();

            void RefreshTreeOnly()
            {
                tree.Clear();
                var visited = new HashSet<string>();
                AddDependencyNode(tree, outputKey, entries, depth: 0, path: visited);
            }

            Button rawButton = null;
            Button depthButton = null;
            void RefreshControls()
            {
                if (rawButton != null) rawButton.text = _showRawInputs ? "Hide Raw" : "Show Raw";
                if (depthButton != null) depthButton.text = $"Depth {_chainDepth}";
            }

            rawButton = T.SmallButton(_showRawInputs ? "Hide Raw" : "Show Raw", () =>
            {
                _showRawInputs = !_showRawInputs;
                SaveSettings();
                RefreshControls();
                RefreshTreeOnly();
            }, T.TextMuted);
            header.Add(rawButton);
            Button preferenceButton = null;
            string PreferenceLabel() => _chainPreference switch
            {
                ChainPreference.AIAssembler => "Prefer AI",
                ChainPreference.AssemblerStation => "Prefer Station",
                _ => "Auto"
            };
            preferenceButton = T.SmallButton(PreferenceLabel(), () =>
            {
                _chainPreference = _chainPreference switch
                {
                    ChainPreference.Auto => ChainPreference.AIAssembler,
                    ChainPreference.AIAssembler => ChainPreference.AssemblerStation,
                    _ => ChainPreference.Auto
                };
                SaveSettings();
                preferenceButton.text = PreferenceLabel();
                RefreshTreeOnly();
            }, T.AccentGold);
            header.Add(preferenceButton);
            header.Add(T.SmallButton("−", () =>
            {
                _chainDepth = Mathf.Max(1, _chainDepth - 1);
                SaveSettings();
                RefreshControls();
                RefreshTreeOnly();
            }, T.TextMuted));
            depthButton = T.SmallButton($"Depth {_chainDepth}", () =>
            {
                _chainDepth = 4;
                SaveSettings();
                RefreshControls();
                RefreshTreeOnly();
            }, T.AccentPurple);
            header.Add(depthButton);
            header.Add(T.SmallButton("+", () =>
            {
                _chainDepth = Mathf.Min(8, _chainDepth + 1);
                SaveSettings();
                RefreshControls();
                RefreshTreeOnly();
            }, T.TextMuted));
            card.Add(header);

            var hint = new Label("Shows the preferred shortest recipe path. Click craftable nodes to focus them.");
            hint.style.color = new StyleColor(T.TextMuted);
            hint.style.fontSize = 9;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 8;
            card.Add(hint);

            tree.style.marginTop = 2;
            card.Add(tree);
            RefreshTreeOnly();
            return card;
        }

        private static void AddDependencyNode(VisualElement parent, string outputKey, List<RecipeEntry> entries, int depth, HashSet<string> path)
        {
            if (string.IsNullOrEmpty(outputKey) || depth > _chainDepth) return;

            var candidates = entries
                .Where(entry => OutputKey(entry.Output) == outputKey)
                .OrderBy(entry => entry.Inputs.Count)
                .ThenBy(entry => entry.Seconds)
                .ToList();
            var recipe = SelectPreferredRecipe(candidates);

            if (recipe == null)
            {
                if (_showRawInputs)
                    parent.Add(RawNode(outputKey, depth));
                return;
            }

            string key = OutputKey(recipe.Output);
            var node = T.Card();
            node.style.marginLeft = depth * 16;
            node.style.marginBottom = 6;
            node.style.paddingTop = 8;
            node.style.paddingBottom = 8;
            node.style.borderLeftWidth = 4;
            node.style.borderLeftColor = new StyleColor(depth == 0 ? T.AccentPurple : T.AccentCyan);
            node.style.backgroundColor = new StyleColor(depth == 0 ? new Color(0.08f, 0.09f, 0.13f, 0.98f) : T.BgCard);

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            node.Add(top);

            var iconDot = new VisualElement();
            iconDot.style.width = 10;
            iconDot.style.height = 10;
            iconDot.style.marginRight = 8;
            iconDot.style.backgroundColor = new StyleColor(recipe.Output != null ? recipe.Output.iconTint : T.AccentCyan);
            T.Radius(iconDot, 5);
            top.Add(iconDot);

            var name = new Label(recipe.Output != null ? recipe.Output.displayName : recipe.Name);
            name.style.flexGrow = 1;
            name.style.color = new StyleColor(depth == 0 ? T.TextPrimary : T.TextSecondary);
            name.style.fontSize = depth == 0 ? 12 : 11;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            top.Add(name);

            var method = new Label(recipe.Kind);
            method.style.fontSize = 8;
            method.style.unityFontStyleAndWeight = FontStyle.Bold;
            method.style.color = new StyleColor(T.AccentGold);
            method.style.backgroundColor = new StyleColor(new Color(T.AccentGold.r, T.AccentGold.g, T.AccentGold.b, 0.14f));
            method.style.paddingLeft = 6;
            method.style.paddingRight = 6;
            method.style.paddingTop = 2;
            method.style.paddingBottom = 2;
            T.Radius(method, 5);
            top.Add(method);

            var meta = new Label($"Output {recipe.OutputCount}x · {recipe.Seconds:0.#}s · {recipe.Inputs.Count} input{(recipe.Inputs.Count == 1 ? "" : "s")}");
            meta.style.color = new StyleColor(T.TextMuted);
            meta.style.fontSize = 9;
            meta.style.marginTop = 3;
            node.Add(meta);

            node.RegisterCallback<ClickEvent>(_ =>
            {
                _selectedOutputKey = key;
                _refreshCurrentDetails?.Invoke();
            });
            parent.Add(node);

            if (path.Contains(key))
            {
                var loop = new Label("↳ already in this chain");
                loop.style.marginLeft = depth * 16 + 16;
                loop.style.color = new StyleColor(T.TextMuted);
                loop.style.fontSize = 9;
                parent.Add(loop);
                return;
            }

            var nextPath = new HashSet<string>(path) { key };
            foreach (var input in recipe.Inputs.Where(input => input.item != null))
            {
                string inputKey = OutputKey(input.item);
                bool craftable = entries.Any(entry => OutputKey(entry.Output) == inputKey);
                if (craftable)
                    AddDependencyNode(parent, inputKey, entries, depth + 1, nextPath);
                else if (_showRawInputs)
                    parent.Add(RawInputNode(input.item, input.count, depth + 1));
            }
        }

        private static VisualElement RawNode(string outputKey, int depth)
        {
            var row = T.Card();
            row.style.marginLeft = depth * 16;
            row.style.marginBottom = 5;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.borderLeftWidth = 4;
            row.style.borderLeftColor = new StyleColor(T.TextMuted);
            var label = new Label($"RAW · {outputKey}");
            label.style.color = new StyleColor(T.TextMuted);
            label.style.fontSize = 10;
            row.Add(label);
            return row;
        }

        private static VisualElement RawInputNode(ItemDefinition item, int count, int depth)
        {
            var row = T.Card();
            row.style.marginLeft = depth * 16;
            row.style.marginBottom = 5;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;
            row.style.borderLeftWidth = 4;
            row.style.borderLeftColor = new StyleColor(T.TextMuted);

            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            row.Add(line);

            var badge = new Label("RAW");
            badge.style.width = 42;
            badge.style.fontSize = 8;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = new StyleColor(T.TextMuted);
            line.Add(badge);

            var label = new Label($"{count}x {(item != null ? item.displayName : "Unknown")}");
            label.style.color = new StyleColor(T.TextSecondary);
            label.style.fontSize = 10;
            line.Add(label);
            return row;
        }

        private static RecipeEntry SelectPreferredRecipe(List<RecipeEntry> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            string wanted = _chainPreference switch
            {
                ChainPreference.AIAssembler => "AI-assembler",
                ChainPreference.AssemblerStation => "Assembler Station",
                _ => string.Empty
            };
            if (!string.IsNullOrEmpty(wanted))
            {
                var preferred = candidates.FirstOrDefault(entry => entry.Kind == wanted);
                if (preferred != null) return preferred;
            }
            return candidates[0];
        }

        private static VisualElement PinButton(RecipeEntry entry)
        {
            string key = OutputKey(entry.Output);
            bool pinned = RecipePinHud.IsPinned(key);
            return T.SmallButton(pinned ? "Unpin Recipe" : "Pin Recipe", () =>
            {
                var pin = new RecipePinHud.Pin
                {
                    Key = key,
                    OutputName = entry.Output != null ? entry.Output.displayName : entry.Name,
                    Tint = entry.Output != null ? entry.Output.iconTint : T.AccentGold,
                    OutputCount = entry.OutputCount,
                    Method = entry.Kind
                };
                foreach (var input in entry.Inputs.Where(input => input.item != null))
                    pin.Inputs.Add($"{input.count}x {input.item.displayName}");
                RecipePinHud.Toggle(pin);
                _refreshCurrentDetails?.Invoke();
            }, pinned ? T.AccentRed : T.AccentGreen);
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
                    SaveSettings();
                    _refreshCurrentDetails?.Invoke();
                });
            return row;
        }

        private static string CleanSmeltingName(SmeltingRecipe recipe)
        {
            if (recipe == null) return "Smelting";
            if (recipe.output != null)
            {
                string name = recipe.output.displayName;
                return name.EndsWith(" Ingot") ? name.Substring(0, name.Length - " Ingot".Length) : name;
            }
            return recipe.name.Replace("Smelt_", string.Empty).Replace("_", " ");
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
                    Name = CleanSmeltingName(recipe),
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
