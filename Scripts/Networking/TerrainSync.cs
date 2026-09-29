// Assets/Scripts/VoxelEngine/Networking/TerrainSync.cs
//
// 14.7.0-dev - Multiplayer milestone 4, phase 1: live terrain replication.
//
// The terrain twin of BuildingSync: gameplay announces voxel operations at
// their authority points, NetworkBootstrap carries them, and this class
// applies them on the other machines. No Fish-Net types here.
//
// The core trick: VoxelEditor's brush is fully determined by its integer
// voxel-space center plus (radius, strength, subtract, fill) - the world
// position only ever picks the center voxel. Integer voxel coordinates are
// immune to floating-origin re-anchoring, and the brush derives every delta
// from the current voxel state, so same-seed worlds that apply the same op
// stream converge exactly. Ops identify their planet by body name; an op for
// a planet you are not on is skipped (that terrain is not loaded here).
//
// Explosions replicate their cosmetic half (fireball, shake) plus the crater
// carve. Damage is deliberately NOT re-applied remotely - tiered pieces
// already converge through building damage sync, and re-running damage would
// double it.
//
// Phase 1 scope: live ops only, loaded chunks only. Not yet synced: edits
// made while the other machine was offline (terrain snapshot / chunk deltas
// are phase 2), fluid sim state, grid-ship voxels.

using UnityEngine;
using VoxelEngine.Core;

namespace VoxelEngine.Networking
{
    public static class TerrainSync
    {
        /// <summary>Raised while a remote op is being applied locally, so the
        /// edit hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        // ─────────────── local action -> network ───────────────

        /// <summary>Called by VoxelEditor after a brush op changed voxels.</summary>
        public static void AnnounceBrush(IVoxelWorld world, Vector3Int center, float radius,
            float strength, bool subtract, byte fillMaterial)
        {
            if (!ShouldAnnounce()) return;
            NetworkBootstrap.Instance.SendTerrainBrush(BodyNameOf(world), center,
                radius, strength, subtract, fillMaterial);
        }

        /// <summary>Called by Explosion.Detonate. Carries the scene position for
        /// the FX and the crater in voxel space for the terrain.</summary>
        public static void AnnounceExplosion(Vector3 position, float radius,
            Vector3Int craterCenter, int craterRadius)
        {
            if (!ShouldAnnounce()) return;
            NetworkBootstrap.Instance.SendExplosion(BodyNameOf(ActiveWorld.Current),
                position, radius, craterCenter, craterRadius);
        }

        // ─────────────── network -> local world ───────────────

        public static void ApplyBrush(string body, Vector3Int center, float radius,
            float strength, bool subtract, byte fillMaterial)
        {
            var world = ActiveWorld.Current;
            if (world == null || BodyNameOf(world) != body) return;   // other planet
            IsApplyingRemote = true;
            try
            {
                VoxelEngine.Modification.VoxelEditor.ApplyReplicated(world,
                    world.MaterialRegistry, center, radius, strength, subtract,
                    (VoxelEngine.Materials.MaterialId)fillMaterial);
            }
            finally { IsApplyingRemote = false; }
        }

        public static void ApplyExplosion(string body, Vector3 position, float radius,
            Vector3Int craterCenter, int craterRadius)
        {
            var world = ActiveWorld.Current;
            if (BodyNameOf(world) != body) return;   // other planet: not visible here

            // Cosmetics: the same fireball, light and distance-based shake the
            // origin saw. Damage is deliberately NOT re-applied - pieces already
            // converge through building damage sync.
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(position);
            float scale = Mathf.Clamp(radius / 5f, 0.6f, 10f);
            VoxelEngine.Combat.ExplosionFX.Spawn(position, up, scale, null);
            var ps = VoxelEngine.Player.PlayerStats.Instance;
            if (ps != null)
            {
                float dist = Vector3.Distance(position, ps.transform.position);
                float shake = Mathf.Clamp01(1f - dist / Mathf.Max(1f, radius * 3f)) * 0.85f;
                VoxelEngine.Player.CameraFeedback.AddShake(shake);
            }

            if (world == null || craterRadius <= 0) return;
            IsApplyingRemote = true;
            try { VoxelEngine.Combat.Explosion.CarveCrater(world, craterCenter, craterRadius); }
            finally { IsApplyingRemote = false; }
        }

        // ─────────────── helpers ───────────────

        private static bool ShouldAnnounce()
            => !IsApplyingRemote
               && NetworkSession.Mode != SessionMode.Offline
               && NetworkBootstrap.Instance != null
               && !NetworkBootstrap.Instance.WorldMismatch;   // wrong terrain: stay silent

        /// <summary>Planet identity for an op ("" when there is no world - e.g. a
        /// blast in deep space, which still shows its fireball everywhere).</summary>
        private static string BodyNameOf(IVoxelWorld world)
        {
            var sphere = world as VoxelEngine.Cosmos.SphereWorld;
            return sphere != null && sphere.body != null && sphere.body.settings != null
                ? sphere.body.settings.bodyName : "";
        }
    }
}
