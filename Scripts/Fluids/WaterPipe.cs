// Assets/Scripts/VoxelEngine/Fluids/WaterPipe.cs
//
// Fluid-network pipe segment. The actual flow logic lives in
// FluidNetwork / FluidNetworkManager — this script is mostly an identity
// + a visual driver. Glass variants reveal an inner water-tinted core
// through the translucent shell.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.Fluids
{
    public class WaterPipe : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Pipe;
        public float maxFlowLps = 50f;
        public bool isGlass = false;

        private PipeVisualBuilder _visuals;
        private readonly List<Vector3> _neighbourPosBuf = new(6);

        private void Awake()
        {
            _visuals = GetComponent<PipeVisualBuilder>();
            if (_visuals == null) _visuals = gameObject.AddComponent<PipeVisualBuilder>();
            _visuals.neighbourPositionsProvider = GetNeighbourPositions;
            _visuals.isGlass = isGlass;
            // Water/fluid pipes use the FATTER copper profile so they're
            // visually distinct from the slim brass gas pipes.
            _visuals.style = VoxelEngine.Networks.PipeStyle.Copper;
        }

        private List<Vector3> GetNeighbourPositions()
        {
            _neighbourPosBuf.Clear();
            if (neighbours == null) return _neighbourPosBuf;
            foreach (var n in neighbours)
                if (n != null) _neighbourPosBuf.Add(n.transform.position);
            return _neighbourPosBuf;
        }
    }
}
