// Assets/Scripts/VoxelEngine/UI/ArmorUpgradeStationPanel.cs
//
// Focused UI Toolkit surface for the anvil-style Armor Upgrade Station. It stays
// deliberately small: one armor input, one module, one finished output, and a live
// installation readout. The station owns all gameplay state; this panel only binds
// presentation and player actions to that state.

using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Combat;
using VoxelEngine.Items;

namespace VoxelEngine.UI
{
    public static class ArmorUpgradeStationPanel
    {
        public static VisualElement Build(
            ArmorUpgradeStation station,
            Func<IItemContainer, int, ItemStack, bool, bool, VisualElement> buildSlot,
            Action refresh)
        {
            var panel = UITheme.MachinePanel();
            if (station == null || buildSlot == null)
            {
                panel.Add(UITheme.Title("Armor Upgrade Station"));
                panel.Add(UITheme.Muted("Station unavailable."));
                return panel;
            }

            var (header, _, _, _) = UITheme.HeaderRow(
                "Armor Upgrade Station",
                station.IsUpgrading ? "FORGING" : "READY",
                station.IsUpgrading ? UITheme.AccentAmber : UITheme.AccentGreen);
            header.Insert(0, UITheme.IconBadge("⚒", UITheme.AccentAmber));
            panel.Add(header);
            panel.Add(UITheme.AccentDivider(UITheme.AccentAmber));

            bool inputsLocked = station.IsUpgrading;
            var slots = new VisualElement();
            slots.style.flexDirection = FlexDirection.Row;
            slots.style.alignItems = Align.FlexStart;
            slots.style.justifyContent = Justify.SpaceBetween;
            slots.style.marginBottom = 10;

            slots.Add(BuildSlotCard("ARMOR", station.ArmorSlot, buildSlot, !inputsLocked));
            slots.Add(BuildFlowArrow());
            slots.Add(BuildSlotCard("MODULE", station.ModuleSlot, buildSlot, !inputsLocked));
            slots.Add(BuildFlowArrow());
            slots.Add(BuildSlotCard("OUTPUT", station.OutputSlot, buildSlot, !inputsLocked));
            panel.Add(slots);

            var armorStack = station.ArmorSlot.GetSlot(0);
            var armor = armorStack != null && !armorStack.IsEmpty ? armorStack.item as ArmorItem : null;
            var moduleStack = station.ModuleSlot.GetSlot(0);
            var module = moduleStack != null && !moduleStack.IsEmpty ? moduleStack.item as ArmorUpgradeItem : null;

            panel.Add(UITheme.Divider());
            BuildArmorTelemetry(panel, armorStack, armor);
            panel.Add(UITheme.Spacer(6));
            BuildModulePreview(panel, station, module);
            panel.Add(UITheme.Spacer(10));

            if (station.IsUpgrading)
            {
                BuildProgress(panel, station);
                var cancel = UITheme.ActionButton("CANCEL — RETURN INPUTS", () =>
                {
                    station.CancelUpgrade();
                    BuildFeedbackHud.Show("Upgrade Cancelled", "Armor and module remain in the station.", null, UITheme.AccentAmber);
                    refresh?.Invoke();
                }, UITheme.AccentRed);
                cancel.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
                panel.Add(cancel);
            }
            else
            {
                bool canStart = station.CanStartUpgrade(out string reason);
                string actionText = canStart && module != null
                    ? $"INSTALL · {station.GetUpgradeDuration(module):0}s"
                    : "INSERT ARMOR + MODULE";
                var start = UITheme.ActionButton(actionText, () =>
                {
                    if (station.TryStartUpgrade(out string startReason))
                    {
                        var activeModule = station.ModuleSlot.GetSlot(0)?.item as ArmorUpgradeItem;
                        string detail = activeModule != null
                            ? $"{activeModule.displayName} installation started — {station.TotalSeconds:0}s."
                            : "Armor installation started.";
                        BuildFeedbackHud.Show("Upgrade Started", detail, activeModule != null ? activeModule.icon : null, UITheme.AccentAmber);
                    }
                    else
                    {
                        BuildFeedbackHud.Show("Upgrade Unavailable", startReason, module != null ? module.icon : null, UITheme.AccentAmber);
                    }
                    refresh?.Invoke();
                }, UITheme.AccentAmber);
                start.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
                start.SetEnabled(canStart);
                panel.Add(start);

                if (!canStart && !string.IsNullOrWhiteSpace(reason))
                {
                    var hint = UITheme.Muted(reason);
                    hint.style.marginTop = 7;
                    hint.style.whiteSpace = WhiteSpace.Normal;
                    panel.Add(hint);
                }
            }

            float baseSeconds = Mathf.Max(0.1f, station.baseUpgradeSeconds);
            var timingHint = UITheme.Muted(
                $"Installation time: T1 {baseSeconds:0}s · T2 {baseSeconds * 2f:0}s · T3 {baseSeconds * 3f:0}s · " +
                $"T4 {baseSeconds * 4f:0}s · T5/Hazmat {baseSeconds * 5f:0}s.");
            timingHint.style.marginTop = 10;
            timingHint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(timingHint);
            return panel;
        }

