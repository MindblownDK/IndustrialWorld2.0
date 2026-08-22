// Assets/Scripts/VoxelEngine/Fire/FireManager.cs
//
// 9.16.0 fire system (Liquids Overhaul, Part 2) — the fire simulation core.
//
//   • A sparse per-cell fire map (cell -> heat + burn accumulator), stepped at 10 Hz
//     with a per-tick budget — fires burn anywhere on the active planet, not just
//     near the player, but each tick only pays for a bounded amount of work.
//   • Burning CONSUMES the liquid: every tick drains fuel levels from the burning
//     cell through the fluid sim, so a fire literally eats the pool it sits on.
//   • Fire SPREADS to adjacent flammable cells while the flame is hot — liquid fuel
//     races across a lake, heavy fuel oil crawls. Flame below the surface is
//     impossible: only cells holding liquid can burn, and the fluid sim refills
//     emptied cells from their neighbours (which then catch, ring by ring).
//   • Water and coolant EXTINGUISH: pour either into a burning cell and the flame
//     dies the instant the cell's liquid stops being flammable.
//   • Players standing in the flames take armour-mitigated burn damage
//     (PlayerStats.ApplyBurn — the same burn the Ifrit's fireballs inflict).
//
// Ignition sources: the Igniter tool (RMB on a flammable pool), Ifrit fireballs and
// fire walls (splash ignition). Fires are transient and runtime-only — nothing new
// is saved, and a save loaded mid-fire simply has no fire.
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Player;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Fire
{
    public class FireManager : MonoBehaviour
    {
        public static FireManager Instance { get; private set; }

        [Tooltip("Fire ticks per second (fuel burn, spread, player burn).")]
        public float tickRate = 10f;
        [Tooltip("Burning cells processed per tick — the rest of a big fire queues across ticks.")]
        public int maxCellsPerTick = 256;
        [Tooltip("Hard cap on simultaneously burning cells (spread guard).")]
        public int maxBurningCells = 2048;
        [Tooltip("Burn damage per second while standing in the flames (armour-mitigated).")]
        public float playerBurnDps = 12f;
        [Tooltip("Horizontal distance (m) at which a flame sets the player alight.")]
        public float playerBurnRadius = 1.15f;
        [Tooltip("Heat lost per tick after the bright ignition flash (embers hold at 40).")]
        public byte heatDecayPerTick = 2;
        [Tooltip("A flame only ignites neighbours while its heat is above this.")]
        public byte spreadHeatThreshold = 150;

        private struct FireCell
        {
            public byte  heat;      // 1..255 — ignition flash fades toward embers
            public float burnAccum; // fractional fuel levels owed (burn rate integrator)
        }

        private readonly Dictionary<Vector3Int, FireCell> _burning = new Dictionary<Vector3Int, FireCell>();
        private readonly List<Vector3Int> _tickCells = new List<Vector3Int>(512);
        private float _timer;
        private IVoxelWorld _lastWorld;

        private static readonly Vector3Int[] SpreadDirs =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.forward, Vector3Int.back, Vector3Int.down,
        };

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (GetComponent<FireRenderer>() == null) gameObject.AddComponent<FireRenderer>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("FireManager");
            Instance = go.AddComponent<FireManager>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.1f, tickRate);
            if (_timer < interval) return;
            _timer -= interval;

            var world = ActiveWorld.Current;
            if (world == null) return;
            if (world != _lastWorld) { _burning.Clear(); _lastWorld = world; }

            StepTick(world);
        }

        private void StepTick(IVoxelWorld world)
        {
            FluidManager.EnsureInstance();
            var fm = FluidManager.Instance;
            if (fm == null) return;

            float perTick = 1f / Mathf.Max(0.1f, tickRate);

            _tickCells.Clear();
            foreach (var kv in _burning) _tickCells.Add(kv.Key);
            int budget = Mathf.Min(_tickCells.Count, maxCellsPerTick);

            for (int i = 0; i < budget; i++)
            {
                Vector3Int v = _tickCells[i];
                FireCell cell = _burning[v];

                LiquidType liquid = fm.GetLiquidType(v);
                if (!FireFuel.IsFlammable(liquid)) { _burning.Remove(v); continue; } // flooded — quenched
                byte level = fm.GetLiquidLevel(v, liquid);
                if (level <= 0) { _burning.Remove(v); continue; }                     // burned out

                // 1) Consume fuel — integrate the per-liquid burn rate so every
                //    liquid really burns at its own speed.
                cell.burnAccum += FireFuel.BurnLevelsPerSecond(liquid) * perTick;
                if (cell.burnAccum >= 1f)
                {
                    byte drain = (byte)Mathf.Min(255, (int)cell.burnAccum);
                    cell.burnAccum -= drain;
                    fm.DrainLiquid(v, liquid, drain);
                }

                // 2) Heat fades from the ignition flash toward steady embers.
                if (cell.heat > 40)
                    cell.heat = (byte)Mathf.Max(40, cell.heat - heatDecayPerTick);

                // 3) Spread — only a lively flame lights its neighbours.
                if (cell.heat > spreadHeatThreshold)
                {
                    float chance = FireFuel.SpreadChancePerTick(liquid) * (cell.heat / 255f);
                    for (int d = 0; d < SpreadDirs.Length; d++)
                    {
                        Vector3Int n = v + SpreadDirs[d];
                        if (_burning.ContainsKey(n)) continue;
                        if (_burning.Count >= maxBurningCells) break;
                        if (UnityEngine.Random.value >= chance) continue;
                        LiquidType nl = fm.GetLiquidType(n);
                        if (!FireFuel.IsFlammable(nl)) continue;
                        if (fm.GetLiquidLevel(n, nl) <= 0) continue;
                        _burning[n] = new FireCell { heat = 255, burnAccum = 0f };
                    }
                }

                _burning[v] = cell;
            }

            // 4) Player contact burn — standing in the flames (or right above them)
            //    keeps the burn alive; armour mitigates exactly like the Ifrit's fire.
            var ps = PlayerStats.Instance;
            if (ps != null && _burning.Count > 0)
            {
                Vector3 playerPos = ps.transform.position;
                Vector3 up = GravityProvider.GetUp(playerPos);
                bool contact = false;
                foreach (var kv in _burning)
                {
                    Vector3 cellCenter = new Vector3(kv.Key.x + 0.5f, kv.Key.y + 0.5f, kv.Key.z + 0.5f);
                    Vector3 delta = playerPos - cellCenter;
                    float vert = Vector3.Dot(delta, up);
                    Vector3 horiz = delta - up * vert;
                    if (horiz.sqrMagnitude <= playerBurnRadius * playerBurnRadius && vert >= -0.25f && vert <= 2.1f)
                    { contact = true; break; }
                }
                if (contact) ps.ApplyBurn(playerBurnDps, 0.6f);
            }
        }

        /// <summary>Ignite a flammable liquid cell (static — ensures the manager exists).</summary>
        public static bool TryIgniteAt(Vector3Int v)
        {
            EnsureInstance();
            return Instance != null && Instance.TryIgnite(v);
        }

        /// <summary>Ignite one cell. Only flammable liquid burns; already-burning cells
        /// and the global cap are refused.</summary>
        public bool TryIgnite(Vector3Int v)
        {
            FluidManager.EnsureInstance();
            var fm = FluidManager.Instance;
            if (fm == null) return false;
            var liquid = fm.GetLiquidType(v);
            if (!FireFuel.IsFlammable(liquid)) return false;
            if (fm.GetLiquidLevel(v, liquid) <= 0) return false;
            if (_burning.ContainsKey(v)) return false;
            if (_burning.Count >= maxBurningCells) return false;
            _burning[v] = new FireCell { heat = 255, burnAccum = 0f };
            return true;
        }

        public bool IsBurning(Vector3Int v) => _burning.ContainsKey(v);
        public int BurningCellCount => _burning.Count;
        public byte HeatAt(Vector3Int v) => _burning.TryGetValue(v, out FireCell c) ? c.heat : (byte)0;

        /// <summary>Copies the currently burning cells into <paramref name="dest"/> (renderer use).</summary>
        public void CopyBurningCells(List<Vector3Int> dest)
        {
            dest.Clear();
            dest.AddRange(_burning.Keys);
        }
    }
}
