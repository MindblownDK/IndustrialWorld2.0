using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    public static class RoadNavigationUI
    {
        public static void AddPlanningTo(VisualElement panel, GridRouteRecorder recorder)
        {
            panel.Add(UITheme.Body("NAMED ROAD DESTINATIONS"));
            var names = GridWaymark.CollectNames();
            if (names.Count > 0)
            {
                var choice = new DropdownField("Destination waymark", names, 0);
                NavigationFieldStyle.Apply(choice);
                panel.Add(choice);
                panel.Add(MakeButton("SAVE ROAD ROUTE TO THIS DESTINATION", () =>
                {
                    var source = GridWaymark.FindSource(choice.value);
                    var book = recorder != null ? recorder.Book : null;
                    if (source == null || recorder == null || recorder.Grid == null || book == null || book.IsRecording)
                    { BuildFeedbackHud.Show("ROUTE PLANNER", "Available grid/waymark required. Finish any recording first."); return; }
                    string wanted = string.IsNullOrWhiteSpace(recorder.nextRouteName) ? "Road to " + choice.value : recorder.nextRouteName.Trim();
                    var route = new ShipRoute { routeName = wanted, travelMode = RouteTravelMode.Road,
                        sceneCoordinates = VoxelEngine.Cosmos.SpaceOrigin.Instance == null };
                    for (int n = 2; book.Find(route.routeName) != null; n++) route.routeName = wanted + " #" + n;
                    route.AddWaypoint(RouteCoordinates.Capture(RoadNavigationAnchor.ForGrid(recorder.Grid, RouteTravelMode.Road), route.sceneCoordinates));
                    route.AddWaypoint(RouteCoordinates.Capture(source.WaymarkWorldPosition, route.sceneCoordinates));
                    if (!book.Append(route)) { BuildFeedbackHud.Show("ROUTE PLANNER", "Could not save this road route."); return; }
                    recorder.SelectRoute(route.routeName);
                    recorder.nextRouteName = "";
                    BuildFeedbackHud.Show("ROUTE SAVED", "Preview it here, then select and start it on the Auto-Run Pilot.");
                    GameUIController.Instance?.RefreshCurrentPanel();
                }));
            }
            else panel.Add(UITheme.Muted("No named destinations. Record a route or use world-point selection above."));
            RoadNetworkUI.AddTo(panel, recorder);
        }

        public static void AddTo(VisualElement panel, GridRouteRecorder recorder)
        {
            var guidance = RoadDriverGuidance.For(recorder);
            panel.Add(UITheme.Spacer(6));
            panel.Add(UITheme.Body("NAMED ROAD DESTINATIONS / WHEEL STATUS"));
            panel.Add(UITheme.Muted("Loaded vehicle roads only. Endpoints snap within 8 m; access to the road and parking are manual. "
                + "Destination road is captured when planned. Replan after moving a waymark. Guidance is not saved."));
            var status = UITheme.Muted(guidance.Status);
            panel.Add(status);
            status.schedule.Execute(() =>
            {
                if (guidance != null) status.text = guidance.Status;
            }).Every(250);
            var names = GridWaymark.CollectNames();
            if (names.Count > 0)
            {
                var choice = new DropdownField("Destination", names, 0);
            NavigationFieldStyle.Apply(choice);
                panel.Add(choice);
                var plan = MakeButton("PLAN ROAD ROUTE", () => guidance.Plan(choice.value));
                panel.Add(plan);
            }
            else panel.Add(UITheme.Muted("No named waymarks available. Name a connector or static refuel pad near the destination road."));
            var stop = MakeButton("STOP ROAD GUIDANCE", guidance.Stop);
            panel.Add(stop);
            stop.schedule.Execute(() => stop.SetEnabled(guidance != null && guidance.HasRoute)).Every(250);
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
            RoadWheelAutopilotUI.AddTo(panel, recorder, guidance);
            RoadNetworkUI.AddTo(panel, recorder);
        }

        internal static Button MakeButton(string title, Action action)
        {
            var button = UITheme.SmallButton(title, action, UITheme.BgSlot);
            button.style.marginTop = 4;
            float hover = 0f, wanted = 0f;
            bool pressed = false;
            button.RegisterCallback<PointerEnterEvent>(_ => wanted = 1f);
            button.RegisterCallback<PointerLeaveEvent>(_ => { wanted = 0f; pressed = false; });
            button.RegisterCallback<PointerDownEvent>(_ => pressed = true);
            button.RegisterCallback<PointerUpEvent>(_ => pressed = false);
            button.schedule.Execute(() =>
            {
                hover = Mathf.MoveTowards(hover, wanted, 0.16f);
                bool enabled = button.enabledInHierarchy;
                button.style.opacity = enabled ? 1f : 0.4f;
                button.style.backgroundColor = Color.Lerp(UITheme.BgSlot, UITheme.AccentCyan,
                    enabled ? pressed ? 0.6f : hover * 0.3f : 0f);
                button.style.scale = new Scale(Vector3.one * (enabled ? pressed ? 0.99f : 1f + hover * 0.03f : 1f));
            }).Every(16);
            return button;
        }
    }
}
