// Assets/Scripts/VoxelEngine/UI/MachineUIs.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      MACHINE UI PANELS — All in-game machines, unified theme   ║
// ║   Each panel: icon badge + header + accent divider + content.  ║
// ║   Built entirely through UITheme for full design consistency.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Gas;
using VoxelEngine.Items;
using VoxelEngine.Nuclear;
using VoxelEngine.Transport;
using VoxelEngine.Simulation;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    /// <summary>
    /// All machine UI panels, built via static factory methods.
    /// GameUIController calls the appropriate method and adds the result to the root.
    /// </summary>
    public static class MachineUIs
    {
        // Delegate type used to build individual inventory slot visuals.
        public delegate VisualElement SlotBuilder(
            IItemContainer c, int idx, ItemStack s, bool highlight, bool interactive);

        // ── Internal Sort Button ──────────────────────────────────────
        private static VisualElement SortRow(ItemContainer container)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginBottom   = 4;
            row.Add(T.SmallButton("⇅  Sort", () => container?.Sort(), T.AccentTeal));
            return row;
        }

        // ── Internal Header Builder ──────────────────────────────────
        /// <summary>
        /// Builds a premium header: icon badge + title + status pill, all on one row.
        /// </summary>
        private static VisualElement BuildHeader(
            string icon, string title, string status, Color statusColor, Color iconColor)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 10;
            row.pickingMode = PickingMode.Ignore;

            row.Add(T.IconBadge(icon, iconColor));

            var titleLbl = T.Title(title);
            titleLbl.style.flexGrow = 1;
            titleLbl.style.fontSize = 15;
            row.Add(titleLbl);

            var (pill, _) = T.StatusPill(status, statusColor);
            row.Add(pill);

            return row;
        }

        // ── Tank Row Helper ───────────────────────────────────────────
        private static VisualElement TankRow(params VisualElement[] gauges)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceAround;
            row.style.marginTop      = 4;
            row.style.marginBottom   = 6;
            row.pickingMode = PickingMode.Ignore;
            foreach (var g in gauges) row.Add(g);
            return row;
        }



        // ════════════════════════════════════════════════════════════
        //                   CRUSHER / ASSEMBLERS
        // ════════════════════════════════════════════════════════════
        public static VisualElement CrusherPanel(Crusher crusher, SlotBuilder slot)
        {
            crusher.EnsureContainers();
            return ProcessingMachinePanel(
                crusher,
                "▣",
                "Crusher",
                crusher.CurrentRecipe,
                crusher.knownRecipes,
                crusher.inputC,
                crusher.outputC,
                crusher.upgradeC,
                slot,
                recipe => crusher.SelectRecipe(recipe),
                UIThemeOverride.ResolveAccent(crusher, T.AccentOrange),
                "Top-fed crusher. Inputs arrive from above; outputs can be pulled from the output buffer.");
        }

        public static VisualElement AssemblerPanel(Assembler assembler, SlotBuilder slot)
        {
            assembler.EnsureContainers();
            Color accent = assembler.tier switch
            {
                AssemblerTier.Mk2 => T.AccentGold,
                AssemblerTier.Mk3 => T.AccentPurple,
                _ => T.AccentCyan
            };
            accent = UIThemeOverride.ResolveAccent(assembler, accent);
            return ProcessingMachinePanel(
                assembler,
                "⚙",
                assembler.MachineName,
                assembler.CurrentRecipe,
                assembler.knownRecipes,
                assembler.inputC,
                assembler.outputC,
                assembler.upgradeC,
                slot,
                recipe => assembler.SelectRecipe(recipe),
                accent,
                "Select a recipe, load matching inputs, then route outputs with belts or funnels.");
        }

        public static VisualElement FunnelPanel(Funnel funnel)
        {
            var p = T.MachinePanel();
            string status = funnel.BufferedCount >= Mathf.Max(1, funnel.bufferSize)
                ? "BLOCKED"
                : funnel.BufferedCount > 0 ? "ACTIVE" : "IDLE";
            Color statusColor = funnel.BufferedCount >= Mathf.Max(1, funnel.bufferSize)
                ? T.AccentRed
                : funnel.BufferedCount > 0 ? T.AccentGreen : T.TextMuted;

            p.Add(BuildHeader("⮃", "Funnel", status, statusColor, T.AccentAmber));
            p.Add(T.AccentDivider(T.AccentAmber));

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1;
            content.style.marginTop = 6;
            T.StyleScroller(content);
            p.Add(content);

            content.Add(T.StatRow("⇄", "Mode", funnel.Mode == FunnelMode.Import ? "Import" : "Export", T.TextPrimary));
            content.Add(T.StatRow("▣", "Buffered", $"{funnel.BufferedCount}/{Mathf.Max(1, funnel.bufferSize)}", T.AccentAmber));
            content.Add(T.StatRow("⏱", "Transfer Interval", $"{funnel.transferInterval:0.00}s", T.TextSecondary));
            content.Add(T.Divider());
            content.Add(T.Subtitle("Mode"));

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 8;
            modeRow.Add(ModeButton("Import", funnel.Mode == FunnelMode.Import, T.AccentAmber, () =>
            {
                funnel.SetMode(FunnelMode.Import);
                GameUIController.Instance?.RequestRefresh();
            }));
            var spacer = new VisualElement();
            spacer.style.width = 8;
            modeRow.Add(spacer);
            modeRow.Add(ModeButton("Export", funnel.Mode == FunnelMode.Export, T.AccentCyan, () =>
            {
                funnel.SetMode(FunnelMode.Export);
                GameUIController.Instance?.RequestRefresh();
            }));
            content.Add(modeRow);

            content.Add(T.Muted("Import pulls items from the belt side into the inventory side. Export pulls from the inventory side and pushes onto the belt side."));
            return p;
        }

        public static VisualElement SplitterPanel(ConveyorSplitter splitter, SlotBuilder slot)
        {
            var p = T.MachinePanel();
            string status = splitter.BufferedCount >= Mathf.Max(1, splitter.bufferSize)
                ? "BLOCKED"
                : splitter.BufferedCount > 0 ? "ACTIVE" : "IDLE";
            Color statusColor = splitter.BufferedCount >= Mathf.Max(1, splitter.bufferSize)
                ? T.AccentRed
                : splitter.BufferedCount > 0 ? T.AccentGreen : T.TextMuted;
            Color accent = splitter.tier switch
            {
                SplitterTier.Mk3 => T.AccentPurple,
                SplitterTier.Mk2 => T.AccentCyan,
                _ => T.AccentGreen
            };

            p.Add(BuildHeader("⇄", $"Conveyor Splitter {splitter.tier}", status, statusColor, accent));
            p.Add(T.AccentDivider(accent));

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1;
            content.style.marginTop = 6;
            T.StyleScroller(content);
            p.Add(content);

            content.Add(T.StatRow("▣", "Buffered", $"{splitter.BufferedCount}/{Mathf.Max(1, splitter.bufferSize)}", accent));
            content.Add(T.StatRow("⇢", "Connected Outputs", $"{splitter.ConnectedOutputCount}/{Mathf.Max(1, splitter.OutputCount)}", T.TextSecondary));
            content.Add(T.StatRow("⚙", "Routing", splitter.RoutingMode == SplitterRoutingMode.RoundRobin ? "Round Robin" : "Nearest First", T.TextPrimary));
            content.Add(T.Divider());

            content.Add(T.Subtitle("Routing Mode"));
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 8;
            modeRow.Add(ModeButton("Round Robin", splitter.RoutingMode == SplitterRoutingMode.RoundRobin, T.AccentGreen, () =>
            {
                splitter.routingMode = SplitterRoutingMode.RoundRobin;
                GameUIController.Instance?.RequestRefresh();
            }));
            var spacer = new VisualElement();
            spacer.style.width = 8;
            modeRow.Add(spacer);
            modeRow.Add(ModeButton("Nearest First", splitter.RoutingMode == SplitterRoutingMode.NearestFirst, T.AccentCyan, () =>
            {
                splitter.routingMode = SplitterRoutingMode.NearestFirst;
                GameUIController.Instance?.RequestRefresh();
            }));
            content.Add(modeRow);

            content.Add(T.Muted("Round Robin rotates evenly across valid outputs. Nearest First always prefers the closest valid connected output."));

            if (splitter.tier == SplitterTier.Mk3)
            {
                content.Add(T.Divider());
                content.Add(T.Subtitle("Mk.3 Output Filters"));
                content.Add(T.Muted("Drag an inventory item onto a filter slot to restrict that output lane. Empty slot = any item."));
                content.Add(T.Spacer(6));

                for (int i = 0; i < splitter.OutputCount; i++)
                {
                    var laneCard = T.Card();
                    laneCard.style.marginBottom = 6;
                    laneCard.style.flexDirection = FlexDirection.Column;

                    var laneHeader = new VisualElement();
                    laneHeader.style.flexDirection = FlexDirection.Row;
                    laneHeader.style.alignItems = Align.Center;
                    var laneTitle = new Label($"OUTPUT {i + 1} · {splitter.GetOutputLabel(i).ToUpperInvariant()}");
                    laneTitle.style.flexGrow = 1;
                    laneTitle.style.color = new StyleColor(T.TextPrimary);
                    laneTitle.style.fontSize = 11;
                    laneTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                    laneHeader.Add(laneTitle);
                    bool connected = splitter.outputTargets != null && i < splitter.outputTargets.Count && splitter.outputTargets[i] != null;
                    var (pill, _) = T.StatusPill(connected ? "CONNECTED" : "NO BELT", connected ? T.AccentGreen : T.AccentRed);
                    laneHeader.Add(pill);
                    laneCard.Add(laneHeader);

                    laneCard.Add(T.Spacer(4));
                    var laneBody = new VisualElement();
                    laneBody.style.flexDirection = FlexDirection.Row;
                    laneBody.style.alignItems = Align.Center;
                    laneBody.style.flexWrap = Wrap.Wrap;

                    var filterSlot = splitter.GetOutputFilterSlot(i);
                    if (filterSlot != null && slot != null)
                        laneBody.Add(slot(filterSlot, 0, filterSlot.GetSlot(0), false, true));

                    var filterText = new Label(splitter.GetOutputFilterItem(i) != null
                        ? $"Filter: {splitter.GetOutputFilterItem(i).displayName}"
                        : "Filter: Any Item");
                    filterText.style.flexGrow = 1;
                    filterText.style.marginLeft = 10;
                    filterText.style.color = new StyleColor(T.TextSecondary);
                    filterText.style.fontSize = 10;
                    laneBody.Add(filterText);

                    var pickBtn = T.SmallButton("Search / Set", () =>
                    {
                        var root = laneCard.panel?.visualTree;
                        if (root != null)
                        {
                            ItemFilterDialog.OpenSingle(root,
                                $"Splitter Output {i + 1} · {splitter.GetOutputLabel(i)}",
                                () => splitter.GetOutputFilterItem(i),
                                item => splitter.SetOutputFilterItem(i, item),
                                () => GameUIController.Instance?.RequestRefresh());
                        }
                    }, T.AccentCyan);
                    pickBtn.style.marginLeft = 8;
                    laneBody.Add(pickBtn);

                    var clearBtn = T.SmallButton("Clear", () =>
                    {
                        splitter.ClearOutputFilter(i);
                        GameUIController.Instance?.RequestRefresh();
                    }, T.AccentRed);
                    clearBtn.style.marginLeft = 8;
                    laneBody.Add(clearBtn);
                    laneCard.Add(laneBody);
                    content.Add(laneCard);
                }
            }

            content.Add(T.Divider());
            content.Add(T.Muted("Use conveyors on the intended output sides. Mk.1 supports a left-side fallback for its second lane if no right-side lane is connected."));
            return p;
        }

        private static VisualElement ProcessingMachinePanel(
            IMachine machine,
            string icon,
            string title,
            MachineRecipe selectedRecipe,
            System.Collections.Generic.List<MachineRecipe> recipes,
            ItemContainer input,
            ItemContainer output,
            ItemContainer upgrades,
            SlotBuilder slot,
            System.Action<MachineRecipe> selectRecipe,
            Color accent,
            string hint)
        {
            var p = T.MachinePanel();
            string status = !machine.UserEnabled ? "DISABLED" : !machine.IsOnline ? "NO POWER" : machine.IsActive ? "RUNNING" : "IDLE";
            Color statusColor = !machine.UserEnabled || !machine.IsOnline ? T.AccentRed : machine.IsActive ? T.AccentGreen : T.TextMuted;
            p.Add(BuildHeader(icon, title, status, statusColor, accent));
            p.Add(T.AccentDivider(accent));

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.style.flexGrow = 1;
            content.style.marginTop = 6;
            T.StyleScroller(content);
            p.Add(content);

            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            top.style.marginBottom = 8;
            var toggle = T.SmallButton(machine.UserEnabled ? "Enabled" : "Disabled", () => machine.UserEnabled = !machine.UserEnabled,
                machine.UserEnabled ? T.AccentGreen : T.AccentRed);
            toggle.style.marginRight = 8;
            top.Add(toggle);
            top.Add(T.StatRow("⚡", "Power", $"{machine.CurrentWattage:0} W", machine.IsOnline ? T.AccentGold : T.TextMuted));
            content.Add(top);

            string recipeName = selectedRecipe != null ? selectedRecipe.GetName() : "Auto / waiting for input";
            content.Add(T.StatRow("⏱", "Recipe", recipeName, selectedRecipe != null ? accent : T.TextMuted));
            var (progressBar, _) = T.ProgressBar(machine.Progress01, accent, 9, false);
            content.Add(progressBar);
            content.Add(T.Divider());

            content.Add(T.Subtitle("Recipe Selection"));
            var recipeList = new ScrollView(ScrollViewMode.Vertical);
            recipeList.style.maxHeight = 168;
            recipeList.style.marginBottom = 8;
            T.StyleScroller(recipeList);
            if (recipes != null)
            {
                foreach (var recipe in recipes)
                {
                    if (recipe == null) continue;
                    recipeList.Add(MachineRecipeCard(recipe, recipe == selectedRecipe, accent, () => selectRecipe?.Invoke(recipe)));
                }
            }
            content.Add(recipeList);

            content.Add(T.Subtitle("Inventory"));
            var slotRow = new VisualElement();
            slotRow.style.flexDirection = FlexDirection.Row;
            slotRow.style.flexWrap = Wrap.Wrap;
            slotRow.style.justifyContent = Justify.Center;
            slotRow.style.marginTop = 5;
            slotRow.Add(T.SlotCard("Inputs", SlotGrid(input, slot)));
            slotRow.Add(T.Spacer(10));
            slotRow.Add(T.SlotCard("Outputs", SlotGrid(output, slot)));
            if (upgrades != null && upgrades.Size > 0)
            {
                slotRow.Add(T.Spacer(10));
                slotRow.Add(T.SlotCard("Upgrades", SlotGrid(upgrades, slot)));
            }
            content.Add(slotRow);
            content.Add(T.Spacer(8));
            content.Add(T.Muted(hint));
            return p;
        }

        private static VisualElement SlotGrid(ItemContainer container, SlotBuilder slot)
        {
            var grid = T.SlotGrid();
            if (container == null || slot == null) return grid;
            for (int i = 0; i < container.Size; i++)
                grid.Add(slot(container, i, container.GetSlot(i), false, true));
            return grid;
        }

        private static Button ModeButton(string text, bool selected, Color accent, System.Action onClick)
        {
            var btn = T.SmallButton(text, onClick, selected ? accent : T.BgSlot);
            btn.style.minWidth = 120;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            return btn;
        }

        private static VisualElement MachineRecipeCard(MachineRecipe recipe, bool selected, Color accent, System.Action onClick)
        {
            var card = T.Card();
            card.style.marginBottom = 5;
            card.style.flexDirection = FlexDirection.Row;
            card.style.alignItems = Align.Center;
            if (selected) T.Border(card, 1, accent);

            // Output icon slot — premium slot look; falls back to a tint chip.
            var iconSlot = new VisualElement();
            iconSlot.style.width = 34; iconSlot.style.height = 34;
            iconSlot.style.marginRight = 8;
            iconSlot.style.alignItems = Align.Center;
            iconSlot.style.justifyContent = Justify.Center;
            iconSlot.style.backgroundColor = new StyleColor(T.BgSlot);
            T.Radius(iconSlot, 5);
            iconSlot.pickingMode = PickingMode.Ignore;
            var recipeIcon = recipe.GetIcon();
            if (recipeIcon != null)
            {
                var rImg = new Image { sprite = recipeIcon };
                rImg.scaleMode = ScaleMode.ScaleToFit;
                rImg.style.width = 30; rImg.style.height = 30;
                rImg.pickingMode = PickingMode.Ignore;
                iconSlot.Add(rImg);
            }
            else
            {
                var tintChip = new VisualElement();
                tintChip.style.width = 22; tintChip.style.height = 22;
                tintChip.style.backgroundColor = new StyleColor(
                    recipe.outputItem != null ? recipe.outputItem.iconTint : accent);
                T.Radius(tintChip, 4);
                tintChip.pickingMode = PickingMode.Ignore;
                iconSlot.Add(tintChip);
            }
            card.Add(iconSlot);

            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.pickingMode = PickingMode.Ignore;
            var name = new Label(recipe.GetName());
            name.style.color = new StyleColor(selected ? T.TextAccent : T.TextPrimary);
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.pickingMode = PickingMode.Ignore;
            column.Add(name);
            var inputs = new Label($"In: {RecipeInputs(recipe)}") { pickingMode = PickingMode.Ignore };
            inputs.style.color = new StyleColor(T.TextSecondary);
            inputs.style.fontSize = 10;
            column.Add(inputs);
            var outputs = new Label($"Out: {RecipeOutputs(recipe)}") { pickingMode = PickingMode.Ignore };
            outputs.style.color = new StyleColor(T.TextMuted);
            outputs.style.fontSize = 10;
            column.Add(outputs);
            card.Add(column);

            var time = new Label($"{recipe.processSeconds:0.#}s");
            time.style.color = new StyleColor(T.TextMuted);
            time.style.fontSize = 10;
            time.style.unityFontStyleAndWeight = FontStyle.Bold;
            time.pickingMode = PickingMode.Ignore;
            card.Add(time);

            card.RegisterCallback<ClickEvent>(_ =>
            {
                onClick?.Invoke();
                GameUIController.Instance?.RequestRefresh();
            });
            card.RegisterCallback<PointerEnterEvent>(_ => card.style.backgroundColor = new StyleColor(T.BgHover));
            card.RegisterCallback<PointerLeaveEvent>(_ => card.style.backgroundColor = new StyleColor(T.BgCard));
            return card;
        }

        private static string RecipeInputs(MachineRecipe recipe)
        {
            if (recipe == null || recipe.inputs == null || recipe.inputs.Length == 0) return "—";
            var parts = new System.Text.StringBuilder();
            for (int i = 0; i < recipe.inputs.Length; i++)
            {
                if (i > 0) parts.Append(" + ");
                var input = recipe.inputs[i];
                parts.Append(input.item != null ? $"{input.count}x {input.item.displayName}" : "?");
            }
            return parts.ToString();
        }

        private static string RecipeOutputs(MachineRecipe recipe)
        {
            if (recipe == null || recipe.outputItem == null) return "—";
            string text = $"{recipe.outputCount}x {recipe.outputItem.displayName}";
            if (recipe.byproductItem != null && recipe.byproductCount > 0)
                text += $" + {recipe.byproductCount}x {recipe.byproductItem.displayName}";
            return text;
        }

        // ════════════════════════════════════════════════════════════
        //                      REACTOR CORE
        // ════════════════════════════════════════════════════════════
        public static VisualElement ReactorCorePanel(ReactorCore r, SlotBuilder slot)
        {
            r.EnsureContainers();
            var p = T.MachinePanel();

            string status    = r.IsOverheating ? "OVERHEAT!" : (r.IsOnline ? "ONLINE" : "OFFLINE");
            Color  statusCol = r.IsOverheating ? T.AccentRed  : (r.IsOnline ? T.AccentGreen : T.TextMuted);

            p.Add(BuildHeader("☢", "Nuclear Reactor", status, statusCol, T.AccentGreen));
            p.Add(T.AccentDivider(T.AccentGreen));

            // Temperature
            float tempRatio = r.coreTemperature / (r.maxSafeTemperature * 1.25f);
            Color tempColor = r.coreTemperature > r.maxSafeTemperature ? T.AccentRed :
                              r.coreTemperature > r.maxSafeTemperature * 0.70f ? T.AccentOrange : T.AccentCyan;
            p.Add(T.StatRow("🌡", "Core Temperature",
                $"{r.coreTemperature:0}°C  /  {r.maxSafeTemperature:0}°C", tempColor));
            var (tempBar, _) = T.ProgressBar(tempRatio, tempColor, 8, false);
            p.Add(tempBar);
            p.Add(T.Spacer(8));

            // Control rods + output + fuel
            p.Add(T.StatRow("⚙", "Control Rods",    $"{r.controlRodLevel * 100f:0}% inserted", T.TextSecondary));
            p.Add(T.StatRow("⚡", "Thermal Output",  $"{r.CurrentThermalKW:0} kW(th)",          T.AccentGold));
            p.Add(T.StatRow("⛽", "Fuel Remaining",  $"{r.FuelRemaining01 * 100f:0}%",           T.AccentCyan));

            var (fuelBar, _) = T.ProgressBar(r.FuelRemaining01, T.AccentCyan, 6, false);
            p.Add(fuelBar);
            p.Add(T.Divider());

            // Fluid tanks
            p.Add(T.Subtitle("Internal Tanks"));
            p.Add(TankRow(
                T.TankGauge("WATER", r.WaterFill01, new Color(0.20f, 0.50f, 0.92f),
                    $"{r.waterAmount:0}/{r.waterTankCapacity:0}"),
                T.TankGauge("STEAM", r.SteamFill01, new Color(0.82f, 0.82f, 0.88f),
                    $"{r.steamAmount:0}/{r.steamTankCapacity:0}")
            ));
            p.Add(T.Divider());

            // Inventory slots
            p.Add(T.Subtitle("Fuel & Waste"));
            var slotRow = new VisualElement();
            slotRow.style.flexDirection  = FlexDirection.Row;
            slotRow.style.justifyContent = Justify.Center;
            slotRow.style.marginTop      = 6;

            var fuelGrid = T.SlotGrid();
            for (int i = 0; i < r.fuelC.Size; i++)
                fuelGrid.Add(slot(r.fuelC, i, r.fuelC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("Fuel Rods", fuelGrid));
            slotRow.Add(T.Spacer(14));

            var spentGrid = T.SlotGrid();
            for (int i = 0; i < r.spentC.Size; i++)
                spentGrid.Add(slot(r.spentC, i, r.spentC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("Spent Rods", spentGrid));
            p.Add(slotRow);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Connect water tanks and steam pipes. Adjust control rods to manage output."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                      STEAM TURBINE
        // ════════════════════════════════════════════════════════════
        public static VisualElement SteamTurbinePanel(SteamTurbine t)
        {
            var p = T.MachinePanel();
            p.Add(BuildHeader("⚙", "Steam Turbine",
                t.IsRunning ? "SPINNING" : "IDLE",
                t.IsRunning ? T.AccentCyan : T.TextMuted, T.AccentCyan));
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⚡", "Power Output",  $"{t.CurrentOutput:0} W",           T.AccentGold));
            p.Add(T.StatRow("📈", "Efficiency",    $"{t.efficiency * 100f:0}%",          T.TextSecondary));
            p.Add(T.StatRow("🔄", "Spin Speed",    $"{t.SpinSpeed01 * 100f:0}%",         T.AccentCyan));
            p.Add(T.Divider());

            p.Add(T.Subtitle("Internal Tanks"));
            p.Add(TankRow(
                T.TankGauge("STEAM IN",  t.SteamFill01,  new Color(0.82f, 0.82f, 0.88f),
                    $"{t.steamAmount:0}/{t.steamTankCapacity:0}"),
                T.TankGauge("WATER OUT", t.WaterFill01,  new Color(0.20f, 0.50f, 0.92f),
                    $"{t.waterAmount:0}/{t.waterTankCapacity:0}")
            ));

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Connect steam pipes from the reactor. Condensed water is recycled automatically."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                    PORTABLE REACTOR
        // ════════════════════════════════════════════════════════════
        public static VisualElement PortableReactorPanel(PortableReactor r, SlotBuilder slot)
        {
            r.EnsureContainers();
            var p = T.MachinePanel();
            p.Add(BuildHeader("☢", "Portable Reactor",
                r.IsRunning ? "RUNNING" : "OFFLINE",
                r.IsRunning ? T.AccentGreen : T.TextMuted, T.AccentGreen));
            p.Add(T.AccentDivider(T.AccentGreen));

            p.Add(T.StatRow("⚡", "Power Output",    r.IsRunning ? $"{r.wattsOutput:0} W" : "0 W", T.AccentGold));
            p.Add(T.StatRow("⛽", "Fuel Remaining",  $"{r.FuelRemaining01 * 100f:0}%",               T.AccentCyan));
            var (fuelBar, _) = T.ProgressBar(r.FuelRemaining01, T.AccentGreen, 8, false);
            p.Add(fuelBar);
            p.Add(T.Divider());

            p.Add(T.Subtitle("Inputs & Waste"));
            var slotRow = new VisualElement();
            slotRow.style.flexDirection  = FlexDirection.Row;
            slotRow.style.justifyContent = Justify.Center;
            slotRow.style.marginTop      = 6;

            var fuelGrid = T.SlotGrid();
            for (int i = 0; i < r.fuelC.Size; i++)
                fuelGrid.Add(slot(r.fuelC, i, r.fuelC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("LEU Fuel", fuelGrid));
            slotRow.Add(T.Spacer(10));

            var iceGrid = T.SlotGrid();
            for (int i = 0; i < r.iceC.Size; i++)
                iceGrid.Add(slot(r.iceC, i, r.iceC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("Ice Coolant", iceGrid));
            slotRow.Add(T.Spacer(10));

            var wasteGrid = T.SlotGrid();
            for (int i = 0; i < r.wasteC.Size; i++)
                wasteGrid.Add(slot(r.wasteC, i, r.wasteC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("Waste", wasteGrid));
            p.Add(slotRow);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Compact RTG — insert LEU pellets and ice. No pipes required."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   URANIUM PROCESSOR
        // ════════════════════════════════════════════════════════════
        public static VisualElement UraniumProcessorPanel(UraniumProcessor u, SlotBuilder slot)
        {
            u.EnsureContainers();
            var p = T.MachinePanel();
            p.Add(BuildHeader("⚛", "Enrichment Centrifuge",
                u.IsProcessing ? "PROCESSING" : "IDLE",
                u.IsProcessing ? T.AccentPurple : T.TextMuted, T.AccentPurple));
            p.Add(T.AccentDivider(T.AccentPurple));

            p.Add(T.StatRow("⏱", "Progress", $"{u.Progress01 * 100f:0}%", T.AccentCyan));
            var (progBar, _) = T.ProgressBar(u.Progress01, T.AccentPurple, 10, false);
            p.Add(progBar);
            p.Add(T.Divider());

            // Input
            p.Add(T.Subtitle("Input"));
            var inGrid = T.SlotGrid();
            inGrid.Add(slot(u.inputC, 0, u.inputC.GetSlot(0), false, true));
            p.Add(T.SlotCard("Uranium Ore", inGrid));
            p.Add(T.Spacer(8));

            // Outputs
            p.Add(T.Subtitle("Outputs"));
            var outRow = new VisualElement();
            outRow.style.flexDirection  = FlexDirection.Row;
            outRow.style.justifyContent = Justify.Center;
            outRow.style.marginTop      = 4;

            var enrichGrid = T.SlotGrid();
            for (int i = 0; i < u.enrichedOutputC.Size; i++)
                enrichGrid.Add(slot(u.enrichedOutputC, i, u.enrichedOutputC.GetSlot(i), false, true));
            outRow.Add(T.SlotCard("Enriched", enrichGrid));
            outRow.Add(T.Spacer(10));

            var wasteGrid = T.SlotGrid();
            for (int i = 0; i < u.wasteOutputC.Size; i++)
                wasteGrid.Add(slot(u.wasteOutputC, i, u.wasteOutputC.GetSlot(i), false, true));
            outRow.Add(T.SlotCard("Waste", wasteGrid));
            p.Add(outRow);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Enriches raw uranium into fuel rods and LEU pellets. Requires power."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                   WASTE REPROCESSOR
        // ════════════════════════════════════════════════════════════
        public static VisualElement WasteReprocessorPanel(WasteReprocessor w, SlotBuilder slot)
        {
            w.EnsureContainers();
            var p = T.MachinePanel();
            p.Add(BuildHeader("♻", "Waste Reprocessor",
                w.IsProcessing ? "REPROCESSING" : "IDLE",
                w.IsProcessing ? T.AccentOrange : T.TextMuted, T.AccentOrange));
            p.Add(T.AccentDivider(T.AccentOrange));

            p.Add(T.StatRow("⏱", "Progress", $"{w.Progress01 * 100f:0}%", T.AccentCyan));
            var (progBar, _) = T.ProgressBar(w.Progress01, T.AccentOrange, 10, false);
            p.Add(progBar);
            p.Add(T.Divider());

            var slotRow = new VisualElement();
            slotRow.style.flexDirection  = FlexDirection.Row;
            slotRow.style.justifyContent = Justify.Center;
            slotRow.style.marginTop      = 4;

            var inGrid = T.SlotGrid();
            for (int i = 0; i < w.inputC.Size; i++)
                inGrid.Add(slot(w.inputC, i, w.inputC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("Input", inGrid));
            slotRow.Add(T.Spacer(10));

            var outGrid = T.SlotGrid();
            for (int i = 0; i < w.outputC.Size; i++)
                outGrid.Add(slot(w.outputC, i, w.outputC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("Recovered", outGrid));
            slotRow.Add(T.Spacer(10));

            var hlGrid = T.SlotGrid();
            for (int i = 0; i < w.wasteOutputC.Size; i++)
                hlGrid.Add(slot(w.wasteOutputC, i, w.wasteOutputC.GetSlot(i), false, true));
            slotRow.Add(T.SlotCard("HL Waste", hlGrid));
            p.Add(slotRow);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Reprocesses spent fuel rods and depleted uranium via PUREX."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                     ELECTROLYSER
        // ════════════════════════════════════════════════════════════
        public static VisualElement ElectrolyserPanel(Electrolyser e, SlotBuilder slot)
        {
            e.EnsureContainers();
            var p = T.MachinePanel();
            p.Add(BuildHeader("⚗", "Electrolyser",
                e.IsRunning ? "ELECTROLYZING" : "IDLE",
                e.IsRunning ? T.AccentCyan : T.TextMuted, T.AccentCyan));
            p.Add(T.AccentDivider(T.AccentCyan));

            p.Add(T.StatRow("⏱", "Progress", $"{e.Progress01 * 100f:0}%", T.AccentCyan));
            var (progBar, _) = T.ProgressBar(e.Progress01, T.AccentCyan, 10, false);
            p.Add(progBar);
            p.Add(T.Divider());

            p.Add(T.Subtitle("Gas Buffers"));
            p.Add(TankRow(
                T.TankGauge("H₂", e.HydrogenBuffer / e.BufferCapacity,
                    new Color(0.28f, 0.68f, 1.0f), $"{e.HydrogenBuffer:0}/{e.BufferCapacity:0}"),
                T.TankGauge("O₂", e.OxygenBuffer / e.BufferCapacity,
                    new Color(0.90f, 0.38f, 0.28f), $"{e.OxygenBuffer:0}/{e.BufferCapacity:0}")
            ));
            p.Add(T.Divider());

            p.Add(T.Subtitle("Ice Input"));
            var iceGrid = T.SlotGrid();
            for (int i = 0; i < e.iceInputC.Size; i++)
                iceGrid.Add(slot(e.iceInputC, i, e.iceInputC.GetSlot(i), false, true));
            p.Add(iceGrid);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Electrolyses ice into H₂ and O₂. Connect gas tanks via gas pipes."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                    HYDROGEN ENGINE
        // ════════════════════════════════════════════════════════════
        public static VisualElement HydrogenEnginePanel(HydrogenEngine h)
        {
            var p = T.MachinePanel();
            p.Add(BuildHeader("🔥", "Hydrogen Engine",
                h.IsRunning ? "RUNNING" : "IDLE",
                h.IsRunning ? T.AccentGold : T.TextMuted, T.AccentGold));
            p.Add(T.AccentDivider(T.AccentGold));

            p.Add(T.StatRow("⚡", "Power Output",     h.IsRunning ? $"{h.wattsOutput:0} W" : "0 W", T.AccentGold));
            p.Add(T.StatRow("💧", "H₂ Consumption",   $"{h.hydrogenPerSecond:0.0} / sec",            T.TextSecondary));
            p.Add(T.Divider());

            p.Add(T.Subtitle("Hydrogen Buffer"));
            p.Add(TankRow(
                T.TankGauge("H₂", h.Buffer01, new Color(0.28f, 0.68f, 1f),
                    $"{h.bufferAmount:0} / {h.bufferCapacity:0}", 120, 64)
            ));

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Burns hydrogen for clean power. Connect a hydrogen gas tank via gas pipes."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                       GAS TANK
        // ════════════════════════════════════════════════════════════
        public static VisualElement GasTankPanel(GasTank t, SlotBuilder slot = null)
        {
            t.EnsureContainers();
            var p = T.MachinePanel();

            var displayType = t.EffectiveType;
            string gasName  = displayType == GasType.None ? "Empty" : displayType.ToString();
            Color  gasColor = displayType switch
            {
                GasType.Hydrogen => new Color(0.28f, 0.68f, 1f),
                GasType.Oxygen   => new Color(0.90f, 0.38f, 0.28f),
                GasType.Steam    => new Color(0.82f, 0.82f, 0.88f),
                GasType.ExhaustGas => new Color(0.55f, 0.45f, 0.35f),
                _                => T.TextMuted
            };

            string status    = t.storedAmount > 0 ? "PRESSURIZED" : "EMPTY";
            Color  statusCol = t.storedAmount > 0 ? gasColor : T.TextMuted;

            p.Add(BuildHeader("🛢", $"Gas Tank  ·  {gasName}", status, statusCol, gasColor));
            p.Add(T.AccentDivider(gasColor));

            // Large centred tank gauge.
            p.Add(TankRow(
                T.TankGauge(gasName.ToUpper(), t.Fill01, gasColor,
                    $"{t.storedAmount:0} / {t.capacity:0}", 110, 128)
            ));
            p.Add(T.Divider());

            // Gas type selector (change freely when empty / matching).
            p.Add(T.Subtitle("Gas Type"));
            var typeRow = new VisualElement();
            typeRow.style.flexDirection = FlexDirection.Row;
            typeRow.style.flexWrap = Wrap.Wrap;
            typeRow.style.marginBottom = 6;
            foreach (GasType gt in System.Enum.GetValues(typeof(GasType)))
            {
                if (gt == GasType.None) continue;
                var captured = gt;
                bool active = t.selectedGasType == gt || (t.selectedGasType == GasType.None && t.storedGasType == gt);
                var btn = T.SmallButton(gt.ToString(), () =>
                {
                    if (active) return;
                    if (t.storedAmount <= 0.001f)
                    {
                        if (t.TrySetSelectedGasType(captured))
                            GameUIController.Instance?.RefreshCurrentPanel();
                        return;
                    }

                    GameUIController.Instance?.ShowTankTypeVoidConfirmation(
                        "Gas", captured.ToString(), t.storedAmount, () =>
                        {
                            t.Drain();
                            t.TrySetSelectedGasType(captured);
                            BuildFeedbackHud.Show("Gas tank changed", captured.ToString(), null, T.AccentCyan);
                        });
                }, active ? T.AccentCyan : (Color?)null);
                typeRow.Add(btn);
            }
            p.Add(typeRow);
            if (t.storedAmount > 0.001f)
                p.Add(T.Muted("Selecting another type asks whether to void gas or cancel."));

            p.Add(T.Spacer(4));
            p.Add(T.StatRow("📥", "Accept Input",  t.acceptInput  ? "YES" : "NO",
                t.acceptInput  ? T.AccentGreen : T.AccentRed));
            p.Add(T.StatRow("📤", "Allow Output",  t.allowOutput  ? "YES" : "NO",
                t.allowOutput  ? T.AccentGreen : T.AccentRed));

            // Portable Hydrogen Tank + hydrogen jetpack dock — only when configured for H₂.
            if (t.IsHydrogenMode && slot != null)
            {
                p.Add(T.Divider());
                p.Add(T.Subtitle("H₂ Fill Dock"));
                var dock = T.SlotGrid(1);
                dock.Add(slot(t.PortableSlot, 0, t.PortableSlot.GetSlot(0), false, true));
                p.Add(dock);
                p.Add(T.Muted("Drop a Portable Hydrogen Tank or a hydrogen jetpack (Hydrogen Boost / Hybrid) here to fill it from bulk H₂ automatically."));
            }

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Stores a single gas type. Connect via gas pipes. Hold a Portable H₂ Tank or hydrogen jetpack and RMB (Shift = fill 100%)."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                      BIOFARM (11.5)
        // ════════════════════════════════════════════════════════════
        public static VisualElement JackPumpPanel(VoxelEngine.Crafting.Pumpjack jackPump, SlotBuilder slot)
        {
            if (jackPump == null) return T.MachinePanel();
            jackPump.EnsureContainers();
            string status = !jackPump.IsOnline ? "NO POWER"
                : !jackPump.HasReservoir ? "NO PIRATE NODE"
                : jackPump.IsPumping ? "PUMPING" : "READY";
            Color statusColor = !jackPump.IsOnline || !jackPump.HasReservoir ? T.AccentRed
                : jackPump.IsPumping ? T.AccentGreen : T.AccentAmber;

            var panel = T.MachinePanel();
            panel.Add(BuildHeader("⚙", "Jack Pump", status, statusColor, T.AccentAmber));
            panel.Add(T.AccentDivider(T.AccentAmber));
            panel.Add(T.StatRow("◉", "Node", jackPump.HasReservoir ? "INFINITE PIRATE OIL" : "Place on rare Pirate oil node", jackPump.HasReservoir ? T.AccentGreen : T.TextMuted));
            panel.Add(T.StatRow("⚡", "Power Draw", PowerFormat.Watts(jackPump.CurrentWattage), T.AccentAmber));
            panel.Add(T.StatRow("◷", "Cycle", $"{jackPump.secondsPerCycle:0}s / barrel", T.AccentCyan));
            panel.Add(T.Spacer(5));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.justifyContent = Justify.SpaceAround;
            panel.Add(row);

            var input = new VisualElement();
            input.style.alignItems = Align.Center;
            input.Add(T.Subtitle("Empty Barrel"));
            var inputGrid = T.SlotGrid(1);
            inputGrid.Add(slot(jackPump.inputC, 0, jackPump.inputC.GetSlot(0), false, true));
            input.Add(inputGrid);
            row.Add(input);

            var output = new VisualElement();
            output.style.alignItems = Align.Center;
            output.Add(T.Subtitle("Crude Oil Output"));
            var outputGrid = T.SlotGrid(2);
            for (int i = 0; i < jackPump.outputC.Size; i++)
                outputGrid.Add(slot(jackPump.outputC, i, jackPump.outputC.GetSlot(i), false, true));
            output.Add(outputGrid);
            row.Add(output);

            panel.Add(T.Spacer(6));
            panel.Add(T.Muted("Requires Pirate Oil Recovery research and a Jack Pump Head recovered only from Pirate ruins. The rare node never depletes, but the pump draws heavy power."));
            return panel;
        }

        public static VisualElement BiofarmPanel(VoxelEngine.Building.Biofarm bf, SlotBuilder slot)
        {
            bf.EnsureContainers();
            var p = T.MachinePanel();
            bool running = bf.IsRunning;
            Color statusCol = running ? T.AccentGreen :
                              bf.Status == "No Power" ? T.AccentRed :
                              bf.Status == "No Water" ? T.AccentAmber :
                              bf.Status == "No Biomass" ? T.AccentOrange : T.TextMuted;

            p.Add(BuildHeader("🌿", "Biofarm Oxygen Garden", bf.Status.ToUpperInvariant(), statusCol, new Color(0.35f,0.85f,0.45f)));
            p.Add(T.AccentDivider(new Color(0.35f,0.85f,0.45f)));

            // Stats
            p.Add(T.StatRow("⚡", "Power Draw", $"{bf.powerDraw:0} W", running ? T.AccentGreen : T.TextMuted));
            p.Add(T.StatRow("💧", "Water Use", $"{bf.waterConsumptionLps:0.00} L/s", T.AccentCyan));
            p.Add(T.StatRow("🌬", "O₂ Output", $"{bf.oxygenPerSecond:0.00} L/s (slow & reliable)", new Color(0.40f,0.90f,0.55f)));
            p.Add(T.StatRow("⏳", "Biomass Time", bf.BiomassTimeRemaining > 0 ? $"{bf.BiomassTimeRemaining:0}s" : "Empty", T.AccentAmber));

            p.Add(T.Divider());
            p.Add(T.Subtitle("Oxygen Buffer"));
            p.Add(TankRow(
                T.TankGauge("O₂", bf.Buffer01, new Color(0.35f,0.85f,0.55f),
                    $"{bf.OxygenBuffer:0}/{bf.bufferCapacity:0} L", 110, 100)
            ));

            p.Add(T.Divider());
            p.Add(T.Subtitle("Biomass Input (Wheat/Corn/Carrot/Seeds/Biomass)"));
            var grid = T.SlotGrid(bf.biomassInput.Size);
            for (int i = 0; i < bf.biomassInput.Size; i++)
                grid.Add(slot(bf.biomassInput, i, bf.biomassInput.GetSlot(i), false, true));
            p.Add(grid);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Passive O₂: needs power + water pipes + biomass. Slower than electrolyser but renewable, ideal for cryobeds & offline survival."));
            return p;
        }

        // ════════════════════════════════════════════════════════════
        //                        QUARRY
        // ════════════════════════════════════════════════════════════
        public static VisualElement QuarryPanel(Quarry q, SlotBuilder slot)
        {
            q.EnsureOutputPublic();
            q.EnsureUpgrades();
            var p = T.MachinePanel();
            var pc = q.GetComponent<VoxelEngine.Power.PowerConsumer>();
            bool powered = pc == null || pc.IsPowered;

            string status = !powered ? "NO POWER" : q.Phase switch
            {
                QuarryPhase.TapeFrame     => "SURVEYING",
                QuarryPhase.BuildingFrame => "BUILDING",
                QuarryPhase.Mining        => q.IsOutputFull ? "OUTPUT FULL" : "DRILLING",
                QuarryPhase.Complete      => "COMPLETE",
                _                         => "IDLE"
            };
            Color sc = !powered ? T.AccentRed : q.Phase switch
            {
                QuarryPhase.TapeFrame     => T.AccentOrange,
                QuarryPhase.BuildingFrame => T.AccentOrange,
                QuarryPhase.Mining        => q.IsOutputFull ? T.AccentRed : T.AccentGreen,
                QuarryPhase.Complete      => T.AccentCyan,
                _                         => T.TextMuted
            };

            p.Add(BuildHeader("\u26CF", "Quarry Drill", status, sc, T.AccentGold));
            p.Add(T.AccentDivider(sc));

            // ═══ BODY: LEFT (upgrades + output) | RIGHT (stats + ports) ═══
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.pickingMode = PickingMode.Ignore;

            // ═══ LEFT COLUMN: Upgrades then Output ═══
            var left = new VisualElement();
            left.style.width = 140;
            left.style.marginRight = 14;
            left.style.alignItems = Align.Center;
            left.pickingMode = PickingMode.Ignore;

            // -- UPGRADES --
            var upgLbl = new Label("UPGRADES");
            upgLbl.style.fontSize = 9;
            upgLbl.style.color = new StyleColor(T.AccentGold);
            upgLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            upgLbl.style.marginBottom = 4;
            upgLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            upgLbl.pickingMode = PickingMode.Ignore;
            left.Add(upgLbl);

            var upgCol = new VisualElement();
            upgCol.style.flexDirection = FlexDirection.Column;
            upgCol.style.alignItems = Align.Center;
            upgCol.style.marginBottom = 10;

            var rRow = UpgSlotRow(q.upgradeC, 0, "R", T.AccentGold, slot);
            rRow.style.marginBottom = 4;
            upgCol.Add(rRow);

            var sRow = UpgSlotRow(q.upgradeC, 1, "S", T.AccentTeal, slot);
            sRow.style.marginBottom = 4;
            upgCol.Add(sRow);

            var eRow = UpgSlotRow(q.upgradeC, 2, "E", T.AccentPurple, slot);
            upgCol.Add(eRow);

            left.Add(upgCol);

            // -- OUTPUT --
            var outLbl = new Label("OUTPUT");
            outLbl.style.fontSize = 9;
            outLbl.style.color = new StyleColor(T.AccentCyan);
            outLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            outLbl.style.marginBottom = 4;
            outLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            outLbl.pickingMode = PickingMode.Ignore;
            left.Add(outLbl);

            var outCol = new VisualElement();
            outCol.style.flexDirection = FlexDirection.Column;
            outCol.style.alignItems = Align.Center;
            // Output as 3 rows of 2 items each
            for (int row = 0; row < 3; row++)
            {
                var orow = new VisualElement();
                orow.style.flexDirection = FlexDirection.Row;
                orow.style.justifyContent = Justify.Center;
                orow.style.marginBottom = 3;
                for (int col = 0; col < 2; col++)
                {
                    int idx = row * 2 + col;
                    if (idx < q.Output.Size)
                    {
                        var sv = slot(q.Output, idx, q.Output.GetSlot(idx), false, true);
                        sv.style.marginRight = col == 0 ? 3 : 0;
                        orow.Add(sv);
                    }
                }
                outCol.Add(orow);
            }
            left.Add(outCol);

            body.Add(left);

            // ═══ RIGHT COLUMN: Stats + Ports ═══
            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.pickingMode = PickingMode.Ignore;

            if (pc != null)
                right.Add(T.StatRow("\u26A1", "Power",
                    powered ? $"{q.EffPowerDraw:0} W  \u00B7  Connected" : "Disconnected",
                    powered ? T.AccentGreen : T.AccentRed));

            right.Add(T.StatRow("\uD83D\uDCD0", "Area",
                $"{q.AreaX}\u00D7{q.AreaZ}  ({q.EffSize}\u00B2)", T.AccentCyan));
            right.Add(T.StatRow("\u2B07", "Depth",
                $"{q.CurrentDepth} / {q.MaxDepth}", T.TextPrimary));
            right.Add(T.StatRow("\u23F1", "Speed",
                $"{q.EffInterval:F2}s", T.AccentTeal));
            right.Add(T.StatRow("\uD83D\uDD27", "Tier",
                $"{q.quarryTier}", T.TextSecondary));

            right.Add(T.Spacer(4));
            var (progBar, _) = T.ProgressBar(q.IsMining ? q.MineProgress01 : 0f, T.AccentCyan, 8, true);
            right.Add(progBar);

            // Item ports are appended by GameUIController via AppendItemPorts so
            // the quarry uses the SAME advanced per-face item widget as every
            // other machine (routing + searchable filters), not the old grid.

            body.Add(right);
            p.Add(body);

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Drop upgrades into the left slots. Cycles: Tape -> Frame -> Mining."));
            return p;
        }

        private static VisualElement UpgSlotRow(ItemContainer c, int idx, string letter, Color accent, SlotBuilder slot)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;

            var badge = new VisualElement();
            badge.style.width = 22; badge.style.height = 22;
            badge.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.18f));
            T.Radius(badge, 5);
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.marginRight = 6;
            badge.pickingMode = PickingMode.Ignore;

            var bl = new Label(letter);
            bl.style.fontSize = 11;
            bl.style.color = new StyleColor(accent);
            bl.style.unityFontStyleAndWeight = FontStyle.Bold;
            bl.pickingMode = PickingMode.Ignore;
            badge.Add(bl);
            row.Add(badge);
            row.Add(slot(c, idx, c.GetSlot(idx), false, false));
            return row;
        }
    }
}
