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
            CollectBridgedClassicTanks(endpoint, type, forDraw: true, s_bridgedTanks);
            foreach (var tank in s_bridgedTanks)
                total += Mathf.Max(0f, tank.StoredLitres);
            return total;
        }

        public float SpaceForLiquidFrom(GridBlock endpoint, LiquidType type)
        {
            float total = 0f;
            foreach (var tank in ConnectedTanks(endpoint, type, requireExistingType: false))
                total += Mathf.Max(0f, tank.capacity - tank.stored);
            CollectBridgedClassicTanks(endpoint, type, forDraw: false, s_bridgedTanks);
            foreach (var tank in s_bridgedTanks)
                total += Mathf.Max(0f, tank.capacityLitres - tank.StoredLitres);
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
            if (drawn < litres)
            {
                CollectBridgedClassicTanks(endpoint, type, forDraw: true, s_bridgedTanks);
                foreach (var tank in s_bridgedTanks)
                {
                    if (drawn >= litres) break;
                    drawn += tank.TakeSome(type, litres - drawn);
                }
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
            if (filled < litres)
            {
                CollectBridgedClassicTanks(endpoint, type, forDraw: false, s_bridgedTanks);
                foreach (var tank in s_bridgedTanks)
                {
                    if (filled >= litres) break;
                    filled += tank.AddSome(type, litres - filled);
                }
            }
            return filled;
        }

        // ════════════════════════════════════════════════════════════════
        //  CLASSIC FLUID NETWORK BRIDGE
        //  The ship-side grid system and the classic ground-side FluidNetwork
        //  are ONE physical pipe world to the player: a classic WaterPipe run
        //  that touches any liquid port (or the body) of an endpoint — engine,
        //  pump, tank, whatever — bridges both systems. This is what finally
        //  makes "liquid pipes to the liquid tank" work no matter WHICH tank
        //  variant the pipe run terminates at.
        // ════════════════════════════════════════════════════════════════
        private const int ClassicWalkCap = 512;
        private static readonly List<VoxelEngine.Fluids.WaterTank> s_bridgedTanks = new(8);
        private static readonly List<Vector3> s_classicSeeds = new(8);
        private static readonly HashSet<VoxelEngine.Fluids.WaterPipe> s_classicVisited = new();
        private static readonly Queue<VoxelEngine.Fluids.WaterPipe> s_classicQueue = new();
        private static readonly Collider[] s_classicProbe = new Collider[16];

        /// <summary>Collect classic <see cref="VoxelEngine.Fluids.WaterTank"/>s reachable
        /// from the endpoint through the classic fluid network. Seed points are the
        /// endpoint's named liquid ports (generous range) plus its body centre.</summary>
        private static void CollectBridgedClassicTanks(GridBlock endpoint, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            outTanks.Clear();
            if (endpoint == null) return;
            float cs = endpoint.Grid != null ? endpoint.Grid.gridSize.CellSize() : 2.5f;

            // Gather probe centres: up to 4 liquid ports + the body centre.
            s_classicSeeds.Clear();
            foreach (Transform child in endpoint.transform.GetComponentsInChildren<Transform>(true))
            {
                if (s_classicSeeds.Count >= 4) break;
                if (child == null || child == endpoint.transform) continue;
                bool liquidPort = false;
                for (int i = 0; i < VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes.Length; i++)
                {
                    if (child.name.StartsWith(VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes[i], System.StringComparison.Ordinal)) { liquidPort = true; break; }
                }
                if (liquidPort) s_classicSeeds.Add(child.position);
            }
            s_classicSeeds.Add(endpoint.transform.position);

            // Seed classic pipes around every probe centre.
            s_classicVisited.Clear();
            s_classicQueue.Clear();
            for (int c = 0; c < s_classicSeeds.Count; c++)
            {
                float radius = c < s_classicSeeds.Count - 1 ? cs * 1.6f : endpoint.EffectiveCellSize * 1.35f;
                int hitCount = Physics.OverlapSphereNonAlloc(s_classicSeeds[c], radius, s_classicProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_classicProbe[i];
                    if (col == null) continue;
                    var pipe = col.GetComponentInParent<VoxelEngine.Fluids.WaterPipe>();
                    if (pipe != null && s_classicVisited.Add(pipe)) s_classicQueue.Enqueue(pipe);
                }
            }

            // Walk the classic neighbour graph, collecting compatible tanks.
            int walked = 0;
            while (s_classicQueue.Count > 0 && walked++ < ClassicWalkCap)
            {
                var pipe = s_classicQueue.Dequeue();
                // Five-cell cardinal corridor: a classic tank parked up to five
                // lattice cells straight off ANY pipe on the run also counts as
                // connected — no pipe needs to physically hump the tank shell.
                ProbeClassicTankCorridor(pipe, type, forDraw, outTanks);
                var neighbours = pipe.neighbours;
                if (neighbours == null) continue;
                foreach (var node in neighbours)
                {
                    if (node is VoxelEngine.Fluids.WaterPipe nextPipe)
                    {
                        if (s_classicVisited.Add(nextPipe)) s_classicQueue.Enqueue(nextPipe);
                    }
                    else if (node is VoxelEngine.Fluids.WaterTank tank)
                    {
                        TryAddBridgedTank(tank, type, forDraw, outTanks);
                    }
                }
            }
        }

        private static readonly Collider[] s_classicRowProbe = new Collider[16];

        private static void TryAddBridgedTank(VoxelEngine.Fluids.WaterTank tank, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            if (tank == null || outTanks.Contains(tank)) return;
            bool usable = forDraw
                ? tank.liquidType == type && tank.StoredLitres > 0.001f
                : tank.IsEmpty || tank.liquidType == type;
            if (usable) outTanks.Add(tank);
        }

        /// <summary>Collect classic water tanks up to five lattice cells away from
        /// <paramref name="pipe"/> in a straight cardinal row (valid direction only —
        /// never diagonal). Grid-mounted pipes probe in their grid's frame.</summary>
        private static void ProbeClassicTankCorridor(VoxelEngine.Fluids.WaterPipe pipe, LiquidType type,
            bool forDraw, List<VoxelEngine.Fluids.WaterTank> outTanks)
        {
            if (pipe == null) return;
            var block = pipe.GetComponentInParent<GridBlock>();
            // Five LATTICE cells — mounted pipes probe on the host grid's cell size.
            float step = block != null && block.Grid != null
                ? block.Grid.gridSize.CellSize()
                : VoxelEngine.Networks.PipeAdjacency.DefaultGridSize;
            Transform frame = block != null && block.Grid != null ? block.Grid.transform : null;
            VoxelEngine.Networks.PipeAdjacency.ProbeCardinal(pipe.transform.position, frame, step, 5,
                s_classicRowProbe, col =>
                {
                    var tank = col.GetComponent<VoxelEngine.Fluids.WaterTank>();
                    if (tank == null) tank = col.GetComponentInParent<VoxelEngine.Fluids.WaterTank>();
                    if (tank != null) TryAddBridgedTank(tank, type, forDraw, outTanks);
                    return false; // corridor sweeps fully — collect every tank it passes
                });
        }

        private IEnumerable<GridLiquidTank> ConnectedTanks(GridBlock endpoint, LiquidType type, bool requireExistingType)
        {
            var grid = endpoint != null ? endpoint.Grid : null;
            if (grid == null) yield break;

            float cs = grid.gridSize.CellSize();
            var visitedPipes = new HashSet<GridBlock>();
            var yieldedTanks = new HashSet<GridLiquidTank>();
            var queue = new Queue<GridBlock>();

            void SeedPipe(GridBlock pipe)
            {
                if (pipe == null || VoxelEngine.Networks.WrenchBlacklist.IsBlocked(endpoint.gameObject, pipe.gameObject)
                    || !visitedPipes.Add(pipe)) return;
                queue.Enqueue(pipe);
            }

            // Classic face-touch adjacency …
            foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, endpoint))
                if (IsLiquidPipe(adjacent)) SeedPipe(adjacent);
            // … PLUS world-space proximity, which is what big machine models actually
            // need: a pipe snapped to Port_FuelInput / Port_CoolantInput can sit one or
            // two lattice cells away from the machine's origin cell and must still feed it.
            foreach (var pipe in grid.AllBlocks)
            {
                if (pipe == null || !IsLiquidPipe(pipe)) continue;
                if (BlocksAreLiquidLinked(endpoint, pipe, cs)) SeedPipe(pipe);
            }

            while (queue.Count > 0)
            {
                var pipeBlock = queue.Dequeue();

                // Pipe → pipe growth (world-touch adjacency + generous proximity).
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (IsLiquidPipe(adjacent)
                        && !VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, adjacent.gameObject)
                        && visitedPipes.Add(adjacent)) queue.Enqueue(adjacent);
                }
                foreach (var pipe in ProximityBlocks(grid, pipeBlock, cs, liquidOnly: true))
                {
                    if (!VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, pipe.gameObject)
                        && visitedPipes.Add(pipe)) queue.Enqueue(pipe);
                }

                // Pipe → tanks. Face adjacency first, then proximity (a tank touching a
                // machine's overhang model, or a port-side pipe, must also register).
                foreach (var adjacent in UnifiedGridTopology.AdjacentBlocks(grid, pipeBlock))
                {
                    if (adjacent is GridLiquidTank tank && tank.Enabled && tank.mode != GridTankMode.Stockpile
                        && !VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject))
                    {
                        bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                        if (typeOk && yieldedTanks.Add(tank)) yield return tank;
                    }
                }
                foreach (var maybeTank in ProximityBlocks(grid, pipeBlock, cs, liquidOnly: false))
                {
                    if (maybeTank is not GridLiquidTank tank || !tank.Enabled || tank.mode == GridTankMode.Stockpile
                        || VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, tank.gameObject)) continue;
                    bool typeOk = tank.liquidType == type || (!requireExistingType && tank.stored <= 0.001f);
                    if (typeOk && yieldedTanks.Add(tank)) yield return tank;
                }
            }
        }

        // Scratch buffers shared by the proximity helpers.
        private static readonly Collider[] s_liquidProbe = new Collider[12];
        private static readonly List<GridBlock> s_proximityResult = new(12);

        /// <summary>World-space adjacency for liquid links: A is a machine/tank endpoint,
        /// B is a pipe. Counts as linked when the pipe's centre is within reach of one of
        /// the endpoint's named liquid ports, OR close enough to the endpoint's body to
        /// touch its (possibly overhanging) visual model.</summary>
        public static bool BlocksAreLiquidLinked(GridBlock endpoint, GridBlock pipe, float cs)
        {
            if (endpoint == null || pipe == null) return false;
            float portRange = cs * 1.5f;
            float portRange2 = portRange * portRange;
            foreach (Transform child in endpoint.transform.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == endpoint.transform) continue;
                bool liquidPort = false;
                for (int i = 0; i < VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes.Length; i++)
                {
                    if (child.name.StartsWith(VoxelEngine.Maritime.MaritimePorts.LiquidPrefixes[i], System.StringComparison.Ordinal)) { liquidPort = true; break; }
                }
                if (!liquidPort) continue;
                if ((child.position - pipe.transform.position).sqrMagnitude <= portRange2) return true;
            }
            float bodyRange = endpoint.EffectiveCellSize * 1.35f;
            return (endpoint.transform.position - pipe.transform.position).sqrMagnitude <= bodyRange * bodyRange;
        }

        /// <summary>All grid blocks within a generous touch radius of <paramref name="origin"/>,
        /// used as the proximity pass that machine-model overhang makes necessary.</summary>
        private static IEnumerable<GridBlock> ProximityBlocks(GridEntity grid, GridBlock origin, float cs, bool liquidOnly)
        {
            s_proximityResult.Clear();
            if (grid == null || origin == null) yield break;
            float radius = Mathf.Max(origin.EffectiveCellSize, GridSize.Small.CellSize()) * 1.35f;
            int hitCount = Physics.OverlapSphereNonAlloc(origin.transform.position, radius, s_liquidProbe, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hitCount; i++)
            {
                var col = s_liquidProbe[i];
                if (col == null) continue;
                var block = col.GetComponentInParent<GridBlock>();
                if (block == null || block == origin || block.Grid != grid) continue;
                if (liquidOnly && !IsLiquidPipe(block)) continue;
                if (s_proximityResult.Contains(block)) continue;
                s_proximityResult.Add(block);
            }
            for (int i = 0; i < s_proximityResult.Count; i++)
                yield return s_proximityResult[i];
        }

        private static bool IsLiquidPipe(GridBlock block)
        {
            return block != null && block.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>(true) != null;
        }
    }
}
