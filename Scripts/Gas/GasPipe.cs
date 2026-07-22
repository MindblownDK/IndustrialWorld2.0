// Assets/Scripts/VoxelEngine/Gas/GasPipe.cs
//
// Universal gas transport pipe. Carries steam, hydrogen, oxygen between
// machines. Auto-connects to neighbours within connectRadius.
//
// VISUAL: hands its live neighbour list to a PipeVisualBuilder so the pipe
// renders the same chunky core+arms style used by Power / Data cables.
// Glass variant exposes an inner medium-tinted core that previews the gas
// flowing through the network.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Networks;

namespace VoxelEngine.Gas
{
    [RequireComponent(typeof(PlacedBlock))]
    public class GasPipe : MonoBehaviour
    {
        [Tooltip("Max pressure this pipe can handle (arbitrary units).")]
        public float maxPressure = 100f;
        public float connectRadius = 3.0f;

        [Header("Visual")]
        [Tooltip("Render as a translucent glass pipe with the carried gas visible inside.")]
        public bool isGlass = false;

        [System.NonSerialized] public List<GasPipe> neighbours = new();

        // ── Visual integration ─────────────────────────────────
        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPosBuf = new(6);
        private static readonly Collider[] s_armProbe = new Collider[12];

        private void Awake()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            // Sync the glass flag onto the visual builder so prefab authors only have
            // to flip a single bool on the GasPipe component.
            _visuals.isGlass = isGlass;
            // Gas pipes are SLIM polished brass — distinct silhouette from the
            // fatter copper water pipes so the player can tell them apart at a glance.
            _visuals.style = VoxelEngine.Networks.PipeStyle.Brass;
        }

        private void OnEnable()
        {
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;
            GasNetwork.EnsureInstance();
            GasNetwork.Instance?.Register(this);
        }
        private void OnDisable() => GasNetwork.Instance?.Unregister(this);

        /// <summary>
        /// Supplier called by <see cref="PipeVisualBuilder"/> every rebuild interval.
        /// Returns live world positions of every connected neighbour pipe.
        /// </summary>
        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            foreach (var n in neighbours)
                if (n != null) _neighbourPosBuf.Add(Vector3.Lerp(transform.position, n.transform.position, 0.5f));

            // If this normal gas pipe is attached to a grid, also draw arms to
            // adjacent gas-capable grid blocks. WrenchBlacklist can disable each
            // pipe ↔ endpoint link. Endpoint arms aim at the block's real GAS port
            // (engine oxygen intake, exhaust-pipe gas tap) when it has one.
            var gridBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var grid = gridBlock != null ? gridBlock.Grid : null;
            if (grid != null)
            {
                void AddArm(VoxelEngine.GridSystem.GridBlock block)
                {
                    if (block == null || block == gridBlock) return;
                    bool endpoint = block is VoxelEngine.GridSystem.GridGasTank
                                 || block is VoxelEngine.GridSystem.GridH2O2Generator
                                 || block is VoxelEngine.GridSystem.GridHydrogenEngine
                                 || block is VoxelEngine.GridSystem.GridThruster
                                 || block is VoxelEngine.Maritime.GridExhaustPipe
                                 || block is VoxelEngine.Maritime.GridMaritimeEngine;
                    bool connectedPipe = block.GetComponentInChildren<GasPipe>(true) != null;
                    if (!endpoint && !connectedPipe) return;
                    if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(gridBlock.gameObject, block.gameObject)) return;
                    _neighbourPosBuf.Add(connectedPipe
                        ? Vector3.Lerp(transform.position, block.transform.position, 0.5f)
                        : VoxelEngine.Maritime.MaritimePorts.PortPositionOrCenter(
                            block, VoxelEngine.Maritime.MaritimePorts.GasPrefixes, transform.position));
                }

                foreach (var block in VoxelEngine.GridSystem.UnifiedGridTopology.AdjacentBlocks(grid, gridBlock))
                    AddArm(block);

                // Proximity arms: gas ports overhang the lattice on the big machine
                // models (engine O2 intakes, the exhaust gas tap), so face-touch alone
                // misses them — reach any gas-capable block in touch range.
                int hitCount = Physics.OverlapSphereNonAlloc(transform.position,
                    Mathf.Max(gridBlock != null ? gridBlock.EffectiveCellSize : 0.5f,
                        VoxelEngine.GridSystem.GridSizeExt.CellSize(VoxelEngine.GridSystem.GridSize.Small)) * 1.35f,
                    s_armProbe, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = s_armProbe[i];
                    if (col == null) continue;
                    var block = col.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
                    if (block == null || block == gridBlock || block.Grid != grid) continue;
                    AddArm(block);
                }
            }
            return _neighbourPosBuf;
        }
    }
}
