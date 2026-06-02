// Assets/Scripts/VoxelEngine/Power/PowerCable.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.Power
{
    /// <summary>
    /// A cable. Doesn't generate or consume; carries power between nodes. Its WireDefinition
    /// determines the segment's capacity. The network's bottleneck is the MINIMUM capacity
    /// along its cables.
    ///
    /// Connection policy: cables only link to neighbours that sit exactly one grid step
    /// away on a single cardinal axis (±X / ±Y / ±Z) AND have an unobstructed line of
    /// sight. This means cables stack vertically, never connect diagonally, and refuse
    /// to "tunnel" through solid blocks.
    ///
    /// Visuals: a central chunky core cube + up to 6 short arm cubes pointing toward
    /// each connected neighbour. The arms are spawned/torn down whenever the network
    /// topology rebuilds, so visuals always match electrical state.
    /// </summary>
    public class PowerCable : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Cable;

        [Header("Tier")]
        public WireDefinition wire;

        [Header("Visual")]
        [Tooltip("Edge length of the central cable hub cube, in metres. A typical 1m grid " +
                 "block uses ~0.35 for a chunky-but-not-blocky look.")]
        [Range(0.1f, 0.9f)] public float coreSize = 0.35f;
        [Tooltip("Thickness (width × height) of each arm extending toward a neighbour.")]
        [Range(0.05f, 0.6f)] public float armThickness = 0.28f;
        [Tooltip("If true, the cable also renders a tiny indicator nub on each face that " +
                 "has NO neighbour, so the player can tell where the cable terminates.")]
        public bool showUnusedFaceCaps = false;

        // ── Internals ─────────────────────────────────────────────
        private Transform _visualRoot;        // parent for all generated meshes
        private Material  _tintedMaterial;    // shared per cable so MPB stays simple
        private readonly List<Vector3> _neighbourPositionsBuf = new(6);

        protected override void OnEnable()
        {
            // Cables live on a 1m grid. The broad-phase needs to reach neighbouring
            // CABLES at exactly 1 grid step AND larger MACHINES whose centre may be
            // 1.5–2.5m away (a multi-voxel generator placed beside the cable's cell).
            // We use a roomier radius and let the per-node CanLinkTo logic enforce
            // the strict grid-alignment rule only when BOTH ends are cables.
            float gs = gridSize > 0 ? gridSize : 1f;
            connectRadius = gs * 3.0f;

            // Cables themselves opt into the strict grid+LOS policy. Machines do not,
            // so a cable-vs-machine link is symmetric-OFF and accepts any face.
            requireGridAlignedNeighbours = true;
            // Default mask: hit everything EXCEPT Ignore Raycast (Unity layer 2).
            connectionBlockingLayers = ~(1 << 2);

            base.OnEnable();

            // Hide the prefab's pre-baked "stretched cube" — we render arms ourselves now.
            var existingRenderers = GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in existingRenderers)
            {
                // Tag the children that came from the prefab as "managed" so we can hide
                // them without nuking the central node script renderer (there is none).
                if (r.transform != transform) r.enabled = false;
            }

            EnsureVisualRoot();
            onNeighboursChanged += RebuildVisuals;
            // Build once immediately so newly placed cables aren't briefly invisible.
            RebuildVisuals();
        }

        protected override void OnDisable()
        {
            onNeighboursChanged -= RebuildVisuals;
            base.OnDisable();
        }

        // ── Visual construction ──────────────────────────────────
        private void EnsureVisualRoot()
        {
            if (_visualRoot != null) return;
            var go = new GameObject("CableVisuals");
            _visualRoot = go.transform;
            _visualRoot.SetParent(transform, worldPositionStays: false);
        }

        private void RebuildVisuals()
        {
            if (_visualRoot == null) EnsureVisualRoot();
            if (_tintedMaterial == null)
            {
                Color tint = wire != null ? wire.tint : new Color(0.85f, 0.45f, 0.20f, 1f);
                _tintedMaterial = GridCableVisuals.CreateTintedMaterial(tint, $"{name}_CableMat");
            }

            // Pass the *actual* neighbour world positions; GridCableVisuals snaps
            // each one to the nearest cardinal axis and grows an arm exactly long
            // enough to bridge the gap. This makes cables visually meet machines
            // whose centres aren't on the cable grid (server racks, generators).
            _neighbourPositionsBuf.Clear();
            foreach (var nb in neighbours)
            {
                if (nb == null) continue;
                _neighbourPositionsBuf.Add(nb.transform.position);
            }

            GridCableVisuals.Rebuild(
                _visualRoot,
                transform.position,
                _neighbourPositionsBuf,
                gridSize > 0 ? gridSize : 1f,
                coreSize,
                armThickness,
                _tintedMaterial,
                showUnusedFaceCaps);
        }
    }
}
