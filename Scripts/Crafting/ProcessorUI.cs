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
            RecipeBook(p, m.knownRecipes, m.Current);
            return p;
        }

        public static VisualElement ChemicalPlantPanel(StationaryChemicalPlant m, MachineUIs.SlotBuilder slot)
        {
            m.EnsureContainers();
            var p = BuildShell("🧪 Chemical Plant", m.IsOnline, m.Current, m.Progress01, m.CurrentWattage);

            FluidRow(p, new[] { m.fluidIn, m.fluidOut });
            ItemSlots(p, "Inputs", m.inputC, slot);
            ItemSlots(p, "Outputs", m.outputC, slot);
            RecipeBook(p, m.knownRecipes, m.Current);
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

            p.Add(T.StatRow("⚡", "Power Use", $"{watts:0} W", T.AccentGold));
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

        private static void RecipeBook(VisualElement p, List<ProcessingRecipe> recipes, ProcessingRecipe current)
        {
            if (recipes == null) return;
            p.Add(GUI.SectionTitle("Recipes"));
            foreach (var r in recipes)
            {
                if (r == null) continue;
                p.Add(T.StatRow("•", r.GetDisplayName(), Summary(r),
                    current == r ? T.AccentGreen : (Color?)null));
            }
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
