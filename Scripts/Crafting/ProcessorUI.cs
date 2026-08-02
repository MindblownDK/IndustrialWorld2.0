// Assets/Scripts/VoxelEngine/Crafting/ProcessorUI.cs
//
// UI panels for the stationary fluid processors — Oil Refinery and Chemical
// Plant. Shows item slots, internal fluid tanks (gauge + Contents/Capacity),
// the active recipe + progress, and per-tank drain buttons.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Industrial;
using VoxelEngine.Items;
using VoxelEngine.UI;
using GUI = VoxelEngine.GridSystem.UI.GridUIHelpers;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Crafting
{
    public static class ProcessorUI
    {
        public static VisualElement OilRefineryPanel(OilRefinery m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            var p = BuildShell("⚗ Oil Refinery", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);

            FluidRow(p, new[] { m.fluidIn, m.fluidOut });
            ItemSlots(p, "Inputs", m.inputC, slot);
            ItemSlots(p, "Outputs", m.outputC, slot);
            UpgradeSlots(p, "Upgrades", m.upgradeC, slot);
            RecipeBook(p, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); });
            return p;
        }

        public static VisualElement ChemicalPlantPanel(StationaryChemicalPlant m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            var p = BuildShell("🧪 Chemical Plant", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);

            FluidRow(p, new[] { m.fluidIn, m.fluidOut });
            ItemSlots(p, "Inputs", m.inputC, slot);
            ItemSlots(p, "Outputs", m.outputC, slot);
            RecipeBook(p, m.knownRecipes, m.Current, m.selectedRecipe,
                rec => { m.selectedRecipe = rec; VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(); });
            return p;
        }

        // ── shared building blocks ──────────────────────────────────────────────
        private static VisualElement BuildShell(string title, bool online,
            ProcessingRecipe current, float progress01, float watts)
        {
            var p = T.MachinePanel();
            p.style.width = 470;
            var (hdr, _, _, _) = T.HeaderRow(title,
                !online ? "NO POWER" : current != null ? "PROCESSING" : "IDLE",
                !online ? T.AccentRed : current != null ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⚡", "Power Use", PowerFormat.Watts(watts), T.AccentGold));
            if (current != null)
            {
                p.Add(T.StatRow("⚙", "Recipe", current.GetDisplayName(), T.AccentCyan));
                var (bar, _) = T.ProgressBar(progress01, T.AccentGreen, 8, true);
                p.Add(bar);
            }
            p.Add(T.Spacer(6));
            return p;
        }

        private static void FluidRow(VisualElement p, MachineFluidTank[] tanks)
        {
            p.Add(GUI.SectionTitle("Fluid Tanks"));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceAround;
            foreach (var t in tanks)
            {
                if (t == null) continue;
                var col = new VisualElement();
                col.style.alignItems = Align.Center;
                col.Add(T.TankGauge(t.liquid.DisplayName(), t.Fill01, t.liquid.Color(),
                    $"{t.stored:0}/{t.capacity:0} L", 64, 100));
                col.Add(T.SmallButton("⊘ Drain", () => t.Drain(), T.AccentRed));
                row.Add(col);
            }
            p.Add(row);
            p.Add(T.Spacer(6));
        }

        private static void ItemSlots(VisualElement p, string label, ItemContainer c, MachineUIs.SlotBuilder slot)
        {
            if (c == null) return;
            p.Add(GUI.SectionTitle(label));
            p.Add(GUI.WeightHeader(MassUtil.ContainerMass(c)));
            var grid = T.SlotGrid(c.Size);
            for (int i = 0; i < c.Size; i++) grid.Add(slot(c, i, c.GetSlot(i), false, true));
            p.Add(grid);
        }

        private static void UpgradeSlots(VisualElement p, string label, ItemContainer c, MachineUIs.SlotBuilder slot)
        {
            if (c == null) return;
            p.Add(GUI.SectionTitle(label));
            var grid = T.SlotGrid(c.Size);
            for (int i = 0; i < c.Size; i++) grid.Add(slot(c, i, c.GetSlot(i), false, true));
            p.Add(grid);
        }

        private static void RecipeBook(VisualElement p, List<ProcessingRecipe> recipes,
            ProcessingRecipe current, ProcessingRecipe selected, System.Action<ProcessingRecipe> onSelect)
        {
            if (recipes == null) return;
            p.Add(GUI.SectionTitle("Recipes  (click to select · Auto by default)"));

            // "Auto" option clears the lock.
            p.Add(RecipeRow("⟳  Auto (first available)", "", null, default, selected == null, current != null && selected == null,
                () => onSelect(null)));

            foreach (var r in recipes)
            {
                if (r == null) continue;
                var captured = r;
                // Icon: first item output's sprite, tinted chip as fallback.
                Sprite rIcon = null; Color rTint = T.TextMuted;
                if (r.outputs != null && r.outputs.Length > 0 && r.outputs[0].item != null)
                {
                    rIcon = r.outputs[0].item.icon;
                    rTint = r.outputs[0].item.iconTint;
                }
                p.Add(RecipeRow(r.GetDisplayName(), Summary(r), rIcon, rTint, selected == r, current == r,
                    () => onSelect(captured)));
            }
        }

        private static VisualElement RecipeRow(string name, string summary, Sprite icon, Color iconTint, bool selected, bool active, System.Action onClick)
        {
            var btn = new Button(onClick);
            btn.style.flexDirection = FlexDirection.Column;
            btn.style.alignItems = Align.FlexStart;
            btn.style.marginBottom = 2; btn.style.paddingTop = 4; btn.style.paddingBottom = 4;
            btn.style.paddingLeft = 8;
            btn.style.backgroundColor = new StyleColor(selected
                ? new Color(0.18f, 0.72f, 0.88f, 0.28f)
                : new Color(0.12f, 0.14f, 0.18f, 0.95f));
            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.pickingMode = PickingMode.Ignore;
            if (icon != null || iconTint != default)
            {
                var iconSlot = new VisualElement();
                iconSlot.style.width = 26; iconSlot.style.height = 26;
                iconSlot.style.marginRight = 7;
                iconSlot.style.alignItems = Align.Center;
                iconSlot.style.justifyContent = Justify.Center;
                iconSlot.style.backgroundColor = new StyleColor(new Color(0.09f, 0.10f, 0.13f, 0.9f));
                T.Radius(iconSlot, 4);
                iconSlot.pickingMode = PickingMode.Ignore;
                if (icon != null)
                {
                    var iconImg = new Image { sprite = icon };
                    iconImg.scaleMode = ScaleMode.ScaleToFit;
                    iconImg.style.width = 22; iconImg.style.height = 22;
                    iconImg.pickingMode = PickingMode.Ignore;
                    iconSlot.Add(iconImg);
                }
                else
                {
                    var chip = new VisualElement();
                    chip.style.width = 16; chip.style.height = 16;
                    chip.style.backgroundColor = new StyleColor(iconTint);
                    T.Radius(chip, 3);
                    chip.pickingMode = PickingMode.Ignore;
                    iconSlot.Add(chip);
                }
                titleRow.Add(iconSlot);
            }
            var title = new Label((selected ? "◉ " : "○ ") + name);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(active ? T.AccentGreen : selected ? T.AccentCyan : new Color(0.85f,0.88f,0.92f));
            title.pickingMode = PickingMode.Ignore;
            titleRow.Add(title);
            btn.Add(titleRow);
            if (!string.IsNullOrEmpty(summary))
            {
                var sub = new Label(summary);
                sub.style.fontSize = 10; sub.style.color = new StyleColor(new Color(0.6f,0.64f,0.7f));
                btn.Add(sub);
            }
            return btn;
        }

        private static string Summary(ProcessingRecipe r)
        {
            var ins = new List<string>();
            if (r.HasFluidInputs) foreach (var f in r.fluidInputs) ins.Add($"{f.litres:0}L {f.liquid.DisplayName()}");
            if (r.HasItemInputs)  foreach (var i in r.inputs) if (i.item != null) ins.Add($"{i.count} {i.item.displayName}");
            var outs = new List<string>();
            if (r.HasFluidOutputs) foreach (var f in r.fluidOutputs) outs.Add($"{f.litres:0}L {f.liquid.DisplayName()}");
            if (r.HasItemOutputs)  foreach (var o in r.outputs) if (o.item != null) outs.Add($"{o.count} {o.item.displayName}");
            return string.Join(" + ", ins) + " → " + string.Join(" + ", outs);
        }
    }
}
