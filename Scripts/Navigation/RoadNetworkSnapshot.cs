using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Environment;

namespace IndustrialWorld.Navigation
{
    /// <summary>A bounded, explicitly refreshed view of one loaded road component.
    /// Open crossings retain network membership; only navigation availability changes.</summary>
    public sealed class RoadNetworkSnapshot
    {
        private readonly List<AsphaltRoad> _roads = new List<AsphaltRoad>();
        private readonly List<AsphaltRoad> _nearby = new List<AsphaltRoad>(32);
        private readonly HashSet<AsphaltRoad> _seen = new HashSet<AsphaltRoad>();
        private readonly Dictionary<RoadRun, float> _runAreas = new Dictionary<RoadRun, float>();
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyList<AsphaltRoad> Roads => _roads;
        public bool IsComplete { get; private set; }
        public string Message { get; private set; }
        public float Area { get; private set; }
        public float Condition01 { get; private set; }
        public float WorstCondition01 { get; private set; }
        public float EstimatedTraffic { get; private set; }
        public int BlockedCells { get; private set; }
        public int UnnamedCells { get; private set; }
        public int RunCount => _runAreas.Count;
        public int NameCount => _names.Count;
        public string DisplayName { get; private set; } = "Unnamed network";

        public void Capture(Vector3 position)
        {
            _roads.Clear();
            _seen.Clear();
            IsComplete = false;
            RoadSurfaceUtility.QueryNearby(position, RoadRoutePlanner.EndpointReach, _nearby);
            AsphaltRoad seed = null;
            float best = RoadRoutePlanner.EndpointReach * RoadRoutePlanner.EndpointReach;
            foreach (var road in _nearby)
            {
                if (!RoadRoutePlanner.IsVehicleRoad(road)) continue;
                float distance = (position - road.transform.position).sqrMagnitude;
                if (distance >= best) continue;
                seed = road;
                best = distance;
            }
            if (seed == null)
            {
                Message = "No loaded vehicle road within 8 m of this vehicle.";
                Summarize();
                return;
            }
            _roads.Add(seed);
            _seen.Add(seed);
            for (int cursor = 0; cursor < _roads.Count; cursor++)
            {
                var current = _roads[cursor];
                RoadSurfaceUtility.QueryNearby(current.transform.position,
                    Mathf.Min(16f, Mathf.Max(1f, current.cellSize * 1.8f)), _nearby);
                foreach (var next in _nearby)
                {
                    if (_seen.Contains(next) || !RoadRoutePlanner.AreConnected(current, next)) continue;
                    if (_roads.Count >= RoadRoutePlanner.NodeBudget)
                    {
                        Message = "Partial snapshot: 4096-cell limit reached. Naming is disabled.";
                        Summarize();
                        return;
                    }
                    _seen.Add(next);
                    _roads.Add(next);
                }
            }
            IsComplete = true;
            Message = "Loaded-road snapshot. Refresh after paving, loading, traffic or repairs.";
            Summarize();
        }

        private void Summarize()
        {
            Area = 0f;
            float wornArea = 0f, worstWear = 0f;
            EstimatedTraffic = 0f;
            BlockedCells = UnnamedCells = 0;
            _runAreas.Clear();
            _names.Clear();
            foreach (var road in _roads)
            {
                float area = CellArea(road);
                Area += area;
                float wear = Mathf.Clamp01(road.SavedWear);
                wornArea += area * wear;
                worstWear = Mathf.Max(worstWear, wear);
                if (RoadRoutePlanner.IsBlocked(road)) BlockedCells++;
                if (string.IsNullOrEmpty(road.NetworkName)) UnnamedCells++;
                else _names.Add(road.NetworkName);
                if (road.Run == null) continue;
                _runAreas.TryGetValue(road.Run, out float share);
                _runAreas[road.Run] = share + area;
            }
            // A run ledger may extend beyond this loaded component. Attribute by covered area,
            // never add its entire traffic counter once per road cell.
            foreach (var pair in _runAreas)
                EstimatedTraffic += pair.Key.TrafficMetres
                    * Mathf.Clamp01(pair.Value / Mathf.Max(1f, pair.Key.PavedArea));
            Condition01 = Area > 0f ? 1f - wornArea / Area : 0f;
            WorstCondition01 = _roads.Count > 0 ? 1f - worstWear : 0f;
            DisplayName = _names.Count == 0 ? "Unnamed network" : _names.Count > 1 ? "Mixed network names" : FirstName();
        }

        // Match the existing wear ledger's paved-area definition, not decorative mesh shoulders.
        private static float CellArea(AsphaltRoad road)
        {
            float cell = Mathf.Max(0.25f, road.cellSize);
            return cell * cell;
        }

        private string FirstName()
        {
            foreach (string name in _names) return name;
            return string.Empty;
        }

        /// <summary>Reports labels without ever guessing a winner when differently named roads join.</summary>
        public string NameSummary()
        {
            var names = new List<string>(_names);
            names.Sort(StringComparer.Ordinal);
            if (names.Count > 4) names.RemoveRange(4, names.Count - 4);
            string summary = string.Join(", ", names);
            if (_names.Count > 4) summary += " (and " + (_names.Count - 4) + " more)";
            if (UnnamedCells > 0) summary += (summary.Length > 0 ? " · " : "") + UnnamedCells + " unnamed cells";
            return summary;
        }

        /// <summary>Compare with a fresh capture before renaming: a changed graph, moved vehicle,
        /// or newly merged label requires the player to review the new scope.</summary>
        public bool SameNamingScope(RoadNetworkSnapshot other)
        {
            if (!IsComplete || !other.IsComplete || _roads.Count != other._roads.Count
                || UnnamedCells != other.UnnamedCells || !_names.SetEquals(other._names)) return false;
            foreach (var road in _roads)
                if (road == null || !other._seen.Contains(road)) return false;
            return true;
        }

        public bool TryRename(string requested, bool replaceExisting, out string reason)
        {
            string name = AsphaltRoad.NormalizeNetworkName(requested);
            if (!IsComplete || _roads.Count == 0)
            { reason = "Inspect a complete loaded network before naming it."; return false; }
            if (name.Length == 0)
            { reason = "Enter a network name (up to 48 characters)."; return false; }
            bool replacesName = false;
            foreach (var road in _roads)
            {
                if (!RoadRoutePlanner.IsVehicleRoad(road))
                { reason = "Road changed. Refresh and review before naming."; return false; }
                if (!string.IsNullOrEmpty(road.NetworkName) && road.NetworkName != name) replacesName = true;
            }
            if (replacesName && !replaceExisting)
            { reason = "Confirm replacement of existing names on these loaded cells."; return false; }
            foreach (var road in _roads) road.SetNetworkName(name);
            Summarize();
            reason = "Named " + _roads.Count + " loaded road cells: " + name + ". Save the world to persist.";
            return true;
        }
    }
}
