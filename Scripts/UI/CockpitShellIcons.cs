// Assets/Scripts/VoxelEngine/UI/CockpitShellIcons.cs
//
// Side-profile icons for the two 2x2x2 industrial cockpit shells on the build wheel:
//   - Style 1 (Wraparound Cockpit): harvester canopy with raked front + chin + floor glazing and roof work-lights.
//   - Style 2 (Reinforced Cockpit): heavy-duty armored cab with bolted glacis, dozer bumper, FOPS cage and radome.

using UnityEngine;

namespace VoxelEngine.UI
{
    public static class CockpitShellIcons
    {
        public static Texture2D For(int style) => IconAtlas.Get("cockpitshell:" + style, b =>
        {
            if (style == 2) DrawReinforcedCab(b);
            else DrawWraparoundHarvester(b);
        });

        private static void DrawWraparoundHarvester(LineArtBuilder b)
        {
            // Panoramic harvester profile facing right: overhanging floodlight roof, raked upper screen,
            // raked lower chin glass, full belly floor glass and slim rear bulkhead.
            b.Closed(
                new Vector2(0.14f, 0.12f),
                new Vector2(1.06f, 0.12f),
                new Vector2(1.36f, 0.42f),
                new Vector2(1.26f, 0.96f),
                new Vector2(0.14f, 0.96f));

            // Overhanging flood-lamp visor at top front
            b.Polyline(new Vector2(1.16f, 0.96f), new Vector2(1.40f, 0.96f), new Vector2(1.36f, 0.88f));

            // Slim mullions & floor glass read lines
            b.Line(new Vector2(0.32f, 0.12f), new Vector2(0.32f, 0.96f));
            b.Line(new Vector2(0.84f, 0.12f), new Vector2(0.84f, 0.96f));
            b.Line(new Vector2(0.96f, 0.42f), new Vector2(1.36f, 0.42f));
            b.Line(new Vector2(0.42f, 0.18f), new Vector2(1.00f, 0.18f));

            // Elevated operator seat silhouette visible through side glass
            b.Polyline(new Vector2(0.50f, 0.34f), new Vector2(0.54f, 0.68f));
            b.Line(new Vector2(0.52f, 0.38f), new Vector2(0.70f, 0.38f));
        }

        private static void DrawReinforcedCab(LineArtBuilder b)
        {
            // Rugged heavy-duty machinery cab: solid thick lower armor, sloped glacis,
            // heavy front push bumper, wide upper window band, FOPS roof ribs & sensor dome.
            b.Closed(
                new Vector2(0.12f, 0.12f),
                new Vector2(1.30f, 0.12f),
                new Vector2(1.30f, 0.50f),
                new Vector2(1.18f, 0.58f),
                new Vector2(1.16f, 0.96f),
                new Vector2(0.12f, 0.96f));

            // Heavy push bumper + clevis
            b.Rect(1.30f, 0.12f, 1.42f, 0.32f);

            // Solid armored sill line & upper panoramic window band
            b.Line(new Vector2(0.12f, 0.56f), new Vector2(1.18f, 0.56f));
            b.Rect(0.26f, 0.60f, 1.10f, 0.90f);
            b.Line(new Vector2(0.76f, 0.60f), new Vector2(0.76f, 0.90f));

            // Lower armor door seam & louver bars
            b.Rect(0.42f, 0.18f, 0.80f, 0.50f);
            b.Line(new Vector2(0.96f, 0.24f), new Vector2(1.22f, 0.24f));
            b.Line(new Vector2(0.96f, 0.34f), new Vector2(1.22f, 0.34f));

            // FOPS roof guard & radome
            b.Line(new Vector2(0.18f, 1.02f), new Vector2(1.14f, 1.02f));
            b.Circle(new Vector2(0.46f, 1.02f), 0.08f, 14);
        }
    }
}
