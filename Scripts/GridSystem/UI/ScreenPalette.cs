// Assets/Scripts/VoxelEngine/GridSystem/UI/ScreenPalette.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║           PER-PAGE INK PALETTES (17.42.0-dev)                   ║
// ║                                                                  ║
// ║  Every page type has its own voice: the power graph speaks in    ║
// ║  cold instrument cyan, the mining dial in deep amber, the cargo  ║
// ║  manifest in terminal green. The five ink classes on the canvas  ║
// ║  are given their colours here, so a page's mood is one lookup    ║
// ║  away and never hard-coded into a layout.                        ║
// ║                                                                  ║
// ║  The screen's own text colour still owns the Custom page and     ║
// ║  every page keeps the same CLASS meaning: Warn is always the     ║
// ║  colour of "something needs you", Ok is always "healthy".        ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.GridSystem.UI
{
    /// <summary>The five ink colours one page draws with.</summary>
    public readonly struct ScreenPalette
    {
        public readonly Color Primary;
        public readonly Color Accent;
        public readonly Color Dim;
        public readonly Color Warn;
        public readonly Color Ok;

        public ScreenPalette(Color primary, Color accent, Color dim, Color warn, Color ok)
        {
            Primary = primary; Accent = accent; Dim = dim; Warn = warn; Ok = ok;
        }

        public Color For(ScreenInk ink) => ink switch
        {
            ScreenInk.Accent => Accent,
            ScreenInk.Dim => Dim,
            ScreenInk.Warn => Warn,
            ScreenInk.Ok => Ok,
            _ => Primary,
        };

        /// <summary>Every ink collapses to one colour, for pages whose whole language is
        /// the player's own text colour.</summary>
        public static ScreenPalette Monochrome(Color colour)
        {
            Color dim = new Color(colour.r * 0.55f, colour.g * 0.55f, colour.b * 0.55f, colour.a);
            return new ScreenPalette(colour, colour, dim, colour, colour);
        }

        /// <summary>The palette for a page type. Unknown pages fall back to the screen's
        /// own text colour, so a new page never renders invisible.</summary>
        public static ScreenPalette ForPage(ScreenDataMode mode, Color fallbackText)
        {
            switch (mode)
            {
                case ScreenDataMode.Manifest:
                case ScreenDataMode.Summary:
                    // The green terminal: cargo and mixed pages read like a station board.
                    return new ScreenPalette(
                        primary: C(0.42f, 0.95f, 0.45f),
                        accent: C(0.62f, 1.00f, 0.62f),
                        dim: C(0.16f, 0.52f, 0.24f),
                        warn: C(1.00f, 0.72f, 0.22f),
                        ok: C(0.55f, 1.00f, 0.55f));

                case ScreenDataMode.Mining:
                case ScreenDataMode.MiningCargo:
                    // The digging dial: deep amber on near-black.
                    return new ScreenPalette(
                        primary: C(1.00f, 0.72f, 0.24f),
                        accent: C(1.00f, 0.58f, 0.10f),
                        dim: C(0.55f, 0.30f, 0.08f),
                        warn: C(1.00f, 0.30f, 0.16f),
                        ok: C(1.00f, 0.80f, 0.30f));

                case ScreenDataMode.BatteryBank:
                    // Green cells, cool case, quiet labels.
                    return new ScreenPalette(
                        primary: C(0.88f, 0.95f, 0.92f),
                        accent: C(0.35f, 0.85f, 0.95f),
                        dim: C(0.35f, 0.45f, 0.48f),
                        warn: C(1.00f, 0.45f, 0.25f),
                        ok: C(0.25f, 0.92f, 0.35f));

                case ScreenDataMode.PlanetMap:
                    // Green chart paper, cold globe, hot name.
                    return new ScreenPalette(
                        primary: C(0.75f, 0.95f, 0.85f),
                        accent: C(0.22f, 0.85f, 0.90f),
                        dim: C(0.13f, 0.45f, 0.22f),
                        warn: C(1.00f, 0.85f, 0.25f),
                        ok: C(0.35f, 0.95f, 0.55f));

                case ScreenDataMode.Farm:
                case ScreenDataMode.FarmPlots:
                    // Cyan-green growth, white ready, red dead, purple empties.
                    return new ScreenPalette(
                        primary: C(0.70f, 1.00f, 0.92f),
                        accent: C(0.30f, 0.95f, 0.85f),
                        dim: C(0.48f, 0.36f, 0.78f),
                        warn: C(1.00f, 0.32f, 0.32f),
                        ok: C(0.35f, 1.00f, 0.65f));

                case ScreenDataMode.InventoryGrid:
                    // White names, green counts, amber fill line.
                    return new ScreenPalette(
                        primary: C(0.92f, 0.94f, 0.96f),
                        accent: C(0.72f, 0.80f, 0.88f),
                        dim: C(0.42f, 0.46f, 0.52f),
                        warn: C(1.00f, 0.82f, 0.25f),
                        ok: C(0.35f, 0.85f, 0.55f));

                case ScreenDataMode.Inventory:
                    // The storage monitor: quiet steel, white numbers.
                    return new ScreenPalette(
                        primary: C(0.86f, 0.88f, 0.90f),
                        accent: C(0.62f, 0.72f, 0.82f),
                        dim: C(0.38f, 0.42f, 0.48f),
                        warn: C(1.00f, 0.60f, 0.30f),
                        ok: C(0.40f, 0.85f, 0.60f));

                case ScreenDataMode.FillBars:
                    // The green LCD strip under the glass.
                    return new ScreenPalette(
                        primary: C(0.55f, 1.00f, 0.55f),
                        accent: C(0.35f, 0.95f, 0.40f),
                        dim: C(0.15f, 0.50f, 0.22f),
                        warn: C(1.00f, 0.70f, 0.25f),
                        ok: C(0.45f, 1.00f, 0.45f));

                case ScreenDataMode.PowerGraph:
                case ScreenDataMode.Power:
                case ScreenDataMode.Bars:
                case ScreenDataMode.System:
                case ScreenDataMode.Environment:
                case ScreenDataMode.Speed:
                case ScreenDataMode.AutoRunPilotInfo:
                case ScreenDataMode.RouteRecorderInfo:
                default:
                    // The instrument family: cold cyan, quiet slate.
                    if (mode == ScreenDataMode.Custom || mode == ScreenDataMode.TeamBanner
                        || mode == ScreenDataMode.Camera)
                        return Monochrome(fallbackText);
                    return new ScreenPalette(
                        primary: C(0.42f, 0.85f, 0.95f),
                        accent: C(0.30f, 0.72f, 0.92f),
                        dim: C(0.22f, 0.34f, 0.42f),
                        warn: C(1.00f, 0.55f, 0.25f),
                        ok: C(0.35f, 0.90f, 0.55f));
            }
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b, 1f);
    }
}
