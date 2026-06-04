// Assets/Scripts/VoxelEngine/UI/QuarryHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║         QUARRY HUD — Right-panel machine UI (via MachineUIs)   ║
// ║   Static builder called from GameUIController.Refresh().       ║
// ║   Provides live-update references for per-frame Tick() calls.  ║
// ║                                                                ║
// ║   v0.4.3 — Added TapeFrame phase display + mining height stat  ║
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
        private static VisualElement _liveStatusPill;
        private static Label         _liveStatusLabel;
        private static VisualElement _liveMiningFill;
        private static Label         _liveDepthLabel;
        private static Label         _livePowerLabel;

        // ── Build ──────────────────────────────────────────────────
        public static VisualElement BuildPanel(
            Quarry quarry,
            System.Func<IItemContainer, int, ItemStack, bool, bool, VisualElement> buildSlotFn)
        {
            quarry.EnsureOutputPublic();

            var panel = T.MachinePanel();

            bool powered = true;
            var pc = quarry.GetComponent<PowerConsumer>();
            if (pc != null) powered = pc.IsPowered;

            string status = GetStatusText(quarry, powered);
            Color statusCol = GetStatusColor(quarry, powered);

            // ── Header ─────────────────────────────────────────────
            var hdrRow = new VisualElement();
            hdrRow.style.flexDirection = FlexDirection.Row;
            hdrRow.style.alignItems    = Align.Center;
            hdrRow.style.marginBottom  = 10;
            hdrRow.pickingMode = PickingMode.Ignore;

            hdrRow.Add(T.IconBadge("\u26CF", T.AccentGold)); // ⛏ pick

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
                    ? $"{pc.wattsPerSecond:0} W  \u00B7  Connected"
                    : "Disconnected";
                var pwrLbl = T.StatRow("\u26A1", "Power", pwrText,
                    powered ? T.AccentGreen : T.AccentRed);
                if (pwrLbl.childCount >= 3)
                    _livePowerLabel = pwrLbl[pwrLbl.childCount - 1] as Label;
                panel.Add(pwrLbl);
            }

            // ── Stats ──────────────────────────────────────────────
            var depthRow = T.StatRow("\u2B07", "Depth",
                $"{quarry.CurrentDepth} / {quarry.MaxDepth}", T.TextPrimary);
            if (depthRow.childCount >= 3)
                _liveDepthLabel = depthRow[depthRow.childCount - 1] as Label;

            panel.Add(T.StatRow("\uD83D\uDCD0", "Area",
                $"{quarry.AreaX} \u00D7 {quarry.AreaZ}", T.AccentCyan));
            panel.Add(depthRow);
            panel.Add(T.StatRow("\uD83D\uDD27", "Tier",
                $"{quarry.quarryTier}", T.TextSecondary));

            // Mining progress bar.
            panel.Add(T.Spacer(4));
            var (mineBar, mineFill) = T.ProgressBar(
                quarry.Phase == QuarryPhase.Mining ? quarry.MineProgress01 : 0f,
                T.AccentCyan, 8, true);
            _liveMiningFill = mineFill;
            panel.Add(mineBar);
            panel.Add(T.Divider());

            // ── Output inventory ───────────────────────────────────
            panel.Add(T.Subtitle("Output Inventory"));

            var sortRow = new VisualElement();
            sortRow.style.flexDirection  = FlexDirection.Row;
            sortRow.style.justifyContent = Justify.FlexEnd;
            sortRow.style.marginBottom   = 4;
            sortRow.Add(T.SmallButton("\u21C5  Sort",
                () => quarry.Output?.Sort(), T.AccentTeal));
            panel.Add(sortRow);

            var grid   = T.SlotGrid();
            var output = quarry.Output;
            for (int i = 0; i < output.Size; i++)
                grid.Add(buildSlotFn(output, i, output.GetSlot(i), false, true));
            panel.Add(grid);

            panel.Add(T.Spacer(8));
            panel.Add(T.Muted(
                "Place two Quarry Landmarks to define a custom mining area. " +
                "Connect item pipes to auto-export."));

            return panel;
        }

        // ── Tick ───────────────────────────────────────────────────
        public static void Tick(Quarry quarry)
        {
            if (quarry == null) return;

            bool powered = true;
            var pc = quarry.GetComponent<PowerConsumer>();
            if (pc != null) powered = pc.IsPowered;

            // Status pill live-update.
            if (_liveStatusPill != null && _liveStatusLabel != null)
            {
                string txt = GetStatusText(quarry, powered);
                Color bg = GetStatusColor(quarry, powered);

                _liveStatusLabel.text = txt;
                _liveStatusPill.style.backgroundColor =
                    new StyleColor(new Color(bg.r, bg.g, bg.b, 0.22f));
                T.Border(_liveStatusPill, 1, new Color(bg.r, bg.g, bg.b, 0.55f));
            }

            // Mining progress fill.
            if (_liveMiningFill != null)
                T.SetFillPercent(_liveMiningFill,
                    quarry.Phase == QuarryPhase.Mining ? quarry.MineProgress01 : 0f);

            // Depth label.
            if (_liveDepthLabel != null)
                _liveDepthLabel.text =
                    $"{quarry.CurrentDepth} / {quarry.MaxDepth}";

            // Power label.
            if (_livePowerLabel != null && pc != null)
            {
                bool p2 = pc.IsPowered;
                _livePowerLabel.text = p2
                    ? $"{pc.wattsPerSecond:0} W  \u00B7  Connected"
                    : "Disconnected";
                _livePowerLabel.style.color =
                    new StyleColor(p2 ? T.AccentGreen : T.AccentRed);
            }
        }

        // ── Helpers ────────────────────────────────────────────────
        private static string GetStatusText(Quarry q, bool powered)
        {
            if (!powered) return "NO POWER";
            return q.Phase switch
            {
                QuarryPhase.Idle          => "IDLE",
                QuarryPhase.TapeFrame     => "SURVEYING",
                QuarryPhase.BuildingFrame => "BUILDING",
                QuarryPhase.Mining        => q.IsOutputFull ? "OUTPUT FULL" : "DRILLING",
                QuarryPhase.Complete      => "COMPLETE",
                _                         => "UNKNOWN"
            };
        }

        private static Color GetStatusColor(Quarry q, bool powered)
        {
            if (!powered) return T.AccentRed;
            return q.Phase switch
            {
                QuarryPhase.Idle          => T.TextMuted,
                QuarryPhase.TapeFrame     => T.AccentOrange,
                QuarryPhase.BuildingFrame => T.AccentOrange,
                QuarryPhase.Mining        => q.IsOutputFull ? T.AccentRed : T.AccentGreen,
                QuarryPhase.Complete      => T.AccentCyan,
                _                         => T.TextMuted
            };
        }
    }
}
