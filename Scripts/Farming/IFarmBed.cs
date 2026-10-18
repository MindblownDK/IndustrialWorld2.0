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
// Biofarms deliberately do not implement it. A Biofarm is an oxygen machine
// that eats biomass; it is not a bed with a crop in it, and a farm page that
// counted it would be lying about what the player owns.

namespace VoxelEngine.Farming
{
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

        /// <summary>True when the bed has no crop in it.</summary>
        bool IsEmpty { get; }
    }
}
