using UnityEngine.UIElements;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    public static class RoadWheelAutopilotUI
    {
        public static void AddTo(VisualElement panel, GridRouteRecorder recorder, RoadDriverGuidance guidance)
        {
            if (recorder.Grid == null) return;
            var grid = recorder.Grid;
            var control = RoadWheelAutopilot.For(grid);
            panel.Add(UITheme.Body("UNATTENDED WHEEL RUN"));
            panel.Add(UITheme.Muted("One-way, loaded paved roads, 4 m/s maximum. 4–16 grounded wheels and an Auto-Run Pilot or cockpit reference required. "
                + "Front axle steers during autonomy; manual wheel settings stay unchanged. No reversing, docking or automatic recovery."));
            var status = UITheme.Muted(control.Status);
            panel.Add(status);
            var confirm = new Toggle("Start an unattended run; I will leave its path clear");
            NavigationFieldStyle.Apply(confirm);
            panel.Add(confirm);
            var start = RoadNavigationUI.MakeButton("START WHEEL RUN · 5 SECOND COUNTDOWN", () =>
            {
                if (control == null || guidance == null || !confirm.value) return;
                confirm.SetValueWithoutNotify(false);
                control.StartRun(recorder, guidance);
            });
            var stop = RoadNavigationUI.MakeButton("STOP AND APPLY WHEEL PARKING BRAKE", () =>
            {
                if (control != null) control.Park("Stopped by operator. Wheel parking brake applied.");
            });
            var release = RoadNavigationUI.MakeButton("RELEASE WHEEL CONTROL / PARKING BRAKE", () =>
            {
                if (control != null) control.Release();
            });
            panel.Add(start);
            panel.Add(stop);
            panel.Add(release);
            panel.Add(UITheme.Muted("Hazards latch a stop: clear the cause and explicitly start again. "
                + "Active runs reload parked, never restart. Seated movement input takes manual control. "
                + "Wheel brakes require ground contact and cannot hold every slope/load. Secure the vehicle before releasing."));
            status.schedule.Execute(() =>
            {
                if (control == null || grid == null) return;
                status.text = control.Status + (grid.WheelParkingBrake ? " · PARKING BRAKE" : "");
                start.SetEnabled(confirm.value && guidance != null && guidance.HasRoute && !control.IsDriving);
                release.SetEnabled(grid.WheelControlHeld);
            }).Every(150);
            start.SetEnabled(false);
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
        }
    }
}
