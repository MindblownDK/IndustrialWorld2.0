// Assets/Scripts/VoxelEngine/Pressure/RoomAtmosphereService.cs
//
// World-level lookup: "is the air at this point breathable because a pressurised
// room encloses it?" Player life support, HUDs and offline survival all ask here,
// so there is exactly one answer in the game.
//
// The registry is push-based (systems register themselves on enable), so the query
// never allocates and never scans the scene.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Pressure
{
    public static class RoomAtmosphereService
    {
        private static readonly List<GridPressureSystem> Systems = new();

        public static void Register(GridPressureSystem system)
        {
            if (system == null || Systems.Contains(system)) return;
            Systems.Add(system);
        }

        public static void Unregister(GridPressureSystem system)
        {
            if (system == null) return;
            Systems.Remove(system);
        }

        /// <summary>The pressurised room containing a world point, or null.</summary>
        public static GridRoom RoomAt(Vector3 worldPosition)
        {
            for (int i = Systems.Count - 1; i >= 0; i--)
            {
                var system = Systems[i];
                if (system == null) { Systems.RemoveAt(i); continue; }
                var room = system.RoomAtWorld(worldPosition);
                if (room != null) return room;
            }
            return null;
        }

        /// <summary>True when a sealed, charged room makes this point breathable.</summary>
        public static bool IsBreathableAt(Vector3 worldPosition)
        {
            var room = RoomAt(worldPosition);
            if (room != null && room.IsBreathable) return true;

            // Hammer-built station compartments (11.23.0). Checked second because ship
            // rooms are the common case; a station is only ever a handful of volumes.
            // Deliberately NOT folded into RoomAt, which returns a GridRoom - a station
            // room is a different type living in world space, and widening that return
            // type would force every existing caller to handle a case it does not have.
            return StationRoomSolver.IsBreathableAt(worldPosition);
        }

        /// <summary>Short HUD descriptor for the room at a point ("—" when outside).</summary>
        public static string StatusAt(Vector3 worldPosition)
        {
            var room = RoomAt(worldPosition);
            if (room != null) return room.StatusLabel;

            var station = StationRoomSolver.RoomAt(worldPosition);
            return station == null ? "—" : station.StatusLabel;
        }
    }
}
