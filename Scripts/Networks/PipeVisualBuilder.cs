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
        [Tooltip("Seconds between neighbour-change checks.")]
        public float rebuildInterval = 0.4f;

        // ── Legacy compat (silently ignored by the new pipe renderer) ──
        // These three fields were used by the old cube-primitive renderer
        // before the upgrade to IndustrialPipeMesh. Kept so prefab assets
        // and old editor wizard code that still poke them keep compiling.
        // The new renderer derives all sizing from the PipeStyle profile.
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
            _scanTimer += Time.deltaTime;
            if (_scanTimer < rebuildInterval) return;
            _scanTimer = 0f;

            int h = ComputeNeighbourHash();
            if (h == _lastHash) return;
            _lastHash = h;
            ForceRebuild();
        }

        /// <summary>Manual rebuild — network managers call this after wrench edits.</summary>
        public void ForceRebuild()
        {
            EnsureVisualRoot();

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
            _scratch.Clear();
            if (neighbourPositionsProvider != null)
            {
                var list = neighbourPositionsProvider();
                if (list != null) _scratch.AddRange(list);
            }

            IndustrialPipeMesh.Rebuild(
                _visualRoot,
                transform.position,
                _scratch,
                gridSize > 0 ? gridSize : 1f,
                style,
                _shellMat,
                (isGlass && !hollowGlass) ? _innerMat : null,
                _accentMat);
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

        private int ComputeNeighbourHash()
        {
            if (neighbourPositionsProvider == null) return 0;
            var list = neighbourPositionsProvider();
            if (list == null) return 0;
            unchecked
            {
                int h = 17;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    h = h * 31 + Mathf.RoundToInt(p.x * 100f);
                    h = h * 31 + Mathf.RoundToInt(p.y * 100f);
                    h = h * 31 + Mathf.RoundToInt(p.z * 100f);
                }
                return h;
            }
        }
    }
}
