// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidNetwork.cs
//
// Registry/topology for grid liquids. There is ONE pipe system: the normal/static
// WaterPipe component can be placed onto a grid and then counts as that grid's
// liquid conduit. Liquid transfer is topology-based: a producer/consumer must
// touch a connected pipe run that reaches a compatible tank.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridLiquidNetwork : MonoBehaviour
    {
        private static GridLiquidNetwork _instance;
        public static GridLiquidNetwork Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("GridLiquidNetwork");
                    _instance = go.AddComponent<GridLiquidNetwork>();
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

        private readonly Dictionary<GridEntity, List<GridLiquidTank>> _tanks = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public void RegisterTank(GridEntity grid, GridLiquidTank tank)
        {
            if (grid == null || tank == null) return;
            if (!_tanks.TryGetValue(grid, out var list)) { list = new List<GridLiquidTank>(); _tanks[grid] = list; }
            if (!list.Contains(tank)) list.Add(tank);
        }

        public void UnregisterTank(GridEntity grid, GridLiquidTank tank)
        {
            if (grid != null && _tanks.TryGetValue(grid, out var list)) list.Remove(tank);
        }

        public bool HasPipes(GridEntity grid)
        {
            if (grid == null) return false;
            foreach (var block in grid.AllBlocks)
                if (IsLiquidPipe(block)) return true;
            return false;
        }

        public IReadOnlyList<GridLiquidTank> GetTanks(GridEntity grid)
            => _tanks.TryGetValue(grid, out var list) ? list : System.Array.Empty<GridLiquidTank>();

        public List<GridLiquidTank> GetTanks(GridEntity grid, LiquidType type)
        {
            var result = new List<GridLiquidTank>();
            foreach (var t in GetTanks(grid)) if (t != null && t.liquidType == type) result.Add(t);
            return result;
        }

        public float AvailableLiquidFor(GridBlock endpoint, LiquidType type)
        {
            float total = 0f;
            foreach (var tank in ConnectedTanks(endpoint, type, requireExistingType: true))
                total += Mathf.Max(0f, tank.stored);
            return total;
        }

        public float SpaceForLiquidFrom(GridBlock endpoint, LiquidType type)
        {
            float total = 0f;
            foreach (var tank in ConnectedTanks(endpoint, type, requireExistingType: false))
                total += Mathf.Max(0f, tank.capacity - tank.stored);
            return total;
        }

        public float DrawLiquidFor(GridBlock endpoint, LiquidType type, float litres)
        {
            if (litres <= 0f) return 0f;
            float drawn = 0f;
            foreach (var tank in ConnectedTanks(endpoint, type, requireExistingType: true))
            {
                if (drawn >= litres) break;
                drawn += tank.Remove(litres - drawn);
            }
            return drawn;
        }

        public float FillLiquidFrom(GridBlock endpoint, LiquidType type, float litres)
        {
            if (litres <= 0f) return 0f;
            float filled = 0f;
            foreach (var tank in ConnectedTanks(endpoint, type, requireExistingType: false))
            {
                if (filled >= litres) break;
                if (tank.liquidType != type && tank.stored > 0.001f) continue;
                if (tank.stored <= 0.001f) tank.liquidType = type;
                filled += tank.Add(litres - filled);
            }
            return filled;
        }

        private IEnumerable<GridLiquidTank> ConnectedTanks(GridBlock endpoint, LiquidType type, bool requireExistingType)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;

            var visitedPipes = new HashSet<GridBlock>();
            var yieldedTanks = new HashSet<GridLiquidTank>();
            var queue = new Queue<GridBlock>();

            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
            {
                if (!IsLiquidPipe(adjacent)
                    || VoxelEngine.Networks.WrenchBlacklist.IsBlocked(endpoint.gameObject, adjacent.gameObject)
                    || !visitedPipes.Add(adjacent)) continue;
                queue.Enqueue(adjacent);
            }

            while (queue.Count > 0)
            {
                var pipeBlock = queue.Dequeue();
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (IsLiquidPipe(adjacent))
                    {
                        if (!VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, adjacent.gameObject)
                            && visitedPipes.Add(adjacent)) queue.Enqueue(adjacent);
                        continue;
                    }

                    if (adjacent is GridLiquidTank tank && tank.Enabled && tank.mode != GridTankMode.Stockpile
                        && !VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject))
                    {
                        bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                        if (typeOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
            }
        }

        private static bool IsLiquidPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
        }
    }
}
