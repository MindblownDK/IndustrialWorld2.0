using System;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    public static class RoadNavigationUI
    {
        public static void AddTo(VisualElement panel, GridRouteRecorder recorder)
        {
            RoadNetworkUI.AddTo(panel, recorder);
            var guidance = RoadDriverGuidance.For(recorder);
            RoadWheelAutopilotUI.AddTo(panel, recorder, guidance);
            panel.Add(UITheme.Spacer(6));
            panel.Add(UITheme.Body("ROAD GUIDANCE · MANUAL DRIVING"));
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
                panel.Add(choice);
                var plan = MakeButton("PLAN ROAD ROUTE", () => guidance.Plan(choice.value));
                panel.Add(plan);
            }
            else panel.Add(UITheme.Muted("No named waymarks available. Name a connector or static refuel pad near the destination road."));
            var stop = MakeButton("STOP ROAD GUIDANCE", guidance.Stop);
            panel.Add(stop);
            stop.schedule.Execute(() => stop.SetEnabled(guidance != null && guidance.HasRoute)).Every(250);
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
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
