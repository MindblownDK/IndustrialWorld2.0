// Assets/Scripts/VoxelEngine/UI/LogisticsMapData.cs
//
// The data layer behind the logistics map: every transport network on the surface,
// collected into one set of drawable primitives.
//
// WHY A SEPARATE DATA LAYER
// Exactly the reason `OrbitalTrackingService` exists for the orbital map. The painter
// runs inside `generateVisualContent` and must stay cheap and allocation-free; the
// gathering walks several registries and allocates. Splitting them means the map can
// repaint on pan and zoom without re-walking every network in the world.
//
// WHAT IT UNIFIES
//   • Rail lines and stations   (RailNetwork / RailStation / RailTrain)
//   • Drone routes and ports    (DroneNetwork / DronePort)
//   • Logistic chest clusters   (Chest with a port lock) as "base zones"
//   • Roads                     (RoadSurfaceUtility)
//
// These were built across many versions and never had a shared view. The map is that
// view: it is where the player finally sees whether their networks actually join up.
//
// This is a LOCAL-SURFACE map in metres, deliberately distinct from the orbital map's
// kilometre scale. Zooming one into the other is a roadmap item, not this file's job.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;
using VoxelEngine.Transport;

namespace VoxelEngine.UI
{
    public enum MapOverlayKind
    {
        RailLine,
        RailStation,
        Train,
        DroneRoute,
        DronePort,
        BaseZone,
        Road,
        Player,
        /// <summary>A deep ore deposit revealed by an orbital resource scanner.</summary>
        Deposit,
    }

    /// <summary>A point of interest on the logistics map.</summary>
    public readonly struct MapMarker
    {
        public readonly MapOverlayKind Kind;
        public readonly Vector3 World;
        public readonly string Label;
        public readonly string Detail;
        public readonly bool Alert;

        public MapMarker(MapOverlayKind kind, Vector3 world, string label, string detail, bool alert)
        {
            Kind = kind; World = world; Label = label; Detail = detail; Alert = alert;
        }
    }

    /// <summary>A line to draw: a rail edge, a drone route, or a road segment.</summary>
    public readonly struct MapLink
    {
        public readonly MapOverlayKind Kind;
        public readonly Vector3 A, B;
        public readonly bool Active;

        public MapLink(MapOverlayKind kind, Vector3 a, Vector3 b, bool active)
        {
            Kind = kind; A = a; B = b; Active = active;
        }
    }

    /// <summary>A circular area of influence: a base zone around a cluster of logistic chests.</summary>
    public readonly struct MapZone
    {
        public readonly Vector3 Centre;
        public readonly float Radius;
        public readonly string Label;
        public readonly int Members;

        public MapZone(Vector3 centre, float radius, string label, int members)
        {
            Centre = centre; Radius = radius; Label = label; Members = members;
        }
    }

    public static class LogisticsMapData
    {
        private static readonly List<MapMarker> _markers = new(128);
        private static readonly List<MapLink> _links = new(512);
        private static readonly List<MapZone> _zones = new(16);

        public static IReadOnlyList<MapMarker> Markers => _markers;
        public static IReadOnlyList<MapLink> Links => _links;
        public static IReadOnlyList<MapZone> Zones => _zones;

        /// <summary>Bounds of everything gathered, so the map can frame itself on open.</summary>
        public static Bounds Extent { get; private set; }
        public static bool HasContent => _markers.Count > 0 || _links.Count > 0;

        public static int RailCells { get; private set; }
        public static int StationCount { get; private set; }
        public static int TrainCount { get; private set; }
        public static int PortCount { get; private set; }

        /// <summary>Deposits currently revealed by orbital scanners.</summary>
        public static int DepositCount { get; private set; }
        public static int ZoneCount => _zones.Count;

        /// <summary>
        /// How close two logistic chests must be to count as the same base. Matches the
        /// drone port's own 48 m service radius, so a zone on the map means the same thing
        /// as a zone the logistics layer actually serves.
        /// </summary>
        private const float ZONE_LINK_RANGE = 48f;

        /// <summary>Roads are dense; drawing every cell would swamp both the painter and the eye.</summary>
        private const int MAX_ROAD_CELLS = 4000;

