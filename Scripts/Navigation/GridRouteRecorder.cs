// Assets/Scripts/VoxelEngine/Navigation/GridRouteRecorder.cs
//
// THE ROUTE RECORDER — the block you bolt to a ship to give it a navigation shelf.
//
// A route is only worth having if it survives being unplanned: written down once, read out at
// every console, and re-evaluated whenever the ship changes under it. That is this block's whole
// job. It holds the ship's `RouteBook`, it re-runs the planner on a slow clock so a panel can
// refresh against a live number without every screen re-deriving the solar system, and it draws a
// real power bill while a capture is running — because a nav deck that costs nothing is a menu,
// not a machine.
//
// Local Road/Water/Flight recording works without a star map. Only the legacy cosmic
// cost planner requires CosmicRegistry; the route book captures once per grid.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Navigation
{
    public class GridRouteRecorder : GridBlock, IGridDataProvider
    {
        [Header("Recorder")]
        [Tooltip("How often a selected route is re-costed, in seconds. A route is a plan, not a telemetry stream: the planets move slowly and so should the answer.")]
        [Range(0.25f, 5f)] public float recomputeIntervalSeconds = 0.75f;

        [Tooltip("Watts the console bills while a capture is running. Standby is free — a book on a shelf should not cost power.")]
        public float recordingWatts = 60f;

        [Tooltip("Name given to the next recording. Left blank, the recorder numbers them.")]
        public string nextRouteName = "";

        [Tooltip("The route the panel keeps open, by name, so a screen reopens on the run you were checking.")]
        public string selectedRouteName = "";

        public RouteTravelMode PlanningMode { get; set; } = RouteTravelMode.Road;

        private RouteBook _book;
        private float _recomputeTimer;
        private RoutePlan _cachedPlan;
        private bool _havePlan;
        private ShipRoute _cachedFor;
        private float _cachedProfileHash;

        public override float PowerDraw => Book != null && Book.IsRecording && Enabled ? recordingWatts : 0f;

        public RouteBook Book
        {
            get
            {
                if (_book == null && Grid != null) _book = RouteBook.For(Grid, create: true);
                return _book;
            }
        }

        public bool HasStarMap => CosmicRegistry.Instance != null && CosmicRegistry.Instance.IsReady
                                  && SpaceOrigin.Instance != null;

        /// <summary>The route the panel is looking at, or the first one on the shelf.</summary>
        public ShipRoute Selected
        {
            get
            {
                var book = Book;
                if (book == null || book.Count == 0) return null;
                var byName = book.Find(selectedRouteName);
                if (byName != null) return byName;
                selectedRouteName = book.Routes[0].routeName;
                return book.Routes[0];
            }
        }

        /// <summary>The last evaluated plan for `Selected`. Recomputed on the block's own clock,
        /// so a screen can read it every frame without paying for the arithmetic.</summary>
        public RoutePlan Plan
        {
            get
            {
                var route = Selected;
                if (route == null || !_havePlan || !ReferenceEquals(_cachedFor, route)
                    || Mathf.Abs(_cachedProfileHash - route.speedProfileIndex) > 0.001f)
                {
                    RecomputeNow();
                }
                return _cachedPlan;
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block")
                blockName = "Route Recorder";
            _ = Book;   // attach the ship's shelf now, so a save always has somewhere to write
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _havePlan = false;
            _cachedFor = null;
        }

        private void Update()
        {
            if (!Enabled || Grid == null) { _havePlan = false; return; }
            _recomputeTimer -= Time.unscaledDeltaTime;
            if (_recomputeTimer <= 0f)
            {
                _recomputeTimer = Mathf.Max(0.05f, recomputeIntervalSeconds);
                RecomputeNow();
            }

        }

        /// <summary>Forces an immediate re-cost — the panel calls it after any edit, so a slider
        /// move and the number under it never disagree by a frame.</summary>
        public void RecomputeNow()
        {
            var route = Selected;
            _cachedFor = route;
            _cachedProfileHash = route != null ? route.speedProfileIndex : -1f;
            _cachedPlan = GridRoutePlanner.Evaluate(route, Grid);
            _havePlan = route != null;
        }

        // ── Panel helpers: the thin verbs the buttons want ───────────────────
        public void BeginRecording() => BeginRecording(HasStarMap ? RouteTravelMode.LegacyFlight : RouteTravelMode.Flight);

        public void BeginRecording(RouteTravelMode mode)
        {
            var book = Book;
            if (book == null || book.IsRecording || !Enabled) return;
            string name = string.IsNullOrWhiteSpace(nextRouteName)
                ? "Recorded Route " + (book.Count + 1)
                : nextRouteName.Trim();
            book.BeginRecording(name);
            book.Draft.travelMode = mode;
            bool isLocal = book.Draft.travelMode == RouteTravelMode.Road || book.Draft.travelMode == RouteTravelMode.RoadNetwork || book.Draft.travelMode == RouteTravelMode.Water || book.Draft.travelMode == RouteTravelMode.Flight;
            book.Draft.sceneCoordinates = isLocal ? true : SpaceOrigin.Instance == null; // v9.56.4-dev: local always scene-local
            var start = IndustrialWorld.Navigation.RouteCoordinates.Capture(IndustrialWorld.Navigation.RoadNavigationAnchor.ForGrid(Grid, book.Draft.travelMode), book.Draft.sceneCoordinates);
            book.ForceCapture(start.positionKm, RouteWaypoint.FindBody(CosmicRegistry.Instance, start.bodyId));
            nextRouteName = "";
        }

        public ShipRoute CommitRecording()
        {
            var book = Book;
            if (book != null && book.IsRecording && book.Draft != null
                && IndustrialWorld.Navigation.RouteCoordinates.CanResolve(book.Draft))
            {
                var end = IndustrialWorld.Navigation.RouteCoordinates.Capture(IndustrialWorld.Navigation.RoadNavigationAnchor.ForGrid(Grid, book.Draft.travelMode), book.Draft.sceneCoordinates);
                if (book.Draft.waypoints.Count < 4096 && (book.Draft.waypoints.Count == 0 || Unity.Mathematics.math.length(end.positionKm
                    - book.Draft.waypoints[book.Draft.waypoints.Count - 1].ResolvedPositionKm(CosmicRegistry.Instance)) > 0.0005d))
                    book.Draft.AddWaypoint(end);
            }
            var route = book?.CommitRecording();
            if (route != null)
            {
                selectedRouteName = route.routeName;
                RecomputeNow();
            }
            return route;
        }

        public void MarkWaypoint()
        {
            var book = Book;
            if (book == null || !book.IsRecording || book.Draft == null || book.Draft.waypoints.Count >= 4096
                || !IndustrialWorld.Navigation.RouteCoordinates.CanResolve(book.Draft)) return;
            book.Draft.AddWaypoint(IndustrialWorld.Navigation.RouteCoordinates.Capture(IndustrialWorld.Navigation.RoadNavigationAnchor.ForGrid(Grid, book.Draft.travelMode), book.Draft.sceneCoordinates));
            RecomputeNow();
        }

        public void AddRouteToBody(BodyInstance body, string routeName = null)
        {
            var book = Book;
            if (book == null || body == null || !HasStarMap) return;
            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;

            // A unique name, never a silent merge: plotting the same leg twice is two attempts at
            // the same trip, and the pilot should get two entries rather than one longer one.
            string wanted = string.IsNullOrWhiteSpace(routeName) ? "To " + body.DisplayName : routeName.Trim();
            var book2 = book;
            string unique = wanted;
            for (int n = 2; book2.Find(unique) != null; n++)
                unique = wanted + " #" + n;
            var route = new ShipRoute { routeName = unique };
            route.AddWaypoint(new RouteWaypoint(origin.GetCosmicKm(transform.position), null));
            double3 dest = registry.CosmicPositionOf(body);

            // Arrival must sit outside the body's own envelope or the plan is a collision with
            // extra steps, so the recorder parks the point above the atmosphere top by rule.
            double radiusKm = Mathf.Max(0.001f, body.settings != null ? body.settings.radiusKm : 1f);
            double topKm = radiusKm * (1d + (double)Mathf.Max(0f,
                body.settings != null ? body.settings.atmosphereHeightRadiusFraction : 0f));
            double clearance = Mathf.Max(RouteRules.ArrivalClearanceCells * 0.25f, (float)radiusKm * 0.02f);
            double3 up = math.normalizesafe(dest - registry.GetSunDirectionKm(dest), new double3(0d, 1d, 0d));
            // Pinned to the body, with the standoff as its offset: the arrival stays outside the
            // atmosphere top as the planet carries on orbiting.
            route.AddWaypoint(new RouteWaypoint(dest + up * (topKm + clearance), body,
                "arrival " + body.DisplayName, registry));

            book.Append(route);
            selectedRouteName = route.routeName;
            RecomputeNow();
        }

        public void RemoveRoute(string name)
        {
            var book = Book;
            if (book == null) return;
            book.Remove(name);
            if (selectedRouteName == name) selectedRouteName = null;
            RecomputeNow();
        }

        public void SelectRoute(string name)
        {
            selectedRouteName = name;
            if (Book != null) IndustrialWorld.Navigation.RoutePathOverlay.For(Book).RefreshPreview(name);
            RecomputeNow();
        }

        /// <summary>Edits the selected route: drop one point, or run the whole thing backwards.
        /// Both go through the route model, so neither can leave a one-point "route" on the shelf.</summary>
        public bool DeleteWaypoint(int index)
        {
            var route = Selected;
            if (route == null) return false;
            bool changed = route.RemoveWaypointAt(index);
            if (changed) RecomputeNow();
            return changed;
        }

        public bool ReverseSelected()
        {
            var route = Selected;
            if (route == null || route.waypoints.Count < 2) return false;
            route.Reverse();
            RecomputeNow();
            return true;
        }

        public void SetProfile(RouteSpeedProfile profile)
        {
            var route = Selected;
            if (route == null) return;
            Book.SetProfile(route, profile);
            RecomputeNow();
        }

        // ── Screen data ──────────────────────────────────────────────────────
        public string SourceName => string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block"
            ? "Route Recorder" : blockName;
        public string DataCategory => "Navigation";

        public string GetDisplayData()
        {
            var book = Book;
            if (book == null) return "RECORDER\nNO GRID";
            if (!HasStarMap) return "RECORDER\nNO STAR MAP";
            if (book.IsRecording)
                return "RECORDING\n" + (book.Draft != null ? book.Draft.routeName : "Route") + "\n"
                     + "POINTS " + (book.Draft != null ? book.Draft.waypoints.Count : 0);

            var plan = Plan;
            var route = Selected;
            if (route == null) return "RECORDER\n" + book.Count + " ROUTES\nNONE SELECTED";
            if (!plan.IsValid) return "ROUTE " + route.routeName.ToUpperInvariant() + "\nCANNOT COST";

            return "ROUTE " + route.routeName.ToUpperInvariant() + "\n"
                 + "DIST " + plan.TotalDistanceKm.ToString("0") + " km\n"
                 + "ETA " + FormatDuration(plan.TotalSeconds) + "\n"
                 + "ENERGY " + plan.TotalWattHours.ToString("0") + " Wh · MARGIN "
                 + (plan.ReserveMargin01 * 100f).ToString("0") + "%"
                 + " · " + BurnCount(plan) + " BURNS"
                 + (plan.HasWarnings ? "\n⚠ " + plan.Warnings.Count + " WARNINGS" : "\nCLEAR");
        }

        /// <summary>Total push-and-brake cycles across the route. Named, because the block label and
        /// the panel both have to agree on what a "burn" is: it is one acceleration and one
        /// deceleration, and a long leg is several of them.</summary>
        static string BurnCount(RoutePlan plan)
        {
            int n = 0;
            if (plan.Legs != null)
                for (int i = 0; i < plan.Legs.Count; i++) n += plan.Legs[i].BurnCount;
            return n.ToString();
        }

        /// <summary>Hours and minutes for short trips, whole hours for long ones — the format a
        /// pilot reads, not a stopwatch.</summary>
        public static string FormatDuration(float seconds)
        {
            if (seconds <= 0.001f) return "—";
            if (seconds < 5400f)
            {
                int m = Mathf.RoundToInt(seconds / 60f);
                return (m / 60) + "h " + (m % 60).ToString("00") + "m";
            }
            if (seconds < 86400f * 3f) return (seconds / 3600f).ToString("0.0") + " h";
            return (seconds / 86400f).ToString("0.0") + " days";
        }
    }
}
