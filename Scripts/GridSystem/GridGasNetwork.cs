// Assets/Scripts/VoxelEngine/GridSystem/GridGasNetwork.cs
//
// Per-grid gas distribution. There is ONE pipe system: the normal/static GasPipe
// component can be placed onto a grid and then counts as that grid's gas conduit.
// Gas transfer is topology-based: a producer/consumer must touch a connected gas
// pipe run that reaches a compatible tank.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridGasNetwork : MonoBehaviour
    {
        private static GridGasNetwork _instance;
        public static GridGasNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridGasNetwork");
                    _instance = go.AddComponent<GridGasNetwork>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private static readonly Vector3Int[] Neighbours =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public bool HasPipes(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var kv in grid.Blocks)
                if (IsGasPipe(kv.Value)) return true;
            return false;
        }

        public float AvailableGas(GridEntity grid, Gas.GasType type, bool includeStockpile = false)
        {
            if (grid == null || type == Gas.GasType.None) return 0f;
            float total = 0f;
            foreach (var kv in grid.Blocks)
            {
                if (kv.Value is GridGasTank tank && tank.Enabled && tank.gasType == type)
                {
                    if (!includeStockpile && tank.mode == GridTankMode.Stockpile) continue;
                    total += Mathf.Max(0f, tank.stored);
                }
            }
            return total;
        }

        public float AvailableGasFor(GridBlock consumer, Gas.GasType type, bool includeStockpile = false)
        {
            float total = 0f;
            foreach (var tank in ConnectedTanks(consumer, type, forOutput: true, includeStockpile))
                total += Mathf.Max(0f, tank.stored);
            return total;
        }

        public float DrawGasFor(GridBlock consumer, Gas.GasType type, float litres, bool includeStockpile = false)
        {
            if (consumer == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float drawn = 0f;
            foreach (var tank in ConnectedTanks(consumer, type, forOutput: true, includeStockpile))
            {
                if (drawn >= litres) break;
                drawn += tank.Draw(litres - drawn, ignoreStockpile: includeStockpile);
            }
            return drawn;
        }

        public float FillGasFrom(GridBlock producer, Gas.GasType type, float litres)
        {
            if (producer == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float filled = 0f;
            foreach (var tank in ConnectedTanks(producer, type, forOutput: false, includeStockpile: true))
            {
                if (filled >= litres) break;
                filled += tank.Add(type, litres - filled);
            }
            return filled;
        }

        // Legacy broad helpers retained for old callers, but new grid machines should use
        // DrawGasFor / FillGasFrom so transfer follows connected pipe topology.
        public float DrawGas(GridEntity grid, Gas.GasType type, float litres, bool includeStockpile = false)
        {
            if (grid == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float drawn = 0f;
            foreach (var kv in grid.Blocks)
            {
                if (drawn >= litres) break;
                if (!(kv.Value is GridGasTank tank) || !tank.Enabled || tank.gasType != type) continue;
                if (!includeStockpile && tank.mode == GridTankMode.Stockpile) continue;
                drawn += tank.Draw(litres - drawn, ignoreStockpile: includeStockpile);
            }
            return drawn;
        }

        public float FillGas(GridEntity grid, Gas.GasType type, float litres)
        {
            if (grid == null || type == Gas.GasType.None || litres <= 0f) return 0f;
            float filled = 0f;
            foreach (var kv in grid.Blocks)
            {
                if (filled >= litres) break;
                if (kv.Value is GridGasTank tank && tank.Enabled)
                    filled += tank.Add(type, litres - filled);
            }
            return filled;
        }

        private IEnumerable<GridGasTank> ConnectedTanks(GridBlock endpoint, Gas.GasType type, bool forOutput, bool includeStockpile)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null || type == Gas.GasType.None) yield break;

            var visitedPipes = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();

            foreach (var dir in Neighbours)
            {
                var p = endpoint.GridPos + dir;
                if (IsGasPipe(grid.GetBlock(p)) && visitedPipes.Add(p)) queue.Enqueue(p);
            }

            while (queue.Count > 0)
            {
                var pipePos = queue.Dequeue();

                foreach (var dir in Neighbours)
                {
                    var pos = pipePos + dir;
                    var block = grid.GetBlock(pos);
                    if (IsGasPipe(block))
                    {
                        if (visitedPipes.Add(pos)) queue.Enqueue(pos);
                        continue;
                    }

                    if (block is GridGasTank tank && tank.Enabled)
                    {
                        bool typeOk = tank.gasType == type || (!forOutput && tank.stored <= 0.001f);
                        bool stockpileOk = includeStockpile || tank.mode != GridTankMode.Stockpile;
                        if (typeOk && stockpileOk) yield return tank;
                    }
                }
            }
        }

        private static bool IsGasPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Gas.GasPipe>(true) != null;
        }
    }
}
