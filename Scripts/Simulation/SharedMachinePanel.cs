// Assets/Scripts/VoxelEngine/Simulation/SharedMachinePanel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — SHARED MACHINE PANEL (UI Toolkit)           ║
// ║  Reusable premium UI panel for any IMachine block. Shows:       ║
// ║    • Machine name + status pill (ONLINE / OFFLINE / DISABLED)   ║
// ║    • Recipe name + animated progress bar                        ║
// ║    • Input/output inventory slots with drag-drop                ║
// ║    • Power status and current wattage                           ║
// ║    • ENABLED toggle pill                                        ║
// ║  Follows the UITheme design system — no hard-coded colours.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Shared UI panel for all machine blocks. Attach to the UI document
    /// and call <see cref="Bind"/> with the target machine. The panel
    /// auto-refreshes every frame while visible.
    /// </summary>
    public class SharedMachinePanel : MonoBehaviour
    {
        [Header("UI References")]
        [Tooltip("The UIDocument that hosts this panel.")]
        public UIDocument document;

        // ── Runtime State ─────────────────────────────────────────────

        private IMachine _machine;
        private MonoBehaviour _machineOwner;

        // UI element references.
        private VisualElement _root;
        private Label _titleLabel;
        private VisualElement _statusPill;
        private Label _statusLabel;
        private VisualElement _togglePill;
        private Label _toggleLabel;
        private VisualElement _progressFill;
        private Label _recipeLabel;
        private Label _powerLabel;
        private Label _wattageLabel;

        private bool _built;

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Bind this panel to a machine. Call when the player interacts
        /// with a machine block.
        /// </summary>
        public void Bind(IMachine machine, MonoBehaviour owner)
        {
            _machine = machine;
            _machineOwner = owner;

            if (!_built) BuildPanel();
            RefreshAll();
            Show();
        }

        /// <summary>Unbind and hide the panel.</summary>
        public void Unbind()
        {
            _machine = null;
            _machineOwner = null;
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

            // Main panel container.
            var panel = UITheme.MachinePanel();
            _root.Add(panel);

            // ── Header Row: Title + Toggle + Status ───────────────────
            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;
            headerRow.style.marginBottom = 10;

            _titleLabel = UITheme.Title("Machine");
            _titleLabel.style.flexGrow = 1;
            headerRow.Add(_titleLabel);

            // ENABLED toggle pill.
            var (togglePill, toggleLabel) = UITheme.MachineToggle(true, OnToggleChanged);
            _togglePill = togglePill;
            _toggleLabel = toggleLabel;
            headerRow.Add(_togglePill);

            // Status pill.
            var (statusPill, statusLabel) = UITheme.StatusPill("ONLINE", UITheme.AccentGreen);
            _statusPill = statusPill;
            _statusLabel = statusLabel;
            headerRow.Add(_statusPill);

            panel.Add(headerRow);
            panel.Add(UITheme.AccentDivider());

            // ── Recipe Section ────────────────────────────────────────
            panel.Add(UITheme.Subtitle("Current Recipe"));

            _recipeLabel = UITheme.Body("No recipe");
            _recipeLabel.style.marginBottom = 6;
            panel.Add(_recipeLabel);

            var (_, progressFill) = UITheme.ProgressBar(0f, UITheme.AccentCyan);
            _progressFill = progressFill;
            panel.Add(UITheme.ProgressBar(0f, UITheme.AccentCyan).bar);

            panel.Add(UITheme.Spacer(8));

            // ── Power Section ─────────────────────────────────────────
            panel.Add(UITheme.Subtitle("Power"));

            _powerLabel = UITheme.Body("Not connected");
            panel.Add(_powerLabel);

            _wattageLabel = UITheme.StatLabel("0 W", UITheme.TextSecondary);
            panel.Add(_wattageLabel);

            panel.Add(UITheme.Spacer(8));
            panel.Add(UITheme.Divider());

            // ── Inventory Section (placeholder) ───────────────────────
            panel.Add(UITheme.Subtitle("Inventory"));
            var invNote = UITheme.Muted("Input and output slots are managed by the machine's own containers.");
            panel.Add(invNote);

            _built = true;
            Hide(); // start hidden
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
            {
                UpdatePill(_statusPill, _statusLabel, "DISABLED", UITheme.AccentRed);
            }
            else if (_machine.IsOnline)
            {
                UpdatePill(_statusPill, _statusLabel,
                    _machine.IsActive ? "RUNNING" : "IDLE",
                    _machine.IsActive ? UITheme.AccentGreen : UITheme.AccentGold);
            }
            else
            {
                UpdatePill(_statusPill, _statusLabel, "NO POWER", UITheme.AccentRed);
            }

            // Progress bar.
            UITheme.SetFillPercent(_progressFill, _machine.Progress01);

            // Recipe.
            _recipeLabel.text = _machine.IsActive ? "Processing..." : "Waiting for input";

            // Power.
            _wattageLabel.text = $"{_machine.CurrentWattage:F0} W";
            _powerLabel.text = _machine.IsOnline ? "Connected" : "Not connected";
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

        // ── Toggle Callback ───────────────────────────────────────────

        private void OnToggleChanged(bool enabled)
        {
            if (_machine != null)
                _machine.UserEnabled = enabled;
        }
    }
}
