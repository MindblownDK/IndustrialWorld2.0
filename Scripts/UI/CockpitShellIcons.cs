// Assets/Scripts/VoxelEngine/UI/CockpitShellIcons.cs
//
// The two cockpit shells, drawn side-on for the build wheel. A cockpit is a shape rather
// than a cube, so a generic block wedge says nothing about which one you are about to
// build - a low nose with a raked screen and a tall square cab read at a glance even at
// fifty-eight pixels.

using UnityEngine;

namespace VoxelEngine.UI
{
    public static class CockpitShellIcons
    {
        /// <summary>Side view of a shell, facing right. Keyed per style so both are cached
        /// once and shared by every wedge that shows them.</summary>
        public static Texture2D For(int style) => IconAtlas.Get("cockpitshell:" + style, b =>
        {
            if (style == 2) DrawCab(b);
            else DrawCrawler(b);
        });

        /// <summary>A wheel, as two concentric circles: the tyre and the rim.</summary>
        private static void Wheel(LineArtBuilder b, float x, float y, float r)
        {
            b.Circle(new Vector2(x, y), r, 18);
            b.Circle(new Vector2(x, y), r * 0.42f, 14);
        }

        /// <summary>
        /// The crawler: a long low body, one raked windscreen carried down to the sill, a
        /// roll hoop behind the cab and a bumper out front.
        /// </summary>
        private static void DrawCrawler(LineArtBuilder b)
        {
            // Hull, from the rear step to the bumper.
            b.Closed(new Vector2(0.06f, 0.10f), new Vector2(1.34f, 0.10f),
                     new Vector2(1.34f, 0.44f), new Vector2(0.06f, 0.44f));
            // Bumper lip and the step behind the rear wheel.
            b.Line(new Vector2(1.34f, 0.14f), new Vector2(1.46f, 0.14f));
            b.Line(new Vector2(1.46f, 0.14f), new Vector2(1.46f, 0.30f));
            b.Line(new Vector2(1.34f, 0.30f), new Vector2(1.46f, 0.30f));

            // Cab: rear pillar, roof, raked screen down to the bonnet line.
            b.Polyline(new Vector2(0.18f, 0.44f), new Vector2(0.18f, 0.90f),
                       new Vector2(0.98f, 0.90f), new Vector2(1.20f, 0.44f));

            // Windscreen and the door line, so the glass reads as glass.
            b.Line(new Vector2(0.98f, 0.86f), new Vector2(1.14f, 0.50f));
            b.Line(new Vector2(0.62f, 0.44f), new Vector2(0.62f, 0.86f));

            // Headlight and the roll hoop.
            b.Line(new Vector2(1.24f, 0.36f), new Vector2(1.32f, 0.36f));
            b.Polyline(new Vector2(0.10f, 0.44f), new Vector2(0.10f, 0.78f));

            Wheel(b, 0.38f, 0.10f, 0.14f);
            Wheel(b, 1.06f, 0.10f, 0.14f);
        }

        /// <summary>
        /// The cab: a tall square box with an upright screen, a grille at the front and the
        /// survey dome on the roof.
        /// </summary>
        private static void DrawCab(LineArtBuilder b)
        {
            // Body: one tall rectangle with a slightly proud bumper.
            b.Rect(0.12f, 0.12f, 1.28f, 1.10f);
            b.Line(new Vector2(1.28f, 0.16f), new Vector2(1.40f, 0.16f));
            b.Line(new Vector2(1.40f, 0.16f), new Vector2(1.40f, 0.34f));
            b.Line(new Vector2(1.28f, 0.34f), new Vector2(1.40f, 0.34f));

            // Windscreen, door line and the grille bars.
            b.Rect(0.30f, 0.62f, 1.16f, 0.98f);
            b.Line(new Vector2(0.72f, 0.12f), new Vector2(0.72f, 0.62f));
            b.Line(new Vector2(1.02f, 0.20f), new Vector2(1.24f, 0.20f));
            b.Line(new Vector2(1.02f, 0.28f), new Vector2(1.24f, 0.28f));
            b.Line(new Vector2(1.02f, 0.36f), new Vector2(1.24f, 0.36f));

            // Roof dome and its mast.
            b.Circle(new Vector2(0.56f, 1.10f), 0.10f, 16);
            b.Line(new Vector2(0.56f, 1.20f), new Vector2(0.56f, 1.32f));

            Wheel(b, 0.38f, 0.12f, 0.13f);
            Wheel(b, 1.04f, 0.12f, 0.13f);
        }
    }
}
