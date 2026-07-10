// Assets/Scripts/VoxelEngine/Building/Tiered/BuildSocket.cs
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// A snap point on a placed building piece. The build system finds the nearest
    /// socket within range and aligns the ghost to it.
    ///
    /// Socket compatibility (which piece can attach to which socket) is governed
    /// by the family pair, see BuildSocketCompat.AreCompatible.
    /// </summary>
    public class BuildSocket : MonoBehaviour
    {
        public SocketSide side    = SocketSide.Top;
        public BuildFamily family = BuildFamily.Foundation;

        // Visual gizmo so designers can see sockets in scene view.
        private void OnDrawGizmos()
        {
            Gizmos.color = side switch
            {
                SocketSide.Top    => new Color(0f, 1f, 0f, 0.6f),
                SocketSide.Bottom => new Color(1f, 0.5f, 0f, 0.6f),
                SocketSide.Center => new Color(1f, 1f, 1f, 0.4f),
                _                 => new Color(0.2f, 0.6f, 1f, 0.6f)
            };
            Gizmos.DrawWireCube(transform.position, Vector3.one * 0.18f);
        }
    }

    public static class BuildSocketCompat
    {
        /// <summary>
        /// Returns true if a piece of `incoming` family can snap to a socket of `host` family on `side`.
        /// Encodes the tiered construction placement rules in one place.
        /// </summary>
        public static bool AreCompatible(BuildFamily host, SocketSide side, BuildFamily incoming)
        {
            // Foundations stack on top of foundations and bind walls on their edges.
            if (host == BuildFamily.Foundation)
            {
                switch (side)
                {
                    case SocketSide.Top:
                        // On top of a foundation we can place: another foundation, a floor (above), pillars, walls.
                        return incoming == BuildFamily.Wall      ||
                               incoming == BuildFamily.Doorway   ||
                               incoming == BuildFamily.Window    ||
                               incoming == BuildFamily.HalfWall  ||
                               incoming == BuildFamily.Pillar    ||
                               incoming == BuildFamily.Floor     ||
                               incoming == BuildFamily.Stairs;
                    case SocketSide.North:
                    case SocketSide.South:
                    case SocketSide.East:
                    case SocketSide.West:
                        // Side-by-side foundations.
                        return incoming == BuildFamily.Foundation;
                    case SocketSide.Bottom:
                        return false;
                    default:
                        return false;
                }
            }

            // Walls/doorways/windows accept floors/roofs on top, and other walls horizontally.
            if (host == BuildFamily.Wall || host == BuildFamily.Doorway ||
                host == BuildFamily.Window || host == BuildFamily.HalfWall)
            {
                if (side == SocketSide.Top)
                    return incoming == BuildFamily.Floor    ||
                           incoming == BuildFamily.Roof     ||
                           incoming == BuildFamily.Wall     ||
                           incoming == BuildFamily.Doorway  ||
                           incoming == BuildFamily.Window;
            }

            // Pillars accept floors on top, walls to all sides.
            if (host == BuildFamily.Pillar)
            {
                if (side == SocketSide.Top)
                    return incoming == BuildFamily.Pillar || incoming == BuildFamily.Floor || incoming == BuildFamily.Roof;
                return incoming == BuildFamily.Wall || incoming == BuildFamily.HalfWall;
            }

            // Floors accept walls and pillars on top, more floors on the side.
            if (host == BuildFamily.Floor)
            {
                if (side == SocketSide.Top)
                    return incoming == BuildFamily.Wall ||
                           incoming == BuildFamily.HalfWall ||
                           incoming == BuildFamily.Pillar ||
                           incoming == BuildFamily.Doorway ||
                           incoming == BuildFamily.Window;
                return incoming == BuildFamily.Floor;
            }

            // Stairs typically attach to foundations / floors via their bottom edge.
            if (host == BuildFamily.Stairs)
                return false;

            // Roofs may accept other roofs above.
            if (host == BuildFamily.Roof)
                return incoming == BuildFamily.Roof;

            return false;
        }
    }
}
