using System.Collections.Generic;
using UnityEngine.UIElements;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    public static class LocalRouteUI
    {
        public static void AddTo(VisualElement panel, GridRouteRecorder recorder)
        {
            panel.Add(UITheme.Body("LOCAL NAVIGATION · CREATE AND START"));
            if (recorder.Grid == null || recorder.Book == null)
            { panel.Add(UITheme.Muted("Place this block on a vehicle first.")); return; }
            var book = recorder.Book;
            var control = LocalRoutePilot.For(recorder.Grid);
            var selected = recorder.Selected;
            int initial = selected != null ? (int)selected.travelMode - 1 : recorder is AutoRunPilot ? 0 : 2;
            var mode = new DropdownField("Travel mode", new List<string> { "Road", "Water", "Flight" },
                UnityEngine.Mathf.Clamp(initial < 0 ? 2 : initial, 0, 2));
            panel.Add(mode);
            var name = new TextField("New route name") { value = recorder.nextRouteName ?? "" };
            name.RegisterValueChangedCallback(evt => recorder.nextRouteName = evt.newValue);
            name.RegisterCallback<FocusInEvent>(_ => UIState.TextInputActive = true);
            name.RegisterCallback<FocusOutEvent>(_ => UIState.TextInputActive = false);
            name.RegisterCallback<DetachFromPanelEvent>(_ => UIState.TextInputActive = false);
            panel.Add(name);
            var status = UITheme.Muted(control.Status);
            panel.Add(status);
            var record = RoadNavigationUI.MakeButton("RECORD ROUTE FROM HERE", () =>
            {
                recorder.BeginRecording(Mode(mode));
                GameUIController.Instance?.RefreshCurrentPanel();
            });
            var file = RoadNavigationUI.MakeButton("STOP RECORDING AND SAVE ROUTE", () =>
            {
                if (recorder.CommitRecording() == null)
                { status.text = "Move at least 0.5 m before saving. Your draft is still recording."; return; }
                GameUIController.Instance?.RefreshCurrentPanel();
            });
            var mark = RoadNavigationUI.MakeButton("MARK WAYPOINT HERE", recorder.MarkWaypoint);
            var abandon = RoadNavigationUI.MakeButton("DISCARD RECORDING", () =>
            {
                book.AbandonRecording();
                GameUIController.Instance?.RefreshCurrentPanel();
            });
            panel.Add(record); panel.Add(file); panel.Add(mark); panel.Add(abandon);
            record.SetEnabled(!book.IsRecording && !control.IsActive);
            file.SetEnabled(book.IsRecording); mark.SetEnabled(book.IsRecording); abandon.SetEnabled(book.IsRecording);
            var capture = UITheme.Muted("");
            panel.Add(capture);
            capture.schedule.Execute(() => capture.text = book.IsRecording && book.Draft != null
                ? "Recording " + book.Draft.travelMode + ": " + book.Draft.waypoints.Count + "/4096 points. Close this panel and drive/pilot; reopen to save."
                : "Recording captures the start, movement every 4 m, and the endpoint when saved.").Every(250);
            var pick = RoadNavigationUI.MakeButton("SET DESTINATION IN THE WORLD", () =>
                RouteDestinationPicker.Begin(recorder, Mode(mode)));
            panel.Add(pick);
            pick.SetEnabled(!book.IsRecording && !control.IsActive);
            panel.Add(UITheme.Muted("World selection closes this panel: click the destination on screen; Escape cancels. "
                + "Flight: empty sky selects a point 100 m along the click ray. Selection saves a route, never starts it."));
            var names = new List<string>();
            foreach (var route in book.Routes) if (route != null) names.Add(route.routeName);
            DropdownField shelf = null;
            if (names.Count > 0)
            {
                shelf = new DropdownField("Saved route", names, UnityEngine.Mathf.Max(0, names.IndexOf(recorder.selectedRouteName)));
                panel.Add(shelf);
                shelf.RegisterValueChangedCallback(evt =>
                {
                    recorder.SelectRoute(evt.newValue);
                    var route = recorder.Selected;
                    if (route != null) mode.index = route.travelMode == RouteTravelMode.LegacyFlight ? 2 : UnityEngine.Mathf.Clamp((int)route.travelMode - 1, 0, 2);
                });
            }
            else panel.Add(UITheme.Muted("No saved routes. Record a run or select a world point above."));
            var reverse = RoadNavigationUI.MakeButton("REVERSE SELECTED ROUTE FOR THE RETURN TRIP", () =>
            {
                if (control.IsActive || (recorder.Grid.WheelAutopilot != null && recorder.Grid.WheelAutopilot.IsDriving)) return;
                recorder.ReverseSelected();
                GameUIController.Instance?.RefreshCurrentPanel();
            });
            reverse.SetEnabled(names.Count > 0 && !book.IsRecording && !control.IsActive);
            panel.Add(reverse);
            panel.Add(UITheme.Muted("Recorded routes replay in their saved order. Start near the first point, or reverse the route when departing its far end."));
            var confirm = new Toggle("I confirm an unattended run and will leave the path clear");
            panel.Add(confirm);
            var start = RoadNavigationUI.MakeButton("START SELECTED ROUTE · 5 SECOND COUNTDOWN", () =>
            {
                if (!confirm.value || book.IsRecording || control.IsActive) return;
                confirm.SetValueWithoutNotify(false);
                var route = shelf != null ? book.Find(shelf.value) : null;
                if (route == null) { status.text = "Create or choose a saved route first."; return; }
                if (route.travelMode != Mode(mode) && !(route.travelMode == RouteTravelMode.LegacyFlight && Mode(mode) == RouteTravelMode.Flight))
                { status.text = "The route was recorded for " + route.travelMode + ". Choose that mode or create a new route."; return; }
                if (route.travelMode == RouteTravelMode.Road)
                {
                    var guidance = RoadDriverGuidance.For(recorder);
                    if (!guidance.Plan(route)) { status.text = guidance.Status; return; }
                    var wheels = RoadWheelAutopilot.For(recorder.Grid);
                    wheels.StartRun(recorder, guidance);
                    status.text = wheels.Status;
                }
                else
                {
                    control.StartRun(recorder, route);
                    status.text = control.Status;
                }
            });
            panel.Add(start);
            start.SetEnabled(false);
            string lastControl = control.Status;
            string lastWheel = recorder.Grid.WheelAutopilot != null ? recorder.Grid.WheelAutopilot.Status : "";
            start.schedule.Execute(() =>
            {
                if (recorder == null || recorder.Grid == null || control == null) return;
                start.SetEnabled(confirm.value && names.Count > 0 && !book.IsRecording && !control.IsActive
                    && (recorder.Grid.WheelAutopilot == null || !recorder.Grid.WheelAutopilot.IsDriving));
                if (control.IsActive || lastControl != control.Status) status.text = control.Status;
                lastControl = control.Status;
                if (!control.IsActive && recorder.Grid.WheelAutopilot != null
                    && (recorder.Grid.WheelAutopilot.IsDriving || lastWheel != recorder.Grid.WheelAutopilot.Status))
                    status.text = recorder.Grid.WheelAutopilot.Status;
                if (recorder.Grid.WheelAutopilot != null) lastWheel = recorder.Grid.WheelAutopilot.Status;
            }).Every(150);
            panel.Add(RoadNavigationUI.MakeButton("STOP · WHEEL BRAKE / WATER OFF / FLIGHT HOLD", () =>
            {
                control.HoldOrStop();
                if (recorder.Grid.WheelAutopilot != null && recorder.Grid.WheelAutopilot.IsDriving)
                    recorder.Grid.WheelAutopilot.Park("Stopped by operator.");
                status.text = control.Status;
            }));
            panel.Add(RoadNavigationUI.MakeButton("RELEASE LOCAL FLIGHT / WATER CONTROL", () =>
            { control.Stop(); status.text = control.Status; }));
            panel.Add(UITheme.Muted("Water: real marine propellers and rudder, 2 m/s, no powered brake or docking; stopping cuts propulsion and the boat coasts. "
                + "Flight: 4 m/s, six-axis thrust plus gravity/lift margin, powered arrival hold. Exit the helm/cockpit before either starts. "
                + "Local water/flight legs: 200 m maximum, 2 km total, clear loaded surroundings. No obstacle detours or automatic resume."));
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
        }

        private static RouteTravelMode Mode(DropdownField field) => (RouteTravelMode)(field.index + 1);
    }
}
