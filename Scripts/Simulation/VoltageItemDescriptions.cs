// Assets/Scripts/VoxelEngine/Simulation/VoltageItemDescriptions.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — VOLTAGE SYSTEM ITEM DESCRIPTIONS            ║
// ║  Centralised description strings for all voltage-related items  ║
// ║  and blocks. Referenced by the crafting UI, tooltips, and the   ║
// ║  research tree to keep lore and gameplay hints consistent.      ║
// ╚══════════════════════════════════════════════════════════════════╝

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Static class holding description strings for every item and block
    /// in the voltage/power distribution system. Used by tooltips,
    /// crafting screens, and the research tree preview.
    /// </summary>
    public static class VoltageItemDescriptions
    {
        // ── Low Voltage Infrastructure ────────────────────────────────

        public const string PowerPole =
            "Standard low-voltage distribution pole. Supports up to 6 wire connections " +
            "spanning up to 15 meters each. Use these to distribute power from generators " +
            "to nearby machines within your base. For distances beyond 15 m, chain multiple " +
            "poles or use an Electrical Substation.";

        public const string ElectricalSubstation =
            "Long-range power relay station. Bridges two low-voltage pole networks up to " +
            "150 meters apart. Essential for sprawling bases where running dozens of poles " +
            "would be impractical. Acts as a step-up relay within the LV distribution grid.";

        public const string Wire =
            "Copper-core insulated wire. Crafted at the crafting bench and used to connect " +
            "power poles to machines, generators, and substations. Each segment spans up to " +
            "15 m between connection points. Wire has a finite power capacity — exceeding it " +
            "causes overheating and power loss.";

        public const string CableInput =
            "Standard cable input socket. Attach to any machine that consumes power. " +
            "Accepts a wire from a nearby power pole or generator. Machines without a " +
            "connected cable input will show 'NO POWER' in their status panel.";

        public const string CableOutput =
            "Standard cable output socket. Attach to any generator or power source. " +
            "Feeds power into a wire connected to a pole or directly to a machine.";

        // ── High Voltage Infrastructure ───────────────────────────────

        public const string HighVoltagePole =
            "Heavy-duty steel lattice transmission tower for high-voltage power lines. " +
            "Stands 12 meters tall with dual cross-arms and ceramic insulator strings. " +
            "HV lines have UNLIMITED power throughput and can span up to 200 meters " +
            "between towers. Required for transmitting power above 25 MW over long distances. " +
            "Cannot be connected directly to machines — requires a Step-Down Transformer.";

        public const string StepUpTransformer =
            "Large step-up transformer station (LV → HV). Converts low-voltage power from " +
            "your base's distribution network into high voltage for long-distance transmission. " +
            "Required when your total power output exceeds 25 MW. Features dual transformer " +
            "tanks with radiator cooling fins, HV bushings, and a control cabinet. " +
            "Identified by its BLUE accent lighting and upward arrow indicators. " +
            "Conversion loss: 2%. Throughput: 200 MW maximum.";

        public const string StepDownTransformer =
            "Large step-down transformer station (HV → LV). Converts high-voltage power " +
            "from transmission towers back to low voltage for use by machines and buildings. " +
            "Must be placed between HV transmission lines and your local power poles. " +
            "Identified by its AMBER accent lighting, downward arrow indicators, and wider " +
            "layout with surge arresters. Distinct from the blue Step-Up station. " +
            "Conversion loss: 2%. Throughput: 200 MW maximum.";

        public const string HVTransmissionLine =
            "High-voltage aluminium conductor steel-reinforced (ACSR) transmission cable. " +
            "Spans up to 200 meters between HV towers with unlimited power throughput. " +
            "Only connects High Voltage Poles and Transformer Stations. " +
            "WARNING: Never connect HV lines directly to machines — the voltage will " +
            "destroy any equipment not rated for high voltage.";

        // ── Lighting ──────────────────────────────────────────────────

        public const string GridLight =
            "Compact spotlight or floodlight for grid vehicles and static bases. " +
            "Configurable colour, intensity (up to 3x), range (up to 20 m), and beam angle. " +
            "Draws 25 W from the grid's power supply. Toggles with the grid's master " +
            "power switch.";

        public const string LEDStrip =
            "Thin, flexible LED accent light strip. Snaps to grid edges and static " +
            "surfaces. Configurable colour, brightness, and animation mode (Static, Pulse, " +
            "Blink, or Chase). Draws 5 W. Perfect for marking pathways, signalling machine " +
            "status, or adding ambiance to your base.";

        public const string StaticFloodLight =
            "Heavy-duty floodlight for base illumination. Wall-mounted and tripod variants " +
            "available. Wide beam angle, long range, and high intensity. Draw 100 W from " +
            "the power network. Ideal for lighting up factory floors, landing pads, and " +
            "perimeter security zones.";

        // ── Machines ──────────────────────────────────────────────────

        public const string Crusher =
            "Industrial ore crusher. Grinds raw ore into fine dust for bonus smelting " +
            "yield, and crushes stone into gravel for construction. Features a chance to " +
            "produce byproduct materials (slag, dust). Draws 250 W while processing, 8 W " +
            "at idle. Accepts items from conveyor belts and outputs to belts or chutes. " +
            "Supports Speed and Efficiency upgrade modules.";

        public const string AssemblerMk1 =
            "Tier 1 automated assembler. Combines multiple input materials into higher-tier " +
            "components — gears, circuits, motors, steel beams, and more. 4 input slots, " +
            "4 output slots, 2 upgrade slots. Draws 300 W while processing, 10 W at idle. " +
            "Integrates with conveyor belts for fully automated production lines.";

        public const string AssemblerMk2 =
            "Tier 2 automated assembler. 6 input slots, 6 output slots, 3 upgrade slots. " +
            "1.5x speed multiplier compared to Mk.1. Handles more complex multi-component " +
            "recipes required for advanced machinery and grid systems.";

        public const string AssemblerMk3 =
            "Tier 3 automated assembler. 9 input slots, 8 output slots, 4 upgrade slots. " +
            "2.5x speed multiplier. The pinnacle of automated crafting — required for " +
            "endgame components, nuclear fuel rods, and aerospace alloys.";

        public const string ConveyorBelt =
            "Standard item transport belt. Carries items visually from input to output. " +
            "Three speed tiers: Basic (2 items/s), Fast (5 items/s), Express (10 items/s). " +
            "Snaps to machines, chutes, and other belts. Items ride on top of the belt " +
            "surface with a slight lateral offset for visual variety.";

        public const string ConveyorChute =
            "Vertical item transport chute. Drops items from one elevation to another — " +
            "essential for multi-level factory designs. Items slide visually through the " +
            "channel. Connects belts and machines at different heights.";

        // ── Gameplay Hints ────────────────────────────────────────────

        public const string VoltageHint =
            "TIP: When your power network exceeds 25 MW, standard low-voltage wires can no " +
            "longer carry the load. Build a Step-Up Transformer (blue) to convert to high " +
            "voltage, run HV transmission lines between lattice towers, then build a " +
            "Step-Down Transformer (amber) to convert back to low voltage near your machines.";

        public const string TransformerPlacementHint =
            "TIP: Place the Step-Up Transformer near your generators and the Step-Down " +
            "Transformer near your machines. The blue station steps voltage UP for " +
            "transmission; the amber station steps voltage DOWN for local use. " +
            "Each conversion loses 2% of power.";
    }
}
