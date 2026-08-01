// Assets/Scripts/VoxelEngine/Building/PaintFinish.cs
//
// Cosmetic material finishes for placed static blocks and grid blocks.
// Purely visual — no gameplay stats. Applied via the Paint Tool.

using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>Authoritative finish catalogue. Ids are stable for save data.</summary>
    public enum PaintFinishId : byte
    {
        None = 0,
        MatteWhite = 1,
        MatteBlack = 2,
        IndustrialGrey = 3,
        Steel = 4,
        Chrome = 5,
        Carbon = 6,
        Rust = 7,
        Copper = 8,
        Brass = 9,
        HazardYellow = 10,
        CrusaderBlue = 11,
        SignalRed = 12,
        ForestGreen = 13,
        GlossWhite = 14,
        FuturisticTeal = 15,
    }

    public static class PaintFinishCatalog
    {
        public struct Def
        {
            public PaintFinishId id;
            public string name;
            public Color color;
            public float metallic;
            public float smoothness;
        }

        private static readonly Def[] _defs =
        {
            new Def { id = PaintFinishId.None,           name = "None",            color = Color.white,                         metallic = 0f,    smoothness = 0.2f },
            new Def { id = PaintFinishId.MatteWhite,     name = "Matte White",     color = new Color(0.92f, 0.92f, 0.90f),      metallic = 0.05f, smoothness = 0.18f },
            new Def { id = PaintFinishId.MatteBlack,     name = "Matte Black",     color = new Color(0.08f, 0.08f, 0.09f),      metallic = 0.08f, smoothness = 0.15f },
            new Def { id = PaintFinishId.IndustrialGrey, name = "Industrial Grey", color = new Color(0.42f, 0.44f, 0.46f),      metallic = 0.25f, smoothness = 0.28f },
            new Def { id = PaintFinishId.Steel,          name = "Steel",           color = new Color(0.55f, 0.58f, 0.62f),      metallic = 0.75f, smoothness = 0.45f },
            new Def { id = PaintFinishId.Chrome,         name = "Chrome",          color = new Color(0.78f, 0.80f, 0.84f),      metallic = 0.95f, smoothness = 0.92f },
            new Def { id = PaintFinishId.Carbon,         name = "Carbon",          color = new Color(0.12f, 0.12f, 0.13f),      metallic = 0.35f, smoothness = 0.55f },
            new Def { id = PaintFinishId.Rust,           name = "Rust",            color = new Color(0.55f, 0.28f, 0.12f),      metallic = 0.40f, smoothness = 0.22f },
            new Def { id = PaintFinishId.Copper,         name = "Copper",          color = new Color(0.72f, 0.40f, 0.22f),      metallic = 0.85f, smoothness = 0.50f },
            new Def { id = PaintFinishId.Brass,          name = "Brass",           color = new Color(0.78f, 0.62f, 0.28f),      metallic = 0.80f, smoothness = 0.55f },
            new Def { id = PaintFinishId.HazardYellow,   name = "Hazard Yellow",   color = new Color(0.92f, 0.78f, 0.12f),      metallic = 0.15f, smoothness = 0.35f },
            new Def { id = PaintFinishId.CrusaderBlue,   name = "Crusader Blue",   color = new Color(0.18f, 0.32f, 0.62f),      metallic = 0.20f, smoothness = 0.40f },
            new Def { id = PaintFinishId.SignalRed,      name = "Signal Red",      color = new Color(0.78f, 0.16f, 0.14f),      metallic = 0.18f, smoothness = 0.38f },
            new Def { id = PaintFinishId.ForestGreen,    name = "Forest Green",    color = new Color(0.18f, 0.42f, 0.24f),      metallic = 0.12f, smoothness = 0.30f },
            new Def { id = PaintFinishId.GlossWhite,     name = "Gloss White",     color = new Color(0.95f, 0.95f, 0.97f),      metallic = 0.10f, smoothness = 0.88f },
            new Def { id = PaintFinishId.FuturisticTeal, name = "Futuristic Teal", color = new Color(0.12f, 0.62f, 0.68f),      metallic = 0.55f, smoothness = 0.70f },
        };

        public static int Count => _defs.Length;

        public static Def Get(PaintFinishId id)
        {
            for (int i = 0; i < _defs.Length; i++)
                if (_defs[i].id == id) return _defs[i];
            return _defs[0];
        }

        public static Def GetByIndex(int index)
        {
            if (index < 0 || index >= _defs.Length) return _defs[0];
            return _defs[index];
        }

        public static PaintFinishId Next(PaintFinishId current, int delta)
        {
            int idx = 0;
            for (int i = 0; i < _defs.Length; i++)
                if (_defs[i].id == current) { idx = i; break; }
            // Skip None when cycling from a painted finish; include None when going backwards onto clear.
            int n = _defs.Length;
            int next = ((idx + delta) % n + n) % n;
            return _defs[next].id;
        }

        public static string DisplayName(PaintFinishId id) => Get(id).name;
    }
}
