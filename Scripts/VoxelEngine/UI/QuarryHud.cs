// Assets/Scripts/VoxelEngine/UI/QuarryHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║         QUARRY HUD — Right-panel machine UI (via MachineUIs)   ║
// ║   Static builder called from GameUIController.Refresh().       ║
// ║   Provides live-update references for per-frame Tick() calls.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Transport;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class QuarryHud
    {
        // Live-updated element references — poked by Tick() every frame.
        private static VisualElement _liveStatusPill;
        private static Label         _liveStatusLabel;
        private static VisualElement _liveMiningFill;
        private static Label         _liveDepthLabel;
        private static Label         _livePowerLabel;

        // ── Build ──────────────────────────────────────────────────
        /// <summary>
        /// Builds the Quarry right-panel and returns it for GameUIController to mount.
        /// </summary>
        public static VisualElement BuildPanel(
            Quarry quarry,
            System.Func<IItemContainer, int, ItemStack, bool, bool, VisualElement> buildSlotFn)
        {
            quarry.EnsureOutputPublic();

            // Use the shared MachinePanel layout dimensions.
            var panel = T.MachinePanel();

            // ── Determine live state ────────────────────────────────
            bool powered = true;
            var pc = quarry.GetComponent<PowerConsumer>();
            if (pc != null) powered = pc.IsPowered;

            string status = quarry.Phase == QuarryPhase.BuildingFrame ? "BUILDING" :
                            quarry.IsMining ? (quarry.IsOutputFull ? "OUTPUT FULL" : "DRILLING") :
                            quarry.CurrentDepth >= quarry.MaxDepth ? "COMPLETE" :
                            !powered ? "NO POWER" : "IDLE";

            Color statusCol = quarry.Phase == QuarryPhase.BuildingFrame ? T.AccentOrange :
                              quarry.IsMining ? (quarry.IsOutputFull ? T.AccentRed : T.AccentGreen) :
                              quarry.CurrentDepth >= quarry.MaxDepth ? T.AccentCyan :
                              !powered ? T.AccentRed : T.TextMuted;

            // ── Header ─────────────────────────────────────────────
            var hdrRow = new VisualElement();
            hdrRow.style.flexDirection = FlexDirection.Row;
            hdrRow.style.alignItems    = Align.Center;
            hdrRow.style.marginBottom  = 10;
            hdrRow.pickingMode = PickingMode.Ignore;

            hdrRow.Add(T.IconBadge("⛏", T.AccentGold));

            var titleLbl = T.Title("Quarry Drill");
            titleLbl.style.flexGrow = 1;
            titleLbl.style.fontSize = 15;
            hdrRow.Add(titleLbl);

            var (pill, pillLbl) = T.StatusPill(status, statusCol);
            _liveStatusPill  = pill;
            _liveStatusLabel = pillLbl;
            hdrRow.Add(pill);
            panel.Add(hdrRow);
            panel.Add(T.AccentDivider(statusCol));

            // ── Power stat ─────────────────────────────────────────
            if (pc != null)
            {
                string pwrText = powered
                    ? $"{pc.wattsPerSecond:0} W  ·  Connected"
                    : "Disconnected";
                var pwrLbl = T.StatRow("⚡", "Power", pwrText, powered ? T.AccentGreen : T.AccentRed);
                // Grab the value label for live updates (last child).
                if (pwrLbl.childCount >= 3)
                    _livePowerLabel = pwrLbl[pwrLbl.childCount - 1] as Label;
                panel.Add(pwrLbl);
            }

            // ── Stats ──────────────────────────────────────────────
            var depthRow = T.StatRow("⬇", "Depth", $"{quarry.CurrentDepth} / {quarry.MaxDepth}", T.TextPrimary);
            if (depthRow.childCount >= 3)
                _liveDepthLabel = depthRow[depthRow.childCount - 1] as Label;
            panel.Add(T.StatRow("📐", "Area",   $"{quarry.AreaX} × {quarry.AreaZ}",  T.AccentCyan));
            panel.Add(depthRow);
            panel.Add(T.StatRow("🔧", "Tier",   $"{quarry.quarryTier}",              T.TextSecondary));

            // Mining progress bar.
            panel.Add(T.Spacer(4));
            var (mineBar, mineFill) = T.ProgressBar(quarry.MineProgress01, T.AccentCyan, 8, true);
            _liveMiningFill = mineFill;
            panel.Add(mineBar);
            panel.Add(T.Divider());

            // ── Output inventory ───────────────────────────────────
            panel.Add(T.Subtitle("Output Inventory"));

            // Sort button.
            var sortRow = new VisualElement();
            sortRow.style.flexDirection  = FlexDirection.Row;
            sortRow.style.justifyContent = Justify.FlexEnd;
            sortRow.style.marginBottom   = 4;
            sortRow.Add(T.SmallButton("⇅  Sort", () => quarry.Output?.Sort(), T.AccentTeal));
            panel.Add(sortRow);

            var grid   = T.SlotGrid();
            var output = quarry.Output;
            for (int i = 0; i < output.Size; i++)
                grid.Add(buildSlotFn(output, i, output.GetSlot(i), false, true));
            panel.Add(grid);

            panel.Add(T.Spacer(8));
            panel.Add(T.Muted("Connect item pipes to auto-export resources. Place landmarks for a custom drill area."));

            return panel;
        }

        // ── Tick ───────────────────────────────────────────────────
        /// <summary>Call every frame while the quarry panel is open.</summary>
        public static void Tick(Quarry quarry)
        {
            if (quarry == null) return;

            bool powered = true;
            var pc = quarry.GetComponent<PowerConsumer>();
            if (pc != null) powered = pc.IsPowered;

            // Status pill live-update.
            if (_liveStatusPill != null && _liveStatusLabel != null)
            {
                string txt = quarry.Phase == QuarryPhase.BuildingFrame ? "BUILDING" :
                             quarry.IsMining ? (quarry.IsOutputFull ? "OUTPUT FULL" : "DRILLING") :
                             quarry.CurrentDepth >= quarry.MaxDepth ? "COMPLETE" :
                             !powered ? "NO POWER" : "IDLE";
                Color bg = quarry.Phase == QuarryPhase.BuildingFrame ? T.AccentOrange :
                           quarry.IsMining ? (quarry.IsOutputFull ? T.AccentRed : T.AccentGreen) :
                           quarry.CurrentDepth >= quarry.MaxDepth ? T.AccentCyan :
                           !powered ? T.AccentRed : T.TextMuted;

                _liveStatusLabel.text = txt;
                _liveStatusPill.style.backgroundColor =
                    new StyleColor(new Color(bg.r, bg.g, bg.b, 0.22f));
                T.Border(_liveStatusPill, 1, new Color(bg.r, bg.g, bg.b, 0.55f));
            }

            // Mining progress fill.
            if (_liveMiningFill != null)
                T.SetFillPercent(_liveMiningFill, quarry.IsMining ? quarry.MineProgress01 : 0f);

            // Depth label.
            if (_liveDepthLabel != null)
                _liveDepthLabel.text = $"{quarry.CurrentDepth} / {quarry.MaxDepth}";

            // Power label.
            if (_livePowerLabel != null && pc != null)
            {
                bool p2 = pc.IsPowered;
                _livePowerLabel.text = p2
                    ? $"{pc.wattsPerSecond:0} W  ·  Connected"
                    : "Disconnected";
                _livePowerLabel.style.color = new StyleColor(p2 ? T.AccentGreen : T.AccentRed);
            }
        }
    }
}
