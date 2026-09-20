// Assets/Scripts/VoxelEngine/Building/TrainSchedule.cs
//
// THE SCHEDULE - what a train does when nobody is driving it.
//
// Modelled on the thing players already know from modded Minecraft: a schedule is an
// ordered list of STOPS, and each stop carries a CONDITION that decides when the train
// leaves again. Everything else - routing, junctions, signals - already exists under it:
// a stop becomes a destination on the bogie, the graph solves the route, and the wait
// condition is checked against the station the train is standing at.
//
// Deliberately data-only. The block that OWNS a schedule (GridTrainScheduleBlock) and the
// boards that READ schedules (RailDisplayScreen) both live elsewhere; this file is the
// shared vocabulary so neither has to invent its own.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>
    /// When a train leaves a stop. Appended, never reordered: a save stores the int.
    /// </summary>
    public enum ScheduleWait
    {
        /// <summary>Leave after a fixed dwell time - the platform clock.</summary>
        Seconds = 0,
        /// <summary>Leave when every slot of the station hold carries something.</summary>
        HoldFull = 1,
        /// <summary>Leave when the station hold has been drained empty.</summary>
        HoldEmpty = 2,
        /// <summary>Leave when the hold has at least one slot free - loaded but not stuffed.</summary>
        HoldHasSpace = 3,
        /// <summary>Leave when every container aboard is empty - unloaded, done here.
        /// Appended 12.3.0-dev; a save stores the int, so waits never reorder.</summary>
        TrainEmpty = 4,
        /// <summary>Leave when every slot of every container aboard carries something.</summary>
        TrainFull = 5,
    }

    [Serializable]
    public sealed class ScheduleEntry
    {
        /// <summary>Station NAME, not a reference: stations are matched by name so a
        /// schedule survives its station being rebuilt, exactly like v1 intended.</summary>
        public string stationName = "";

        public ScheduleWait wait = ScheduleWait.Seconds;

        /// <summary>Dwell for <see cref="ScheduleWait.Seconds"/>. Ignored by the others.</summary>
        public float seconds = 15f;

        public ScheduleEntry() { }

        public ScheduleEntry(string stationName, ScheduleWait wait, float seconds)
        {
            this.stationName = stationName;
            this.wait = wait;
            this.seconds = seconds;
        }
    }

    [Serializable]
    public sealed class TrainScheduleData
    {
        public List<ScheduleEntry> entries = new();

        public bool IsEmpty => entries == null || entries.Count == 0;

        public ScheduleEntry this[int index]
        {
            get
            {
                if (entries == null || entries.Count == 0) return null;
                int i = ((index % entries.Count) + entries.Count) % entries.Count;
                return entries[i];
            }
        }
    }

    /// <summary>
    /// Shared reading of a wait condition against a station, so the schedule block, the
    /// console and any future conductor UI all agree on what "full" means.
    /// </summary>
    public static class ScheduleConditions
    {
        /// <summary>
        /// Whether the wait at <paramref name="entry"/> is satisfied, given the station the
        /// train is standing at and when it arrived. A missing station never satisfies:
        /// a train waits for a platform that does not exist rather than leaving early,
        /// which is the failure a player can see and fix.
        /// </summary>
        public static bool Satisfied(ScheduleEntry entry, RailStation station, float arrivedAt,
            int trainUsed, int trainSlots)
        {
            if (entry == null) return true;

            switch (entry.wait)
            {
                case ScheduleWait.Seconds:
                    return Time.time - arrivedAt >= Mathf.Max(0f, entry.seconds);

                case ScheduleWait.HoldFull:
                case ScheduleWait.HoldEmpty:
                case ScheduleWait.HoldHasSpace:
                {
                    if (station == null || station.Hold == null) return false;
                    int used = 0;
                    for (int i = 0; i < station.Hold.Size; i++)
                        if (!station.Hold.GetSlot(i).IsEmpty) used++;

                    if (entry.wait == ScheduleWait.HoldFull) return used >= station.Hold.Size;
                    if (entry.wait == ScheduleWait.HoldEmpty) return used == 0;
                    return used < station.Hold.Size;
                }

                // Train-side waits (12.3.0): the mirror images of the hold waits. A load
                // stop releases on TrainFull, an unload stop on TrainEmpty - without them
                // a schedule can only guess at what the transfer actually achieved.
                case ScheduleWait.TrainEmpty:
                    return trainSlots == 0 || trainUsed == 0;

                case ScheduleWait.TrainFull:
                    return trainSlots > 0 && trainUsed >= trainSlots;

                default:
                    return true;
            }
        }

        /// <summary>One line naming the condition, for consoles and boards.</summary>
        public static string Describe(ScheduleEntry entry)
        {
            if (entry == null) return "no stop";
            string wait = entry.wait switch
            {
                ScheduleWait.HoldFull => "until hold full",
                ScheduleWait.HoldEmpty => "until hold empty",
                ScheduleWait.HoldHasSpace => "until hold has space",
                ScheduleWait.TrainEmpty => "until train empty",
                ScheduleWait.TrainFull => "until train full",
                _ => $"dwell {Mathf.Max(0f, entry.seconds):0}s",
            };
            string name = string.IsNullOrEmpty(entry.stationName) ? "unnamed station" : entry.stationName;
            return $"{name}  ·  {wait}";
        }
    }
}
