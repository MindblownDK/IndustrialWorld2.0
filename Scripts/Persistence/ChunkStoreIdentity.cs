// Assets/Scripts/VoxelEngine/Persistence/ChunkStoreIdentity.cs
using System;
using System.IO;
using UnityEngine;

namespace VoxelEngine.Persistence
{
    /// <summary>
    /// The terrain identity a body's chunk store was written against.
    ///
    /// A region file only says "these voxels belong to chunk (x,y,z) of this body" — it says
    /// nothing about the FIELD those voxels were extracted from. Until now the only guard was
    /// <see cref="RegionFile"/>'s format version, which catches a changed binary layout but not
    /// a changed terrain: a body that regenerates from a different seed, radius, sea level or
    /// continent/mountain scale produces a completely different surface, and a stored chunk
    /// from the old field then loads as an island of the wrong terrain inside the new one —
    /// floating slabs, mesh/data disagreement and edits (mined or placed voxels) that no longer
    /// belong anywhere.
    ///
    /// This file is written once, next to the region files, and never rewritten while the store
    /// lives. On open, <see cref="ChunkStorage"/> compares it with the identity the LIVE body
    /// reports: equal means the stored chunks belong to this field, different means the store is
    /// quarantined (moved aside, never deleted) and the body regenerates fresh.
    /// </summary>
    [Serializable]
    public class ChunkStoreIdentity
    {
        /// <summary>
        /// Bump when the MEANING of this file changes (new compared value, new rule).
        /// 2 (10.0.0): the stored payload is fixed (3 bytes per voxel, `RegionFile` V4), so a
        /// store carrying an identity from format 1 holds chunks that cannot be read whole.
        /// </summary>
        public const int FormatVersion = 2;

        /// <summary>File name inside the per-body store folder.</summary>
        public const string FileName = "store.json";

        // ── The field identity: every value a stored chunk was generated from ──
        public int    formatVersion = FormatVersion;
        public string bodyName          = "";
        public int    seed;
        public float  radiusWorld;
        public float  baseHeight;
        public float  seaRadius;
        public float  continentScaleDir;
        public float  mountainScale;
        public int    isAsteroidBelt;

        // ── Diagnostics only: recorded, never compared ──
        public string build      = "";
        public string writtenUtc = "";

        /// <summary>One-line human summary for the console — the identity a store carries.</summary>
        public string Summary =>
            $"seed {seed}, radius {radiusWorld:0} m, sea {seaRadius:0} m, base {baseHeight:0} m, " +
            $"continent {continentScaleDir:0.#####}, mountains {mountainScale:0.###}" +
            (isAsteroidBelt == 1 ? ", asteroid belt" : "");

        /// <summary>
        /// True when this identity describes the same field as <paramref name="other"/>.
        /// Floats are compared with a tolerance so a save round-trip through JSON cannot
        /// quarantine a perfectly good store over a last-bit difference.
        /// </summary>
        public bool Matches(ChunkStoreIdentity other, out string difference)
        {
            difference = "";
            if (other == null) { difference = "no identity to compare against"; return false; }

            var diffs = new System.Text.StringBuilder();
            void Note(string label, object was, object now)
            {
                if (diffs.Length > 0) diffs.Append(", ");
                diffs.Append(label).Append(' ').Append(was).Append(" → ").Append(now);
            }

            if (formatVersion != other.formatVersion)
                Note("store format", formatVersion, other.formatVersion);
            if (seed != other.seed) Note("seed", seed, other.seed);
            if (!Close(radiusWorld, other.radiusWorld, 0.5f)) Note("radius", $"{radiusWorld:0} m", $"{other.radiusWorld:0} m");
            if (!Close(baseHeight, other.baseHeight, 0.5f)) Note("base height", $"{baseHeight:0} m", $"{other.baseHeight:0} m");
            if (!Close(seaRadius, other.seaRadius, 0.5f)) Note("sea", $"{seaRadius:0} m", $"{other.seaRadius:0} m");
            if (!Close(continentScaleDir, other.continentScaleDir, 1e-5f)) Note("continent scale", $"{continentScaleDir:0.#####}", $"{other.continentScaleDir:0.#####}");
            if (!Close(mountainScale, other.mountainScale, 1e-3f)) Note("mountain scale", $"{mountainScale:0.###}", $"{other.mountainScale:0.###}");
            if (isAsteroidBelt != other.isAsteroidBelt) Note("asteroid belt", isAsteroidBelt, other.isAsteroidBelt);
            if (!string.Equals(bodyName, other.bodyName, StringComparison.OrdinalIgnoreCase))
                Note("body", bodyName, other.bodyName);

            difference = diffs.ToString();
            return difference.Length == 0;
        }

        private static bool Close(float a, float b, float tolerance)
        {
            if (float.IsNaN(a) || float.IsNaN(b) || float.IsInfinity(a) || float.IsInfinity(b)) return false;
            return Mathf.Abs(a - b) <= tolerance;
        }

        /// <summary>Write this identity into the store folder. Returns false when the write fails.</summary>
        public bool TryWrite(string storeFolder)
        {
            try
            {
                build      = VoxelEngine.Core.GameVersion.Display;
                writtenUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(Path.Combine(storeFolder, FileName), JsonUtility.ToJson(this, true));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChunkStorage] Could not write the store identity in '{storeFolder}': {ex.Message}");
                return false;
            }
        }

        /// <summary>Read a store identity. False when the file is missing or unreadable.</summary>
        public static bool TryRead(string storeFolder, out ChunkStoreIdentity identity)
        {
            identity = null;
            string path = Path.Combine(storeFolder, FileName);
            if (!File.Exists(path)) return false;
            try
            {
                identity = JsonUtility.FromJson<ChunkStoreIdentity>(File.ReadAllText(path));
                return identity != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChunkStorage] Unreadable store identity '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
