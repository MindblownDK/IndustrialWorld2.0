using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Navigation;
using VoxelEngine.UI;

namespace IndustrialWorld.Navigation
{
    public static class RoadNetworkUI
    {
        public static void AddTo(VisualElement panel, GridRouteRecorder recorder)
        {
            var snapshot = new RoadNetworkSnapshot();
            var section = new VisualElement { name = "LoadedRoadNetwork" };
            section.style.marginTop = 8;
            section.style.marginBottom = 8;
            section.Add(UITheme.Body("ROAD NETWORK · LOADED CELLS"));
            var title = UITheme.Body("Nearby road network");
            title.style.color = UITheme.AccentCyan;
            var metrics = UITheme.Muted(string.Empty);
            var names = UITheme.Muted(string.Empty);
            var status = UITheme.Muted(string.Empty);
            section.Add(title);
            section.Add(metrics);
            section.Add(names);
            section.Add(UITheme.Muted("Traffic is an area-attributed estimate from current wear-run ledgers, not vehicles/hour or lifetime traffic. "
                + "Repairs, run splits and world reloads can reset those counters. Road condition and names survive saves."));
            section.Add(UITheme.Muted("This view covers connected loaded vehicle roads only, not unloaded extensions. "
                + "Open drawbridges stay in the network. New cells start unnamed; split sections keep their labels."));
            var input = new TextField("Network name") { maxLength = AsphaltRoad.NetworkNameLimit };
            NavigationFieldStyle.Apply(input);
            var confirm = new Toggle("Replace existing names on these loaded cells");
            NavigationFieldStyle.Apply(confirm);
            section.Add(input);
            section.Add(confirm);
            Button save = null;

            System.Action render = () =>
            {
                title.text = snapshot.DisplayName;
                metrics.text = snapshot.Roads.Count == 0 ? "No network selected."
                    : snapshot.Roads.Count + " cells · " + snapshot.Area.ToString("0.0") + " m² · "
                      + snapshot.RunCount + " wear runs\n"
                      + "Condition " + (snapshot.Condition01 * 100f).ToString("0.0") + "% · Worst "
                      + (snapshot.WorstCondition01 * 100f).ToString("0.0") + "%\n"
                      + "Estimated weighted travel " + snapshot.EstimatedTraffic.ToString("0.0") + " m · "
                      + snapshot.BlockedCells + " unavailable cells";
                names.text = snapshot.NameSummary();
                status.text = snapshot.Message;
                save?.SetEnabled(snapshot.IsComplete && snapshot.Roads.Count > 0);
            };
            System.Func<Vector3> position = () => recorder.Grid != null
                ? recorder.Grid.Body != null ? recorder.Grid.Body.position : recorder.Grid.transform.position
                : recorder.transform.position;
            var refresh = RoadNavigationUI.MakeButton("REFRESH NEARBY NETWORK", () =>
            {
                if (recorder == null) return;
                snapshot.Capture(position());
                confirm.SetValueWithoutNotify(false);
                render();
                if (string.IsNullOrWhiteSpace(input.value) && snapshot.NameCount == 1)
                    input.SetValueWithoutNotify(snapshot.DisplayName);
            });
            save = RoadNavigationUI.MakeButton("SAVE NAME TO LOADED NETWORK", () =>
            {
                if (recorder == null) return;
                var current = new RoadNetworkSnapshot();
                current.Capture(position());
                if (!snapshot.SameNamingScope(current))
                {
                    snapshot = current;
                    confirm.SetValueWithoutNotify(false);
                    render();
                    status.text = "Network scope or names changed. Review this snapshot, then confirm again.";
                    return;
                }
                bool applied = current.TryRename(input.value, confirm.value, out string reason);
                snapshot = current;
                render();
                status.text = reason;
                if (applied)
                {
                    input.SetValueWithoutNotify(snapshot.DisplayName);
                    confirm.SetValueWithoutNotify(false);
                    BuildFeedbackHud.Show("Road network named", reason);
                }
            });
            section.Add(refresh);
            section.Add(save);
            section.Add(status);
            panel.Add(section);
            panel.Add(UITheme.AccentDivider(UITheme.AccentCyan));
            snapshot.Capture(position());
            render();
            if (snapshot.NameCount == 1) input.SetValueWithoutNotify(snapshot.DisplayName);
        }
    }
}
