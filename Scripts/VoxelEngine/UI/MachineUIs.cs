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
        public static VisualElement GasTankPanel(GasTank t)
        {
            var p = T.MachinePanel();

            string gasName  = t.storedGasType == GasType.None ? "Empty" : t.storedGasType.ToString();
            Color  gasColor = t.storedGasType switch
            {
                GasType.Hydrogen => new Color(0.28f, 0.68f, 1f),
                GasType.Oxygen   => new Color(0.90f, 0.38f, 0.28f),
                GasType.Steam    => new Color(0.82f, 0.82f, 0.88f),
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

            p.Add(T.StatRow("📥", "Accept Input",  t.acceptInput  ? "YES" : "NO",
                t.acceptInput  ? T.AccentGreen : T.AccentRed));
            p.Add(T.StatRow("📤", "Allow Output",  t.allowOutput  ? "YES" : "NO",
                t.allowOutput  ? T.AccentGreen : T.AccentRed));

            p.Add(T.Spacer(8));
            p.Add(T.Muted("Stores a single gas type. Connect via gas pipes to machines."));
            return p;
        }

// ════════════════════════════════════════════════════════════
//                        QUARRY
// ════════════════════════════════════════════════════════════
public static VisualElement QuarryPanel(Quarry q, SlotBuilder slot)
{
    q.EnsureOutputPublic();
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

    // Power
    if (pc != null)
        p.Add(T.StatRow("\u26A1", "Power",
            powered ? $"{q.EffectivePowerDraw:0} W  \u00B7  Connected" : "Disconnected",
            powered ? T.AccentGreen : T.AccentRed));

    // Stats
    p.Add(T.StatRow("\uD83D\uDCD0", "Area",
        $"{q.AreaX}\u00D7{q.AreaZ}  ({q.EffectiveSize}\u00B2)", T.AccentCyan));
    p.Add(T.StatRow("\u2B07", "Depth",
        $"{q.CurrentDepth} / {q.MaxDepth}", T.TextPrimary));
    p.Add(T.StatRow("\u23F1", "Speed",
        $"{q.EffectiveMineInterval:F2}s", T.AccentTeal));
    p.Add(T.StatRow("\uD83D\uDD27", "Tier",
        $"{q.quarryTier}", T.TextSecondary));

    // Mining progress
    p.Add(T.Spacer(4));
    var (progBar, _) = T.ProgressBar(q.IsMining ? q.MineProgress01 : 0f, T.AccentCyan, 8, true);
    p.Add(progBar);

    // Upgrade slots (actual drop-target slots)
    q.EnsureUpgrades();
    p.Add(T.Divider());
    p.Add(T.Subtitle("Upgrades"));
    var upgGrid = T.SlotGrid();
    for (int i = 0; i < q.upgradeC.Size; i++)
        upgGrid.Add(slot(q.upgradeC, i, q.upgradeC.GetSlot(i), false, false));
    p.Add(upgGrid);
    // Upgrade level cards
    var ug = new VisualElement();
    ug.style.flexDirection = FlexDirection.Row;
    ug.style.flexWrap = Wrap.Wrap;
    ug.style.marginTop = 6;
    ug.Add(UpgCard("Range",      q.InstalledRangeLevel,      Quarry.MaxRangeLevel,      T.AccentGold,   "\uD83D\uDCCF", "+1 size  \u00B7  +25W"));
    ug.Add(UpgCard("Speed",      q.InstalledSpeedLevel,      Quarry.MaxSpeedLevel,      T.AccentTeal,   "\u26A1",       "-0.04s  \u00B7  +25W"));
    ug.Add(UpgCard("Efficiency", q.InstalledEfficiencyLevel, Quarry.MaxEfficiencyLevel, T.AccentPurple, "\u2B50",       "-35W"));
    p.Add(ug);

    // Port Config button
    var portCfg = q.GetComponent<VoxelEngine.Transport.PortConfig>();
    if (portCfg != null)
    {
        p.Add(T.Spacer(6));
        p.Add(T.SmallButton("\u2699  Configure Ports", () =>
            PortConfigHud.Open(q.gameObject, portCfg), T.AccentTeal));
    }

    // Output    // Output
    p.Add(T.Divider());
    p.Add(T.Subtitle("Output"));
    p.Add(SortRow(q.Output));
    var grid = T.SlotGrid();
    var output = q.Output;
    for (int i = 0; i < output.Size; i++)
        grid.Add(slot(output, i, output.GetSlot(i), false, true));
    p.Add(grid);
    p.Add(T.Spacer(8));
    p.Add(T.Muted("Right-click with Quarry Upgrades to install. Connect item pipes to auto-export."));
    return p;
}

private static VisualElement UpgCard(string name, int lvl, int max, Color accent, string icon, string desc)
{
    var card = new VisualElement();
    card.style.flexGrow = 1; card.style.minWidth = 110;
    card.style.paddingTop = 7; card.style.paddingBottom = 7;
    card.style.paddingLeft = 9; card.style.paddingRight = 9;
    card.style.marginRight = 5; card.style.marginBottom = 5;
    card.style.backgroundColor = new StyleColor(new Color(T.BgCard.r, T.BgCard.g, T.BgCard.b, 0.92f));
    T.Radius(card, T.CardRadius);
    T.Border(card, 1, new Color(accent.r, accent.g, accent.b, 0.18f));
    card.pickingMode = PickingMode.Ignore;

    var tr = new VisualElement();
    tr.style.flexDirection = FlexDirection.Row; tr.style.alignItems = Align.Center; tr.style.marginBottom = 3;
    tr.pickingMode = PickingMode.Ignore;
    var ico = new Label(icon); ico.style.fontSize = 11; ico.style.marginRight = 4; ico.style.color = new StyleColor(accent); ico.pickingMode = PickingMode.Ignore;
    tr.Add(ico);
    var nm = new Label(name); nm.style.fontSize = 9; nm.style.color = new StyleColor(T.TextSecondary); nm.style.unityFontStyleAndWeight = FontStyle.Bold; nm.style.flexGrow = 1; nm.pickingMode = PickingMode.Ignore;
    tr.Add(nm);
    var lv = new Label($"{lvl}/{max}"); lv.style.fontSize = 10; lv.style.color = new StyleColor(lvl >= max ? T.AccentGreen : accent); lv.style.unityFontStyleAndWeight = FontStyle.Bold; lv.pickingMode = PickingMode.Ignore;
    tr.Add(lv);
    card.Add(tr);

    var bg = new VisualElement(); bg.style.height = 4; bg.style.backgroundColor = new StyleColor(new Color(T.BgBase.r, T.BgBase.g, T.BgBase.b, 0.8f)); T.Radius(bg, 2); bg.style.marginBottom = 3; bg.pickingMode = PickingMode.Ignore;
    var fill = new VisualElement(); fill.style.height = 4; fill.style.backgroundColor = new StyleColor(accent); T.Radius(fill, 2); fill.style.width = Length.Percent((float)lvl / Mathf.Max(1, max) * 100f); fill.pickingMode = PickingMode.Ignore;
    bg.Add(fill); card.Add(bg);

    var dl = new Label(desc); dl.style.fontSize = 8; dl.style.color = new StyleColor(T.TextMuted); dl.pickingMode = PickingMode.Ignore;
    card.Add(dl);
    return card;
}
    }
}
