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
            return _neighbourPosBuf;
        }
    }
}
