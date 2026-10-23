// Assets/Scripts/VoxelEngine/Combat/IExplosiveCharge.cs
//
// 17.45.3-dev: a placed explosive answers ONE question - "was I hit?".
//
// A block's health lives on PlacedBlock, and PlacedBlock destroys the object the moment
// its own hit points run out. A charge never went through that path, so a bomb shot with
// a rifle or struck with a pickaxe was damaged like a wall, surviving hit after hit and
// finally vanishing without ever going off. Anything that implements this interface is
// set off by the FIRST point of damage it takes, whatever its block health says.

namespace VoxelEngine.Combat
{
    public interface IExplosiveCharge
    {
        /// <summary>Set the charge off now.</summary>
        void Detonate();
    }
}