        private static readonly List<AsphaltRoad> _roadScratch = new(256);
        private static readonly List<Chest> _chestScratch = new(64);

        /// <summary>
        /// Rebuilds every overlay. Called when the map opens and on a slow refresh tick,
        /// never per-frame from the painter.
        /// </summary>
        public static void Rebuild(Vector3 viewer)
        {
            _markers.Clear();
            _links.Clear();
            _zones.Clear();

            bool any = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            void Grow(Vector3 p)
            {
                any = true;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            GatherRail(Grow);
            GatherDrones(Grow);
            GatherZones(Grow);
            GatherRoads(Grow);
            GatherDeposits(Grow);

            _markers.Add(new MapMarker(MapOverlayKind.Player, viewer, "YOU", "", false));
            Grow(viewer);

            if (any)
            {
                var centre = (min + max) * 0.5f;
                var size = max - min;
                // A floor on the size keeps a single-marker world from framing at infinite zoom.
                size = Vector3.Max(size, new Vector3(64f, 1f, 64f));
                Extent = new Bounds(centre, size);
            }
            else
            {
                Extent = new Bounds(viewer, new Vector3(128f, 1f, 128f));
            }
        }

        // ── Rail ─────────────────────────────────────────────────────────────────
        private static void GatherRail(System.Action<Vector3> grow)
        {
            RailCells = RailNetwork.TrackCount;

            // Each edge is walked from both ends, so only emit it from the end with the
            // lower hash. Without that every rail line draws twice.
            foreach (var track in RailNetwork.AllTracks)
            {
                if (track == null) continue;
                Vector3 a = track.RailPosition;
                grow(a);

                var links = track.Links;
                for (int i = 0; i < links.Count; i++)
                {
                    var other = links[i];
                    if (other == null) continue;
                    if (track.GetHashCode() > other.GetHashCode()) continue;
                    _links.Add(new MapLink(MapOverlayKind.RailLine, a, other.RailPosition, true));
                }
            }

            StationCount = 0;
            var stations = RailStation.All;
            for (int i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (station == null) continue;
                StationCount++;

                bool onLine = station.ServedTrack != null;
                _markers.Add(new MapMarker(MapOverlayKind.RailStation, station.transform.position,
                    station.StationName,
                    onLine ? station.RoleLabel : "NO TRACK",
                    !onLine));
                grow(station.transform.position);
            }

            TrainCount = 0;
            var trains = RailTrain.All;
            for (int i = 0; i < trains.Count; i++)
            {
                var train = trains[i];
                if (train == null) continue;
                TrainCount++;

                string detail = train.State switch
                {
                    TrainState.Running => "TO " + train.CurrentTargetName,
                    TrainState.Docked => "AT " + train.CurrentTargetName,
                    TrainState.Blocked => "BLOCKED",
                    _ => "IDLE",
                };
                _markers.Add(new MapMarker(MapOverlayKind.Train, train.transform.position,
                    train.TrainName, detail, train.State == TrainState.Blocked));
                grow(train.transform.position);
            }
        }

        // ── Drones ───────────────────────────────────────────────────────────────
        private static void GatherDrones(System.Action<Vector3> grow)
        {
            PortCount = 0;
            var network = DroneNetwork.Instance;
            if (network == null) return;

            var ports = network.Ports;
            for (int i = 0; i < ports.Count; i++)
            {
                var port = ports[i];
                if (port == null) continue;
                PortCount++;

                Vector3 p = port.transform.position;
                grow(p);

                bool dormant = !port.IsLoaded;
                string detail = dormant ? "DORMANT"
                    : port.IsInFlight ? "IN FLIGHT"
                    : port.IsPowered ? "READY" : "NO POWER";

                _markers.Add(new MapMarker(MapOverlayKind.DronePort, p,
                    port.portName, detail, dormant || !port.IsPowered));

                var partner = port.CurrentPartner;
                if (partner == null) continue;
                // Same one-sided emit as rail, so a pair draws one route rather than two.
                if (port.GetHashCode() > partner.GetHashCode()) continue;
                _links.Add(new MapLink(MapOverlayKind.DroneRoute, p,
                    partner.transform.position, port.IsInFlight));
            }
        }

        // ── Base zones ───────────────────────────────────────────────────────────
        // A "base" is not an authored object in this game, so it has to be inferred. A
        // cluster of logistic chests is the honest proxy: it is exactly the thing the
        // logistics network already treats as one place.
        private static void GatherZones(System.Action<Vector3> grow)
        {
            _chestScratch.Clear();
            var all = Object.FindObjectsByType<Chest>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var chest = all[i];
                if (chest != null && chest.IsOnLogisticsNetwork) _chestScratch.Add(chest);
            }
            if (_chestScratch.Count == 0) return;

            // Single-link clustering: chests within ZONE_LINK_RANGE of any member join the
            // same zone. Cheap, order-independent, and matches how the logistics layer
            // itself reasons about range.
            var claimed = new bool[_chestScratch.Count];
            var pending = new List<int>(16);
            var members = new List<Vector3>(16);

            for (int seed = 0; seed < _chestScratch.Count; seed++)
            {
                if (claimed[seed]) continue;

                pending.Clear();
                members.Clear();
                pending.Add(seed);
                claimed[seed] = true;

                while (pending.Count > 0)
                {
                    int index = pending[^1];
                    pending.RemoveAt(pending.Count - 1);
                    Vector3 here = _chestScratch[index].transform.position;
                    members.Add(here);

                    for (int j = 0; j < _chestScratch.Count; j++)
                    {
                        if (claimed[j]) continue;
                        if ((_chestScratch[j].transform.position - here).sqrMagnitude
                            > ZONE_LINK_RANGE * ZONE_LINK_RANGE) continue;
                        claimed[j] = true;
                        pending.Add(j);
                    }
                }

                // A lone chest is not a base. Two or more is a place worth naming.
                if (members.Count < 2) continue;

                Vector3 centre = Vector3.zero;
                for (int m = 0; m < members.Count; m++) centre += members[m];
                centre /= members.Count;

                float radius = 12f;
                for (int m = 0; m < members.Count; m++)
                    radius = Mathf.Max(radius, Vector3.Distance(centre, members[m]) + 8f);

                _zones.Add(new MapZone(centre, radius, $"Base {_zones.Count + 1}", members.Count));
                grow(centre + new Vector3(radius, 0f, radius));
                grow(centre - new Vector3(radius, 0f, radius));
            }
        }

