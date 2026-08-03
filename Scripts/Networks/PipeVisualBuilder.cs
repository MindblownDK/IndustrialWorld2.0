// Assets/Scripts/VoxelEngine/Networks/PipeVisualBuilder.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║   PIPE VISUAL BUILDER — drives the IndustrialPipeMesh helper    ║
// ║   from a live neighbour-position provider supplied by the pipe   ║
// ║   component (GasPipe / ItemPipe / WaterPipe / DataCable / …).   ║
// ║                                                                  ║
// ║   • Style enum picks brass, copper, BC-sleeve or wire-arm.       ║
// ║   • Hashes the neighbour set every poll, only rebuilds on change.║
// ╚══════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networks
{
    /// <summary>
    /// Attach this to any pipe / cable prefab. The owning component sets
    /// <see cref="neighbourPositionsProvider"/> in Awake; the builder takes
    /// it from there.
    /// </summary>
    [DisallowMultipleComponent]
    public class PipeVisualBuilder : MonoBehaviour
    {
        [Header("Style")]
        [Tooltip("Which industrial profile to render. Pick Copper for water, " +
                 "Brass for gas, Sleeve for item pipes, WireArm for cables.")]
        public PipeStyle style = PipeStyle.Copper;

        [Tooltip("Build-grid cell size — caps the visual arm length so " +
                 "wildly-far neighbours can't grow goofy arms.")]
        public float gridSize = 1f;

        [Header("Colours")]
        [Tooltip("Main shell colour (the metallic shaft / sleeve).")]
        public Color shellTint = new(0.78f, 0.50f, 0.20f, 1f); // copper default

        [Tooltip("Optional accent colour for flange collars and end-terminals. " +
                 "Leave equal to shellTint for a monochrome look, set brighter " +
                 "for a polished-fitting highlight.")]
        public Color accentTint = new(0.92f, 0.78f, 0.40f, 1f);

        [Tooltip("Inner medium colour seen through the glass shell (glass pipes only).")]
        public Color innerMediumTint = new(0.25f, 0.55f, 0.95f, 1f);

        [Tooltip("Render as translucent glass with a visible inner medium core.")]
        public bool isGlass = false;

        [Tooltip("Glass pipes only: leave the tube HOLLOW (skip the opaque inner " +
                 "medium core) so external content — e.g. animated item pellets — " +
                 "is visible THROUGH the transparent shell. Item pipes set this true.")]
        public bool hollowGlass = false;

        [Header("Performance")]
        [Tooltip("Seconds between safety-net neighbour checks. Rebuilds are normally " +
                 "event-driven (fired when a block is placed/removed via " +
                 "NotifyTopologyChanged); this is only a slow fallback so nothing " +
                 "stays permanently stale.")]
        public float rebuildInterval = 4.0f;

        // ── Topology-change signal ─────────────────────────────────────
        // A topology change may affect many pipes. We therefore enqueue each builder
        // and let a small shared per-frame budget process visual rebuilds over several
        // frames instead of destroying/recreating every pipe mesh in one 5-FPS spike.
        public static int TopologyVersion { get; private set; }
        // Rebuilding a copper pipe can create many flange/bolt primitives. One real
        // rebuild per frame is intentionally conservative; local topology dispatch
        // below means a normal edit only queues the few pipes that can actually link.
        private const int MaxQueuedRebuildsPerFrame = 1;
        private const float DefaultTopologyInfluenceRadius = 6.5f;
        private static int s_rebuildBudgetFrame = -1;
        private static int s_rebuildBudgetUsed;
        private static readonly List<PipeVisualBuilder> s_builderScratch = new(64);

        /// <summary>
        /// Compatibility fallback for rare topology events without a location. Normal
        /// placement/network callers use the positioned overload so a long pipe run
        /// never rebuilds globally after one edit.
        /// </summary>
        public static void NotifyTopologyChanged()
        {
            TopologyVersion++;
            s_builderScratch.Clear();
            foreach (var pair in _AllBuilders)
                if (pair.Key != null) s_builderScratch.Add(pair.Key);
            for (int i = 0; i < s_builderScratch.Count; i++)
                s_builderScratch[i].RequestTopologyRefresh();
        }

        /// <summary>
        /// Queues only pipes within the largest legal five-cell linking corridor of
        /// a changed position. This keeps a 20+ pipe farm from tearing down every
        /// visual hierarchy when one pipe or endpoint is edited.
        /// </summary>
        public static void NotifyTopologyChanged(Vector3 changedPosition, float influenceRadius = 0f)
        {
            TopologyVersion++;
            s_builderScratch.Clear();
            foreach (var pair in _AllBuilders)
            {
                var builder = pair.Key;
                if (builder == null) continue;
                float cellSize = Mathf.Max(0.5f, builder.gridSize);
                // A caller-supplied radius is authoritative: network managers know
                // the exact changed pipe lattice. The default remains generous only
                // for generic non-pipe endpoint edits.
                float reach = influenceRadius > 0f
                    ? influenceRadius
                    : Mathf.Max(DefaultTopologyInfluenceRadius, cellSize * 5.6f + 0.75f);
                if ((builder.transform.position - changedPosition).sqrMagnitude <= reach * reach)
                    s_builderScratch.Add(builder);
            }
            for (int i = 0; i < s_builderScratch.Count; i++)
                s_builderScratch[i].RequestTopologyRefresh();
        }

        // ── Legacy compat / visual toggles ───────────────────────────
        // These fields were used by the old cube-primitive renderer before
        // the upgrade to IndustrialPipeMesh. Sizing is now profile-driven,
        // but showUnusedFaceCaps remains a live clarity toggle: gas/liquid
        // pipes disable it so dead-end caps do not look like fake links.
        [HideInInspector] public float coreSize           = 0.34f;
        [HideInInspector] public float armThickness       = 0.24f;
        [HideInInspector] public bool  showUnusedFaceCaps = true;

        // ── Hook set by the pipe component in Awake ─────────────
        /// <summary>Supplier of neighbour world positions (cardinal-aligned).</summary>
        public Func<List<Vector3>> neighbourPositionsProvider;

        // ── Visual-priority registry ─────────────────────────────
        // Pipes register their world position + isGlass flag so glass pipes
        // can skip rendering joint geometry at faces where a SOLID pipe of
        // any kind already sits — eliminates the Z-fighting "flash" the
        // player reported when solid and glass variants meet.
        // Solid wins. Cleared when a pipe disables.
        private static readonly Dictionary<PipeVisualBuilder, bool> _AllBuilders = new();

        // One-time per-session compilation verification log. If you don't
        // see this line in the console after a pipe spawns, Unity is still
        // running a stale assembly cache.
        private static bool _builderLoggedOnce;

        /// <summary>
        /// FIRES BEFORE ANY SCENE LOADS — guaranteed to execute the moment
        /// the assembly is loaded into the Unity runtime, regardless of any
        /// scene / prefab state. If you don't see THIS in the console after
        /// pressing Play, the new VoxelEngine assembly DID NOT compile and
        /// Unity is running a stale cached DLL. Solutions:
        ///   1. Close Unity → delete the `Library/ScriptAssemblies` folder
        ///      from the project → reopen Unity.
        ///   2. OR: in Unity menu, Assets → Reimport All.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AssemblyLoadProbe()
        {
            Debug.Log("[IndustrialWorld] ✓ VoxelEngine assembly v5 loaded — PipeVisualBuilder is ready.");
        }

        // ── Internals ───────────────────────────────────────────
        private Transform _visualRoot;
        private Material  _shellMat;
        private Material  _innerMat;
        private Material  _accentMat;
        private readonly List<Vector3> _scratch = new(6);
        private int   _lastHash;
        private float _scanTimer;
        private int   _seenVersion = -1;
        private bool  _topologyRefreshQueued;
        private bool  _hasBuilt;

        private void Awake()
        {
            EnsureVisualRoot();
            HidePrebakedMesh();
            // DO NOT cache materials here — at Awake() time the parent pipe
            // component (GasPipe/ItemPipe/WaterPipe) hasn't yet assigned
            // `style`, `isGlass`, `shellTint`, etc. on us, so any material
            // built here would use the wrong colours/style. Materials are
            // now created lazily inside ForceRebuild() which runs in
            // OnEnable, AFTER the parent has set our public fields.
        }

        /// <summary>
        /// Aggressively hide every renderer that came from the original
        /// prefab so old "stretched cube Mesh" children don't show through
        /// the new round shaft visuals. Setting enabled=false alone wasn't
        /// enough — some old prefabs ship the cube with a BoxCollider AND
        /// the renderer re-enabled itself when materials were swapped, so
        /// we now DESTROY the entire Mesh child GameObject as well, leaving
        /// the new PipeVisuals as the only thing left to render.
        /// </summary>
        private void HidePrebakedMesh()
        {
            // Walk children once; collect candidates to destroy in a list so
            // we don't mutate the hierarchy while iterating.
            var toDestroy = new List<GameObject>();
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.transform == transform) continue;
                if (_visualRoot != null && r.transform.IsChildOf(_visualRoot)) continue;
                r.enabled = false;
                toDestroy.Add(r.gameObject);
            }
            foreach (var go in toDestroy)
            {
                // Destroy at end of frame to avoid stomping on Unity's
                // current iteration. Skipped in editor inspection mode
                // (we only run during play).
                if (Application.isPlaying) Destroy(go);
            }
        }

        private void OnEnable()
        {
            _lastHash = 0;
            _hasBuilt = false;
            _topologyRefreshQueued = false;
            _seenVersion = TopologyVersion;
            if (VoxelEngine.Building.BuildSystem.IsCreatingGhost) return;
            _AllBuilders[this] = isGlass;
            // Defer one frame so the parent pipe component's Awake/OnEnable
            // has a chance to set our style/tint fields before we cache
            // materials. Critical for prefabs that DON'T ship with a
            // PipeVisualBuilder baked in (the auto-add path in
            // GasPipe.Awake): Unity runs Awake on us synchronously inside
            // AddComponent, so our materials would otherwise be built with
            // the default copper tint regardless of what the parent
            // intended. A 1-frame delay ensures every public field is set.
            StartCoroutine(DeferredFirstRebuild());
        }

        private System.Collections.IEnumerator DeferredFirstRebuild()
        {
            yield return null;
            // Drop any prematurely-cached materials so they get rebuilt
            // with the post-Awake style/tint values.
            _shellMat = null;
            _innerMat = null;
            _accentMat = null;
            if (!_builderLoggedOnce)
            {
                _builderLoggedOnce = true;
                Debug.Log($"[IndustrialWorld] PipeVisualBuilder v4 loaded — style={style}, isGlass={isGlass}, shellTint={shellTint}");
            }
            // An immediate placement may already have built this pipe. Force one
            // post-Awake rebuild anyway because style/tint assignment can happen after
            // the builder's Awake, but record the resulting hash to prevent a second
            // redundant topology rebuild on the following frame.
            ForceRebuild();
        }

        private void OnDisable()
        {
            _AllBuilders.Remove(this);
            if (_visualRoot != null)
            {
                for (int i = _visualRoot.childCount - 1; i >= 0; i--)
                    Destroy(_visualRoot.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// True when this builder is the GLASS variant AND another (solid)
        /// PipeVisualBuilder sits at exactly the same world position. In
        /// that case we suppress our visuals entirely so the solid pipe's
        /// material wins, eliminating the Z-fighting flash the player saw
        /// when solid + glass variants were stacked at the same cell.
        /// </summary>
        private bool ShouldYieldToSolid()
        {
            if (!isGlass) return false;
            Vector3 self = transform.position;
            foreach (var kv in _AllBuilders)
            {
                var other = kv.Key;
                if (other == null || other == this) continue;
                if (other.isGlass) continue; // only YIELD to a solid
                if ((other.transform.position - self).sqrMagnitude < 0.05f * 0.05f)
                    return true;
            }
            return false;
        }

        private void Update()
        {
            // Event-driven work is queued by NotifyTopologyChanged. The shared budget
            // prevents one newly placed pipe from forcing every existing pipe to rebuild
            // its primitive hierarchy in the same frame.
            if (_topologyRefreshQueued)
            {
                if (!TryConsumeRebuildBudget()) return;
                _topologyRefreshQueued = false;
                RefreshIfNeighbourHashChanged();
                return;
            }

            // Slow safety net for topology signals from legacy content. It uses the
            // same budget and hash gate, so dense pipe runs stay smooth.
            _scanTimer += Time.deltaTime;
            if (_scanTimer < Mathf.Max(rebuildInterval, 3f)) return;
            if (!TryConsumeRebuildBudget()) return;
            _scanTimer = 0f;
            _seenVersion = TopologyVersion;
            RefreshIfNeighbourHashChanged();
        }

        /// <summary>Queues a hash-gated visual refresh without rebuilding immediately.</summary>
        public void RequestTopologyRefresh()
        {
            _seenVersion = TopologyVersion;
            _topologyRefreshQueued = true;
        }

        private static bool TryConsumeRebuildBudget()
        {
            if (s_rebuildBudgetFrame != Time.frameCount)
            {
                s_rebuildBudgetFrame = Time.frameCount;
                s_rebuildBudgetUsed = 0;
            }
            if (s_rebuildBudgetUsed >= MaxQueuedRebuildsPerFrame) return false;
            s_rebuildBudgetUsed++;
            return true;
        }

        private void RefreshIfNeighbourHashChanged()
        {
            PopulateScratch();
            int hash = HashPositions(_scratch);
            if (_hasBuilt && hash == _lastHash) return;
            RebuildFromScratch(hash);
        }

        /// <summary>Manual immediate rebuild for the just-placed pipe/ghost only.</summary>
        public void ForceRebuild()
        {
            PopulateScratch();
            int hash = HashPositions(_scratch);
            // Several placement paths can request the just-created pipe in the same
            // frame. Do not destroy/recreate an identical hierarchy twice.
            if (_hasBuilt && hash == _lastHash) return;
            RebuildFromScratch(hash);
        }

        private void PopulateScratch()
        {
            _scratch.Clear();
            if (neighbourPositionsProvider == null) return;
            var list = neighbourPositionsProvider();
            if (list != null) _scratch.AddRange(list);
        }

        private void RebuildFromScratch(int hash)
        {
            EnsureVisualRoot();
            _lastHash = hash;
            _hasBuilt = true;

            // Hide ourselves entirely if a solid version of the same pipe sits
            // at this same cell — prevents the Z-fighting flash when solid +
            // glass variants are placed on top of each other.
            if (ShouldYieldToSolid())
            {
                for (int i = _visualRoot.childCount - 1; i >= 0; i--)
                    Destroy(_visualRoot.GetChild(i).gameObject);
                return;
            }

            EnsureMaterials();
            IndustrialPipeMesh.Rebuild(
                _visualRoot,
                transform.position,
                _scratch,
                gridSize > 0 ? gridSize : 1f,
                style,
                _shellMat,
                (isGlass && !hollowGlass) ? _innerMat : null,
                _accentMat,
                showUnusedFaceCaps);
        }

        // ────────────────────────────────────────────────────────
        private void EnsureVisualRoot()
        {
            if (_visualRoot != null) return;
            var go = new GameObject("PipeVisuals");
            _visualRoot = go.transform;
            _visualRoot.SetParent(transform, worldPositionStays: false);
        }

        private void EnsureMaterials()
        {
            if (_shellMat == null)
            {
                _shellMat = isGlass
                    ? IndustrialPipeMesh.CreateGlassMaterial(shellTint, $"{name}_Shell")
                    : IndustrialPipeMesh.CreateMetalMaterial(shellTint, $"{name}_Shell");
            }
            if (_accentMat == null)
            {
                // Always opaque metallic for collars/terminals so they read
                // distinctly even on a translucent shell.
                _accentMat = IndustrialPipeMesh.CreateMetalMaterial(
                    accentTint, $"{name}_Accent", metallic: 0.95f, smoothness: 0.85f);
            }
            if (isGlass && _innerMat == null)
            {
                _innerMat = IndustrialPipeMesh.CreateInnerCoreMaterial(
                    innerMediumTint, $"{name}_Inner");
            }
        }

        private static int HashPositions(IReadOnlyList<Vector3> list)
        {
            if (list == null || list.Count == 0) return 0;
            unchecked
            {
                // Physics overlap order is not stable. An order-sensitive hash made an
                // unchanged pipe occasionally tear down/rebuild just because Unity
                // returned the same neighbours in a different order.
                int sum = 0;
                int xor = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    int x = Mathf.RoundToInt(p.x * 100f);
                    int y = Mathf.RoundToInt(p.y * 100f);
                    int z = Mathf.RoundToInt(p.z * 100f);
                    int key = x * 73856093 ^ y * 19349663 ^ z * 83492791;
                    sum += key;
                    int shift = key & 7;
                    int rotated = shift == 0 ? key : (key << shift) | (int)((uint)key >> (32 - shift));
                    xor ^= rotated;
                }
                return list.Count * 486187739 ^ sum ^ xor;
            }
        }
    }
}
