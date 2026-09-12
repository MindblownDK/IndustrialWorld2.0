using System.Collections.Generic;
using UnityEngine.UIElements;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    public static class AutoRunPilotUI
    {
        public static VisualElement BuildPanel(AutoRunPilot pilot)
        {
            var panel = UITheme.MachinePanel();
            panel.name = "AutoRunPilotPanel";
            panel.style.width = 500;
            panel.Add(UITheme.Body("AUTO-RUN PILOT · VEHICLE CONTROL"));
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
            if (pilot == null || pilot.Grid == null || pilot.Book == null)
            { panel.Add(UITheme.Muted("Place this pilot on a grid first.")); return panel; }
            panel.Add(UITheme.Muted("Create and manage routes in the Route Planner / existing Route Recorder or Nav Plotter on this grid. This pilot only executes a saved route."));
            var session = RouteRunSession.For(pilot.Grid);
            var names = new List<string>();
            foreach (var route in pilot.Book.Routes) if (route != null) names.Add(route.routeName);
            int index = names.IndexOf(session.SelectedRouteName);
            if (index < 0 && names.Count > 0) { index = 0; session.SelectedRouteName = names[0]; session.Confirmed = false; }
            if (names.Count > 0)
            {
                var choice = new DropdownField("Saved route", names, index);
                NavigationFieldStyle.Apply(choice);
                choice.RegisterValueChangedCallback(evt =>
                { session.SelectedRouteName = evt.newValue; session.Confirmed = false; GameUIController.Instance?.RefreshCurrentPanel(); });
                panel.Add(choice);
            }
            else panel.Add(UITheme.Muted("No saved routes on this grid. Open the Route Planner to create one."));
            var details = UITheme.Muted("");
            panel.Add(details);
            var confirm = new Toggle("I confirm an unattended run and will leave its path clear") { value = session.Confirmed };
            NavigationFieldStyle.Apply(confirm);
            confirm.RegisterValueChangedCallback(evt => session.Confirmed = evt.newValue);
            panel.Add(confirm);
            var start = RoadNavigationUI.MakeButton("START SELECTED ROUTE", () =>
            {
                session.Start(pilot);
                confirm.SetValueWithoutNotify(session.Confirmed);
            });
            start.style.minHeight = 40;
            start.style.fontSize = 12;
            panel.Add(start);
            // Do not silently disable this button: every press gives a durable result/reason.
            var status = UITheme.Body(session.Status);
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.color = UITheme.AccentCyan;
            status.style.fontSize = 12;
            panel.Add(status);
            status.schedule.Execute(() =>
            {
                if (pilot == null || session == null) return;
                status.text = session.Status;
                var route = pilot.Book?.Find(session.SelectedRouteName);
                details.text = route == null ? "No route selected." : route.travelMode + " · " + route.waypoints.Count + " saved points";
            }).Every(100);
            panel.Add(RoadNavigationUI.MakeButton("STOP · WHEEL BRAKE / WATER OFF / FLIGHT HOLD", () => session.Stop(false)));
            panel.Add(RoadNavigationUI.MakeButton("RELEASE CONTROL / WHEEL PARKING BRAKE", () => session.Stop(true)));
            panel.Add(UITheme.Muted("Road/Water/Flight local runs use a 5-second countdown after preflight. Legacy space routes use the space controller's own arming checks. A refused start shows its reason here and in a notification."));
            panel.Add(UITheme.Muted("Water coasts when propulsion is cut. Local Flight Stop keeps a powered hold; Release can let an airborne ship fall. Exit the helm/cockpit and stop before a local ship run. Vehicle locks, power, propulsion and path-clearance checks still apply."));
            panel.Add(UITheme.Muted("Cockpit forward is preferred; otherwise align the pilot's forward arrow with travel. Local-run pilot control power: " + pilot.controlWatts.ToString("0.#") + " W, plus propulsion."));
            var legacy = pilot.Grid.GetComponent<VoxelEngine.Navigation.GridRouteAutopilot>();
            if (legacy != null && pilot.HasStarMap
                && pilot.Book.Find(session.SelectedRouteName)?.travelMode == VoxelEngine.Navigation.RouteTravelMode.LegacyFlight)
            {
                pilot.selectedRouteName = session.SelectedRouteName;
                var loop = new Foldout { text = "Legacy space loop controls", value = false };
                NavigationFieldStyle.Apply(loop);
                VoxelEngine.Navigation.GridRouteUI.AddAutoRunRows(loop, pilot);
                panel.Add(loop);
            }
            return panel;
        }
    }
}
