// Assets/Scripts/VoxelEngine/UI/JumpTargetIcons.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — JUMP DESTINATION ICON LIBRARY         ║
// ║                                                                  ║
// ║  Drawn marks for the jump drive dial. A destination has to be    ║
// ║  readable at 58 pixels while the ship is moving, so each kind    ║
// ║  gets a silhouette that cannot be confused with the others:      ║
// ║  a ringed planet, a cratered moon, a beacon mast, a route        ║
// ║  diamond. The hub glyphs report drive state with the same hand.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.UI
{
    public static class JumpTargetIcons
    {
        /// <summary>A charted planet: a disc with an inclined ring and a terminator.</summary>
        public static Texture2D Planet => IconAtlas.Get("jump:planet", b =>
        {
            b.Circle(Vector2.zero, 0.34f, 40);
            b.Arc(Vector2.zero, 0.52f, 0.17f, 0f, 360f, 44);
            b.Arc(new Vector2(0.06f, 0f), 0.22f, 0.33f, 90f, 270f, 18);
        });

        /// <summary>A moon: a smaller disc, cratered, with no ring.</summary>
        public static Texture2D Moon => IconAtlas.Get("jump:moon", b =>
        {
            b.Circle(Vector2.zero, 0.42f, 40);
            b.Circle(new Vector2(-0.12f, 0.12f), 0.11f, 14);
            b.Circle(new Vector2(0.14f, -0.06f), 0.07f, 12);
            b.Circle(new Vector2(-0.04f, -0.22f), 0.05f, 10);
        });

        /// <summary>A powered beacon: a mast on a tripod throwing two pulse arcs.</summary>
        public static Texture2D Beacon => IconAtlas.Get("jump:beacon", b =>
        {
            b.Line(new Vector2(0f, -0.42f), new Vector2(0f, 0.22f));
            b.Line(new Vector2(0f, -0.42f), new Vector2(-0.26f, -0.5f));
            b.Line(new Vector2(0f, -0.42f), new Vector2(0.26f, -0.5f));
            b.Circle(new Vector2(0f, 0.3f), 0.1f, 14);
            b.Arc(new Vector2(0f, 0.3f), 0.26f, 0.26f, 20f, 160f, 14);
            b.Arc(new Vector2(0f, 0.3f), 0.42f, 0.42f, 30f, 150f, 16);
        });

        /// <summary>A route-book destination: a plotted diamond at the end of a dashed leg.</summary>
        public static Texture2D Route => IconAtlas.Get("jump:route", b =>
        {
            b.Closed(new Vector2(0.18f, 0.34f), new Vector2(0.5f, 0.02f),
                     new Vector2(0.18f, -0.3f), new Vector2(-0.14f, 0.02f));
            b.Line(new Vector2(-0.52f, -0.34f), new Vector2(-0.36f, -0.26f));
            b.Line(new Vector2(-0.26f, -0.21f), new Vector2(-0.1f, -0.13f));
            b.Line(new Vector2(0f, -0.08f), new Vector2(0.1f, -0.03f));
            b.Circle(new Vector2(-0.56f, -0.37f), 0.07f, 12);
        });

        /// <summary>Hub glyph: the coil winding up.</summary>
        public static Texture2D Spinup => IconAtlas.Get("jump:spinup", b =>
        {
            b.Arc(Vector2.zero, 0.44f, 0.44f, 40f, 320f, 30);
            b.Polyline(new Vector2(0.28f, 0.42f), new Vector2(0.34f, 0.2f), new Vector2(0.14f, 0.26f));
            b.Line(new Vector2(0f, -0.2f), new Vector2(0f, 0.2f));
            b.Line(new Vector2(-0.16f, 0f), new Vector2(0.16f, 0f));
        });

        /// <summary>Hub glyph: the drive is charged and the throat is open.</summary>
        public static Texture2D JumpReady => IconAtlas.Get("jump:ready", b =>
        {
            b.Circle(Vector2.zero, 0.46f, 40);
            b.Circle(Vector2.zero, 0.2f, 24);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(a), Mathf.Sin(a));
                b.Line(dir * 0.24f, dir * 0.44f);
            }
        });

        /// <summary>Hub glyph: the coil is venting and the drive will not fire.</summary>
        public static Texture2D Cooldown => IconAtlas.Get("jump:cooldown", b =>
        {
            b.Circle(Vector2.zero, 0.44f, 36);
            b.Line(Vector2.zero, new Vector2(0f, 0.28f));
            b.Line(Vector2.zero, new Vector2(0.2f, -0.06f));
            b.Line(new Vector2(-0.44f, 0.44f), new Vector2(0.44f, -0.44f));
        });
    }
}
