// Assets/Scripts/VoxelEngine/Cosmos/SystemSeedState.cs
//
// Per-planet seed table for world creation.
//
// Design intent (per Thomas): when creating / cloning a world the player can set a CUSTOM SEED
// for every planet in the chosen solar system. A number is randomized for each planet from the
// start, the player can edit any of them, and that number is what generates (and re-generates,
// deterministically, on every load) that planet. Seeds are NEVER re-randomised after creation —
// they are persisted in the world sidecar and reused verbatim.
//
// Keyed by planet ORDER within the system template (index-based), stamped with the template's
// name so a mismatched template on load is detected gracefully. Phase 2 = breaking change, so
// index alignment is fine; if the template's planet order changes it's a fresh world anyway.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [Serializable]
    public class SystemSeedState
    {
        [Serializable]
        public struct PlanetSeed
        {
            public string planetName;   // display only
            public int    seed;
        }

        /// <summary>Name of the solar-system template this table was built for (validation).</summary>
        public string systemName;

        /// <summary>Ordered list — index aligns with SolarSystemTemplate.planets[] order.</summary>
        public List<PlanetSeed> planets = new List<PlanetSeed>();

        public bool IsValidFor(SolarSystemTemplate template)
            => template != null && template.systemName == systemName && planets != null;

        /// <summary>Get the seed for the planet at <paramref name="index"/> (falls back to master seed).</summary>
        public int GetSeed(int index, int fallback)
        {
            if (planets == null || index < 0 || index >= planets.Count) return fallback;
            return planets[index].seed;
        }

        public void SetSeed(int index, int seed)
        {
            if (planets == null || index < 0 || index >= planets.Count) return;
            var p = planets[index];
            p.seed = seed;
            planets[index] = p;
        }

        /// <summary>
        /// Build a fresh table for a template: one editable, RANDOM seed per planet. This is the
        /// "randomize a number from the start" step — every entry is a usable default the player
        /// may then tweak. Call only at world creation.
        /// </summary>
        public static SystemSeedState CreateRandomized(SolarSystemTemplate template)
        {
            var state = new SystemSeedState
            {
                systemName = template != null ? template.systemName : "Unknown",
            };
            if (template != null && template.planets != null)
            {
                foreach (var p in template.planets)
                {
                    state.planets.Add(new PlanetSeed
                    {
                        planetName = p != null && p.body != null ? p.body.bodyName : "Planet",
                        seed       = RandomSeed(),
                    });
                }
            }
            return state;
        }

        /// <summary>Re-randomise every planet's seed in place (the dice button).</summary>
        public void RandomizeAll()
        {
            if (planets == null) return;
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i];
                p.seed = RandomSeed();
                planets[i] = p;
            }
        }

        /// <summary>A reasonably-spread positive seed (1 .. int.MaxValue).</summary>
        public static int RandomSeed() => UnityEngine.Random.Range(1, int.MaxValue);

        public string ToJson()
            => JsonUtility.ToJson(this, prettyPrint: true);

        public static SystemSeedState FromJson(string json)
            => string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SystemSeedState>(json);
    }
}
