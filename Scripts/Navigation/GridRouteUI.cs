// Assets/Scripts/VoxelEngine/Navigation/GridRouteUI.cs
//
// THE ROUTE PANEL — what a pilot reads when they ask "can we fly that, and what does it cost".
//
// The panel is a read-out with two hands on it: it prints the planner's answer for the selected
// route (and every reason not to go, by name, in red), and it offers only the verbs that change
// the book — record, mark, file, select, delete, pick a profile, plot a leg to a body. There is
// no second calculator in here and no cached copy of the sky: every number comes off the block's
// own clock, so the panel and the machine can never disagree.
//
// Built inside VoxelEngine.Navigation rather than in the shared block UI file, because a route is
// a navigation object and its layout belongs with it; `GridBlockUI` only routes to here.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Navigation
{
    public static class GridRouteUI
    {
        private static readonly Color OkInk = new Color(0.42f, 0.80f, 0.52f);
        private static readonly Color WarnInk = new Color(0.92f, 0.60f, 0.12f);
        private static readonly Color BadInk = new Color(0.82f, 0.22f, 0.18f);

        public static VisualElement BuildPanel(GridRouteRecorder recorder)
        {
            var p = VoxelEngine.UI.UITheme.MachinePanel();
            p.name = "RoutePanel";
            p.style.width = 500;

            if (recorder == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Body("No route recorder under the cursor."));
                return p;
            }

            var book = recorder.Book;
            bool live = recorder.HasStarMap;
            string state = book == null ? "NO BOOK"
                : book.IsRecording ? "RECORDING"
                : book.Count == 0 ? "EMPTY" : "READY";
            Color stateColor = book != null && book.IsRecording ? VoxelEngine.UI.UITheme.AccentAmber
                : book != null && book.Count > 0 ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.AccentDim;

            var (hdr, _, _, _) = VoxelEngine.UI.UITheme.HeaderRow("✦ " + recorder.SourceName, state, stateColor);
            p.Add(hdr);
            p.Add(VoxelEngine.UI.UITheme.AccentDivider(VoxelEngine.UI.UITheme.AccentCyan));
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));

            IndustrialWorld.Navigation.LocalRouteUI.AddTo(p, recorder);
            IndustrialWorld.Navigation.RoadNavigationUI.AddPlanningTo(p, recorder);

            if (!live || (recorder.Selected != null && recorder.Selected.travelMode != RouteTravelMode.LegacyFlight))
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("Local recording and navigation are available above without a star map. "
                    + "The legacy interplanetary planner below is only available for cosmic flight routes with a loaded star map."));
                return p;
            }

            var route = recorder.Selected;

            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Selected Route"));
            if (route == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("No route on the shelf yet. Record one by flying it, or plot a leg straight to a body below."));
            }
            else
            {
                p.Add(VoxelEngine.UI.UITheme.StatRow("❖", "Route", route.routeName, VoxelEngine.UI.UITheme.AccentCyan));
                p.Add(VoxelEngine.UI.UITheme.StatRow("↦", "Legs", route.LegCount.ToString()
                    + (route.waypoints.Count > 0 ? " · " + route.waypoints.Count + " points" : string.Empty),
                    VoxelEngine.UI.UITheme.TextSecondary));
                AddPlanRows(p, recorder.Plan);
                AddProfileRow(p, recorder, route);
                AddLegRows(p, recorder.Plan);

                var removeRow = Row();
                removeRow.Add(VoxelEngine.UI.UITheme.SmallButton("REVERSE", () =>
                {
                    recorder.ReverseSelected();
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.BgSlot));
                removeRow.Add(VoxelEngine.UI.UITheme.SmallButton("DELETE ROUTE", () =>
                {
                    recorder.RemoveRoute(route.routeName);
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.BgSlot));
                p.Add(removeRow);

                AddWaypointRows(p, recorder, route);
            }

            AddBookRows(p, recorder, book);
            AddCaptureRows(p, recorder, book);
            AddDestinationRows(p, recorder);

            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.UI.UITheme.Muted("Cost is the ship's own arithmetic: its real mass, the thrust it can hold, the energy "
                + "in its batteries and the load it carries while the trip is being flown. Refit the ship and the "
                + "plan moves with it — a route is never a promise the engine cannot keep."));
            return p;
        }

        // ── The points ───────────────────────────────────────────────────────
        // A route is editable, not a recording you have to re-fly to change. Dropping a point is
        // the whole of it in practice: a bad capture is thrown away, the run underneath stays.
        private static void AddWaypointRows(VisualElement p, GridRouteRecorder recorder, ShipRoute route)
        {
            if (route == null || route.waypoints.Count == 0) return;
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle(
                "Points · " + route.waypoints.Count + " (a route needs at least two)"));

            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            double3 prev = default; bool havePrev = false;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "PanelScroll_RoutePoints";
            scroll.style.maxHeight = 132f;
            VoxelEngine.UI.UITheme.StyleScroller(scroll);

            for (int i = 0; i < route.waypoints.Count; i++)
            {
                var wp = route.waypoints[i];
                double3 here = wp.ResolvedPositionKm(registry);
                string gap = havePrev ? (math.length(here - prev)).ToString("0.0") + " km" : "start";
                prev = here; havePrev = true;

                var row = Row();
                row.Add(VoxelEngine.UI.UITheme.StatRow((i + 1).ToString(),
                    string.IsNullOrEmpty(wp.label) ? (string.IsNullOrEmpty(wp.bodyId) ? "Free point" : wp.bodyId) : wp.label,
                    gap, VoxelEngine.UI.UITheme.TextSecondary));
                int idx = i;
                row.Add(VoxelEngine.UI.UITheme.SmallButton("✕", () =>
                {
                    recorder.DeleteWaypoint(idx);
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.BgSlot));
                scroll.Add(row);
            }
            p.Add(scroll);
        }

        // ── The plan ─────────────────────────────────────────────────────────
        private static void AddPlanRows(VisualElement p, RoutePlan plan)
        {
            if (!plan.IsValid)
            {
                p.Add(VoxelEngine.UI.UITheme.StatRow("◇", "Plan", "NOTHING TO COST", VoxelEngine.UI.UITheme.AccentDim));
                return;
            }

            bool clear = !plan.HasWarnings;
            p.Add(VoxelEngine.UI.UITheme.StatRow("⇥", "Distance", plan.TotalDistanceKm.ToString("0") + " km", VoxelEngine.UI.UITheme.AccentCyan));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⧗", "Travel time", GridRouteRecorder.FormatDuration(plan.TotalSeconds),
                plan.EstimatedHours > 12f ? WarnInk : VoxelEngine.UI.UITheme.AccentGreen));
            p.Add(VoxelEngine.UI.UITheme.StatRow("➤", "Hold speed", plan.PeakSpeedMs.ToString("0") + " m/s", VoxelEngine.UI.UITheme.AccentBlue));
            // The reserve line names what it is measuring. A hydrogen drive has no battery line to
            // read, and showing "0 Wh stored" on a ship with a full tank would be a lie about the
            // ship rather than about the route.
            string store = plan.StoredEnergyWh >= 1000f
                ? (plan.StoredEnergyWh / 1000f).ToString("0.0") + " kWh"
                : plan.StoredEnergyWh.ToString("0") + " Wh";
            p.Add(VoxelEngine.UI.UITheme.StatRow("⚡", "Energy", plan.TotalWattHours.ToString("0") + " Wh of "
                + store + " in the " + plan.StoreName,
                plan.ReserveMargin01 >= RouteRules.MinimumReserve01 ? VoxelEngine.UI.UITheme.AccentGreen : BadInk));
            p.Add(VoxelEngine.UI.UITheme.StatRow("◍", "Reserve left", (plan.ReserveMargin01 * 100f).ToString("0") + " %",
                plan.ReserveMargin01 >= RouteRules.MinimumReserve01 ? OkInk : BadInk));
            // The drive's own cost, spelled out: pilots ask "how long can I hold the burn" before
            // they ask anything else, and that is this line and nothing else.
            if (plan.ThrustPowerWatts > 0.001f)
                p.Add(VoxelEngine.UI.UITheme.StatRow("☄", "Drive draw", plan.ThrustPowerWatts.ToString("0") + " W for "
                    + GridRouteRecorder.FormatDuration(plan.TotalBurnSeconds) + " of burn", VoxelEngine.UI.UITheme.AccentAmber));
            if (plan.HydrogenLitres > 0.5f)
                p.Add(VoxelEngine.UI.UITheme.StatRow("⌬", "Hydrogen", plan.HydrogenLitres.ToString("0") + " L burned in the burns",
                    plan.HydrogenLitres > 0.5f ? WarnInk : OkInk));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⛭", "Thrust asked", plan.RequiredThrustNewtons.ToString("0") + " kN of "
                + (plan.AvailableThrustNewtons / 1000f).ToString("0.0") + " kN",
                plan.RequiredThrustNewtons <= plan.AvailableThrustNewtons * 1.02f ? OkInk : BadInk));
            if (clear) p.Add(VoxelEngine.UI.UITheme.StatRow("✓", "Verdict", "CLEAR — flyable as planned", OkInk));
        }

        private static void AddLegRows(VisualElement p, RoutePlan plan)
        {
            if (!plan.IsValid || plan.Legs == null || plan.Legs.Count == 0) return;
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Legs"));
            for (int i = 0; i < plan.Legs.Count; i++)
            {
                var leg = plan.Legs[i];
                string detail = leg.DistanceKm.ToString("0") + " km · "
                              + GridRouteRecorder.FormatDuration(leg.Seconds) + " · "
                              + leg.WattHours.ToString("0") + " Wh";
                if (leg.BurnCount > 1) detail += " · " + leg.BurnCount + " burns";
                if (leg.Well != null) detail += " · well " + leg.Well.DisplayName;
                if (leg.ThroughAtmosphere) detail += " · atmosphere";
                Color ink = leg.NeedsMultiBurn ? WarnInk : leg.ThroughAtmosphere ? WarnInk : VoxelEngine.UI.UITheme.TextSecondary;
                p.Add(VoxelEngine.UI.UITheme.StatRow((i + 1).ToString(), "Leg", detail, ink));
            }
        }

        private static void AddProfileRow(VisualElement p, GridRouteRecorder recorder, ShipRoute route)
        {
            var row = Row();
            row.Add(VoxelEngine.UI.UITheme.SmallButton("ECONOMY", () =>
            {
                recorder.SetProfile(RouteSpeedProfile.Economy);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, route.Profile == RouteSpeedProfile.Economy ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.BgSlot));
            row.Add(VoxelEngine.UI.UITheme.SmallButton("STANDARD", () =>
            {
                recorder.SetProfile(RouteSpeedProfile.Standard);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, route.Profile == RouteSpeedProfile.Standard ? VoxelEngine.UI.UITheme.AccentCyan : VoxelEngine.UI.UITheme.BgSlot));
            row.Add(VoxelEngine.UI.UITheme.SmallButton("SPRINT", () =>
            {
                recorder.SetProfile(RouteSpeedProfile.Sprint);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, route.Profile == RouteSpeedProfile.Sprint ? VoxelEngine.UI.UITheme.AccentAmber : VoxelEngine.UI.UITheme.BgSlot));
            p.Add(row);
            p.Add(VoxelEngine.UI.UITheme.Muted(route.Profile switch
            {
                RouteSpeedProfile.Economy => "Slow, cheap, and the only profile a light ship can hold across a well.",
                RouteSpeedProfile.Sprint   => "All the thrust there is, all the time: burns energy and arrives hot.",
                _ => "The working compromise — most of the speed for much less of the bill.",
            }));
        }

        // ── The shelf ────────────────────────────────────────────────────────
        private static void AddBookRows(VisualElement p, GridRouteRecorder recorder, RouteBook book)
        {
            p.Add(VoxelEngine.UI.UITheme.Spacer(6));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Route Book" + (book != null && book.Count > 0 ? " · " + book.Count : string.Empty)));
            if (book == null || book.Count == 0)
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("Empty. A route is a ship's most valuable cargo: record the run once and the "
                    + "next captain can cost it before they burn anything."));
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "PanelScroll_RouteBook";
            scroll.style.maxHeight = 150f;
            scroll.style.marginTop = 2;
            scroll.style.marginBottom = 2;
            VoxelEngine.UI.UITheme.StyleScroller(scroll);

            var routes = book.Routes;
            for (int i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null) continue;
                bool selected = r.routeName == recorder.selectedRouteName;
                var row = Row();
                var pick = VoxelEngine.UI.UITheme.SmallButton((selected ? "▸ " : "  ") + r.routeName
                    + "  (" + r.LegCount + ")", () =>
                {
                    recorder.SelectRoute(r.routeName);
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, selected ? VoxelEngine.UI.UITheme.AccentCyan : VoxelEngine.UI.UITheme.BgSlot);
                pick.style.flexGrow = 1;
                row.Add(pick);
                row.Add(VoxelEngine.UI.UITheme.SmallButton("✕", () =>
                {
                    recorder.RemoveRoute(r.routeName);
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.BgSlot));
                scroll.Add(row);
            }
            p.Add(scroll);
        }

        // ── The auto-run ─────────────────────────────────────────────────────
        // The route panel owns the loop because the loop *is* a route with an intent bolted on: the
        // names, the targets and the armed stops all live next to the plan they are priced against, so
        // reading the cost and setting the schedule is one screen rather than a scavenger hunt.
        static bool _confirmRunOut;

        public static void AddAutoRunRows(VisualElement p, GridRouteRecorder recorder)
        {
            var grid = recorder.Grid;
            if (grid == null) return;
            var ap = grid.GetComponentInParent<GridRouteAutopilot>() ?? grid.GetComponent<GridRouteAutopilot>()
                     ?? grid.GetComponentInChildren<GridRouteAutopilot>();
            if (ap == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Spacer(6));
                p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Auto-Run"));
                p.Add(VoxelEngine.UI.UITheme.Muted("No autopilot on this grid: the recorder can cost a flight, "
                    + "but nothing here can be told to fly one. Fit an AUTO-RUN PILOT to the ship's control grid."));
                return;
            }

            var route = recorder.Selected;
            p.Add(VoxelEngine.UI.UITheme.Spacer(6));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Auto-Run"));
            p.Add(VoxelEngine.UI.UITheme.StatRow("◈", "State", ap.StatusLine,
                ap.IsFlying ? VoxelEngine.UI.UITheme.AccentCyan
                : ap.IsHoldingForTransfer ? VoxelEngine.UI.UITheme.AccentAmber
                : ap.State == AutoRunState.Halted ? VoxelEngine.UI.UITheme.TextSecondary : WarnInk));

            // Waymark ends: buttons cycle the live list rather than opening a second picker, because
            // a pad list is short and a modal for two names is ceremony.
            var names = GridWaymark.CollectNames();
            var startRow = Row();
            startRow.Add(VoxelEngine.UI.UITheme.StatRow("⇤", "Home", string.IsNullOrEmpty(ap.startWaymark) ? "not set" : ap.startWaymark,
                string.IsNullOrEmpty(ap.startWaymark) ? BadInk : OkInk));
            startRow.Add(VoxelEngine.UI.UITheme.SmallButton("◀ ▶", () =>
            {
                ap.startWaymark = NextName(names, ap.startWaymark);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(startRow);

            var endRow = Row();
            endRow.Add(VoxelEngine.UI.UITheme.StatRow("⇥", "Far end", string.IsNullOrEmpty(ap.endWaymark) ? "not set" : ap.endWaymark,
                string.IsNullOrEmpty(ap.endWaymark) ? BadInk : OkInk));
            endRow.Add(VoxelEngine.UI.UITheme.SmallButton("◀ ▶", () =>
            {
                ap.endWaymark = NextName(names, ap.endWaymark);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(endRow);

            var modeRow = Row();
            modeRow.Add(VoxelEngine.UI.UITheme.StatRow("↻", "At the far end", ModeName(ap), VoxelEngine.UI.UITheme.AccentBlue));
            modeRow.Add(VoxelEngine.UI.UITheme.SmallButton("MODE", () =>
            {
                ap.mode = (AutoRunMode)(((int)ap.mode + 1) % 3);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(modeRow);

            var t1 = Row();
            t1.Add(VoxelEngine.UI.UITheme.StatRow("⚡", "Leave at charge", (ap.targetCharge01 * 100f).ToString("0") + " %",
                ap.WantsPower ? WarnInk : OkInk));
            t1.Add(VoxelEngine.UI.UITheme.SmallButton("+5 / -5", () =>
            {
                ap.targetCharge01 = Mathf.Clamp01(ap.targetCharge01 + (UnityEngine.Input.GetKey(KeyCode.LeftShift) ? -0.05f : 0.05f));
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(t1);

            var t2 = Row();
            t2.Add(VoxelEngine.UI.UITheme.StatRow("⌬", "Leave at fuel / H2",
                (ap.targetFuel01 * 100f).ToString("0") + " % / " + (ap.targetHydrogen01 * 100f).ToString("0") + " %",
                (ap.WantsFuel || ap.WantsHydrogen) ? WarnInk : OkInk));
            t2.Add(VoxelEngine.UI.UITheme.SmallButton("+5 / -5", () =>
            {
                float d = UnityEngine.Input.GetKey(KeyCode.LeftShift) ? -0.05f : 0.05f;
                ap.targetFuel01 = Mathf.Clamp01(ap.targetFuel01 + d);
                ap.targetHydrogen01 = Mathf.Clamp01(ap.targetHydrogen01 + d);
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(t2);

            var condRow = Row();
            condRow.Add(VoxelEngine.UI.UITheme.SmallButton(ap.stopWhenCargoFull ? "CARGO FULL: ARMED" : "CARGO FULL: off", () =>
            {
                ap.stopWhenCargoFull = !ap.stopWhenCargoFull;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, ap.stopWhenCargoFull ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.BgSlot));
            condRow.Add(VoxelEngine.UI.UITheme.SmallButton(ap.stopWhenCargoEmpty ? "CARGO EMPTY: ARMED" : "CARGO EMPTY: off", () =>
            {
                ap.stopWhenCargoEmpty = !ap.stopWhenCargoEmpty;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, ap.stopWhenCargoEmpty ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.BgSlot));
            p.Add(condRow);

            var ctrlRow = Row();
            ctrlRow.Add(VoxelEngine.UI.UITheme.SmallButton(ap.IsArmed ? "DISARM" : "ARM LOOP", () =>
            {
                if (ap.IsArmed) ap.Disarm(); else ap.Arm();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, ap.IsArmed ? BadInk : VoxelEngine.UI.UITheme.AccentCyan));
            ctrlRow.Add(VoxelEngine.UI.UITheme.SmallButton(ap.State == AutoRunState.Paused ? "RESUME" : "PAUSE", () =>
            {
                if (ap.State == AutoRunState.Paused) ap.Resume(); else ap.Pause();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(ctrlRow);

            // The dangerous switch, offered and confirmed, never silent.
            bool noReserve = ap.minimumReserve01 <= 0.0001f;
            var dangerRow = Row();
            dangerRow.Add(VoxelEngine.UI.UITheme.SmallButton(
                noReserve ? (_confirmRunOut ? "CONFIRM: FLY UNTIL IT RUNS OUT" : "RESTORE RESERVE RULE")
                          : "RUN UNTIL IT RUNS OUT", () =>
            {
                if (!noReserve) { ap.SetRunUntilItRunsOut(true); _confirmRunOut = true; }
                else if (_confirmRunOut) { ap.SetRunUntilItRunsOut(false); _confirmRunOut = false; }
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, noReserve ? BadInk : VoxelEngine.UI.UITheme.BgSlot));
            p.Add(dangerRow);
            p.Add(VoxelEngine.UI.UITheme.Muted(noReserve
                ? "No reserve is held back. The loop flies until the plan itself refuses, then holds station and "
                  + "says why — that is the deal, and nothing here will rescue it."
                : "Each leg is priced against what is in the ship right now, plus the reserve rule: a leg that has "
                  + "stopped being affordable is refused before it is started, not discovered halfway."));

            if (!string.IsNullOrEmpty(ap.BlockReason) && ap.State == AutoRunState.Blocked)
                p.Add(VoxelEngine.UI.UITheme.StatRow("⚠", "Held", ap.BlockReason, BadInk));

            var log = ap.Log;
            if (log != null && log.Count > 0)
                for (int i = log.Count - 1; i >= Mathf.Max(0, log.Count - 3); i--)
                    p.Add(VoxelEngine.UI.UITheme.Muted(log[i]));
        }

        static string NextName(List<string> names, string current)
        {
            if (names == null || names.Count == 0) return current;
            int i = names.IndexOf(current ?? "");
            return names[(i + 1) % names.Count];
        }

        static string ModeName(GridRouteAutopilot ap) => ap.mode switch
        {
            AutoRunMode.OneWayThenPark => "one way, then park",
            AutoRunMode.FixedRunCount  => "fixed count · " + ap.RunsCompleted + "/" + Mathf.Max(1, ap.fixedRuns),
            _                          => "round trip, forever",
        };

        // ── Capture ──────────────────────────────────────────────────────────
        private static void AddCaptureRows(VisualElement p, GridRouteRecorder recorder, RouteBook book)
        {
            p.Add(VoxelEngine.UI.UITheme.Spacer(6));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Capture"));

            var row = Row();
            if (book != null && book.IsRecording)
            {
                int points = book.Draft != null ? book.Draft.waypoints.Count : 0;
                row.Add(VoxelEngine.UI.UITheme.SmallButton("STOP · FILE " + points + " POINTS", () =>
                {
                    var saved = recorder.CommitRecording();
                    if (saved != null) recorder.selectedRouteName = saved.routeName;
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.AccentGreen));
                row.Add(VoxelEngine.UI.UITheme.SmallButton("ABANDON", () =>
                {
                    book.AbandonRecording();
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.AccentRed));
            }
            else
            {
                row.Add(VoxelEngine.UI.UITheme.SmallButton("RECORD THIS RUN", () =>
                {
                    recorder.BeginRecording();
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.AccentCyan));
            }
            row.Add(VoxelEngine.UI.UITheme.SmallButton("MARK HERE", () =>
            {
                recorder.MarkWaypoint();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.BgSlot));
            p.Add(row);
            p.Add(VoxelEngine.UI.UITheme.Muted(book != null && book.IsRecording
                ? "A point is added whenever the ship has actually moved, so fly the run the way you would fly it "
                    + "with cargo and the shape of it is written down for you."
                : "Recording costs the console its 60 W and nothing else: it follows the ship, you do not follow it."));
        }

        // ── Destinations ─────────────────────────────────────────────────────
        private static void AddDestinationRows(VisualElement p, GridRouteRecorder recorder)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Bodies.Count == 0) return;

            p.Add(VoxelEngine.UI.UITheme.Spacer(6));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Plot A Leg"));

            var bodies = registry.Bodies;
            var origin = SpaceOrigin.Instance;
            double3 here = origin != null ? origin.GetCosmicKm(recorder.transform.position) : default;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.name = "PanelScroll_RouteDestinations";
            scroll.style.maxHeight = 170f;
            VoxelEngine.UI.UITheme.StyleScroller(scroll);

            var ordered = new List<BodyInstance>(bodies.Count);
            for (int i = 0; i < bodies.Count; i++) if (bodies[i] != null) ordered.Add(bodies[i]);
            ordered.Sort((a, b) => RangeKm(here, a).CompareTo(RangeKm(here, b)));

            for (int i = 0; i < ordered.Count && i < 24; i++)
            {
                var body = ordered[i];
                float km = RangeKm(here, body);
                var row = Row();
                var btn = VoxelEngine.UI.UITheme.SmallButton(body.DisplayName + "  ·  " + km.ToString("0") + " km", () =>
                {
                    recorder.AddRouteToBody(body);
                    VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                }, VoxelEngine.UI.UITheme.BgSlot);
                btn.style.flexGrow = 1;
                row.Add(btn);
                scroll.Add(row);
            }
            p.Add(scroll);
            p.Add(VoxelEngine.UI.UITheme.Muted("A plotted leg parks the arrival above the body's own atmosphere top: the recorder will "
                + "not write down a route whose last point is a collision."));
        }

        static float RangeKm(double3 from, BodyInstance body)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || body == null) return float.MaxValue;
            double3 to = registry.CosmicPositionOf(body);
            double dx = to.x - from.x, dy = to.y - from.y, dz = to.z - from.z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            r.style.marginTop = 2;
            r.style.marginBottom = 2;
            return r;
        }

    }
}
