// Assets/Scripts/VoxelEngine/UI/Farming/PlantBoxPanel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   PLANT BOX / PLANT BED / AUTO FARM PLOT PANEL (18.7.0-dev)     ║
// ║   One panel for every planting block: seed bin in, harvest bin  ║
// ║   out, a row per cell, water, power and the re-seed toggle.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Farming;
using VoxelEngine.GridSystem.UI;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>
    /// The planting-box surface. A farm block used to answer a right-click with a toast
    /// and nothing else, which is not a UI a player can work with: there was no way to
    /// load a seed bin, empty a harvest bin, or read a single cell without walking around
    /// the bed swinging a canister. This panel is that surface.
    ///
    /// It is written for all three sizes and both families (the hand-fed boxes and the
    /// powered Auto Farm Plots), because both run on the same PlantBox component. The
    /// Large bed gets the extra paragraph about the tree, since that is the only place a
    /// sapling will take root.
    /// </summary>
    public static class PlantBoxPanel
    {
        public static VisualElement BuildPanel(PlantBox box, MachineUIs.SlotBuilder slot)
        {
            if (box == null) return T.MachinePanel();
            box.EnsureContainers();
            box.EnsureSlots();

            var p = T.MachinePanel();
            p.style.width = 470;

            Color plant = new Color(0.34f, 0.78f, 0.36f, 1f);
            string status = box.StateLabel;
            Color statusColor =
                status == "NO POWER" ? T.AccentRed
                : status == "DEAD" || status == "NO O2" ? T.AccentRed
                : status == "DRY" ? T.AccentAmber
                : status == "EMPTY" ? T.TextMuted
                : box.IsReady ? T.AccentGreen
                : plant;

            var (hdr, _, _, _) = T.HeaderRow(box.BedName, status, statusColor);
            p.Add(hdr);
            p.Add(IndustrialTheme.HazardDivider());
            p.Add(IndustrialTheme.Lamps(box.PowerMissing ? 0 : box.IsReady ? 2 : 1));

            // ── Gauges: water and, on a tree bed, the tree instead of the cell count ──
            var gauges = Row();
            gauges.style.justifyContent = Justify.SpaceAround;
            gauges.Add(T.TankGauge("Water", box.Water01, new Color(0.25f, 0.55f, 0.95f),
                $"{box.waterStored:0}/{box.waterCapacity:0} L", 60, 96));
            if (box.TreeBed)
            {
                gauges.Add(T.TankGauge("Timber", box.TreeGrowth01, new Color(0.55f, 0.40f, 0.22f),
                    $"{box.TreeGrowth01 * 100f:0}%", 60, 96));
            }
            else
            {
                gauges.Add(T.TankGauge("Beds", box.CellsFill01, plant,
                    $"{box.PlantedCount}/{box.CellCount} planted", 60, 96));
            }
            p.Add(gauges);
            p.Add(T.Spacer(6));

            var (bar, _) = T.ProgressBar(box.Growth01,
                box.TreeBed ? new Color(0.55f, 0.40f, 0.22f) : plant, 8);
            p.Add(bar);
            p.Add(T.Spacer(6));

            // ── State rows ──────────────────────────────────────────────────────
            p.Add(T.StatRow("🌱", "Crop", box.IsEmpty ? "Nothing planted" : box.CropName,
                box.IsEmpty ? T.TextMuted : plant));
            p.Add(T.StatRow("🌾", "Ready", $"{box.ReadyCount} of {box.CellCount}", T.AccentCyan));
            if (box.DeadCount > 0)
                p.Add(T.StatRow("☠", "Lost Crops", box.DeadCount + " - clear before replanting", T.AccentRed));

            if (box.powerDraw > 0f)
            {
                p.Add(T.StatRow("⚡", "Power Draw", PowerFormat.Watts(box.powerDraw),
                    box.PowerMissing ? T.AccentRed : T.AccentGreen));
            }
            else
            {
                p.Add(T.StatRow("⚡", "Power", "Not required - this bed runs by hand", T.TextMuted));
            }

            p.Add(T.StatRow("💧", "Irrigation",
                box.HasSprinkler ? "Sprinkler feeding the bed"
                    : box.Water01 > 0.01f ? $"{box.Water01 * 100f:0}% in the tank - watered by hand"
                    : "Dry - add water or fit a Sprinkler",
                box.IsIrrigated ? T.AccentGreen : T.AccentAmber));
            p.Add(T.StatRow("🌬", "Air",
                box.HasAir ? "Breathable" : "No oxygen - oxygen crops paused",
                box.HasAir ? T.AccentGreen : T.AccentAmber));
            p.Add(T.StatRow("🔁", "Self Re-seeding",
                box.replantAfterPortHarvest ? "On - one seed kept back per harvest" : "Off - bed empties when harvested",
                box.replantAfterPortHarvest ? T.AccentGreen : T.TextMuted));
            p.Add(T.Spacer(4));

            // ── Toggles ────────────────────────────────────────────────────────
            var toggles = Row();
            var reseedBtn = T.SmallButton(
                box.replantAfterPortHarvest ? "Self Re-seeding: ON" : "Self Re-seeding: OFF",
                () =>
                {
                    box.replantAfterPortHarvest = !box.replantAfterPortHarvest;
                    GameUIController.Instance?.RequestRefresh();
                },
                box.replantAfterPortHarvest ? T.AccentGreen : T.BgSlot);
            reseedBtn.style.marginRight = 6;
            toggles.Add(reseedBtn);
            var dumpBtn = T.SmallButton(
                box.autoDumpWhenRipe ? "Auto Output: ON" : "Auto Output: OFF",
                () =>
                {
                    box.autoDumpWhenRipe = !box.autoDumpWhenRipe;
                    GameUIController.Instance?.RequestRefresh();
                },
                box.autoDumpWhenRipe ? T.AccentCyan : T.BgSlot);
            toggles.Add(dumpBtn);
            p.Add(toggles);
            p.Add(T.Spacer(4));

            var actions = Row();
            if (box.ReadyCount > 0)
            {
                var harvestBtn = T.SmallButton("Harvest into Bin", () =>
                {
                    box.HarvestAllToOutput();
                    GameUIController.Instance?.RequestRefresh();
                }, T.AccentGreen);
                harvestBtn.style.marginRight = 6;
                actions.Add(harvestBtn);
            }
            if (box.DeadCount > 0)
            {
                var clearBtn = T.SmallButton("Clear Lost Crops", () =>
                {
                    box.ClearAllDead();
                    GameUIController.Instance?.RequestRefresh();
                }, T.AccentRed);
                clearBtn.style.marginRight = 6;
                actions.Add(clearBtn);
            }
            if (actions.childCount > 0) p.Add(actions);

            // ── Cells ──────────────────────────────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Cells"));
            if (box.TreeBed)
            {
                p.Add(T.StatRow("🌳", "Tree Bed",
                    box.ReadyCount > 0 ? "Ready to fell - fills the whole bed"
                        : $"Growing - {box.TreeGrowth01 * 100f:0}%",
                    box.ReadyCount > 0 ? T.AccentGreen : T.AccentAmber));
                p.Add(T.Muted("A tree claims the whole bed. No crop cells are used while it grows, and it comes down as timber, saplings and biomass in one harvest."));
            }
            else
            {
                for (int i = 0; i < box.CellCount; i++)
                {
                    var cell = box.CellAt(i);
                    string name = cell == null ? "-"
                        : cell.dead ? "Lost crop"
                        : cell.tree ? "Timber"
                        : cell.crop != null ? cell.crop.cropName
                        : "Empty";
                    float growth = cell != null ? Mathf.Clamp01(cell.growth) : 0f;
                    Color tint = cell == null || (cell.crop == null && !cell.tree) ? T.TextMuted
                        : cell.dead ? T.AccentRed
                        : growth >= 1f ? T.AccentGreen
                        : T.AccentCyan;
                    p.Add(T.StatRow("#" + (i + 1), name,
                        cell != null && (cell.crop != null || cell.tree) ? $"{growth * 100f:0}%" : "-", tint));
                }
            }
            p.Add(T.Spacer(6));

            // ── Seed bin ───────────────────────────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Seeds (planted automatically)"));
            if (box.seedInput != null)
            {
                p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(box.seedInput), "Seeds"));
                var seedGrid = T.SlotGrid(box.seedInput.Size);
                for (int i = 0; i < box.seedInput.Size; i++)
                    seedGrid.Add(slot(box.seedInput, i, box.seedInput.GetSlot(i), false, true));
                p.Add(seedGrid);
            }
            p.Add(T.Spacer(6));

            // ── Harvest bin ────────────────────────────────────────────────────
            p.Add(GridUIHelpers.SectionTitle("Harvest (also pushed to a connected belt)"));
            if (box.harvestOutput != null)
            {
                p.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(box.harvestOutput), "Harvest"));
                var outGrid = T.SlotGrid(box.harvestOutput.Size);
                for (int i = 0; i < box.harvestOutput.Size; i++)
                    outGrid.Add(slot(box.harvestOutput, i, box.harvestOutput.GetSlot(i), false, true));
                p.Add(outGrid);
            }
            p.Add(T.Spacer(4));

            p.Add(T.Muted(box.AllowsTrees
                ? "Drop seeds or a sapling in the bin and the bed fills itself. Only this size takes a tree, and a tree uses the whole bed. Water comes from a Sprinkler or a canister by hand."
                : "Drop seeds in the bin and the bed fills itself. Trees need the Large bed - this size grows crops only. Water comes from a Sprinkler or a canister by hand."));

            IndustrialTheme.Frame(p);
            return p;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }
    }
}
