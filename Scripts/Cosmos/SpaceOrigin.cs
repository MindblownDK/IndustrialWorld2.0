// Assets/Scripts/VoxelEngine/Cosmos/SpaceOrigin.cs
//
// THE floating origin + reference-frame system that makes real, infinite, continuous
// space possible. Everything in the cosmos lives in double-precision kilometres
// (CosmicRegistry); the scene is a small float-precision window around the player.
//
// Responsibilities:
//
//  1. ANCHOR. `AnchorKm` is the cosmic position of scene origin. Every frame each
//     celestial body's scene transform is placed at (cosmic − anchor)·1000, so the
//     whole solar system is real geometry around you — not a skybox.
//
//  2. FRAME. The scene is the co-moving frame of the DOMINANT body (the one whose
//     gravity wins at the player's position — a planet/moon, or the star in deep
//     space). The frame body stays still in the scene while everything else visibly
//     orbits it. When dominance changes (leaving a gravity well, entering another),
//     the frame switches and every scene object's velocity is re-expressed by the
//     frame-velocity delta — exactly how real astrodynamics handles reference-frame
//     changes, so cosmic (inertial) velocity is always conserved.
//
//  3. REBASE. When the player's scene position drifts beyond `rebaseDistanceMeters`,
//     the whole world is shifted back so floats stay precise — visually invisible
//     because every scene object (player, grids, chunks under their bodies, dropped
//     items) moves by the same delta.
//
// Registered roots are the scene objects that must be shifted: celestial body roots,
// the player, every grid root, and (via periodic sweep) any rigidbody/character
// controller not already under a registered root.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Floating-origin + reference-frame manager. [DefaultExecutionOrder(-1000)] so the
    /// frame velocity / rebase is applied BEFORE GridEntity (0) and PlayerController.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SpaceOrigin : MonoBehaviour
    {
        public static SpaceOrigin Instance { get; private set; }

        [Header("Floating Origin")]
        [Tooltip("Scene-space drift allowed before the world is re-based (metres). " +
                 "32 km keeps float precision near millimetre level everywhere.")]
        public float rebaseDistanceMeters = 32000f;

        [Header("Frame Selection")]
        [Tooltip("A candidate body must exceed the current frame body's pull by this factor before the frame switches (hysteresis — prevents flicker).")]
        [Range(1.05f, 2f)] public float frameSwitchHysteresis = 1.25f;

        [Tooltip("Minimum gravity (m/s²) a body needs at the player to be eligible as the scene frame body.")]
        public float frameEligibilityGravityMps2 = 0.012f;

        [Header("References")]
        [Tooltip("Player transform (auto-resolved). Scene origin keeps this near zero.")]
        public Transform viewer;

        [Header("Spawn Stability")]
        [Tooltip("Seconds after scene load during which automatic reference-frame switches " +
                 "are suppressed (the frame is pinned to the home body at bootstrap; this " +
                 "prevents any spawn-time frame-velocity kick).")]
        [Range(0f, 10f)] public float spawnGraceSeconds = 3f;

        [Tooltip("While true, NO automatic reference-frame switch may run (and therefore no " +
                 "frame-velocity delta is ever applied). The spawner sets this around spawn / " +
                 "respawn teleports so the freshly-placed player can never be kicked sideways " +
                 "by a frame switch landing between teleport and control handover.")]
        public bool suppressAutoFrameSwitches;

        // ── State ─────────────────────────────────────────────────
        /// <summary>Cosmic position (km) of the scene origin.</summary>
        public double3 AnchorKm { get; private set; }

        /// <summary>Velocity (km/s) of the scene frame in the cosmic inertial frame.</summary>
        public double3 FrameVelocityKmS { get; private set; }

        /// <summary>The body whose co-moving frame the scene is in. Null = the star frame (deep space).</summary>
        public CelestialBody FrameBody { get; private set; }

        /// <summary>True when the player is in deep space (outside every body's frame).</summary>
        public bool IsDeepSpace => FrameBody == null;

        /// <summary>Cosmic position (km) of the viewer, refreshed every fixed tick.</summary>
        public double3 ViewerCosmicKm { get; private set; }

        private readonly HashSet<Transform> _roots = new HashSet<Transform>();
        private float _sweepTimer;
        private bool _frameReady;

        // ── Events ────────────────────────────────────────────────
        /// <summary>Fired when the scene reference frame changes (body, or null = star frame).</summary>
        public static event System.Action<CelestialBody> OnFrameChanged;

        /// <summary>Fired with the scene-space velocity delta (m/s) that was applied to every scene object.</summary>
        public static event System.Action<Vector3> OnFrameVelocityApplied;

        // ── Lifecycle ─────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Initialize(Transform viewerTransform, double3 initialAnchorKm)
        {
            viewer = viewerTransform;
            AnchorKm = initialAnchorKm;
            _frameReady = true;
        }

        // ── Registration ──────────────────────────────────────────
        public void RegisterRoot(Transform root)
        {
            if (root == null) return;
            _roots.Add(root);
        }

        public void UnregisterRoot(Transform root)
        {
            if (root != null) _roots.Remove(root);
        }

        /// <summary>Scene position (m) for a cosmic position (km).</summary>
        public Vector3 GetScenePos(double3 cosmicKm) => (Vector3)(float3)((cosmicKm - AnchorKm) * 1000d);

        /// <summary>
        /// Re-anchor the origin so the given body keeps its CURRENT scene position
        /// (used when the bootstrap places the home body at the viewer's surface).
        /// </summary>
        public void AlignAnchorToBodyScenePosition(CelestialBody body)
        {
            var reg = CosmicRegistry.Instance;
            if (reg == null || body == null) return;
            BodyInstance inst = null;
            foreach (var b in reg.Bodies)
            {
                if (b != null && b.settings == body.settings) { inst = b; break; }
            }
            if (inst == null) return;
            AnchorKm = reg.CosmicPositionOf(inst) - CosmicRegistry.ToDouble3(body.transform.position) / 1000d;
            _frameReady = true;
        }

        /// <summary>Cosmic position (km) for a scene position (m).</summary>
        public double3 GetCosmicKm(Vector3 scenePos) => AnchorKm + CosmicRegistry.ToDouble3(scenePos) / 1000d;

        /// <summary>
        /// Teleport the origin so the viewer's cosmic position equals the given value.
        /// Used by save/load (logging out in deep space) and by the warp drive.
        /// </summary>
        public void TeleportCosmic(double3 newViewerCosmicKm)
        {
            if (viewer == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) viewer = pc.transform;
                if (viewer == null) return;
                RegisterRoot(viewer);
            }
            double3 newAnchor = newViewerCosmicKm - CosmicRegistry.ToDouble3(viewer.position) / 1000d;
            double3 delta = newAnchor - AnchorKm;
            if (math.lengthsq(delta) < 1e-18) return;
            AnchorKm = newAnchor;
            // Keep relative geometry: shift every scene root by −delta·1000.
            Vector3 shift = (Vector3)(float3)(-delta * 1000d);
            ShiftWorld(shift);
            ViewerCosmicKm = newViewerCosmicKm;
            PlaceBodies();
            // Re-pick the frame WITHOUT applying a velocity delta: a teleport re-anchors
            // the world, and velocities are the caller's responsibility (the warp drive
            // zeroes the ship, save-restore zeroes the player). Applying the delta here
            // used to kick freshly-teleported players sideways at hundreds of m/s.
            var reg = CosmicRegistry.Instance;
            if (reg != null && reg.IsReady)
            {
                BodyInstance dominant = reg.GetDominantBody(newViewerCosmicKm, out _);
                CelestialBody frame = null;
                if (dominant != null) reg.SceneBodies.TryGetValue(dominant, out frame);
                SetFrame(frame);
                PlaceBodies();
            }
        }

        /// <summary>Set the frame directly (used by save restore to co-move with the right body).</summary>
        public void SetFrame(CelestialBody body)
        {
            if (body == FrameBody) return;
            FrameBody = body;
            if (body != null)
            {
                var inst = FindInstanceOf(body);
                if (inst != null)
                    FrameVelocityKmS = CosmicRegistry.Instance != null ? CosmicRegistry.Instance.VelocityOf(inst) : double3.zero;
                else
                    FrameVelocityKmS = double3.zero;
            }
            else
            {
                FrameVelocityKmS = double3.zero;
            }
            _frameReady = true;
        }

        private static BodyInstance FindInstanceOf(CelestialBody body)
        {
            var reg = CosmicRegistry.Instance;
            if (reg == null) return null;
            foreach (var kv in reg.SceneBodies)
                if (kv.Value == body) return kv.Key;
            return null;
        }

        // ── Per-frame simulation ──────────────────────────────────
        private void FixedUpdate()
        {
            var reg = CosmicRegistry.Instance;
            if (reg == null || !reg.IsReady) return;
            // ALWAYS track the real player: the bootstrap may have initialised us with a
            // placeholder transform before the player existed. Viewer position drives the
            // rebase and the frame-selection point — a stale viewer corrupts both.
            if (viewer == null || viewer.GetComponent<PlayerController>() == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc != null)
                {
                    viewer = pc.transform;
                    RegisterRoot(viewer);
                }
                if (viewer == null) return;
            }

            if (!_frameReady)
            {
                _frameReady = true;
                PlaceBodies();
            }

            ViewerCosmicKm = GetCosmicKm(viewer.position);

            // 1. Frame selection by gravitational dominance.
            ReEvaluateFrame(force: false);

            // 2. Anchor follows the frame body's orbital motion (so the frame body is
            //    static in the scene while everything else moves along real orbits).
            if (math.lengthsq(FrameVelocityKmS) > 1e-18)
                AnchorKm += FrameVelocityKmS * Time.fixedDeltaTime;

            // 3. Place every body at its true scene position.
            PlaceBodies();

            // 4. Rebase when the viewer drifts too far from scene origin.
            Vector3 scenePos = viewer.position;
            if (scenePos.sqrMagnitude > rebaseDistanceMeters * rebaseDistanceMeters)
            {
                Vector3 shift = -scenePos;
                ShiftWorld(shift);
                AnchorKm += CosmicRegistry.ToDouble3(scenePos) / 1000d;
                ViewerCosmicKm = GetCosmicKm(Vector3.zero);
            }

            // 5. Sweep for late-registered scene objects (grids placed after boot, etc.).
            _sweepTimer -= Time.fixedDeltaTime;
            if (_sweepTimer <= 0f)
            {
                _sweepTimer = 2f;
                RegisterLateObjects();
            }
        }

        /// <summary>
        /// Choose the frame body by gravity dominance (with hysteresis) and apply the
        /// frame-velocity delta to every scene object when it changes.
        /// </summary>
        private void ReEvaluateFrame(bool force)
        {
            var reg = CosmicRegistry.Instance;
            if (reg == null) return;

            // Spawn grace: during the first seconds after scene load the frame is already
            // pinned to the home body and NO automatic frame switch may apply a velocity
            // delta (that delta is what flung players at spawn). Forced switches (warp,
            // save restore) still work. The spawner also suppresses auto switches around
            // spawn/respawn teleports for the same reason.
            if (!force && (suppressAutoFrameSwitches || Time.timeSinceLevelLoad < spawnGraceSeconds)) return;

            BodyInstance dominant = reg.GetDominantBody(ViewerCosmicKm, out double candidateAccel);
            CelestialBody candidateBody = null;
            if (dominant != null)
            {
                reg.SceneBodies.TryGetValue(dominant, out candidateBody);
                if (candidateBody == null) return; // body factory not ready yet
            }

            if (candidateBody == FrameBody) return;

            // Hysteresis: only switch when the new candidate meaningfully wins.
            if (!force)
            {
                if (FrameBody == null)
                {
                    // Deep space → body: require real pull.
                    if (candidateAccel < frameEligibilityGravityMps2) return;
                }
                else
                {
                    double currentAccel = 0d;
                    var curInst = FindInstanceOf(FrameBody);
                    if (curInst != null)
                    {
                        double3 toCur = curInst.positionKmD - ViewerCosmicKm;
                        double dCur = math.length(toCur);
                        double rKm = curInst.settings != null ? curInst.settings.radiusKm : 1d;
                        if (dCur < rKm) dCur = rKm;
                        if (dCur > 0.05d)
                            currentAccel = curInst.gravitationalParamKm3S2 * 1000d / (dCur * dCur);
                    }
                    // Switch when the candidate clearly wins (or the current frame body is gone).
                    if (candidateAccel < currentAccel * frameSwitchHysteresis) return;
                }
            }

            // ── Apply the frame switch ────────────────────────────
            double3 oldFrameVel = FrameVelocityKmS;
            CelestialBody oldFrameBody = FrameBody;
            FrameBody = candidateBody;

            if (candidateBody != null)
            {
                var inst = FindInstanceOf(candidateBody);
                FrameVelocityKmS = inst != null ? reg.VelocityOf(inst) : double3.zero;
            }
            else
            {
                FrameVelocityKmS = double3.zero;
            }

            // Re-express every scene object's velocity in the new frame (cosmic velocity
            // is conserved: v_scene_new = v_scene_old + (v_oldFrame − v_newFrame)).
            double3 deltaKmS = oldFrameVel - FrameVelocityKmS;
            if (math.lengthsq(deltaKmS) > 1e-12)
            {
                Vector3 deltaMps = (Vector3)(float3)(deltaKmS * 1000d);
                ApplyVelocityDeltaToScene(deltaMps);
                OnFrameVelocityApplied?.Invoke(deltaMps);
            }

            string frameName = candidateBody != null
                ? $"'{candidateBody.DisplayName}' frame"
                : "SOL (deep space) frame";
            Debug.Log($"[SpaceOrigin] Reference frame → {frameName} (Δv {(Vector3)(float3)(deltaKmS * 1000d):0.0} m/s).");

            if (oldFrameBody != candidateBody)
                OnFrameChanged?.Invoke(candidateBody);
        }

        /// <summary>Apply a scene-space velocity delta to every dynamic scene object.</summary>
        private void ApplyVelocityDeltaToScene(Vector3 deltaMps)
        {
            if (deltaMps.sqrMagnitude < 0.0001f) return;

            // Every dynamic Rigidbody (grids, dropped items, debris) re-expresses its
            // velocity in the new frame. Kinematic objects (chunks, LODs) are unaffected.
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Include))
            {
                if (rb == null || rb.isKinematic) continue;
                rb.linearVelocity += deltaMps;
            }

            // The player is a CharacterController, not a Rigidbody — adjust it directly.
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsInactive.Include))
            {
                if (pc == null) continue;
                pc.AddFrameVelocityDelta(deltaMps);
            }
        }

        /// <summary>Place every celestial body at its true scene position.</summary>
        private void PlaceBodies()
        {
            var reg = CosmicRegistry.Instance;
            if (reg == null || reg.SceneBodies == null) return;

            foreach (var kv in reg.SceneBodies)
            {
                var body = kv.Value;
                if (body == null) continue;
                double3 absolute = reg.CosmicPositionOf(kv.Key);
                body.transform.position = GetScenePos(absolute);
            }
        }

        /// <summary>Shift every registered scene root by a uniform delta (rebase / teleport).</summary>
        private void ShiftWorld(Vector3 delta)
        {
            if (delta.sqrMagnitude < 1e-9f) return;

            foreach (var root in _roots)
            {
                if (root == null) continue;
                root.position += delta;
            }

            // Rigidbody roots must keep physics in sync with their transforms.
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Include))
            {
                if (rb == null) continue;
                var t = rb.transform;
                if (t == null) continue;
                // Skip objects already shifted through a registered ancestor.
                if (IsUnderRegisteredRoot(t)) continue;
                t.position += delta;
            }

            // CharacterControllers (player) — already moved with their root, but a
            // transform move on a CC needs a physics sync to update its internal capsule.
            Physics.SyncTransforms();
        }

        private bool IsUnderRegisteredRoot(Transform t)
        {
            foreach (var root in _roots)
            {
                if (root == null) continue;
                if (t == root || t.IsChildOf(root)) return true;
            }
            return false;
        }

        /// <summary>
        /// Register roots that were created after bootstrap: grids, dropped items,
        /// scatter objects — anything with a Rigidbody or CharacterController that is
        /// not already under a registered root (bodies cover their chunk children).
        /// </summary>
        private void RegisterLateObjects()
        {
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Include))
            {
                if (rb == null || rb.transform == null) continue;
                if (IsUnderRegisteredRoot(rb.transform)) continue;
                RegisterRoot(rb.transform);
            }
            foreach (var cc in FindObjectsByType<CharacterController>(FindObjectsInactive.Include))
            {
                if (cc == null || cc.transform == null) continue;
                if (IsUnderRegisteredRoot(cc.transform)) continue;
                RegisterRoot(cc.transform);
            }
        }

        // ── Editor debug ───────────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            if (viewer == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(viewer.position, 2f);
        }
    }
}
