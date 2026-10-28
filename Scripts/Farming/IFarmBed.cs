// Assets/Scripts/VoxelEngine/Farming/IFarmBed.cs
//
// 17.36.0-dev — one shape for everything a screen can call a farm bed.
//
// A screen used to scan for FarmPlot and stop there, so a seed planter and a
// sapling planter were invisible to the page that exists to report them. The
// page does not need to know which component is growing the crop; it needs to
// know whether the bed is empty, what is in it, how far along it is, and
// whether it is thirsty. That is this interface, and nothing else.
//
// 18.2.0-dev — the interface learned the two things the page was faking.
//
// The remastered farm page drew a water column that was either a stub (dry) or
// full (watered) and a bed that read as "dead" only while it reported a missing
// water or oxygen requirement. Both were guesses about a number the beds
// already had. Water01 and IsDead are that number: the hydration a bed is
// actually holding right now, and whether the crop on it is already lost.
// Neither field is saved in a new shape and no bed changed how it grows, so a
// save written before this round loads exactly as it did.
//
// Biofarms deliberately do not implement it. A Biofarm is an oxygen machine
// that eats biomass; it is not a bed with a crop in it, and a farm page that
// counted it would be lying about what the player owns.

using UnityEngine;
using VoxelEngine.Pressure;

namespace VoxelEngine.Farming
{
    /// <summary>Shared greenhouse and atmosphere checks for crops that require oxygen.</summary>
    public static class FarmEnvironment
    {
        /// <summary>Sealed, breathable volume only: a charged grid room or a hammer-built
        /// station compartment. Ambient planetary air does NOT count here - this is the
        /// rule a greenhouse box on an airless moon has to satisfy.</summary>
        public static bool HasBreathableEnclosure(Vector3 worldPosition)
        {
            var gridRoom = RoomAtmosphereService.RoomAt(worldPosition);
            if (gridRoom != null && gridRoom.IsSealed)
                return gridRoom.IsBreathable;
            return StationRoomSolver.IsBreathableAt(worldPosition);
        }

        /// <summary>
        /// 18.3.0-dev: "has oxygen" for a crop, in the order that actually happens in play -
        /// a sealed breathable room first, then the planet itself. An oxygen-atmosphere
        /// world with real air density is breathable to a crop standing on its surface, so
        /// a farm on Earth needs no enclosure while the same box on the Moon does. This is
        /// the same authority the player's own life support uses, so crops and players
        /// never disagree about whether it is safe to breathe.
        /// </summary>
        public static bool HasBreathableAir(Vector3 worldPosition)
        {
            if (HasBreathableEnclosure(worldPosition)) return true;

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null || body.settings == null) return false;
            if (!body.settings.HasOxygen) return false;

            var sample = VoxelEngine.GridSystem.AtmosphereManager.Sample(worldPosition);
            return sample.AirDensity >= VoxelEngine.Player.PlayerEquipment.AtmosphereDensityThreshold;
        }
    }

    /// <summary>
    /// A bed a player can plant in: tilled soil, a seed planter, a sapling bed.
    /// Read-only by design - the page reports, the player plants through the
    /// block itself.
    /// </summary>
    public interface IFarmBed
    {
        /// <summary>What kind of bed this is, in one short word for a narrow column:
        /// "SOIL", "PLANTER", "TREE BED".</summary>
        string BedName { get; }

        /// <summary>The crop's name, or empty when nothing is planted.</summary>
        string CropName { get; }

        /// <summary>0..1 growth. Zero when the bed is empty.</summary>
        float Growth01 { get; }

        /// <summary>True when the crop can be harvested right now.</summary>
        bool IsReady { get; }

        /// <summary>True when a planted crop wants water it is not getting.</summary>
        bool NeedsWater { get; }

        /// <summary>True when a planted crop requires a sealed breathable enclosure and
        /// this bed is not currently in one.</summary>
        bool NeedsOxygen { get; }

        /// <summary>True when the bed has no crop in it.</summary>
        bool IsEmpty { get; }

        /// <summary>
        /// 0..1 hydration the bed is holding right now: 1 while it is being watered,
        /// falling as a planted crop goes without, 0 when the tray is empty. This is a
        /// reading, not a preference - a bed that needs no water still reports the
        /// water it has, so a screen can draw one honest column for every bed type.
        /// </summary>
        float Water01 { get; }

        /// <summary>
        /// True when the crop on this bed is already lost - it dried out, or it was
        /// planted somewhere it could never grow. A dead bed still holds its crop
        /// (that is what the player is looking at) but it cannot be harvested and it
        /// cannot be replanted until it is cleared. An empty bed is not dead.
        /// </summary>
        bool IsDead { get; }
    }
}
