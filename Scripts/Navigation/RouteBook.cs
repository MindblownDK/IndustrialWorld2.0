// Assets/Scripts/VoxelEngine/Navigation/RouteBook.cs
//
// THE ROUTE BOOK — the named journeys a ship has recorded, carried on the grid itself.
//
// Routes belong to the ship rather than to the block that happens to be looking at them: a
// haul run you recorded at the helm should still be there when you open the panel on the other
// side of the vessel, and a ship you rebuild from a save keeps its book. So this is one component
// on `GridEntity`, not a stack of per-block state, and the persistence layer writes it back as
// part of the grid record rather than the block record.
//
// It stores, it does not decide. The planner in `GridRoutePlanner` is the only thing here that
// does arithmetic, and the only thing this class knows about the sky is the name of the body a
// waypoint rides — which is enough to re-derive a parked point above a moon eight hours later.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;   // CosmicRegistry + BodyInstance (9.34.0)

namespace VoxelEngine.Navigation
{
    /// <summary>A ship's saved routes, in cosmic kilometres with optional body anchors.</summary>
    public class RouteBook : MonoBehaviour
    {
        [SerializeField] private List<ShipRoute> _routes = new();

        /// <summary>True while a route is being driven into, recording each stop.</summary>
        public bool IsRecording { get; set; }

        /// <summary>The route being captured right now, or null when idle.</summary>
        public ShipRoute Draft { get; private set; }

        /// <summary>Minimum distance, in kilometres of cosmic travel, between two captured
        /// points. Stops closer than this are folded into the previous one, so a slow crawl
        /// around an anchorage does not become a fifty-point route.</summary>
        public float captureSpacingKm = 2.5f;

        private double3 _lastCapturedKm;
        private bool _haveLast;

        public IReadOnlyList<ShipRoute> Routes => _routes;
        public int Count => _routes != null ? _routes.Count : 0;
        public bool IsEmpty => _routes == null || _routes.Count == 0;

        /// <summary>Find the book on a grid, creating it the first time a player asks. Safe to
        /// call from a panel or a save restore; it never destroys an existing book.</summary>
        public static RouteBook For(VoxelEngine.GridSystem.GridEntity grid, bool create = false)
        {
            if (grid == null) return null;
            var book = grid.GetComponent<RouteBook>();
            if (book == null && create) book = grid.gameObject.AddComponent<RouteBook>();
            if (book != null) IndustrialWorld.Navigation.RoutePathOverlay.For(book);
            return book;
        }

        private void LateUpdate()
        {
            if (IsRecording) IndustrialWorld.Navigation.RoutePathOverlay.For(this);
            if (!IsRecording || Draft == null || Draft.waypoints.Count >= 4096) return;
            if (!IndustrialWorld.Navigation.RouteCoordinates.CanResolve(Draft)) return;
            var point = IndustrialWorld.Navigation.RouteCoordinates.Capture(IndustrialWorld.Navigation.RoadNavigationAnchor.ForGrid(GetComponent<VoxelEngine.GridSystem.GridEntity>(), Draft.travelMode), Draft.sceneCoordinates);
            CaptureNow(point.positionKm, RouteWaypoint.FindBody(CosmicRegistry.Instance, point.bodyId));
        }

        // ── Recording ────────────────────────────────────────────────────────
        public void BeginRecording(string name)
        {
            if (IsRecording) return;
            string wanted = string.IsNullOrWhiteSpace(name) ? "Recorded Route" : name.Trim();
            string unique = wanted;
            for (int n = 2; Find(unique) != null; n++) unique = wanted + " #" + n;
            Draft = new ShipRoute { routeName = unique };
            _haveLast = false;
            IsRecording = true;
        }

        public void AbandonRecording()
        {
            Draft = null;
            IsRecording = false;
            _haveLast = false;
        }

