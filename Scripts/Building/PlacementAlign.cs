// Assets/Scripts/VoxelEngine/Building/PlacementAlign.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        PLACEMENT ALIGN — the frame a block lands in              ║
// ║                                                                  ║
// ║  Static placement used to round X, Y and Z against the WORLD.    ║
// ║  On a round planet that is not a grid at all: two foundations    ║
// ║  laid 200 m apart each round against a different tangent plane,  ║
// ║  so when they finally meet they are centimetres apart in height  ║
// ║  and degrees apart in heading - and the player is left nudging   ║
// ║  the ghost around trying to find the cell that agrees.           ║
// ║                                                                  ║
// ║  The fix is a FRAME, not a bigger snap radius. Every placement   ║
// ║  resolves the frame of whatever it is aimed at:                  ║
// ║    • aimed at a placed block  → that block's own axes, so the    ║
// ║      new one lands on the same lattice and the same heading     ║
// ║    • aimed at a deck / grid   → that structure's axes            ║
// ║    • aimed at ground         → local gravity: up is the surface  ║
// ║      normal, forward is a stable tangent                         ║
// ║                                                                  ║
// ║  Ctrl then means what it means in every factory builder: LINE UP. ║
// ║  While it is held the heading snaps to the frame's cardinal and  ║
// ║  the lattice quantises to the frame origin instead of drifting.  ║
// ║                                                                  ║
// ║  Pure geometry. No input, no UI, no scene objects - so the ghost ║
// ║  and the commit can never disagree about where a block lands.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Building
{
    /// <summary>A right-handed placement frame: an origin and three unit axes.</summary>
    public struct PlacementFrame
    {
        public bool valid;
        public Vector3 origin;
        public Vector3 up;
        public Vector3 forward;
        public Vector3 right;
        /// <summary>"FOUNDATION", "DECK", "CONSTRUCT", "GROUND" - for the HUD.</summary>
        public string label;
        /// <summary>True when the frame came from a player-built structure.</summary>
        public bool fromStructure;

        public static PlacementFrame Ground(Vector3 position)
        {
            Vector3 up = GravityProvider.GetUp(position);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();
            return FromUp(position, up, "GROUND", false);
        }

        /// <summary>Builds a frame from an up vector and a stable tangent reference.</summary>
        public static PlacementFrame FromUp(Vector3 origin, Vector3 up, string label, bool fromStructure)
        {
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            // Project a world axis onto the plane; pick a second candidate when the
            // first is parallel to up (straight up at a pole, or a vertical wall).
            Vector3 reference = Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(reference, up)) > 0.98f) reference = Vector3.right;
            Vector3 forward = Vector3.ProjectOnPlane(reference, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            forward.Normalize();

            Vector3 right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();
            forward = Vector3.Cross(right, up).normalized;

            return new PlacementFrame
            {
                valid = true,
                origin = origin,
                up = up,
                forward = forward,
                right = right,
                label = label,
                fromStructure = fromStructure,
            };
        }

        /// <summary>The horizontal (frame-tangent) part of a world vector.</summary>
        public Vector3 Planar(Vector3 world) => Vector3.ProjectOnPlane(world, up);

        /// <summary>Frame-space coordinates of a world point: x = right, y = up, z = forward.</summary>
        public Vector3 ToLocal(Vector3 world)
        {
            Vector3 d = world - origin;
            return new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, forward));
        }

        public Vector3 ToWorld(Vector3 local)
            => origin + right * local.x + up * local.y + forward * local.z;

        /// <summary>The nearest cardinal direction of this frame to a world vector.</summary>
        public Vector3 NearestCardinal(Vector3 direction)
        {
            Vector3 planar = Planar(direction);
            if (planar.sqrMagnitude < 0.0001f) return forward;
            return Vector3.Dot(planar, right) >= 0f
                ? (Vector3.Dot(planar, forward) >= 0f ? forward : -forward)
                : (Vector3.Dot(planar, forward) >= 0f ? right : -right);
        }

        /// <summary>A compass word for a world direction, relative to this frame.</summary>
        public string Compass(Vector3 direction)
        {
            Vector3 planar = Planar(direction);
            if (planar.sqrMagnitude < 0.0001f) return "-";
            Vector3 cardinal = NearestCardinal(planar);
            if (Vector3.Dot(cardinal, forward) > 0.5f) return "AHEAD";
            if (Vector3.Dot(cardinal, -forward) > 0.5f) return "BACK";
            return Vector3.Dot(cardinal, right) > 0.5f ? "RIGHT" : "LEFT";
        }
    }

    /// <summary>Resolves placement frames and refines a pose inside them.</summary>
    public static class PlacementAlign
    {
        /// <summary>
        /// The frame of whatever was aimed at. A structure supplies its own axes; open
        /// ground supplies the local tangent plane. Never fails - ground is the fallback.
        /// </summary>
        public static PlacementFrame Resolve(Collider aimed, Vector3 surfaceUp, Vector3 at)
        {
            if (aimed != null)
            {
                var placed = aimed.GetComponentInParent<PlacedBlock>();
                if (placed != null)
                    return FromTransform(placed.transform, "BLOCK", surfaceUp);

                var tiered = aimed.GetComponentInParent<VoxelEngine.Building.Tiered.PlacedTieredBlock>();
                if (tiered != null)
                    return FromTransform(tiered.transform, "DECK", surfaceUp);

                var grid = aimed.GetComponentInParent<GridEntity>();
                if (grid != null)
                    return FromTransform(grid.transform, "CONSTRUCT", surfaceUp);
            }
            return PlacementFrame.FromUp(at, surfaceUp, "GROUND", false);
        }

        private static PlacementFrame FromTransform(Transform host, string label, Vector3 surfaceUp)
        {
            Vector3 up = host.up;
            // A machine authored with a slight tilt must not tilt the whole lattice; when
            // its up disagrees with gravity the surface still decides which way is up.
            if (surfaceUp.sqrMagnitude > 0.5f && Vector3.Dot(up, surfaceUp) < 0.55f) up = surfaceUp;
            if (up.sqrMagnitude < 0.0001f) up = surfaceUp;

            Vector3 forward = Vector3.ProjectOnPlane(host.forward, up);
            var frame = PlacementFrame.FromUp(host.position, up, label, true);
            if (forward.sqrMagnitude > 0.0001f)
            {
                // Keep the host's own heading as the reference, and the host's pivot as
                // the lattice origin: that is what makes a run of blocks agree.
                forward.Normalize();
                Vector3 right = Vector3.Cross(up, forward).normalized;
                if (right.sqrMagnitude > 0.0001f)
                {
                    frame.right = right;
                    frame.forward = Vector3.Cross(right, up).normalized;
                }
            }
            frame.origin = host.position;
            frame.label = label;
            frame.fromStructure = true;
            frame.valid = true;
            return frame;
        }

        /// <summary>
        /// Quantises a position onto the frame lattice. <paramref name="step"/> &lt;= 0
        /// returns the pose untouched (free placement).
        /// </summary>
        public static Vector3 Quantise(Vector3 position, in PlacementFrame frame, float step)
        {
            if (step <= 0.0001f || !frame.valid) return position;
            Vector3 local = frame.ToLocal(position);
            float height = local.y;                 // the surface decides height, not the lattice
            local.x = Mathf.Round(local.x / step) * step;
            local.z = Mathf.Round(local.z / step) * step;
            local.y = height;
            return frame.ToWorld(local);
        }

        /// <summary>
        /// Snaps a heading to the frame cardinal. A pose that has been deliberately
        /// pitched or rolled is left exactly as it is - the player meant it.
        /// </summary>
        public static Quaternion AlignRotation(Quaternion rotation, in PlacementFrame frame)
        {
            if (!frame.valid) return rotation;
            Vector3 up = rotation * Vector3.up;
            if (Vector3.Dot(up, frame.up) < 0.985f) return rotation;

            Vector3 forward = frame.NearestCardinal(rotation * Vector3.forward);
            if (forward.sqrMagnitude < 0.0001f) return rotation;
            return Quaternion.LookRotation(forward, frame.up);
        }

        /// <summary>
        /// The rotation of the aimed structure when it is the very same item - placing a
        /// second wall next to the first should not require re-orienting it. Returns
        /// <paramref name="fallback"/> when there is no such host.
        /// </summary>
        public static Quaternion InheritRotation(Collider aimed, VoxelEngine.Items.BlockItem block,
                                                 Quaternion fallback)
        {
            if (aimed == null || block == null) return fallback;
            var placed = aimed.GetComponentInParent<PlacedBlock>();
            if (placed == null || placed.Item != block) return fallback;
            return placed.transform.rotation;
        }

        /// <summary>A short human read of how a heading sits in the frame, for the HUD.</summary>
        public static string Heading(Quaternion rotation, in PlacementFrame frame, string fallback = "--")
        {
            if (!frame.valid) return fallback;
            return frame.Compass(rotation * Vector3.forward);
        }
    }
}