        private static VisualElement BuildSlotCard(
            string label,
            ItemContainer container,
            Func<IItemContainer, int, ItemStack, bool, bool, VisualElement> buildSlot,
            bool interactive)
        {
            var column = new VisualElement();
            column.style.alignItems = Align.Center;
            column.style.flexGrow = 1;

            var title = UITheme.Label(label, 9);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 0.7f;
            title.style.color = new StyleColor(UITheme.TextSecondary);
            title.style.marginBottom = 4;
            column.Add(title);

            var slot = buildSlot(container, 0, container.GetSlot(0), false, interactive);
            column.Add(slot);
            return column;
        }

        private static VisualElement BuildFlowArrow()
        {
            var arrow = new Label("›");
            arrow.style.fontSize = 24;
            arrow.style.color = new StyleColor(UITheme.AccentAmber);
            arrow.style.marginTop = 29;
            arrow.style.marginLeft = 2;
            arrow.style.marginRight = 2;
            arrow.pickingMode = PickingMode.Ignore;
            return arrow;
        }

        private static void BuildArmorTelemetry(VisualElement panel, ItemStack armorStack, ArmorItem armor)
        {
            panel.Add(UITheme.Subtitle("Installed Armor Profile"));
            if (armor == null)
            {
                panel.Add(UITheme.Muted("Insert any Crusader armor piece to inspect and improve its installed modules."));
                return;
            }

            var identity = UITheme.StatRow("◈", armor.displayName,
                $"Tier {armor.tier} · {armor.damageReduction * 100f:0}% physical reduction",
                UITheme.AccentGold);
            panel.Add(identity);

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.marginTop = 6;
            grid.style.marginBottom = 2;
            panel.Add(grid);

            foreach (ArmorUpgradeKind kind in Enum.GetValues(typeof(ArmorUpgradeKind)))
            {
                int tier = ArmorUpgrades.GetTier(armorStack, kind);
                var chip = new Label($"{ArmorUpgradeKindInfo.DisplayName(kind)}  T{tier}");
                chip.style.fontSize = 9;
                chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                chip.style.color = new StyleColor(tier > 0 ? UITheme.AccentCyan : UITheme.TextMuted);
                chip.style.backgroundColor = new StyleColor(tier > 0
                    ? new Color(UITheme.AccentCyan.r, UITheme.AccentCyan.g, UITheme.AccentCyan.b, 0.13f)
                    : UITheme.BgSlot);
                chip.style.paddingLeft = 6;
                chip.style.paddingRight = 6;
                chip.style.paddingTop = 3;
                chip.style.paddingBottom = 3;
                chip.style.marginRight = 4;
                chip.style.marginBottom = 4;
                UITheme.Radius(chip, UITheme.ButtonRadius);
                UITheme.Border(chip, 1, tier > 0 ? UITheme.BorderBright : UITheme.BorderSubtle);
                grid.Add(chip);
            }

            var hazmat = new Label(ArmorUpgrades.HasHazmat(armorStack)
                ? "HAZMAT SEAL · RADIATION IMMUNE"
                : "HAZMAT SEAL · NOT INSTALLED");
            hazmat.style.fontSize = 9;
            hazmat.style.unityFontStyleAndWeight = FontStyle.Bold;
            hazmat.style.color = new StyleColor(ArmorUpgrades.HasHazmat(armorStack) ? UITheme.AccentGreen : UITheme.TextMuted);
            hazmat.style.marginTop = 2;
            panel.Add(hazmat);
        }

        private static void BuildModulePreview(VisualElement panel, ArmorUpgradeStation station, ArmorUpgradeItem module)
        {
            panel.Add(UITheme.Subtitle("Module Preview"));
            if (module == null)
            {
                panel.Add(UITheme.Muted("Insert a crafted module. Higher tiers take longer to install."));
                return;
            }

            string title = module.isHazmat
                ? "Hazmat Module"
                : $"{ArmorUpgradeKindInfo.DisplayName(module.kind)} · Tier {module.InstallationTier}";
            string effect = module.isHazmat
                ? "Applies a permanent radiation-immunity seal to this armor piece."
                : ArmorUpgradeKindInfo.Description(module.kind);

            panel.Add(UITheme.StatRow("◆", title, $"{station.GetUpgradeDuration(module):0}s install", UITheme.AccentAmber));
            var description = UITheme.Muted(effect);
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 3;
            panel.Add(description);
        }

        private static void BuildProgress(VisualElement panel, ArmorUpgradeStation station)
        {
            panel.Add(UITheme.Subtitle("Installation In Progress"));
            var progressRow = new VisualElement();
            progressRow.style.flexDirection = FlexDirection.Row;
            progressRow.style.alignItems = Align.Center;
            progressRow.style.marginTop = 5;
            progressRow.style.marginBottom = 8;

            var (bar, _) = UITheme.ProgressBar(station.Progress01, UITheme.AccentAmber, 9f, flexGrow: true);
            progressRow.Add(bar);

            var time = UITheme.Label($"{station.RemainingSeconds:0.0}s", 11);
            time.style.color = new StyleColor(UITheme.TextPrimary);
            time.style.unityFontStyleAndWeight = FontStyle.Bold;
            time.style.marginLeft = 8;
            progressRow.Add(time);
            panel.Add(progressRow);

            var hint = UITheme.Muted("The anvil process continues while this panel is closed and resumes after a save/load.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(hint);
        }
    }
}