        /// <summary>Captures the ship's current cosmic position as the next point. Returns false
        /// when the point is inside the spacing threshold, i.e. nothing worth recording happened.</summary>
        public bool CaptureNow(double3 positionKm, VoxelEngine.Cosmos.BodyInstance rides)
        {
            if (!IsRecording || Draft == null) return false;
            if (_haveLast)
            {
                double dx = positionKm.x - _lastCapturedKm.x;
                double dy = positionKm.y - _lastCapturedKm.y;
                double dz = positionKm.z - _lastCapturedKm.z;
                double movedKm = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (movedKm < (Draft.travelMode == RouteTravelMode.LegacyFlight ? captureSpacingKm : 0.004f)) return false;
            }
            // The registry is what lets a pinned point store its offset from the body. No registry,
            // no anchor: the point is filed absolute, which is stale rather than wrong.
            var registry = CosmicRegistry.Instance;
            Draft.AddWaypoint(new RouteWaypoint(positionKm,
                registry != null ? rides : null, null, registry));
            _lastCapturedKm = positionKm;
            _haveLast = true;
            return true;
        }

        /// <summary>Adds a point without the spacing rule — the "mark it here anyway" button.</summary>
        public void ForceCapture(double3 positionKm, VoxelEngine.Cosmos.BodyInstance rides, string label = null)
        {
            var forcedRegistry = CosmicRegistry.Instance;
            var wp = new RouteWaypoint(positionKm, forcedRegistry != null ? rides : null, label, forcedRegistry);
            if (IsRecording && Draft != null)
            {
                Draft.AddWaypoint(wp);
                _lastCapturedKm = positionKm;
                _haveLast = true;
                return;
            }
            // No recording in progress: append straight to a route of the same name, or start one.
            var route = FindOrCreate("Pinned Waypoints");
            route.AddWaypoint(wp);
        }

        /// <summary>Ends the capture and files the route. A route needs two points; one is a note.</summary>
        public ShipRoute CommitRecording()
        {
            if (Draft == null || Draft.waypoints.Count < 2) return null;
            var finished = Draft;
            Draft = null;
            IsRecording = false;
            _haveLast = false;
            if (finished.waypoints.Count < 2) return null;
            _routes.Add(finished);
            return finished;
        }

        // ── The shelf ─────────────────────────────────────────────────────────
        /// <summary>Files a finished route. `Routes` is a read-only view on purpose — a panel or a
        /// destination picker must not be able to write into the shelf behind the book's back — so
        /// anything that creates a route comes through here and gets the same duplicate-name rule
        /// the recorder uses.</summary>
        public bool Append(ShipRoute route)
        {
            if (route == null || string.IsNullOrWhiteSpace(route.routeName)) return false;
            if (Find(route.routeName) != null) return false;   // never silently merge two routes
            if (_routes == null) _routes = new List<ShipRoute>();
            _routes.Add(route);
            return true;
        }

        public ShipRoute FindOrCreate(string name)
        {
            var existing = Find(name);
            if (existing != null) return existing;
            var created = new ShipRoute { routeName = name };
            _routes.Add(created);
            return created;
        }

        public ShipRoute Find(string name)
        {
            if (_routes == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < _routes.Count; i++)
                if (_routes[i] != null && _routes[i].routeName == name) return _routes[i];
            return null;
        }

        public bool Remove(string name)
        {
            var route = Find(name);
            return route != null && _routes.Remove(route);
        }

        public void Rename(ShipRoute route, string name)
        {
            if (route == null || string.IsNullOrWhiteSpace(name)) return;
            if (Find(name) != null && Find(name) != route) return;   // never silently merge two routes
            route.routeName = name.Trim();
        }

        public void SetProfile(ShipRoute route, RouteSpeedProfile profile)
        {
            if (route == null) return;
            route.speedProfileIndex = (int)profile;
        }

        public void ClearAll() { _routes.Clear(); }

        /// <summary>Snapshot for the save layer. Kept in the order the player sees them.</summary>
        public List<ShipRoute> Snapshot()
        {
            var copy = new List<ShipRoute>(_routes.Count);
            for (int i = 0; i < _routes.Count; i++)
            {
                var r = _routes[i];
                if (r == null) continue;
                copy.Add(new ShipRoute
                {
                    routeName = r.routeName,
                    travelMode = r.travelMode, sceneCoordinates = r.sceneCoordinates,
                    speedProfileIndex = r.speedProfileIndex,
                    waypoints = new List<RouteWaypoint>(r.waypoints),
                });
            }
            return copy;
        }

        /// <summary>Restore from a save. Replaces the shelf wholesale: a load is a load.</summary>
        public void Restore(IEnumerable<ShipRoute> routes)
        {
            _routes.Clear();
            if (routes == null) return;
            foreach (var r in routes)
                if (r != null && r.waypoints != null) _routes.Add(r);
        }
    }
}
