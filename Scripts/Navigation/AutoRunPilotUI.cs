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
            panel.style.width = 520;
            panel.Add(UITheme.Body("AUTO-RUN PILOT · VEHICLE CONTROL"));
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
            if (pilot == null || pilot.Grid == null || pilot.Book == null)
            { panel.Add(UITheme.Muted("Place this pilot on a grid first.")); return panel; }
            panel.Add(UITheme.Muted("Create and manage routes in the Route Planner / existing Route Recorder or Nav Plotter on this grid. This pilot only executes a saved route. Assessment below uses live block stats — change a block's output and it updates."));
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

            // Dynamic assessment — reads live block stats, not hardcoded
            var assessmentLabel = UITheme.Body("");
            assessmentLabel.style.whiteSpace = WhiteSpace.Normal;
            assessmentLabel.style.fontSize = 11;
            assessmentLabel.style.color = UITheme.TextSecondary;
            assessmentLabel.style.backgroundColor = new UnityEngine.Color(0.06f, 0.08f, 0.09f, 0.9f);
            assessmentLabel.style.paddingLeft = 8;
            assessmentLabel.style.paddingRight = 8;
            assessmentLabel.style.paddingTop = 8;
            assessmentLabel.style.paddingBottom = 8;
            assessmentLabel.style.marginTop = 6;
            assessmentLabel.style.marginBottom = 6;
            UITheme.Radius(assessmentLabel, 4);
            panel.Add(assessmentLabel);

            var details = UITheme.Muted("");
            panel.Add(details);
            panel.Add(UITheme.Muted("Road Network runs approach the nearer end, brake, then drive to the other end; either leg may reverse at up to 1.5 m/s. Keep both directions clear."));

            var confirm = new Toggle("I confirm an unattended run and will leave its path clear") { value = session.Confirmed };
            NavigationFieldStyle.Apply(confirm);
            confirm.RegisterValueChangedCallback(evt => session.Confirmed = evt.newValue);
            panel.Add(confirm);

            var start = RoadNavigationUI.MakeButton("START SELECTED ROUTE", () =>
            {
                session.TryStartRoute(pilot);
                confirm.SetValueWithoutNotify(session.Confirmed);
            });
            start.style.minHeight = 40;
            start.style.fontSize = 12;
            panel.Add(start);

            var status = UITheme.Body(session.Status);
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.color = UITheme.AccentCyan;
            status.style.fontSize = 12;
            panel.Add(status);

            // Live refresh of status + dynamic assessment
            status.schedule.Execute(() =>
            {
                if (pilot == null || session == null) return;
                status.text = session.Status;
                var route = pilot.Book?.Find(session.SelectedRouteName);
                if (route == null)
                {
                    details.text = "No route selected.";
                    assessmentLabel.text = "No route selected — assessment unavailable.";
                }
                else
                {
                    details.text = route.travelMode + " · " + route.waypoints.Count + " saved points";
                    // Dynamic assessment using live block stats
                    assessmentLabel.text = PilotRouteAssessment.BuildAssessment(route, pilot.Grid, pilot);
                }
            }).Every(250);

            panel.Add(RoadNavigationUI.MakeButton("STOP · WHEEL BRAKE / WATER OFF / FLIGHT HOLD", () => session.Stop(false)));
            panel.Add(RoadNavigationUI.MakeButton("RELEASE CONTROL / WHEEL PARKING BRAKE", () => session.Stop(true)));
            panel.Add(UITheme.Muted("Road/Water/Flight local runs use a 5-second countdown after preflight. Legacy space routes use the space controller's own arming checks. A refused start shows its reason here and in a notification."));
            panel.Add(UITheme.Muted("Water coasts when propulsion is cut. Local Flight Stop keeps a powered hold; Release can let an airborne ship fall. Exit the helm/cockpit and stop before a local ship run. Vehicle locks, power, propulsion and path-clearance checks still apply."));
            panel.Add(UITheme.Muted("Cockpit forward is preferred; otherwise align the pilot's forward arrow with travel. Local-run pilot control power: " + pilot.controlWatts.ToString("0.#") + " W (live from block), plus propulsion (wheel powerDrawWatts * suspensionStrength, thruster powerAtMaxThrust, maritime maxWattOutput/maxRPM/maxTorque — all dynamic)."));

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
