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

        private void OnEnable()  { GasNetwork.EnsureInstance(); GasNetwork.Instance?.Register(this); }
        private void OnDisable() => GasNetwork.Instance?.Unregister(this);

        /// <summary>
        /// Supplier called by <see cref="PipeVisualBuilder"/> every rebuild interval.
        /// Returns live world positions of every connected neighbour pipe.
        /// </summary>
        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            foreach (var n in neighbours)
                if (n != null) _neighbourPosBuf.Add(n.transform.position);

            // If this normal gas pipe is attached to a grid, also draw arms to
            // adjacent gas-capable grid blocks. WrenchBlacklist can disable each
            // pipe ↔ endpoint link.
            var gridBlock = GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            var grid = gridBlock != null ? gridBlock.Grid : null;
            if (grid != null)
            {
                AddGridEndpoint(grid, gridBlock, gridBlock.GridPos + Vector3Int.right);
                AddGridEndpoint(grid, gridBlock, gridBlock.GridPos + Vector3Int.left);
                AddGridEndpoint(grid, gridBlock, gridBlock.GridPos + Vector3Int.up);
                AddGridEndpoint(grid, gridBlock, gridBlock.GridPos + Vector3Int.down);
                AddGridEndpoint(grid, gridBlock, gridBlock.GridPos + new Vector3Int(0, 0, 1));
                AddGridEndpoint(grid, gridBlock, gridBlock.GridPos + new Vector3Int(0, 0, -1));
            }
            return _neighbourPosBuf;
        }

        private void AddGridEndpoint(VoxelEngine.GridSystem.GridEntity grid, VoxelEngine.GridSystem.GridBlock pipeBlock, Vector3Int pos)
        {
            var block = grid.GetBlock(pos);
            if (block == null || block == pipeBlock) return;
            bool endpoint = block is VoxelEngine.GridSystem.GridGasTank
                         || block is VoxelEngine.GridSystem.GridH2O2Generator
                         || block is VoxelEngine.GridSystem.GridHydrogenEngine
                         || block is VoxelEngine.GridSystem.GridThruster;
            if (!endpoint) return;
            if (VoxelEngine.Networks.WrenchBlacklist.IsBlocked(pipeBlock.gameObject, block.gameObject)) return;
            _neighbourPosBuf.Add(grid.GridToWorld(pos));
        }
    }
}
