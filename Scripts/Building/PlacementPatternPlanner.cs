// Assets/Scripts/VoxelEngine/Building/PlacementPatternPlanner.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      PLACEMENT PATTERN PLANNER — anchor, aim, list of cells      ║
// ║                                                                  ║
// ║  Turns "where the run started" and "where the ghost is now" into  ║
// ║  the exact cells a click will build. Pure maths, no scene state, ║
// ║  so the preview and the commit walk the same list - a run can    ║
// ║  never draw ten cells and place nine.                            ║
// ║                                                                  ║
// ║  Direction selection is deliberate: the axis is whichever of     ║
// ║  the frame's three axes the aim has moved furthest along, so a   ║
// ║  run is always axis-aligned and always predictable. A diagonal   ║
// ║  drag builds the dominant axis, not a ragged staircase.          ║
// ║                                                                  ║
// ║  Caps live in PlacementOptions: ten for a run or a pillar, four  ║
// ║  per edge for a deck, sixteen cells for any single commit.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>One block of a run: exactly what the commit will build.</summary>
    public struct PlacementCell
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    /// <summary>
    /// The two axes a DECK fills, stated by the caller instead of guessed from the frame.
    ///
    /// A frame borrowed from a wall has the wall's NORMAL as its up, so a frame-local Y is the
    /// distance away from that wall and not height at all. Reading a vertical plan out of such
    /// a frame is why a deck could not build a wall - the whole vertical drag landed in the
    /// component the planner ignored. When this is valid the planner uses exactly these two
    /// axes and their pitches; when it is not, the frame decides, as it always did.
    /// </summary>
    public struct DeckPlane
    {
        public bool valid;
        public Vector3 axisA;
        public Vector3 axisB;
        public float pitchA;
        public float pitchB;
    }

    /// <summary>Builds the cell list for the current pattern. Allocation-free once warm.</summary>
    public static class PlacementPatternPlanner
    {
        /// <summary>
        /// How many cells one axis of a deck reaches for a given aim offset.
        ///
        /// A row appears once the aim has covered about a third of the block's own pitch on
        /// that axis - not half. Half a pitch meant a machine 4.65 m across demanded 2.3 m of
        /// aim before the first row existed, so an aim that drifted slightly off one axis
        /// produced a deck with no depth at all.
        /// </summary>
        private static int DeckCount(float offset, float pitch, int cap)
        {
            float reach = Mathf.Abs(offset);
            if (reach <= 0.0001f || pitch <= 0.0001f) return 0;
            int count = Mathf.FloorToInt(reach / pitch + 0.5f);
            if (count == 0 && reach > pitch * 0.34f) count = 1;
            count = Mathf.Clamp(count, 0, cap);
            return offset < 0f ? -count : count;
        }
        /// <summary>
        /// Fills <paramref name="cells"/> with the run from <paramref name="anchor"/> toward
        /// <paramref name="target"/>, anchor first. Returns the number of cells written
        /// (always at least one when the pattern is valid).
        /// </summary>
        public static int Build(PlacementPattern pattern, in PlacementFrame frame,
                                Vector3 anchorPosition, Quaternion anchorRotation,
                                Vector3 targetPosition, float step, Vector3 pitch, int cellBudget,
                                List<PlacementCell> cells, DeckPlane plane = default)
        {
            if (cells == null) return 0;
            cells.Clear();

            if (step <= 0.0001f) step = 1f;

            // 17.44.1-dev: the lattice step says where a cell starts, the pitch says how
            // much room the block standing on it needs. A machine wider than one metre
            // gets pitched by its own footprint so a run never lays it into its neighbour.
            if (pitch.x <= 0.0001f) pitch.x = step;
            if (pitch.y <= 0.0001f) pitch.y = step;
            if (pitch.z <= 0.0001f) pitch.z = step;

            int budget = Mathf.Clamp(cellBudget, 1, PlacementOptions.MaxCells);

            // Slot 0 is always the anchor: a run is never empty, and a click with no drag
            // is still one block.
            cells.Add(new PlacementCell { position = anchorPosition, rotation = anchorRotation });
            if (pattern == PlacementPattern.Single || budget <= 1) return cells.Count;

            Vector3 delta = targetPosition - anchorPosition;
            Vector3 local = frame.valid
                ? frame.ToLocal(targetPosition) - frame.ToLocal(anchorPosition)
                : delta;

            if (pattern == PlacementPattern.Deck)
            {
                // 17.44.11-dev: a deck is counted per lattice step and spaced by the LARGER of
                // the step and the block, so the gesture is always "one metre of drag adds a
                // row or a column" while neighbouring cells stay apart. Counting on the
                // footprint made a four-by-four deck need ten metres of drag; spacing on the
                // footprint alone made a deck of wide machines sprawl across fifteen.
                // 17.45.2-dev: a deck is counted in the block's OWN pitch on both axes, so
                // pointing at a spot fills the rectangle out to that spot. Counted in lattice
                // metres, a machine four metres across needed four metres of aim per column
                // and the far aim saturated one axis while the other stayed at zero - which
                // is a LINE, not an area, and is exactly what "deck places upwards and
                // sideways, not an area" looked like on screen.
                // ── 17.47.1-dev: A DECK IS A PLANE, AND THE PLANE CAN STAND UP. ────────
                // The second axis was always the frame's FORWARD, and the frame's up was
                // never read at all. A deck dragged up a wall therefore put its whole offset
                // into a component nothing looked at, and the plan came out one cell - which
                // is why a deck could not make a wall. The second axis is now whichever of
                // forward and up the aim actually travels along, so a drag across the ground
                // lays a floor and a drag up a face lays a wall, with the same pattern.
                // Which two axes the rectangle fills. A caller that knows the plane - a deck
                // aimed at a face, where the plane is "up the wall and along it" - states it;
                // otherwise the frame's own right and forward/up decide, as before.
                bool deckVertical = Mathf.Abs(local.y) > Mathf.Abs(local.z);
                Vector3 axisRight;
                Vector3 axisForward;
                float deckPitchRight;
                float deckPitchSecond;
                if (plane.valid)
                {
                    axisRight = plane.axisA.sqrMagnitude > 0.0001f ? plane.axisA.normalized : Vector3.right;
                    axisForward = plane.axisB.sqrMagnitude > 0.0001f ? plane.axisB.normalized : Vector3.up;
                    deckPitchRight = Mathf.Max(step, plane.pitchA);
                    deckPitchSecond = Mathf.Max(step, plane.pitchB);
                }
                else
                {
                    axisRight = frame.valid ? frame.right : Vector3.right;
                    axisForward = deckVertical
                        ? (frame.valid ? frame.up : Vector3.up)
                        : (frame.valid ? frame.forward : Vector3.forward);
                    deckPitchRight = Mathf.Max(step, pitch.x);
                    deckPitchSecond = deckVertical ? Mathf.Max(step, pitch.y) : Mathf.Max(step, pitch.z);
                }

                int sideCap = PlacementOptions.MaxDeckSide - 1;
                int alongRight = DeckCount(Vector3.Dot(delta, axisRight), deckPitchRight, sideCap);
                int alongForward = DeckCount(Vector3.Dot(delta, axisForward), deckPitchSecond, sideCap);

                // 17.47.1-dev: A DECK ON A FACE IS A WALL, AND A WALL IS A BAND.
                // An aim that runs straight up a face travels along ONE of the face's two
                // axes and gives the other nothing, which is a stripe one block wide - the
                // shape the player could not call a wall. On a vertical face the axis the aim
                // did not travel along still takes its neighbour, so the same drag that makes
                // a floor run makes a wall two blocks across, and dragging along the wall
                // widens it. A ground aim is untouched by this.
                if (plane.valid && budget >= 4 && (alongRight == 0) != (alongForward == 0))
                {
                    if (alongRight == 0) alongRight = 1;
                    else alongForward = 1;
                }

                if (alongRight == 0 && alongForward == 0) return cells.Count;
                int rows = Mathf.Abs(alongForward);
                int columns = Mathf.Abs(alongRight);
                float signForward = Mathf.Sign(alongForward);
                float signRight = Mathf.Sign(alongRight);

                // 17.45.0-dev: a deck is a RECTANGLE or it is nothing. The loop used to
                // stop the moment the cell budget ran out, so a plan that needed fifteen
                // cells and had twelve drew three full rows and half of the fourth - which
                // is the lopsided, scattered shape the player reported. The rectangle now
                // shrinks its longer side until the whole plan fits, so every deck the
                // preview draws is complete and symmetrical.
                //
                // 17.46.1-dev: AND IT STILL COLLAPSED INTO A LINE. The shrink subtracted from
                // whichever side was LARGER, and on a square aim the two are equal - so the
                // forward axis was always the one shortened, all the way to zero, and a deck
                // aimed diagonally came out as a single sideways row. That is exactly what
                // "deck only works sideways" was. The shrink now keeps the aim's PROPORTIONS:
                // each pass shortens whichever axis is further past its own aim, so a square
                // aim stays square and both axes shrink together.
                int aimedRows = rows;
                int aimedColumns = columns;
                while ((rows + 1) * (columns + 1) > budget && (rows > 0 || columns > 0))
                {
                    if (rows == 0) { columns--; continue; }
                    if (columns == 0) { rows--; continue; }
                    float pastRows = aimedRows > 0 ? rows / (float)aimedRows : 0f;
                    float pastColumns = aimedColumns > 0 ? columns / (float)aimedColumns : 0f;
                    if (pastRows >= pastColumns) rows--;
                    else columns--;
                }

                float pitchRight = pitch.x * 1.04f + 0.12f;
                float pitchForward = deckPitchSecond * 1.04f + 0.12f;

                // 17.46.1-dev: cells are emitted by RINGS - everything one step from the
                // anchor, then everything two steps - instead of row by row. Anything that
                // only draws the first few of a plan (a preview cap, a HUD counter) then
                // shows a centred patch of the deck rather than the anchor's own row, which
                // is the shape that made a deck look like a sideways line on screen.
                for (int ring = 1; ring <= rows + columns; ring++)
                {
                    for (int f = 0; f <= rows; f++)
                    {
                        int r = ring - f;
                        if (r < 0 || r > columns) continue;
                        if (f == 0 && r == 0) continue;
                        if (cells.Count >= budget) return cells.Count;

                        Vector3 offset = axisRight * (signRight * r * pitchRight)
                                       + axisForward * (signForward * f * pitchForward);
                        cells.Add(new PlacementCell { position = anchorPosition + offset, rotation = anchorRotation });
                    }
                }
                return cells.Count;
            }

            // SPAN and PILLAR: one axis, the dominant one.
            Vector3 axis;
            float pitchAlong;
            if (pattern == PlacementPattern.Pillar)
            {
                // 17.44.11-dev: a pillar is VERTICAL, always - straight up from the anchor
                // along gravity. It used to take the frame's up, and a frame borrowed from a
                // neighbouring machine can be rolled, so a "pillar" came out lying on the
                // ground beside the block it started from.
                //
                // 17.45.0-dev: the caller now measures this block's height along GRAVITY for
                // a pillar (BuildSystem.HandlePlacementPattern), so pitch.y is the height of
                // the block even when the frame it was aimed in was borrowed from a wall.
                axis = VoxelEngine.Cosmos.GravityProvider.GetUp(anchorPosition);
                if (axis.sqrMagnitude < 0.0001f) axis = Vector3.up;
                axis.Normalize();
                pitchAlong = pitch.y;
            }
            else
            {
                // 17.44.3-dev: SPAN follows the drag in ANY of the frame's three axes - a
                // wall laid along a face, a row across the ground, or a column built up a
                // slope. It used to consider only right and forward, so a vertical drag
                // could not produce a vertical run at all; PILLAR remains the way to force
                // a column when the drag is ambiguous.
                float ax = Mathf.Abs(local.x);
                float ay = Mathf.Abs(local.y);
                float az = Mathf.Abs(local.z);
                Vector3 axisRight = frame.valid ? frame.right : Vector3.right;
                Vector3 axisUp = frame.valid ? frame.up : Vector3.up;
                Vector3 axisForward = frame.valid ? frame.forward : Vector3.forward;

                if (ay > ax && ay >= az)
                {
                    axis = axisUp;
                    pitchAlong = pitch.y;
                }
                else if (ax >= az)
                {
                    axis = axisRight;
                    pitchAlong = pitch.x;
                }
                else
                {
                    axis = axisForward;
                    pitchAlong = pitch.z;
                }
            }

            // The block's own measured size along this axis, before the spacing shim:
            // a pillar counts in whole block heights, so it needs the honest number.
            float rawPitch = pitchAlong;

            // How far the aim has moved ALONG the chosen axis. A drag across the run's
            // own axis contributes nothing, which is what keeps the run dead straight.
            // 17.44.15-dev: a probe at a cell's own centre must not land inside its
            // neighbour, so cells are laid a few centimetres clear of touching.
            pitchAlong = rawPitch * 1.04f + 0.12f;

            float distance = Vector3.Dot(delta, axis);

            int cap = Mathf.Min(PlacementOptions.MaxRun, budget - 1);
            int count;

            if (pattern == PlacementPattern.Pillar)
            {
                // ── 17.45.0-dev: A PILLAR IS COUNTED IN BLOCKS, NOT IN METRES ──────────
                // A stack is built by LOOKING up a wall or up the sky, and the aim then
                // travels tens of metres for a small tilt. Counting that in lattice metres
                // turned a glance upward into a fifteen-metre tower, and counting it in
                // footprints - the previous rule - made the same glance do nothing at all
                // because a drag across the ground is one block per 5 metres of walking.
                // So: one cell per BLOCK HEIGHT of aim above the anchor. Look up by one
                // machine and the stack grows by one machine, which is what the gesture
                // has always meant.
                int look = Mathf.RoundToInt(distance / Mathf.Max(0.01f, rawPitch));
                if (look != 0)
                {
                    count = Mathf.Clamp(look, -cap, cap);
                }
                else
                {
                    // Nothing vertical in the aim: the planar drag drives the stack, one
                    // lattice metre per cell. Push away to build up, pull back to build down.
                    float planar = Mathf.Sqrt(local.x * local.x + local.z * local.z);
                    Vector3 planarReference = frame.valid ? frame.forward : Vector3.forward;
                    float signedPlanar = Vector3.Dot(delta, planarReference) >= 0f ? planar : -planar;
                    count = Mathf.Clamp(Mathf.RoundToInt(signedPlanar / step), -cap, cap);
                    if (count == 0 && Mathf.Abs(signedPlanar / step) > 0.2f)
                        count = signedPlanar > 0f ? 1 : -1;
                }
            }
            else
            {
                // ── 17.45.2-dev: a SPAN fills out to where the player is POINTING ──────
                // Counted in lattice metres, one metre of aim added a cell, so a machine four
                // metres wide - and an aim that can reach a distant hillside - saturated the
                // ten-cell cap the instant the player looked up. Counted in the block's own
                // pitch the run simply covers the gap between the anchor and the crosshair:
                // point one machine further on and the run is one cell longer, point at the
                // horizon and it stops at the cap. Spacing is unchanged, so cells still sit
                // flush against each other.
                float spanPitch = Mathf.Max(step, rawPitch);
                count = Mathf.Clamp(Mathf.RoundToInt(distance / spanPitch), -cap, cap);
                if (count == 0)
                {
                    // A short drag still reads as a run: the first cell in the dragged
                    // direction, so the preview always answers the gesture.
                    if (Mathf.Abs(distance / spanPitch) > 0.25f) count = distance > 0f ? 1 : -1;
                }
            }

            for (int i = 1; i <= Mathf.Abs(count) && cells.Count < budget; i++)
            {
                cells.Add(new PlacementCell
                {
                    position = anchorPosition + axis * (Mathf.Sign(count) * i * pitchAlong),
                    rotation = anchorRotation,
                });
            }
            return cells.Count;
        }

        /// <summary>
        /// How many cells of a run are actually buildable: the plan, the cap and what the
        /// player is holding all have a vote.
        /// </summary>
        public static int Affordable(int planned, int held) => Mathf.Clamp(Mathf.Min(planned, held), 0, PlacementOptions.MaxCells);
    }
}
