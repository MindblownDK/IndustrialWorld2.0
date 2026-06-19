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

        // Cached so Resources.Load is only called ONCE per session — if the asset has a broken
        // script reference, it logs "missing script" each load, so caching suppresses repeat spam.
        private static CosmosTemplateLibrary _cached;
        private static bool _loaded;

        /// <summary>Load the project's library, or null if none authored yet. Cached.</summary>
        public static CosmosTemplateLibrary Load()
        {
            if (_loaded) return _cached;
            _loaded = true;
            try
            {
                _cached = Resources.Load<CosmosTemplateLibrary>(ResourceName);
                if (_cached == null) return null;
                // Validate it actually has the systems list (broken .asset may load but be corrupt).
                if (_cached.systems == null) _cached.systems = new List<SolarSystemTemplate>();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CosmosTemplateLibrary] Failed to load: " + ex.Message +
                                 ". If Resources/CosmosTemplateLibrary.asset exists with a broken script " +
                                 "reference, delete it and re-run Tools ▸ Voxel Engine ▸ Create Solar System (Sol).");
                _cached = null;
            }
            return _cached;
        }

        /// <summary>Force a fresh reload (called by the authoring tools after creating/updating).</summary>
        public static void InvalidateCache()
        {
            _cached = null;
            _loaded = false;
        }

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
