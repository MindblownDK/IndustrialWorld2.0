// Assets/Scripts/VoxelEngine/UI/LucideIcons.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          LUCIDE ICON FONT — codepoint constants                ║
// ║   Subset of the Lucide icon font used across IndustrialWorld.  ║
// ║   Full font lives at Resources/Fonts/Lucide.ttf                ║
// ║   Source: https://lucide.dev/icons/                            ║
// ╚══════════════════════════════════════════════════════════════════╝
//
// Usage: assign Lucide.ttf as a Font to your Label/Button (or wrap a single
// glyph in a child Label whose unityFontDefinition points to it), then set
// the text to one of the constants below.
//
// Every codepoint here is from the official Lucide font CSS:
// https://unpkg.com/lucide-static@latest/font/lucide.css
// Cross-referenced via juliettef/IconFontCppHeaders → IconsLucide.cs.

namespace VoxelEngine.UI
{
    /// <summary>
    /// Subset of Lucide icon codepoints used by the Main Menu and HUD.
    /// Extend as new icons are needed — do not inline raw \uXXXX strings
    /// elsewhere in the codebase (single source of truth).
    /// </summary>
    public static class LucideIcons
    {
        // ── Resource path for runtime loading ─────────────────────────
        public const string ResourcePath = "Fonts/Lucide";

        // ── Main-menu glyphs ──────────────────────────────────────────
        public const string Play        = "\ue08e"; // play
        public const string Plus        = "\ue09f"; // plus  (NEW WORLD)
        public const string Settings    = "\ue0b5"; // settings (gear)
        public const string X           = "\ue123"; // x (close / quit / delete)
        public const string ArrowLeft   = "\ue04f"; // arrow-left  (BACK)
        public const string Factory     = "\ue4f7"; // factory (brand)
        public const string Globe       = "\ue068"; // globe  (no-worlds placeholder)
        public const string Dice5       = "\ue4a0"; // dices  (RANDOM seed)
        public const string Save        = "\ue0b1"; // save
        public const string Trash       = "\ue0bd"; // trash-2
    }
}
