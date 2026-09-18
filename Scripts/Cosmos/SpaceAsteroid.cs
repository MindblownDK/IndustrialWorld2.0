// Assets/Scripts/VoxelEngine/Cosmos/SpaceAsteroid.cs
//
// A procedural, minable asteroid drifting in deep space.
//
// 11.27.0: this is now a real VOXEL BODY, not a destructible prop. It used to be a
// single icosphere with health - you hit it, it popped, it gave you ore. That meant you
// could never tunnel into a rock, never see an ore seam, and never leave one half-mined.
//
// The rock is now a small dense voxel volume (see AsteroidVoxelBody) of stone shot
// through with ore veins. Mining carves material out of it a scoop at a time, the mesh
// and collider rebuild from what is left, and the rock disappears only when genuinely
// hollowed out. Health is gone: a rock is not killed, it is consumed.
//
// Asteroids are deterministic per spawn seed (same position + seed → same rock), so
// revisiting a region of space feels consistent. They are static in the cosmic frame;
// SpaceOrigin rebases them with the rest of the world.
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    [RequireComponent(typeof(AsteroidVoxelBody))]
    public class SpaceAsteroid : MonoBehaviour
    {
        [Header("Asteroid")]
        [Tooltip("Nominal radius in metres.")]
        public float sizeMetres = 12f;

        [Tooltip("Primary ore material threaded through the rock as veins.")]
        public MaterialId oreMaterial = MaterialId.Stone;

        [Tooltip("Visual tumble speed (deg/s).")]
        public float tumbleSpeed = 4f;

        private Vector3 _tumbleAxis;
        private Vector3 _driftVelocity;

        public static SpaceAsteroid Spawn(Vector3 position, float radius, MaterialId material, int seed)
            => Spawn(position, radius, material, seed, Vector3.zero);

        /// <summary>
        /// Builds a voxel rock. No drop list is passed any more: what the rock yields is
        /// whatever material the player actually digs out of it, resolved per voxel through
        /// the MaterialRegistry. A precomputed drop table would let the visible ore veins
        /// and the granted items disagree.
        /// </summary>
        public static SpaceAsteroid Spawn(Vector3 position, float radius, MaterialId material,
            int seed, Vector3 driftVelocity)
        {
            var go = new GameObject("SpaceAsteroid_" + material);
            go.transform.position = position;

            var asteroid = go.AddComponent<SpaceAsteroid>();
            asteroid.sizeMetres = radius;
            asteroid.oreMaterial = material;
            asteroid._driftVelocity = driftVelocity;
            asteroid._tumbleAxis = new Vector3(
                Mathf.Sin(seed * 12.9898f), Mathf.Cos(seed * 78.233f), Mathf.Sin(seed * 37.719f)).normalized;

            // The voxel body owns the geometry, the collider and the material data.
            var voxels = go.GetComponent<AsteroidVoxelBody>();
            voxels.Generate(radius, material, seed);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = radius * radius * radius * 0.25f;
            rb.useGravity = false;
            rb.isKinematic = true;          // rocks drift with the frame; nothing pushes them
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            return asteroid;
        }

        /// <summary>The voxel volume this rock is made of.</summary>
        public AsteroidVoxelBody Voxels => _voxels != null
            ? _voxels
            : _voxels = GetComponent<AsteroidVoxelBody>();
        private AsteroidVoxelBody _voxels;

        private void Update()
        {
            float dt = Time.deltaTime;
            if (tumbleSpeed > 0.01f)
                transform.Rotate(_tumbleAxis, tumbleSpeed * dt, Space.World);
            // Gentle through-field drift (9.15.0): the belt is alive, not parked.
            if (_driftVelocity.sqrMagnitude > 0.0001f)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic) rb.MovePosition(rb.position + _driftVelocity * dt);
                else transform.position += _driftVelocity * dt;
            }
        }

    }
}
