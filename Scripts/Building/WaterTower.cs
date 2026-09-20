// Assets/Scripts/VoxelEngine/Building/WaterTower.cs
//
// THE WATER TOWER - platform-side water for steam locomotives.
//
// A grand riveted-steel tank on six braced legs, with a railed balcony, a ladder
// and a spout over the platform side. It fills itself from the water network it
// stands next to (any FluidNode whose network holds a WaterTank with water in it,
// the same source a sprinkler drinks from), or slowly from open water beside it -
// a tower by a pond seeps full the way real ones were pumped full. Locomotives
// berthed within reach of the spout take their water from here.
//
// Deliberately a simple float tank and not a FluidNode itself: a tower is storage
// the player can see a level on, not a pipe segment, and joining it to the fluid
// graph would make its level a network property instead of a tank's.
//
// 12.5.0-dev: the tank grows from 4 000 L to 12 000 L with fill rates to match,
// and the tower reports its supply state so the E-console can show it.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Fluids;

namespace VoxelEngine.Building
{
    [DisallowMultipleComponent]
    public class WaterTower : MonoBehaviour
    {
        [Tooltip("Litres the tank holds.")]
        public float capacity = 12000f;

        [Tooltip("Litres aboard right now.")]
        public float stored;

        [Tooltip("How fast the tower fills from a water network, litres per second.")]
        public float fillRate = 24f;

        [Tooltip("How fast the tower seeps full standing by open water, litres per second.")]
        public float seepRate = 6f;

        public float Fill01 => capacity > 0f ? Mathf.Clamp01(stored / capacity) : 0f;

        public bool IsFull => stored >= capacity - 0.01f;

        /// <summary>Where the last water came from. Runtime only: a tower that just
        /// loaded has taken nothing yet, so it honestly reports Isolated.</summary>
        public enum TowerSource { Isolated, Network, OpenWater }

        [System.NonSerialized] public TowerSource lastSource = TowerSource.Isolated;
        [System.NonSerialized] public float lastFillTime = -1f;

        /// <summary>True while water is actually arriving (a sip landed recently).</summary>
        public bool IsFilling => !IsFull && lastFillTime > 0f && Time.time - lastFillTime < 2f;

        /// <summary>One-line supply state for the E-console status pill.</summary>
        public string StatusText => IsFull ? "FULL"
            : IsFilling ? (lastSource == TowerSource.Network ? "FILLING FROM NETWORK" : "SEEPING FROM OPEN WATER")
            : "ISOLATED";

        private static readonly List<WaterTower> s_all = new();
        public static IReadOnlyList<WaterTower> All => s_all;

        private float _nextSip;

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>The tower whose standpipe a position stands within reach of.</summary>
        public static WaterTower Nearest(Vector3 pos, float radius)
        {
            WaterTower best = null;
            float bestSq = radius * radius;
            for (int i = 0; i < s_all.Count; i++)
            {
                var t = s_all[i];
                if (t == null) continue;
                float d = (t.transform.position - pos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = t; }
            }
            return best;
        }

        /// <summary>Water taken by a berthed locomotive. Returns what was actually
        /// given - an empty tower gives nothing and says so by the number.</summary>
        public float TakeSome(float litres)
        {
            float take = Mathf.Min(litres, stored);
            stored -= take;
            return take;
        }

        private void Update()
        {
            if (Time.time < _nextSip || stored >= capacity - 0.01f) return;
            float dt = Time.time - (_last < 0f ? Time.time : _last);
            _last = Time.time;
            _nextSip = Time.time + 0.5f;

            float want = fillRate * Mathf.Max(dt, 0.01f);

            // Drink from a water network within 3 m, exactly as a sprinkler does:
            // overlap sphere, first FluidNode, first WaterTank in its network.
            var hits = Physics.OverlapSphere(transform.position, 3f);
            foreach (var col in hits)
            {
                if (want <= 0f) break;
                if (col.gameObject == gameObject) continue;
                var node = col.GetComponent<FluidNode>();
                if (node == null || node.network == null) continue;
                foreach (var n in node.network.nodes)
                {
                    if (n is WaterTank t && t.water > 1f)
                    {
                        float take = Mathf.Min(want, t.water - 1f, capacity - stored);
                        t.TakeSome(take);
                        stored += take;
                        want -= take;
                        if (take > 0f) { lastSource = TowerSource.Network; lastFillTime = Time.time; }
                        if (want <= 0f || stored >= capacity - 0.01f) return;
                    }
                }
            }

            // No network: a tower by open water seeps full, slowly.
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world != null && stored < capacity - 0.01f)
            {
                var vp = world.WorldToVoxel(transform.position);
                for (int dx = -2; dx <= 2 && want > 0f; dx++)
                for (int dz = -2; dz <= 2 && want > 0f; dz++)
                {
                    var v = world.GetVoxelWorld(new Vector3Int(vp.x + dx, vp.y - 1, vp.z + dz));
                    if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel)
                    {
                        float take = Mathf.Min(seepRate * Mathf.Max(dt, 0.01f), want, capacity - stored);
                        stored += take;
                        if (take > 0f) { lastSource = TowerSource.OpenWater; lastFillTime = Time.time; }
                        return;
                    }
                }
            }
        }

        private float _last = -1f;
    }
}
