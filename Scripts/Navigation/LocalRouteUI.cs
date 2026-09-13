using System.Collections.Generic;
using UnityEngine.UIElements;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    /// <summary>Route authoring only. Vehicle execution lives exclusively in AutoRunPilotUI.</summary>
    public static class LocalRouteUI
    {
        public static void AddTo(VisualElement panel, GridRouteRecorder recorder)
        {
            panel.Add(UITheme.Body("ROUTE PLANNER · RECORD / DESTINATIONS / PATHS"));
            if (recorder.Grid == null || recorder.Book == null)
            { panel.Add(UITheme.Muted("Place this planner on a grid first.")); return; }
            var book = recorder.Book;
            var overlay = RoutePathOverlay.For(book);
            var mode = new DropdownField("New route mode", new List<string> { "Road", "Water", "Flight" }, UnityEngine.Mathf.Clamp((int)recorder.PlanningMode - 1, 0, 2));
            NavigationFieldStyle.Apply(mode);
            mode.RegisterValueChangedCallback(_ => recorder.PlanningMode = (RouteTravelMode)(mode.index + 1));
            panel.Add(mode);
            var name = new TextField("Route name (new / rename)") { value = recorder.nextRouteName ?? "" };
            NavigationFieldStyle.Apply(name);
            name.RegisterValueChangedCallback(evt => recorder.nextRouteName = evt.newValue);
            panel.Add(name);
            var status = UITheme.Body("Create a route here; select and execute it from an Auto-Run Pilot on this grid.");
            status.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(status);
            System.Action<string> report = message => { status.text = message; BuildFeedbackHud.Show("ROUTE PLANNER", message); };
            System.Func<bool> canEdit = () =>
            {
                var local = recorder.Grid.GetComponent<LocalRoutePilot>();
                var legacy = recorder.Grid.GetComponent<GridRouteAutopilot>();
                if ((local != null && local.IsActive) || (legacy != null && legacy.IsArmed)
                    || (recorder.Grid.WheelAutopilot != null && recorder.Grid.WheelAutopilot.IsDriving))
                { report("Stop/release the active pilot before editing or recording routes."); return false; }
                return true;
            };
            panel.Add(RoadNavigationUI.MakeButton("RECORD ROUTE FROM HERE", () =>
            {
                if (!canEdit()) return;
                if (book.IsRecording) { report("A recording is already in progress."); return; }
                recorder.BeginRecording(recorder.PlanningMode);
                report(book.IsRecording ? "Recording started. Points and path stay visible until recording finishes." : "Recording could not start. Check the enabled planner and grid.");
                GameUIController.Instance?.RefreshCurrentPanel();
            }));
            panel.Add(RoadNavigationUI.MakeButton("MARK WAYPOINT HERE", () =>
            {
                if (!book.IsRecording) { report("Start a recording first."); return; }
                recorder.MarkWaypoint();
            }));
            panel.Add(RoadNavigationUI.MakeButton("FINISH AND SAVE RECORDING", () =>
            {
                if (!book.IsRecording) { report("No recording is active."); return; }
                if (recorder.CommitRecording() == null) { report("Move at least 0.5 m before saving; the draft is still recording."); return; }
                overlay.RefreshPreview(recorder.selectedRouteName);
                GameUIController.Instance?.RefreshCurrentPanel();
            }));
            panel.Add(RoadNavigationUI.MakeButton("DISCARD RECORDING", () =>
            { book.AbandonRecording(); GameUIController.Instance?.RefreshCurrentPanel(); }));
            var capture = UITheme.Muted("");
            panel.Add(capture);
            capture.schedule.Execute(() => capture.text = book.IsRecording && book.Draft != null
                ? "RECORDING · " + book.Draft.waypoints.Count + "/4096 points · amber path forced visible. Close this panel and drive/pilot normally."
                : "Recording idle. Samples about every 4 m, with start/end capture.").Every(150);
            panel.Add(RoadNavigationUI.MakeButton("PREPARE ROAD NETWORK RUN · NEAR END THEN OTHER END", () =>
            {
                if (!canEdit()) return;
                if (book.IsRecording) { report("Finish the recording first."); return; }
                var approach = new List<VoxelEngine.Building.AsphaltRoad>();
                var across = new List<VoxelEngine.Building.AsphaltRoad>();
                if (!RoadNetworkRun.TryPlan(RoadNavigationAnchor.ForGrid(recorder.Grid, RouteTravelMode.Road), approach, across, out var reason))
                { report(reason); return; }
                string wanted = string.IsNullOrWhiteSpace(recorder.nextRouteName) ? "Road Network Run" : recorder.nextRouteName.Trim();
                var route = new ShipRoute { routeName = wanted, travelMode = RouteTravelMode.RoadNetwork,
                    sceneCoordinates = true }; // v9.56.4-dev: always scene-local to avoid 1.4km cosmic offset
                for (int n = 2; book.Find(route.routeName) != null; n++) route.routeName = wanted + " #" + n;
                route.AddWaypoint(RouteCoordinates.Capture(RoadNavigationAnchor.SurfaceCentre(across[0]), route.sceneCoordinates));
                route.AddWaypoint(RouteCoordinates.Capture(RoadNavigationAnchor.SurfaceCentre(across[across.Count - 1]), route.sceneCoordinates));
                if (!book.Append(route)) { report("Could not save network route."); return; }
                recorder.SelectRoute(route.routeName); overlay.RefreshPreview(route.routeName); report(reason);
                GameUIController.Instance?.RefreshCurrentPanel();
            }));
            panel.Add(UITheme.Muted("Network runs use the nearer end first, then the other end. Vehicle must already be on that loaded, uniform-width, unbranched road. Low-speed reversing replaces unsafe automatic U-turns. Branches/loops require an explicit destination."));
            panel.Add(RoadNavigationUI.MakeButton("SET DESTINATION IN THE WORLD", () =>
            {
                if (!canEdit()) return;
                if (book.IsRecording) { report("Finish the recording before selecting another destination."); return; }
                RouteDestinationPicker.Begin(recorder, recorder.PlanningMode);
            }));
            panel.Add(UITheme.Muted("Move and look normally, aim the crosshair, then left-click the destination. Escape cancels. Tool/build clicks are reserved for selection. Flight empty sky selects 100 m along the crosshair."));
            var names = new List<string>();
            foreach (var route in book.Routes) if (route != null) names.Add(route.routeName);
            if (names.Count > 0)
            {
                int index = UnityEngine.Mathf.Max(0, names.IndexOf(recorder.selectedRouteName));
                recorder.SelectRoute(names[index]);
                var shelf = new DropdownField("Saved route to edit / preview", names, index);
                NavigationFieldStyle.Apply(shelf);
                panel.Add(shelf);
                shelf.RegisterValueChangedCallback(evt =>
                { recorder.SelectRoute(evt.newValue); overlay.RefreshPreview(evt.newValue); });
            }
            else panel.Add(UITheme.Muted("No saved routes yet."));
            panel.Add(RoadNavigationUI.MakeButton("SHOW SELECTED PATH", () =>
            {
                if (recorder.Selected == null) { report("Create or select a saved route first."); return; }
                overlay.SetPreview(recorder.Selected.routeName, true);
                report("Path display enabled. Invalid road plans show amber waypoint previews with a reason below.");
            }));
            panel.Add(RoadNavigationUI.MakeButton("HIDE PATH", () =>
            {
                overlay.SetPreview(recorder.selectedRouteName, false);
                report(book.IsRecording ? "Live recording stays visible until finished; saved preview is hidden."
                    : "Path hidden, including the current inspector preview.");
            }));
            panel.Add(RoadNavigationUI.MakeButton("REVERSE SELECTED ROUTE", () =>
            {
                if (!canEdit() || book.IsRecording) return;
                recorder.ReverseSelected(); overlay.RefreshPreview(recorder.selectedRouteName);
                report("Selected route reversed. Face the vehicle toward the new first leg before starting.");
            }));
            panel.Add(RoadNavigationUI.MakeButton("RENAME SELECTED ROUTE", () =>
            {
                if (!canEdit() || book.IsRecording) return;
                string wanted = (name.value ?? "").Trim();
                var selected = recorder.Selected;
                if (selected == null || wanted.Length == 0 || (book.Find(wanted) != null && book.Find(wanted) != selected))
                { report("Select a route and enter a non-empty, unique name above."); return; }
                book.Rename(selected, wanted); recorder.SelectRoute(wanted); overlay.RefreshPreview(wanted);
                GameUIController.Instance?.RefreshCurrentPanel();
            }));
            panel.Add(RoadNavigationUI.MakeButton("DELETE SELECTED SAVED ROUTE", () =>
            {
                if (!canEdit() || book.IsRecording || recorder.Selected == null) return;
                recorder.RemoveRoute(recorder.Selected.routeName); overlay.RefreshPreview(recorder.selectedRouteName);
                GameUIController.Instance?.RefreshCurrentPanel();
            }));
            var pathStatus = UITheme.Muted("");
            panel.Add(pathStatus);
            pathStatus.schedule.Execute(() => pathStatus.text = overlay.Status).Every(200);
            panel.Add(UITheme.Muted("The Grid Inspector overlay also has a ROUTES category. Amber markers show recordings; cyan paths show saved-route previews. Road previews resolve the pavement centreline used by the road planner, not a straight off-road shortcut. Arrows indicate point order."));
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
        }
    }
}
