// Assets/Scripts/VoxelEngine/Simulation/RecipeSelectionPanel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — RECIPE SELECTION PANEL (UI Toolkit)         ║
// ║  Shared UI panel for machines that need recipe selection:       ║
// ║  Crusher, Assembler, and future chemical plants.                ║
// ║  Shows available recipes, lets the player pick one, displays    ║
// ║  inputs/outputs, progress bar, power status, and toggle.        ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Recipe selection + machine status panel for any machine with
    /// a list of known MachineRecipes. Binds to a MonoBehaviour that
    /// implements IMachine and exposes a knownRecipes list.
    /// </summary>
    public class RecipeSelectionPanel : MonoBehaviour
    {
        [Header("UI References")]
        public UIDocument document;

        // ── Runtime State ─────────────────────────────────────────────

        private IMachine _machine;
        private MonoBehaviour _owner;
        private List<MachineRecipe> _recipes;
        private System.Action<MachineRecipe> _onRecipeSelected;
        private MachineRecipe _currentRecipe;

        // UI elements.
        private VisualElement _root;
        private Label _titleLabel;
        private VisualElement _statusPill;
        private Label _statusLabel;
        private VisualElement _progressFill;
        private Label _recipeNameLabel;
        private Label _powerLabel;
        private VisualElement _recipeList;
        private Label _inputListLabel;
        private Label _outputListLabel;
        private bool _built;

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Bind this panel to a machine with a recipe list.
        /// </summary>
        public void Bind(IMachine machine, MonoBehaviour owner,
            List<MachineRecipe> recipes, MachineRecipe currentRecipe,
            System.Action<MachineRecipe> onRecipeSelected)
        {
            _machine = machine;
            _owner = owner;
            _recipes = recipes ?? new List<MachineRecipe>();
            _currentRecipe = currentRecipe;
            _onRecipeSelected = onRecipeSelected;

            if (!_built) BuildPanel();
            RefreshAll();
            Show();
        }

        /// <summary>Unbind and hide.</summary>
        public void Unbind()
        {
            _machine = null;
            _owner = null;
            Hide();
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Update()
        {
            if (_machine == null || !_built) return;
            RefreshDynamic();
        }

        // ── Build UI ──────────────────────────────────────────────────

        private void BuildPanel()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (document == null) return;

            _root = document.rootVisualElement;
            _root.Clear();

            var panel = UITheme.MachinePanel();
            _root.Add(panel);

            // ── Scroll container ──────────────────────────────────────
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            panel.Add(scroll);
            UITheme.StyleScroller(scroll);

            var content = new VisualElement();
            scroll.Add(content);

            // ── Header: Title + Status ────────────────────────────────
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 10;

            _titleLabel = UITheme.Title("Machine");
            _titleLabel.style.flexGrow = 1;
            headerRow.Add(_titleLabel);

            var (statusPill, statusLabel) = UITheme.StatusPill("IDLE", UITheme.AccentGold);
            _statusPill = statusPill;
            _statusLabel = statusLabel;
            headerRow.Add(_statusPill);

            content.Add(headerRow);
            content.Add(UITheme.AccentDivider());

            // ── Current Recipe Section ────────────────────────────────
            content.Add(UITheme.Subtitle("Current Recipe"));

            _recipeNameLabel = UITheme.Body("No recipe selected");
            _recipeNameLabel.style.marginBottom = 6;
            content.Add(_recipeNameLabel);

            var (bar, fill) = UITheme.ProgressBar(0f, UITheme.AccentCyan);
            _progressFill = fill;
            content.Add(bar);

            content.Add(UITheme.Spacer(4));

            // Input/output info.
            _inputListLabel = UITheme.Muted("Inputs: —");
            content.Add(_inputListLabel);
            _outputListLabel = UITheme.Muted("Output: —");
            content.Add(_outputListLabel);

            content.Add(UITheme.Spacer(8));

            // ── Power Section ─────────────────────────────────────────
            content.Add(UITheme.Subtitle("Power"));
            _powerLabel = UITheme.Body("—");
            content.Add(_powerLabel);

            content.Add(UITheme.Divider());

            // ── Recipe List ───────────────────────────────────────────
            content.Add(UITheme.Subtitle("Select Recipe"));

            _recipeList = new VisualElement();
            content.Add(_recipeList);

            BuildRecipeList();

            _built = true;
            Hide();
        }

        private void BuildRecipeList()
        {
            if (_recipeList == null) return;
            _recipeList.Clear();

            foreach (var recipe in _recipes)
            {
                if (recipe == null) continue;

                var card = MakeRecipeCard(recipe);
                _recipeList.Add(card);
            }
        }

        private VisualElement MakeRecipeCard(MachineRecipe recipe)
        {
            bool isSelected = recipe == _currentRecipe;

            var card = UITheme.Card();
            card.style.marginBottom = 4;
            card.style.cursor = new StyleCursor(StyleKeyword.Null);
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;

            if (isSelected)
                UITheme.Border(card, 1, UITheme.AccentCyan);

            // Selection indicator.
            var indicator = new VisualElement();
            indicator.style.width = 8;
            indicator.style.height = 8;
            indicator.style.marginRight = 8;
            UITheme.Radius(indicator, 4);
            indicator.style.backgroundColor = new StyleColor(
                isSelected ? UITheme.AccentCyan : UITheme.TextMuted);
            indicator.pickingMode = PickingMode.Ignore;
            card.Add(indicator);

            // Recipe info column.
            var infoCol = new VisualElement();
            infoCol.style.flexGrow = 1;
            infoCol.pickingMode = PickingMode.Ignore;

            var nameLabel = new Label(recipe.GetName());
            nameLabel.style.color = new StyleColor(
                isSelected ? UITheme.TextAccent : UITheme.TextPrimary);
            nameLabel.style.fontSize = 12;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.pickingMode = PickingMode.Ignore;
            infoCol.Add(nameLabel);

            // Input summary.
            var inputText = "In: ";
            if (recipe.inputs != null)
            {
                for (int i = 0; i < recipe.inputs.Length; i++)
                {
                    if (i > 0) inputText += " + ";
                    var inp = recipe.inputs[i];
                    inputText += inp.item != null ? $"{inp.count}x {inp.item.displayName}" : "?";
                }
            }
            var inputLabel = new Label(inputText);
            inputLabel.style.color = new StyleColor(UITheme.TextSecondary);
            inputLabel.style.fontSize = 10;
            inputLabel.pickingMode = PickingMode.Ignore;
            infoCol.Add(inputLabel);

            // Output summary.
            var outputText = recipe.outputItem != null
                ? $"Out: {recipe.outputCount}x {recipe.outputItem.displayName}"
                : "Out: —";
            if (recipe.byproductItem != null && recipe.byproductCount > 0)
                outputText += $" + {recipe.byproductCount}x {recipe.byproductItem.displayName}";
            var outputLabel = new Label(outputText);
            outputLabel.style.color = new StyleColor(UITheme.TextMuted);
            outputLabel.style.fontSize = 10;
            outputLabel.pickingMode = PickingMode.Ignore;
            infoCol.Add(outputLabel);

            card.Add(infoCol);

            // Time badge.
            var timeBadge = new Label($"{recipe.processSeconds:F1}s");
            timeBadge.style.color = new StyleColor(UITheme.TextMuted);
            timeBadge.style.fontSize = 10;
            timeBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            timeBadge.style.marginLeft = 8;
            timeBadge.pickingMode = PickingMode.Ignore;
            card.Add(timeBadge);

            // Click handler.
            card.RegisterCallback<ClickEvent>(_ =>
            {
                _currentRecipe = recipe;
                _onRecipeSelected?.Invoke(recipe);
                BuildRecipeList(); // Refresh selection visuals.
                RefreshDynamic();
            });

            // Hover effect.
            card.RegisterCallback<PointerEnterEvent>(_ =>
                card.style.backgroundColor = new StyleColor(UITheme.BgHover));
            card.RegisterCallback<PointerLeaveEvent>(_ =>
                card.style.backgroundColor = new StyleColor(UITheme.BgCard));

            return card;
        }

        // ── Refresh ───────────────────────────────────────────────────

        private void RefreshAll()
        {
            if (_machine == null || !_built) return;
            _titleLabel.text = _machine.MachineName ?? "Machine";
            RefreshDynamic();
        }

        private void RefreshDynamic()
        {
            if (_machine == null || !_built) return;

            // Status pill.
            if (!_machine.UserEnabled)
                UpdatePill(_statusPill, _statusLabel, "DISABLED", UITheme.AccentRed);
            else if (_machine.IsOnline)
                UpdatePill(_statusPill, _statusLabel,
                    _machine.IsActive ? "RUNNING" : "IDLE",
                    _machine.IsActive ? UITheme.AccentGreen : UITheme.AccentGold);
            else
                UpdatePill(_statusPill, _statusLabel, "NO POWER", UITheme.AccentRed);

            // Progress.
            UITheme.SetFillPercent(_progressFill, _machine.Progress01);

            // Current recipe info.
            if (_currentRecipe != null)
            {
                _recipeNameLabel.text = _currentRecipe.GetName();

                var inputText = "Inputs: ";
                if (_currentRecipe.inputs != null)
                {
                    for (int i = 0; i < _currentRecipe.inputs.Length; i++)
                    {
                        if (i > 0) inputText += " + ";
                        var inp = _currentRecipe.inputs[i];
                        inputText += inp.item != null ? $"{inp.count}x {inp.item.displayName}" : "?";
                    }
                }
                _inputListLabel.text = inputText;

                var outputText = _currentRecipe.outputItem != null
                    ? $"Output: {_currentRecipe.outputCount}x {_currentRecipe.outputItem.displayName}"
                    : "Output: —";
                if (_currentRecipe.byproductItem != null && _currentRecipe.byproductCount > 0)
                    outputText += $" (+ {_currentRecipe.byproductCount}x {_currentRecipe.byproductItem.displayName})";
                _outputListLabel.text = outputText;
            }
            else
            {
                _recipeNameLabel.text = "No recipe selected";
                _inputListLabel.text = "Inputs: —";
                _outputListLabel.text = "Output: —";
            }

            // Power.
            _powerLabel.text = $"{_machine.CurrentWattage:F0} W";
        }

        private static void UpdatePill(VisualElement pill, Label label, string text, Color bg)
        {
            if (pill == null || label == null) return;
            label.text = text;
            label.style.color = new StyleColor(bg);
            pill.style.backgroundColor = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.22f));
            UITheme.Border(pill, 1, new Color(bg.r, bg.g, bg.b, 0.55f));
        }

        // ── Show / Hide ───────────────────────────────────────────────

        private void Show()
        {
            if (_root != null) _root.style.display = DisplayStyle.Flex;
        }

        private void Hide()
        {
            if (_root != null) _root.style.display = DisplayStyle.None;
        }
    }
}
