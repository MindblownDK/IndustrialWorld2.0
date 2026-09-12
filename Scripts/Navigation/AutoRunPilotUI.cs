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
            panel.Add(UITheme.Body("AUTO-RUN PILOT"));
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
            if (pilot == null) { panel.Add(UITheme.Muted("Pilot block unavailable.")); return panel; }
            panel.Add(UITheme.Muted("1. Choose a nearby-road destination and plan a route. 2. Confirm and start the wheel run. "
                + "3. Stand clear during the countdown. No separate Route Recorder is needed."));
            panel.Add(UITheme.Muted("An existing cockpit supplies forward direction; without one, the pilot's cyan arrow points forward. "
                + "Control power while running: " + pilot.controlWatts.ToString("0.#") + " W, plus the vehicle's wheel demand."));
            RoadNavigationUI.AddTo(panel, pilot);
            return panel;
        }
    }
}
