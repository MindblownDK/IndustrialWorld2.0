// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Pilot cockpit for ship control.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        public Player.PlayerController Pilot { get; private set; }

        public void Enter(Player.PlayerController player)
        {
            Pilot = player;
            // Enter ship control mode
        }

        public void Exit()
        {
            Pilot = null;
        }
    }
}