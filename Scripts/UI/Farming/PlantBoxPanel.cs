// Assets/Scripts/VoxelEngine/UI/Farming/PlantBoxPanel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   PLANT BOX / PLANT BED / AUTO FARM PLOT PANEL (18.7.0-dev)     ║
// ║   One panel for every planting block: seed bin in, harvest bin  ║
// ║   out, one chip per cell, water, power and the re-seed toggle.  ║
// ║                                                                  ║
// ║   18.7.1-dev: rebuilt as a live page. The readouts are written   ║
// ║   in place five times a second, so a growing crop reads as a     ║
// ║   moving number instead of a frozen one, and the panel is never  ║
// ║   torn down under the player's cursor to achieve it.            ║
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
    /// Live element handles for an open planting-box page. Built by
    /// <see cref="PlantBoxPanel.BuildPanel"/> and written in place by
    /// <see cref="PlantBoxPanel.Tick"/>, the same discipline the Distillation Plant and
    /// the Catalytic Cracker use: a rebuild would hand the player a brand new page
    /// every quarter second, which resets the scroll and eats a click.
    /// </summary>
    public sealed class PlantBoxPanelLive
    {
        public VisualElement StatusPill;
        public Label StatusLabel;
        public VisualElement WaterFill;
        public Label WaterValue;
        public VisualElement GrowthFill;
        public Label GrowthValue;
        public Label CropValue;
        public Label ReadyValue;
        public Label PowerValue;
        public Label AirValue;
        public Label SupplyValue;
        public Label ReseedValue;
        public Label[] CellName = new Label[0];
        public Label[] CellPercent = new Label[0];
        public VisualElement[] CellChip = new VisualElement[0];
    }

    /// <summary>
    /// The planting-box surface. A farm block used to answer a right-click with a toast
    /// and nothing else, which is not a UI a player can work with: there was no way to
    /// load a seed bin, empty a harvest bin, or read a single cell without walking around
    /// the bed swinging a canister.
    ///
    /// One page covers all six blocks - the three hand-fed boxes and the three Auto Farm
    /// Plots - because all six run on the same PlantBox component. The Large bed gets the
    /// paragraph about the tree, since that is the only bed a sapling will take root in.
    /// </summary>
    public static class PlantBoxPanel
    {
        private static readonly Color PlantGreen = new Color(0.34f, 0.78f, 0.36f, 1f);
        private static readonly Color WaterBlue = new Color(0.28f, 0.60f, 0.95f, 1f);
        private static readonly Color TimberBrown = new Color(0.62f, 0.46f, 0.24f, 1f);

        public static VisualElement BuildPanel(PlantBox box, MachineUIs.SlotBuilder slot, out PlantBoxPanelLive live)
        {
            live = new PlantBoxPanelLive();
            if (box == null) return T.MachinePanel();

            box.EnsureContainers();
            box.EnsureSlots();

            var p = T.MachinePanel();
            p.style.width = 430;

            var (hdr, _, pill, pillLabel) = T.HeaderRow(box.BedName, box.StateLabel, StatusColor(box));
            live.StatusPill = pill;
            live.StatusLabel = pillLabel;
            p.Add(hdr);
            p.Add(IndustrialTheme.HazardDivider());
            p.Add(IndustrialTheme.Lamps(box.PowerMissing ? 0 : box.IsReady ? 2 : 1));

            // The body scrolls inside a fixed panel, so a tall bed never pushes the header
            // out of the way - and because the numbers are written in place, scrolling this
            // list does not jump back to the top five times a second.
            var body = new ScrollView(ScrollViewMode.Vertical) { name = "PlantBoxBody" };
            body.style.flexGrow = 1;
            body.style.marginTop = 4;
            T.StyleScroller(body);
            p.Add(body);

            // ── Levels ─────────────────────────────────────────────────────────
            live.WaterValue = LevelBar(body, "Water", WaterBlue, out live.WaterFill);
            live.GrowthValue = LevelBar(body, box.TreeBed ? "Timber" : "Growth",
                box.TreeBed ? TimberBrown : PlantGreen, out live.GrowthFill);
            body.Add(T.Spacer(4));

            var rows = T.Card();
            rows.style.marginTop = 2;
            rows.Add(LiveRow("🌱", "Crop", out live.CropValue));
            rows.Add(LiveRow("🌾", "Ready", out live.ReadyValue));
            rows.Add(LiveRow("⚡", "Power", out live.PowerValue));
            rows.Add(LiveRow("💧", "Water Supply", out live.SupplyValue));
            rows.Add(LiveRow("🌬", "Air", out live.AirValue));
            rows.Add(LiveRow("🔁", "Self Re-seeding", out live.ReseedValue));
            body.Add(rows);
            body.Add(T.Spacer(6));

            // ── Switches ───────────────────────────────────────────────────────
            var toggles = Row();
            var reseedBtn = T.SmallButton(
                box.replantAfterPortHarvest ? "Self Re-seeding: ON" : "Self Re-seeding: OFF",
                () =>
                {
                    box.replantAfterPortHarvest = !box.replantAfterPortHarvest;
                    GameUIController.Instance?.RequestRefresh();
                },
                box.replantAfterPortHarvest ? T.AccentGreen : T.BgSlot);
            reseedBtn.style.flexGrow = 1;
            reseedBtn.style.marginRight = 4;
            toggles.Add(reseedBtn);

            var dumpBtn = T.SmallButton(
                box.autoDumpWhenRipe ? "Auto Output: ON" : "Auto Output: OFF",
                () =>
                {
                    box.autoDumpWhenRipe = !box.autoDumpWhenRipe;
                    GameUIController.Instance?.RequestRefresh();
                },
                box.autoDumpWhenRipe ? T.AccentCyan : T.BgSlot);
            dumpBtn.style.flexGrow = 1;
            toggles.Add(dumpBtn);
            body.Add(toggles);
            body.Add(T.Spacer(4));

            // Both actions are always here, tinted by what they can do right now. A button
            // that appears only when a crop ripens would be invisible until the page was
            // rebuilt, and the whole point of 18.7.1-dev is that the page stays put.
            var actions = Row();
            var harvestBtn = T.SmallButton(
                box.ReadyCount > 0 ? $"Harvest {box.ReadyCount} into Bin" : "Harvest into Bin",
                () =>
                {
                    box.HarvestAllToOutput();
                    GameUIController.Instance?.RefreshCurrentPanel();
                }, box.ReadyCount > 0 ? T.AccentGreen : T.BgSlot);
            harvestBtn.style.flexGrow = 1;
            harvestBtn.style.marginRight = 4;
            actions.Add(harvestBtn);

            var clearBtn = T.SmallButton(
                box.DeadCount > 0 ? $"Clear {box.DeadCount} Lost" : "Clear Lost Crops",
                () =>
                {
                    box.ClearAllDead();
                    GameUIController.Instance?.RefreshCurrentPanel();
                }, box.DeadCount > 0 ? T.AccentRed : T.BgSlot);
            clearBtn.style.flexGrow = 1;
            actions.Add(clearBtn);
            body.Add(actions);
            body.Add(T.Spacer(6));

            // ── Cells: a chip each, not nine stacked rows ───────────────────────
            body.Add(GridUIHelpers.SectionTitle(box.TreeBed ? "Tree Bed" : "Cells"));
            if (box.TreeBed)
            {
                body.Add(T.StatRow("🌳", "Standing in",
                    box.treeCell >= 0 ? $"Cell #{box.treeCell + 1} - every other cell locked" : "Cell #1",
                    T.AccentAmber));
                body.Add(T.Muted("A tree fills the whole bed from its own square, so the other cells are locked until it is felled. Anything already growing in them was harvested into the bin. It comes down as timber, saplings and biomass in one harvest."));
                body.Add(T.Spacer(4));
            }
            else
            {
                int cells = box.CellCount;
                box.EnsureCellInputs();
                var chips = Row();
                chips.style.flexWrap = Wrap.Wrap;
                live.CellName = new Label[cells];
                live.CellPercent = new Label[cells];
                live.CellChip = new VisualElement[cells];
                for (int i = 0; i < cells; i++)
                {
                    var chip = CellChip(i, out var nameLabel, out var percentLabel);
                    live.CellChip[i] = chip;
                    live.CellName[i] = nameLabel;
                    live.CellPercent[i] = percentLabel;

                    // One one-slot bin per cell: drop a seed here and it is planted in THIS
                    // square. A cell locked by a tree shows a tag instead of a slot, so an
                    // offer that cannot be accepted is never presented as one.
                    if (box.IsCellLocked(i))
                    {
                        var locked = new Label("LOCKED");
                        locked.style.color = new StyleColor(T.AccentAmber);
                        locked.style.fontSize = 8;
                        locked.style.unityFontStyleAndWeight = FontStyle.Bold;
                        locked.style.marginTop = 3;
                        locked.pickingMode = PickingMode.Ignore;
                        chip.Add(locked);
                    }
                    else
                    {
                        var cellBin = box.CellInput(i);
                        if (cellBin != null)
                        {
                            var cellSlot = slot(cellBin, 0, cellBin.GetSlot(0), false, true);
                            cellSlot.style.marginTop = 3;
                            chip.Add(cellSlot);
                        }
                    }
                    chips.Add(chip);
                }
                body.Add(chips);
                body.Add(T.Spacer(2));
                body.Add(T.Muted("Drop a seed into a cell's slot to plant that square, or fill the seed bin below and let the bed fill itself."));
                body.Add(T.Spacer(4));
            }

            // ── Bins ───────────────────────────────────────────────────────────
            body.Add(GridUIHelpers.SectionTitle("Seeds - planted automatically"));
            if (box.seedInput != null)
            {
                body.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(box.seedInput), "Seeds"));
                var seedGrid = T.SlotGrid(box.seedInput.Size);
                for (int i = 0; i < box.seedInput.Size; i++)
                    seedGrid.Add(slot(box.seedInput, i, box.seedInput.GetSlot(i), false, true));
                body.Add(seedGrid);
            }
            body.Add(T.Spacer(6));

            body.Add(GridUIHelpers.SectionTitle("Harvest - also pushed to a connected belt"));
            if (box.harvestOutput != null)
            {
                body.Add(GridUIHelpers.WeightHeader(MassUtil.ContainerMass(box.harvestOutput), "Harvest"));
                var outGrid = T.SlotGrid(box.harvestOutput.Size);
                for (int i = 0; i < box.harvestOutput.Size; i++)
                    outGrid.Add(slot(box.harvestOutput, i, box.harvestOutput.GetSlot(i), false, true));
                body.Add(outGrid);
            }
            body.Add(T.Spacer(6));

            body.Add(T.Muted(box.AllowsTrees
                ? "Seeds or a sapling in the bin fill the bed on their own. Only this size takes a tree, and a tree uses the whole bed. Water comes from a Sprinkler or from a canister by hand."
                : "Seeds in the bin fill the bed on their own. Trees need the Large bed - this size grows crops only. Water comes from a Sprinkler or from a canister by hand."));

            IndustrialTheme.Frame(p);
            Tick(box, live);
            return p;
        }

        /// <summary>
        /// Write the live readouts in place. Called every frame by the UI controller while
        /// the page is open; costs a score of label writes and rebuilds nothing.
        /// </summary>
        public static void Tick(PlantBox box, PlantBoxPanelLive live)
        {
            if (box == null || live == null) return;

            if (live.StatusLabel != null) live.StatusLabel.text = box.StateLabel;
            if (live.StatusPill != null)
            {
                Color status = StatusColor(box);
                live.StatusPill.style.backgroundColor = new StyleColor(
                    new Color(status.r, status.g, status.b, 0.22f));
            }

            float waterFill = box.waterCapacity > 0f
                ? Mathf.Clamp01(box.waterStored / box.waterCapacity)
                : 0f;
            SetFill(live.WaterFill, waterFill);
            if (live.WaterValue != null)
                live.WaterValue.text = $"{box.waterStored:0} / {box.waterCapacity:0} L";

            float growth = Mathf.Clamp01(box.Growth01);
            SetFill(live.GrowthFill, growth);
            if (live.GrowthValue != null)
                live.GrowthValue.text = $"{growth * 100f:0}% · {box.ReadyCount} ready";

            if (live.CropValue != null)
            {
                live.CropValue.text = box.IsEmpty ? "Nothing planted" : box.CropName;
                live.CropValue.style.color = new StyleColor(box.IsEmpty ? T.TextMuted : PlantGreen);
            }
            if (live.ReadyValue != null)
                live.ReadyValue.text = $"{box.ReadyCount} of {box.CellCount}";

            if (live.PowerValue != null)
            {
                if (box.powerDraw <= 0f)
                {
                    live.PowerValue.text = "Not required";
                    live.PowerValue.style.color = new StyleColor(T.TextMuted);
                }
                else
                {
                    live.PowerValue.text = PowerFormat.Watts(box.powerDraw) + (box.PowerMissing ? " · NO POWER" : "");
                    live.PowerValue.style.color = new StyleColor(box.PowerMissing ? T.AccentRed : T.AccentGreen);
                }
            }

            if (live.SupplyValue != null)
            {
                live.SupplyValue.text = box.HasSprinkler ? "Sprinkler feeding"
                    : waterFill > 0.01f ? "Tank · watered by hand"
                    : "Empty - fill or fit a Sprinkler";
                live.SupplyValue.style.color = new StyleColor(box.IsIrrigated ? T.AccentGreen : T.AccentAmber);
            }

            if (live.AirValue != null)
            {
                live.AirValue.text = box.HasAir ? "Breathable" : "None · oxygen crops paused";
                live.AirValue.style.color = new StyleColor(box.HasAir ? T.AccentGreen : T.AccentAmber);
            }

            if (live.ReseedValue != null)
            {
                live.ReseedValue.text = box.replantAfterPortHarvest ? "On · keeps a seed back" : "Off · bed empties";
                live.ReseedValue.style.color = new StyleColor(box.replantAfterPortHarvest ? T.AccentGreen : T.TextMuted);
            }

            for (int i = 0; i < live.CellPercent.Length; i++)
            {
                var cell = box.CellAt(i);
                if (live.CellPercent[i] == null) continue;

                string name = cell == null ? "-"
                    : cell.dead ? "Lost"
                    : cell.tree ? "Timber"
                    : cell.crop != null ? cell.crop.cropName
                    : "Empty";
                float cellGrowth = cell != null ? Mathf.Clamp01(cell.growth) : 0f;

                if (live.CellName[i] != null) live.CellName[i].text = name;
                live.CellPercent[i].text = cell != null && (cell.crop != null || cell.tree)
                    ? $"{cellGrowth * 100f:0}%"
                    : "-";

                Color tint = cell == null || (cell.crop == null && !cell.tree) ? T.TextMuted
                    : cell.dead ? T.AccentRed
                    : cellGrowth >= 1f ? T.AccentGreen
                    : T.AccentCyan;
                live.CellPercent[i].style.color = new StyleColor(tint);
            }
        }

        private static Color StatusColor(PlantBox box)
        {
            string status = box.StateLabel;
            if (status == "NO POWER" || status == "DEAD" || status == "NO O2") return T.AccentRed;
            if (status == "DRY") return T.AccentAmber;
            if (status == "EMPTY") return T.TextMuted;
            if (box.IsReady) return T.AccentGreen;
            return PlantGreen;
        }

        private static void SetFill(VisualElement fill, float t)
        {
            if (fill == null) return;
            fill.style.width = new StyleLength(new Length(Mathf.Clamp01(t) * 100f, LengthUnit.Percent));
        }

        /// <summary>Caption on the left, live value on the right, thin bar underneath.</summary>
        private static Label LevelBar(VisualElement parent, string caption, Color color, out VisualElement fill)
        {
            var col = new VisualElement();
            col.style.marginBottom = 6;

            var row = Row();
            var cap = new Label(caption.ToUpperInvariant());
            cap.style.color = new StyleColor(T.TextMuted);
            cap.style.fontSize = 9;
            cap.style.unityFontStyleAndWeight = FontStyle.Bold;
            cap.style.letterSpacing = 1.2f;
            cap.style.flexGrow = 1;
            cap.pickingMode = PickingMode.Ignore;
            row.Add(cap);

            var value = new Label(string.Empty);
            value.style.color = new StyleColor(T.TextPrimary);
            value.style.fontSize = 10;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.pickingMode = PickingMode.Ignore;
            row.Add(value);

            col.Add(row);
            var (bar, fillEl) = T.ProgressBar(0f, color, 8, true);
            col.Add(bar);
            parent.Add(col);

            fill = fillEl;
            return value;
        }

        /// <summary>Icon, caption and a right-aligned live value - the panel's stat line.</summary>
        private static VisualElement LiveRow(string icon, string caption, out Label value)
        {
            var row = Row();
            row.style.marginBottom = 5;
            row.style.minHeight = 16;
            row.pickingMode = PickingMode.Ignore;

            var ico = new Label(icon);
            ico.style.fontSize = 11;
            ico.style.marginRight = 5;
            ico.style.width = 16;
            ico.pickingMode = PickingMode.Ignore;
            row.Add(ico);

            var cap = new Label(caption);
            cap.style.color = new StyleColor(T.TextSecondary);
            cap.style.fontSize = 10;
            cap.style.flexGrow = 1;
            cap.pickingMode = PickingMode.Ignore;
            row.Add(cap);

            value = new Label(string.Empty);
            value.style.color = new StyleColor(T.TextPrimary);
            value.style.fontSize = 10;
            value.style.unityFontStyleAndWeight = FontStyle.Bold;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.pickingMode = PickingMode.Ignore;
            row.Add(value);
            return row;
        }

        /// <summary>
        /// One cell as a compact chip: number and crop on top, growth underneath. Nine of
        /// these read as a bed at a glance; nine full-width rows read as a spreadsheet.
        /// </summary>
        private static VisualElement CellChip(int index, out Label name, out Label percent)
        {
            var chip = new VisualElement();
            chip.style.width = new StyleLength(new Length(31f, LengthUnit.Percent));
            chip.style.marginRight = new StyleLength(new Length(2.5f, LengthUnit.Percent));
            chip.style.marginBottom = 4;
            chip.style.paddingTop = 5;
            chip.style.paddingBottom = 5;
            chip.style.paddingLeft = 6;
            chip.style.paddingRight = 6;
            chip.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(chip, 4);
            T.Border(chip, 1, T.BorderDim);
            chip.pickingMode = PickingMode.Ignore;

            var head = Row();
            var num = new Label("#" + (index + 1));
            num.style.color = new StyleColor(T.TextMuted);
            num.style.fontSize = 9;
            num.style.unityFontStyleAndWeight = FontStyle.Bold;
            num.style.flexGrow = 1;
            num.pickingMode = PickingMode.Ignore;
            head.Add(num);

            percent = new Label("-");
            percent.style.color = new StyleColor(T.TextMuted);
            percent.style.fontSize = 9;
            percent.style.unityFontStyleAndWeight = FontStyle.Bold;
            percent.pickingMode = PickingMode.Ignore;
            head.Add(percent);
            chip.Add(head);

            name = new Label("Empty");
            name.style.color = new StyleColor(T.TextPrimary);
            name.style.fontSize = 9;
            name.style.marginTop = 1;
            name.style.overflow = Overflow.Hidden;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.pickingMode = PickingMode.Ignore;
            chip.Add(name);
            return chip;
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
