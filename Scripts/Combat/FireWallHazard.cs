// Assets/Scripts/VoxelEngine/Combat/FireWallHazard.cs
//
// A lingering wall/patch of fire raised by the Ifrit. A flat glowing disc laid on the
// surface (oriented to radial up, so it hugs curved planets) that burns the player for
// as long as they stand inside it, then dissipates. Purely a hazard — no collider.

using UnityEngine;
using VoxelEngine.Player;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Combat
{
    public class FireWallHazard : MonoBehaviour
    {
        public float duration     = 5f;
        public float burnDps      = 6f;
        public float radius       = 1.9f;
        public float tickInterval = 0.4f;

        private float _life, _tickTimer;

        public static FireWallHazard Spawn(Vector3 pos, Vector3 up, Material mat, float dur, float dps, float radius)
        {
            var go = new GameObject("FireWall");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up); // lay flat on the surface

            var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disk.transform.SetParent(go.transform, false);
            disk.transform.localScale = new Vector3(radius, 0.12f, radius);
            var col = disk.GetComponent<Collider>(); if (col != null) Destroy(col);
            var ren = disk.GetComponent<Renderer>(); if (mat != null) ren.sharedMaterial = mat;

            var hz = go.AddComponent<FireWallHazard>();
            hz.duration = dur; hz.burnDps = dps; hz.radius = radius;

            // 9.16.0 fire system — a fire wall raised over flammable liquid ignites it,
            // so an Ifrit ambush on an industrial world can torch whole fuel lakes.
            var aw = VoxelEngine.Core.ActiveWorld.Current;
            if (aw != null) VoxelEngine.Fire.FireManager.TryIgniteAt(aw.WorldToVoxel(pos));
            return hz;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _life += dt;
            if (_life >= duration) { Destroy(gameObject); return; }

            _tickTimer += dt;
            if (_tickTimer >= tickInterval)
            {
                _tickTimer = 0f;
                var ps = PlayerStats.Instance;
                if (ps == null) return;
                Vector3 up = GravityProvider.GetUp(transform.position);
                Vector3 toPlayer = Vector3.ProjectOnPlane(ps.transform.position - transform.position, up);
                if (toPlayer.magnitude <= radius)
                    ps.ApplyBurn(burnDps, tickInterval + 0.5f);   // keep the burn alive while inside
            }
        }
    }
}
