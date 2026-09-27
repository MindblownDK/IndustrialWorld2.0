// Assets/Scripts/VoxelEngine/Environment/SurfaceProfileLibrary.cs
//
// THE LOOKUP. One asset holds every SurfaceProfile and the keys that reach them.
//
// Three key spaces share one table because a single contact patch can be described
// by any of them: a Unity Terrain layer, a collider's PhysicsMaterial, or a voxel
// MaterialId from this engine's own world. Resolution is dictionary-backed and
// built once, so a wheel asking "what am I standing on" costs a hash lookup and
// never a string comparison per frame.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Environment
{
    [CreateAssetMenu(menuName = "Voxel Engine/Environment/Surface Profile Library", fileName = "SurfaceProfileLibrary")]
    public class SurfaceProfileLibrary : ScriptableObject
    {
        /// <summary>Resources path the runtime loads the shipped library from.</summary>
        public const string ResourceName = "SurfaceProfileLibrary";

        [Tooltip("Every authored surface. Order matters only for keyword ties (first wins).")]
        public List<SurfaceProfile> profiles = new List<SurfaceProfile>();

        [Tooltip("Used when nothing matches the contact patch.")]
        public SurfaceProfile defaultProfile;

        private Dictionary<string, SurfaceProfile> _byKeyword;
        private Dictionary<byte, SurfaceProfile> _byVoxel;
        private bool _built;

        // ── Singleton access ────────────────────────────────────────────────
        private static SurfaceProfileLibrary s_active;
        private static bool s_searched;

        /// <summary>The library in use, or null when the project has not built one yet.</summary>
        public static SurfaceProfileLibrary Active
        {
            get
            {
                if (s_active != null) return s_active;
                if (!s_searched)
                {
                    s_searched = true;
                    s_active = Resources.Load<SurfaceProfileLibrary>(ResourceName);
                }
                return s_active;
            }
            set { s_active = value; s_searched = true; }
        }

        /// <summary>Editor tooling calls this after rebuilding the asset.</summary>
        public static void InvalidateActive() { s_active = null; s_searched = false; }

        public SurfaceProfile Default => defaultProfile;

        private void OnEnable() => _built = false;
        public void Rebuild() { _built = false; Build(); }

        private void Build()
        {
            if (_built) return;
            _byKeyword = new Dictionary<string, SurfaceProfile>(64, System.StringComparer.OrdinalIgnoreCase);
            _byVoxel = new Dictionary<byte, SurfaceProfile>(32);

            foreach (var profile in profiles)
            {
                if (profile == null) continue;
                AddKeyword(profile.surfaceName, profile);
                AddKeyword(profile.name, profile);
                if (profile.terrainLayerKeywords != null)
                    foreach (var key in profile.terrainLayerKeywords) AddKeyword(key, profile);
                if (profile.physicsMaterialKeywords != null)
                    foreach (var key in profile.physicsMaterialKeywords) AddKeyword(key, profile);
                if (profile.voxelMaterials != null)
                    foreach (var voxel in profile.voxelMaterials) _byVoxel[(byte)voxel] = profile;
            }
            _built = true;
        }

        private void AddKeyword(string key, SurfaceProfile profile)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            key = key.Trim();
            if (!_byKeyword.ContainsKey(key)) _byKeyword[key] = profile;
        }

        /// <summary>Exact key first, then a contains-scan so "Terrain_Sand_01" finds "sand".</summary>
        public SurfaceProfile ResolveName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return Default;
            Build();
            if (_byKeyword.TryGetValue(rawName, out var direct) && direct != null) return direct;

            string lowered = rawName.ToLowerInvariant();
            foreach (var pair in _byKeyword)
            {
                if (pair.Value == null) continue;
                if (lowered.Contains(pair.Key.ToLowerInvariant())) return pair.Value;
            }
            return Default;
        }

        public SurfaceProfile ResolveVoxel(MaterialId material)
        {
            Build();
            return _byVoxel.TryGetValue((byte)material, out var profile) && profile != null ? profile : Default;
        }

        public SurfaceProfile ResolveVoxel(byte material)
        {
            Build();
            return _byVoxel.TryGetValue(material, out var profile) && profile != null ? profile : Default;
        }
    }
}