        // ── Deep deposits ────────────────────────────────────────────────────────
        // Only deposits an orbital RESOURCE SCANNER has surveyed appear here. The map
        // deliberately does not show every deposit in the world: that would make the
        // scanner pointless and hand the player a finished prospecting answer for free.
        // What the satellite has seen, the map shows - nothing more.
        private static void GatherDeposits(System.Action<Vector3> grow)
        {
            DepositCount = 0;

            var payloads = VoxelEngine.GridSystem.GridSatellitePayload.All;
            for (int i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                if (payload == null || !payload.CanSurveyResources) continue;

                var deposits = payload.SurveyDeposits();
                for (int d = 0; d < deposits.Count; d++)
                {
                    var node = deposits[d];

                    string label = VoxelEngine.Generation.DeepOreField.MaterialName(node.Material);
                    string detail = node.IsDepleted
                        ? "EXHAUSTED"
                        : $"{node.Remaining:N0}";

                    _markers.Add(new MapMarker(MapOverlayKind.Deposit, node.Centre,
                        label, detail, false));
                    grow(node.Centre);
                    DepositCount++;
                }
            }
        }

        // ── Roads ────────────────────────────────────────────────────────────────
        private static void GatherRoads(System.Action<Vector3> grow)
        {
            _roadScratch.Clear();
            RoadSurfaceUtility.CopyRegistered(_roadScratch, MAX_ROAD_CELLS);

            // Roads are drawn as short marks at each cell rather than as a traced network:
            // the road layer has no edge list, and tracing one here would duplicate work the
            // road system deliberately does not do. A dotted underlay reads correctly at
            // map zoom and costs one primitive per cell.
            for (int i = 0; i < _roadScratch.Count; i++)
            {
                var road = _roadScratch[i];
                if (road == null) continue;
                Vector3 p = road.transform.position;
                _links.Add(new MapLink(MapOverlayKind.Road, p, p + new Vector3(0.5f, 0f, 0f), true));
                grow(p);
            }
        }
    }
}
