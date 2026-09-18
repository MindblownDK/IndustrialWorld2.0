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
            // ── Orbital station pieces (11.22.0-dev) ──
            // A station is a sealed pressure vessel, so its pieces only mate with each
            // other. Letting a wooden wall close a hull run would produce a "sealed" room
            // with a plank in it, which is exactly the kind of thing that makes a
            // pressure system feel arbitrary once one is wired up.
            bool hostIsStation = BuildFamilyInfo.GroupOf(host) == BuildFamilyGroup.OrbitalStation;
            bool incomingIsStation = BuildFamilyInfo.GroupOf(incoming) == BuildFamilyGroup.OrbitalStation;
            if (hostIsStation || incomingIsStation)
                return hostIsStation && incomingIsStation && StationCompatible(host, side, incoming);

            // Foundations bind neighboring decks, wall-like perimeter pieces, and stairs.
            if (host == BuildFamily.Foundation)
            {
                switch (side)
                {
                    case SocketSide.Top:
                        return incoming == BuildFamily.Pillar
                            || incoming == BuildFamily.Floor;
                    case SocketSide.TopNorth:
                    case SocketSide.TopSouth:
                    case SocketSide.TopEast:
                    case SocketSide.TopWest:
                        return incoming == BuildFamily.Wall
                            || incoming == BuildFamily.Doorway
                            || incoming == BuildFamily.Window
                            || incoming == BuildFamily.HalfWall
                            || incoming == BuildFamily.Stairs;
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

            // Doorways accept their separate Door at centre and a descending
            // staircase at the exterior threshold.
            if (host == BuildFamily.Doorway && side == SocketSide.Center)
                return incoming == BuildFamily.Door;
            if (host == BuildFamily.Doorway && side == SocketSide.Bottom)
                return incoming == BuildFamily.Stairs;

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
                if (side == SocketSide.East || side == SocketSide.West)
                    return incoming == BuildFamily.Wall
                        || incoming == BuildFamily.Doorway
                        || incoming == BuildFamily.Window
                        || incoming == BuildFamily.HalfWall;
            }

            // Pillars accept floors on top, walls to all sides.
            if (host == BuildFamily.Pillar)
            {
                if (side == SocketSide.Top)
                    return incoming == BuildFamily.Pillar || incoming == BuildFamily.Floor || incoming == BuildFamily.Roof;
                return incoming == BuildFamily.Wall || incoming == BuildFamily.HalfWall;
            }

            // Floors accept walls and pillars on top, stairs on their perimeter,
            // and more floors one complete module to the side.
            if (host == BuildFamily.Floor)
            {
                if (side == SocketSide.Top)
                    return incoming == BuildFamily.Wall ||
                           incoming == BuildFamily.HalfWall ||
                           incoming == BuildFamily.Pillar ||
                           incoming == BuildFamily.Doorway ||
                           incoming == BuildFamily.Window;
                if (side == SocketSide.TopNorth || side == SocketSide.TopSouth ||
                    side == SocketSide.TopEast || side == SocketSide.TopWest)
                    return incoming == BuildFamily.Stairs;
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

        /// <summary>
        /// Station mating rules. Deliberately permissive between station pieces: a station
        /// is built in open space with no ground to anchor to, so over-constraining the
        /// sockets would make it impossible to close a ring corridor back on itself.
        /// </summary>
        private static bool StationCompatible(BuildFamily host, SocketSide side, BuildFamily incoming)
        {
            // A dock collar is the outer face of a station: nothing attaches beyond it.
            if (host == BuildFamily.StationDock) return false;

            // A dome caps a run and only ever sits on top of hull or a junction.
            if (incoming == BuildFamily.StationDome)
                return side == SocketSide.Top
                    && (host == BuildFamily.StationHull || host == BuildFamily.StationJunction);

            // Decks lie flat inside the pressure envelope.
            if (incoming == BuildFamily.StationFloor)
                return side == SocketSide.Top || side == SocketSide.Bottom;

            // Everything else joins edge to edge, which is how a station actually grows.
            switch (side)
            {
                case SocketSide.North:
                case SocketSide.South:
                case SocketSide.East:
                case SocketSide.West:
                case SocketSide.Top:
                case SocketSide.Bottom:
                    return true;
                default:
                    return false;
            }
        }
    }
}
