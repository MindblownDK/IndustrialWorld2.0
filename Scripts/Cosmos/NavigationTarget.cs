// Assets/Scripts/VoxelEngine/Cosmos/NavigationTarget.cs
//
// The player's navigation target: one named thing, set by clicking a contact
// on the orbital map, cleared by clicking empty space. Session-robust (the
// name persists in PlayerPrefs) and always resolved live — bodies from the
// cosmic registry, constructs from their grid identity, the belt from its
// rock centroid — so the target tracks moving craft without the map open.
//
// Deliberately UI-free: the map sets it, and item 8 (route recorder /
// autopilot) will follow it. Neither depends on the other's UI.

using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    public static class NavigationTarget
    {
        private const string PrefsKey = "IW_NavTarget";
        private const char PrefsSep = '|';

        public const string BeltName = "Asteroid Belt";

        private static bool _restored;

        public static bool HasTarget { get; private set; }
        public static string TargetName { get; private set; } = "";
        public static MapEntryKind TargetKind { get; private set; } = MapEntryKind.Planet;

        /// <summary>Set (and persist) the target. An empty name clears it.</summary>
        public static void Set(string name, MapEntryKind kind)
        {
            if (string.IsNullOrEmpty(name)) { Clear(); return; }
            HasTarget = true;
            TargetName = name;
            TargetKind = kind;
            PlayerPrefs.SetString(PrefsKey, $"{(int)kind}{PrefsSep}{name}");
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            HasTarget = false;
            TargetName = "";
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        /// <summary>
        /// Restore the persisted target once (validating it still resolves).
        /// Safe to call every frame; only runs the lookup the first time.
        /// </summary>
        public static void EnsureRestored()
        {
            if (_restored) return;
            _restored = true;
            if (!PlayerPrefs.HasKey(PrefsKey)) return;
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            int sep = raw.IndexOf(PrefsSep);
            if (sep <= 0) return;
            if (!int.TryParse(raw.Substring(0, sep), out int kindInt)) return;
            string name = raw.Substring(sep + 1);
            if (string.IsNullOrEmpty(name)) return;
            HasTarget = true;
            TargetName = name;
            TargetKind = (MapEntryKind)kindInt;
            if (!TryResolve(out _)) Clear();
        }

        /// <summary>Live cosmic position (km) of the target, or false when it no longer exists.</summary>
        public static bool TryResolve(out double3 cosmicKm)
        {
            cosmicKm = default;
            if (!HasTarget || string.IsNullOrEmpty(TargetName)) return false;

            // Fresh map snapshot first — exact for everything while the map is open.
            var entries = OrbitalTrackingService.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Name == TargetName)
                {
                    cosmicKm = entries[i].PositionKm;
                    return true;
                }
            }

            // Map closed (stale snapshot): resolve live from the world.
            var registry = CosmicRegistry.Instance;
            if (registry != null && registry.IsReady)
            {
                if (TargetKind == MapEntryKind.Asteroid && TargetName == BeltName)
                    return TryResolveBelt(registry, out cosmicKm);
                if (registry.Sun != null)
                {
                    string sunName = registry.Sun.settings != null ? registry.Sun.settings.displayName : "Sun";
                    if (TargetName == sunName) { cosmicKm = registry.Sun.positionKmD; return true; }
                }
                var bodies = registry.Bodies;
                if (bodies != null)
                {
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        var body = bodies[i];
                        if (body != null && body.DisplayName == TargetName)
                        {
                            cosmicKm = registry.CosmicPositionOf(body);
                            return true;
                        }
                    }
                }
            }

            var grid = FindGrid(TargetName);
            if (grid != null)
            {
                Vector3 scenePos = grid.Body != null ? grid.Body.worldCenterOfMass : grid.transform.position;
                var origin = SpaceOrigin.Instance;
                cosmicKm = origin != null ? origin.GetCosmicKm(scenePos) : default;
                return true;
            }

            return false;
        }

        /// <summary>The target construct's grid, when the target is a live craft.</summary>
        public static bool TryResolveGrid(out GridEntity grid)
        {
            grid = null;
            if (!HasTarget) return false;
            grid = FindGrid(TargetName);
            return grid != null;
        }

        private static GridEntity FindGrid(string displayName)
        {
            var identities = GridIdentity.All;
            for (int i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (identity == null || identity.Grid == null) continue;
                if (identity.DisplayName == displayName) return identity.Grid;
            }
            return null;
        }

        private static bool TryResolveBelt(CosmicRegistry registry, out double3 cosmicKm)
        {
            cosmicKm = BeltCentroidKm(registry);
            return registry.Asteroids != null && registry.Asteroids.Count > 0;
        }

        /// <summary>
        /// Mean rock position (cosmic km) of the system's asteroid shell.
        /// Shared with the tracking snapshot so the map and the resolver agree.
        /// </summary>
        public static double3 BeltCentroidKm(CosmicRegistry registry)
        {
            double3 sum = default;
            int n = 0;
            var rocks = registry != null ? registry.Asteroids : null;
            if (rocks != null)
            {
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (rocks[i] == null) continue;
                    sum += new double3(rocks[i].positionKm.x, rocks[i].positionKm.y, rocks[i].positionKm.z);
                    n++;
                }
            }
            double3 sun = registry != null && registry.Sun != null ? registry.Sun.positionKmD : default;
            return n == 0 ? sun : sun + sum / n;
        }

        /// <summary>Nearest rock distance (km) from the centroid — the shell's inner edge.</summary>
        public static double BeltInnerRadiusKm(CosmicRegistry registry, double3 centroidKm)
        {
            double minR = double.MaxValue;
            double3 sun = registry != null && registry.Sun != null ? registry.Sun.positionKmD : default;
            var rocks = registry != null ? registry.Asteroids : null;
            if (rocks != null)
            {
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (rocks[i] == null) continue;
                    double3 p = sun + new double3(rocks[i].positionKm.x, rocks[i].positionKm.y, rocks[i].positionKm.z);
                    double d = math.length(p - centroidKm);
                    if (d < minR) minR = d;
                }
            }
            return minR == double.MaxValue ? 0d : minR;
        }

        /// <summary>Furthest rock distance (km) from the centroid — the belt's drawn radius.</summary>
        public static double BeltRadiusKm(CosmicRegistry registry, double3 centroidKm)
        {
            double maxR = 0d;
            double3 sun = registry != null && registry.Sun != null ? registry.Sun.positionKmD : default;
            var rocks = registry != null ? registry.Asteroids : null;
            if (rocks != null)
            {
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (rocks[i] == null) continue;
                    double3 p = sun + new double3(rocks[i].positionKm.x, rocks[i].positionKm.y, rocks[i].positionKm.z);
                    double d = math.length(p - centroidKm);
                    if (d > maxR) maxR = d;
                }
            }
            return maxR;
        }
    }
}
