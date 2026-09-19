// Assets/Scripts/VoxelEngine/GridSystem/GridTrainScheduleBlock.cs
//
// THE CONDUCTOR'S DESK - a grid block that drives a train's schedule.
//
// WHY A BLOCK AND NOT A FIELD ON THE BOGIE
// The bogie knows how to follow rail; it should not know what the train is FOR. Putting
// the schedule in its own block means a train earns its schedule the way it earns a
// container or a turret - by the player building one onto it - and the block is the thing
// the console, the departure boards and the save all point at.
//
// HOW IT DRIVES
// Each stop becomes a destination on the bogie (A* over the rail graph). When the bogie
// raises Arrived, the block starts timing the stop's wait condition against the station
// it is standing at; when the condition clears, the index advances and the next stop is
// routed. The loop wraps, so a schedule is a service pattern, not a one-shot script.
//
// It never fights the player: hand-driving (clearing the destination, lifting the truck
// off the rail) simply pauses the service, and the status line says so instead of the
// train teleporting back to work.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public class GridTrainScheduleBlock : GridBlock
    {
        [Header("Service")]
        [Tooltip("The ordered stop list. Stations are matched by NAME, so rebuilding a " +
                 "station does not orphan the service that serves it.")]
        public TrainScheduleData schedule = new();

        /// <summary>Index of the stop the service is currently working on.</summary>
        public int CurrentIndex { get; private set; }

        /// <summary>True while the train is standing at a stop serving its wait condition.</summary>
        public bool IsWaiting => _waiting;

        /// <summary>One line for consoles and departure boards.</summary>
        public string StatusLabel { get; private set; } = "No schedule.";

        private GridRailBogie _bogie;
        private bool _waiting;
        private float _waitStart;
        private float _nextTick;

        // ── Registry: departure boards read every service in the world ──────────
        private static readonly List<GridTrainScheduleBlock> s_all = new();
        public static IReadOnlyList<GridTrainScheduleBlock> All => s_all;

        /// <summary>The grid's name, for boards and consoles.</summary>
        public string TrainName => _bogie != null && _bogie.transform != null
            ? _bogie.gameObject.name
            : gameObject.name;

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
            _bogie = GetComponentInParent<GridRailBogie>();
            if (_bogie != null) _bogie.Arrived += OnArrived;
        }

        private void OnDisable()
        {
            s_all.Remove(this);
            if (_bogie != null) _bogie.Arrived -= OnArrived;
            _bogie = null;
            _waiting = false;
        }

        private void OnArrived(GridRailBogie bogie)
        {
            if (bogie != _bogie || schedule.IsEmpty) return;
            _waiting = true;
            _waitStart = Time.time;
        }

        private void FixedUpdate()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 0.4f;

            if (schedule.IsEmpty)
            {
                StatusLabel = "No schedule.";
                return;
            }
            if (_bogie == null)
            {
                _bogie = GetComponentInParent<GridRailBogie>();
                if (_bogie != null) _bogie.Arrived += OnArrived;
            }
            if (_bogie == null)
            {
                StatusLabel = "Needs a Rail Truck on this grid.";
                return;
            }
            if (!_bogie.IsOnRails)
            {
                _waiting = false;
                StatusLabel = "Truck is off the rails.";
                return;
            }

            var entry = schedule[CurrentIndex];

            // Standing at the stop of the current entry without a wait running (a fresh
            // load, a hand-driven arrival) starts the wait rather than idling forever.
            if (!_waiting && _bogie.Destination == null && _bogie.Speed < 0.05f &&
                AtStationOf(entry))
            {
                _waiting = true;
                _waitStart = Time.time;
            }

            if (_waiting)
            {
                var station = FindStation(entry != null ? entry.stationName : "");
                if (ScheduleConditions.Satisfied(entry, station, _waitStart))
                {
                    _waiting = false;
                    CurrentIndex = (CurrentIndex + 1) % schedule.entries.Count;
                    RouteToCurrent();
                }
                else
                {
                    StatusLabel = $"Waiting at {EntryStationName(entry)} - " +
                                  ScheduleConditions.Describe(entry);
                }
                return;
            }

            if (_bogie.Destination == null && _bogie.Speed < 0.05f)
            {
                // Idle with a schedule: resume the service.
                RouteToCurrent();
                return;
            }

            StatusLabel = _bogie.Destination != null
                ? $"To {EntryStationName(schedule[CurrentIndex])}"
                : "Running.";
        }

        /// <summary>
        /// Sends the train to the current entry's station. Names that resolve to no station,
        /// or stations with no track, are reported rather than swallowed - a service that
        /// silently skips a stop teaches the player to distrust the board.
        /// </summary>
        private void RouteToCurrent()
        {
            var entry = schedule[CurrentIndex];
            string name = EntryStationName(entry);
            var station = FindStation(entry.stationName);

            if (station == null)
            {
                StatusLabel = $"No station named {name}.";
                return;
            }
            var track = station.ServedTrack;
            if (track == null)
            {
                StatusLabel = $"{name} has no track beside it.";
                return;
            }
            if (!_bogie.SetDestination(track))
            {
                StatusLabel = $"No rail route to {name}.";
                return;
            }

            _bogie.powered = true;
            StatusLabel = $"To {name}";
        }

        private bool AtStationOf(ScheduleEntry entry)
        {
            if (entry == null || _bogie == null || _bogie.CurrentTrack == null) return false;
            var station = FindStation(entry.stationName);
            if (station == null || station.ServedTrack == null) return false;

            var here = _bogie.CurrentTrack;
            if (here == station.ServedTrack) return true;
            var links = here.Links;
            for (int i = 0; i < links.Count; i++)
                if (links[i] == station.ServedTrack) return true;
            return false;
        }

        private static string EntryStationName(ScheduleEntry entry)
            => entry == null || string.IsNullOrEmpty(entry.stationName)
                ? "unnamed station"
                : entry.stationName;

        private static RailStation FindStation(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var all = RailStation.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s != null && string.Equals(s.StationName, name, System.StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        // ── Save support (persistence calls these) ─────────────────────────────
        /// <summary>Serialised stop list, additive in the save record.</summary>
        public string ScheduleJson
        {
            get => JsonUtility.ToJson(schedule);
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                var data = JsonUtility.FromJson<TrainScheduleData>(value);
                if (data != null) schedule = data;
            }
        }

        public void RestoreServiceState(int index)
        {
            CurrentIndex = schedule.IsEmpty ? 0 : Mathf.Clamp(index, 0, schedule.entries.Count - 1);
        }
    }
}
