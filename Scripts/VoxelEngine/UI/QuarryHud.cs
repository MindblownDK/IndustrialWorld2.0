// Assets/Scripts/VoxelEngine/UI/QuarryHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║    QUARRY HUD — Dark steel premium OS-dashboard machine UI     ║
// ║    Upgrade slots, progress ring, live status pill, port hint.  ║
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
        private static VisualElement _liveStatusPill, _liveMiningFill;
        private static Label _liveStatusLabel, _liveDepthLabel, _livePowerLabel, _liveAreaLabel;
        private static VisualElement _rangeBar, _speedBar, _effBar;
        private static Label _rangeLbl, _speedLbl, _effLbl;

        public static VisualElement BuildPanel(
            Quarry quarry,
            System.Func<IItemContainer, int, ItemStack, bool, bool, VisualElement> buildSlotFn)
        {
            quarry.EnsureOutputPublic();
            var panel = T.MachinePanel();

            bool powered = true;
            var pc = quarry.GetComponent<PowerConsumer>();
            if (pc != null) powered = pc.IsPowered;

            string status = GetStatus(quarry, powered);
            Color sc = GetStatusColor(quarry, powered);

            // ── Header ───────────────────────────────────────────
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems = Align.Center;
            hdr.style.marginBottom = 10;
            hdr.pickingMode = PickingMode.Ignore;
            hdr.Add(T.IconBadge("\u26CF", T.AccentGold));
            var title = T.Title("Quarry Drill");
            title.style.flexGrow = 1; title.style.fontSize = 15;
            hdr.Add(title);
            var (pill, pillLbl) = T.StatusPill(status, sc);
            _liveStatusPill = pill; _liveStatusLabel = pillLbl;
            hdr.Add(pill);
            panel.Add(hdr);
            panel.Add(T.AccentDivider(sc));

            // ── Power ────────────────────────────────────────────
            if (pc != null)
            {
                string pTxt = powered ? $"{pc.wattsPerSecond:0} W  \u00B7  Connected" : "Disconnected";
                var pr = T.StatRow("\u26A1", "Power", pTxt, powered ? T.AccentGreen : T.AccentRed);
                if (pr.childCount >= 3) _livePowerLabel = pr[pr.childCount - 1] as Label;
                panel.Add(pr);
            }

            // ── Stats grid ───────────────────────────────────────
            var areaR = T.StatRow("\uD83D\uDCD0", "Area",
                $"{quarry.AreaX}\u00D7{quarry.AreaZ}  ({quarry.EffectiveSize}\u00B2)", T.AccentCyan);
            if (areaR.childCount >= 3) _liveAreaLabel = areaR[areaR.childCount - 1] as Label;

            var depthR = T.StatRow("\u2B07", "Depth", $"{quarry.CurrentDepth} / {quarry.MaxDepth}", T.TextPrimary);
            if (depthR.childCount >= 3) _liveDepthLabel = depthR[depthR.childCount - 1] as Label;

            panel.Add(areaR);
            panel.Add(depthR);
            panel.Add(T.StatRow("\uD83D\uDD27", "Tier", $"{quarry.quarryTier}", T.TextSecondary));

            // Speed stat
            panel.Add(T.StatRow("\u23F1", "Speed", $"{quarry.EffectiveMineInterval:F2}s", T.AccentTeal));

            // ── Upgrade Slots ────────────────────────────────────
            panel.Add(T.Spacer(4));
            panel.Add(T.Divider());
            panel.Add(T.Subtitle("Upgrades"));

            var upgGrid = new VisualElement();
            upgGrid.style.flexDirection = FlexDirection.Row;
            upgGrid.style.flexWrap = Wrap.Wrap;
            upgGrid.style.marginTop = 4;
            upgGrid.style.marginBottom = 4;

            upgGrid.Add(BuildUpgradeSlot("Range",
                quarry.InstalledRangeLevel, Quarry.MaxRangeLevel,
                T.AccentGold, "\uD83D\uDCCF", "+1 size per",
                out _rangeBar, out _rangeLbl));
            upgGrid.Add(BuildUpgradeSlot("Speed",
                quarry.InstalledSpeedLevel, Quarry.MaxSpeedLevel,
                T.AccentTeal, "\u26A1", "-0.04s per",
                out _speedBar, out _speedLbl));
            upgGrid.Add(BuildUpgradeSlot("Efficiency",
                quarry.InstalledEfficiencyLevel, Quarry.MaxEfficiencyLevel,
                T.AccentPurple, "\u2B50", "+1 vox/tick per",
                out _effBar, out _effLbl));

            panel.Add(upgGrid);
            panel.Add(T.Divider());

            // ── Mining progress ──────────────────────────────────
            panel.Add(T.Spacer(4));
            var (mineBar, mineFill) = T.ProgressBar(
                quarry.Phase == QuarryPhase.Mining ? quarry.MineProgress01 : 0f, T.AccentCyan, 8, true);
            _liveMiningFill = mineFill;
            panel.Add(mineBar);
            panel.Add(T.Divider());

            // ── Output Inventory ─────────────────────────────────
            panel.Add(T.Subtitle("Output"));
            var sortRow = new VisualElement();
            sortRow.style.flexDirection = FlexDirection.Row;
            sortRow.style.justifyContent = Justify.FlexEnd;
            sortRow.style.marginBottom = 4;
            sortRow.Add(T.SmallButton("\u21C5 Sort", () => quarry.Output?.Sort(), T.AccentTeal));
            panel.Add(sortRow);

            var grid = T.SlotGrid();
            var output = quarry.Output;
            for (int i = 0; i < output.Size; i++)
                grid.Add(buildSlotFn(output, i, output.GetSlot(i), false, true));
            panel.Add(grid);

            panel.Add(T.Spacer(8));
            panel.Add(T.Muted("Right-click with a Quarry Upgrade to install. Drop upgrades onto the machine."));

            return panel;
        }

        private static VisualElement BuildUpgradeSlot(string name, int level, int max,
            Color accent, string icon, string desc,
            out VisualElement fillBar, out Label levelLbl)
        {
            var card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.minWidth = 120;
            card.style.paddingTop = 8; card.style.paddingBottom = 8;
            card.style.paddingLeft = 10; card.style.paddingRight = 10;
            card.style.marginRight = 6; card.style.marginBottom = 6;
            card.style.backgroundColor = new StyleColor(new Color(T.BgCard.r, T.BgCard.g, T.BgCard.b, 0.92f));
            T.Radius(card, T.CardRadius);
            T.Border(card, 1, new Color(accent.r, accent.g, accent.b, 0.18f));
            card.pickingMode = PickingMode.Ignore;

            // Icon + name row
            var topR = new VisualElement();
            topR.style.flexDirection = FlexDirection.Row;
            topR.style.alignItems = Align.Center;
            topR.style.marginBottom = 4;
            topR.pickingMode = PickingMode.Ignore;

            var ico = new Label(icon);
            ico.style.fontSize = 12; ico.style.marginRight = 4;
            ico.style.color = new StyleColor(accent);
            ico.pickingMode = PickingMode.Ignore;
            topR.Add(ico);

            var nm = new Label(name);
            nm.style.fontSize = 10;
            nm.style.color = new StyleColor(T.TextSecondary);
            nm.style.unityFontStyleAndWeight = FontStyle.Bold;
            nm.style.flexGrow = 1;
            nm.pickingMode = PickingMode.Ignore;
            topR.Add(nm);

            levelLbl = new Label($"{level}/{max}");
            levelLbl.style.fontSize = 11;
            levelLbl.style.color = new StyleColor(level >= max ? T.AccentGreen : accent);
            levelLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            levelLbl.pickingMode = PickingMode.Ignore;
            topR.Add(levelLbl);

            card.Add(topR);

            // Mini progress bar
            var barBg = new VisualElement();
            barBg.style.height = 4;
            barBg.style.backgroundColor = new StyleColor(new Color(T.BgBase.r, T.BgBase.g, T.BgBase.b, 0.8f));
            T.Radius(barBg, 2);
            barBg.style.marginBottom = 3;
            barBg.pickingMode = PickingMode.Ignore;

            fillBar = new VisualElement();
            fillBar.style.height = 4;
            fillBar.style.backgroundColor = new StyleColor(accent);
            T.Radius(fillBar, 2);
            fillBar.style.width = Length.Percent((float)level / Mathf.Max(1, max) * 100f);
            fillBar.pickingMode = PickingMode.Ignore;
            barBg.Add(fillBar);
            card.Add(barBg);

            // Description
            var descLbl = new Label(desc);
            descLbl.style.fontSize = 8;
            descLbl.style.color = new StyleColor(T.TextMuted);
            descLbl.pickingMode = PickingMode.Ignore;
            card.Add(descLbl);

            return card;
        }

        public static void Tick(Quarry quarry)
        {
            if (quarry == null) return;
            bool powered = true;
            var pc = quarry.GetComponent<PowerConsumer>();
            if (pc != null) powered = pc.IsPowered;

            if (_liveStatusPill != null && _liveStatusLabel != null)
            {
                string txt = GetStatus(quarry, powered);
                Color bg = GetStatusColor(quarry, powered);
                _liveStatusLabel.text = txt;
                _liveStatusPill.style.backgroundColor = new StyleColor(new Color(bg.r, bg.g, bg.b, 0.22f));
                T.Border(_liveStatusPill, 1, new Color(bg.r, bg.g, bg.b, 0.55f));
            }
            if (_liveMiningFill != null)
                T.SetFillPercent(_liveMiningFill, quarry.Phase == QuarryPhase.Mining ? quarry.MineProgress01 : 0f);
            if (_liveDepthLabel != null)
                _liveDepthLabel.text = $"{quarry.CurrentDepth} / {quarry.MaxDepth}";
            if (_liveAreaLabel != null)
                _liveAreaLabel.text = $"{quarry.AreaX}\u00D7{quarry.AreaZ}  ({quarry.EffectiveSize}\u00B2)";
            if (_livePowerLabel != null && pc != null)
            {
                bool p2 = pc.IsPowered;
                _livePowerLabel.text = p2 ? $"{pc.wattsPerSecond:0} W  \u00B7  Connected" : "Disconnected";
                _livePowerLabel.style.color = new StyleColor(p2 ? T.AccentGreen : T.AccentRed);
            }

            // Upgrade levels
            if (_rangeBar != null) _rangeBar.style.width = Length.Percent((float)quarry.InstalledRangeLevel / Quarry.MaxRangeLevel * 100f);
            if (_rangeLbl != null) _rangeLbl.text = $"{quarry.InstalledRangeLevel}/{Quarry.MaxRangeLevel}";
            if (_speedBar != null) _speedBar.style.width = Length.Percent((float)quarry.InstalledSpeedLevel / Quarry.MaxSpeedLevel * 100f);
            if (_speedLbl != null) _speedLbl.text = $"{quarry.InstalledSpeedLevel}/{Quarry.MaxSpeedLevel}";
            if (_effBar != null) _effBar.style.width = Length.Percent((float)quarry.InstalledEfficiencyLevel / Quarry.MaxEfficiencyLevel * 100f);
            if (_effLbl != null) _effLbl.text = $"{quarry.InstalledEfficiencyLevel}/{Quarry.MaxEfficiencyLevel}";
        }

        private static string GetStatus(Quarry q, bool p) => !p ? "NO POWER" : q.Phase switch
        {
            QuarryPhase.TapeFrame => "SURVEYING",
            QuarryPhase.BuildingFrame => "BUILDING",
            QuarryPhase.Mining => q.IsOutputFull ? "OUTPUT FULL" : "DRILLING",
            QuarryPhase.Complete => "COMPLETE",
            _ => "IDLE"
        };

        private static Color GetStatusColor(Quarry q, bool p) => !p ? T.AccentRed : q.Phase switch
        {
            QuarryPhase.TapeFrame => T.AccentOrange,
            QuarryPhase.BuildingFrame => T.AccentOrange,
            QuarryPhase.Mining => q.IsOutputFull ? T.AccentRed : T.AccentGreen,
            QuarryPhase.Complete => T.AccentCyan,
            _ => T.TextMuted
        };
    }
}
