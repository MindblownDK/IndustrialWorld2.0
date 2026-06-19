// Assets/Scripts/VoxelEngine/Cosmos/CosmosTemplateLibrary.cs
//
// A Resources-loaded registry of the solar-system templates available to the player at world
// creation. Keeps templates authored anywhere in the project reachable from runtime code
// (WorldSession / main menu) without forcing every asset into a Resources folder.
//
// One asset lives at Resources/CosmosTemplateLibrary.asset. The authoring tools keep its
// `systems` list up to date.
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [CreateAssetMenu(menuName = "Voxel Engine/Planets/Cosmos Template Library", fileName = "CosmosTemplateLibrary")]
    public class CosmosTemplateLibrary : ScriptableObject
    {
        [Tooltip("Solar systems the player can choose from when creating a world.")]
        public List<SolarSystemTemplate> systems = new List<SolarSystemTemplate>();

        public const string ResourceName = "CosmosTemplateLibrary";

        /// <summary>Load the project's library, or null if none authored yet.</summary>
        public static CosmosTemplateLibrary Load()
            => Resources.Load<CosmosTemplateLibrary>(ResourceName);

        /// <summary>Resolve a system template by name (case-insensitive).</summary>
        public SolarSystemTemplate FindByName(string name)
        {
            if (systems == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < systems.Count; i++)
                if (systems[i] != null && systems[i].systemName == name) return systems[i];
            return null;
        }
    }
}
