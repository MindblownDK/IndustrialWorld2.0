// Assets/Scripts/VoxelEngine/Player/PlayerSpawner.cs
//
// Coordinated spawn for fresh worlds, relogs, and deaths.
//
// Key invariants:
//   • The player's transform IS the world streamer's "viewer" — moving it forces the chunk
//     streamer to load the chunks around that position. So we MUST move the player to the
//     target spawn early (even if we can't release control yet) so chunks start streaming.
//   • While chunks load, we keep the CharacterController disabled to prevent falling-through.
//   • Player control is only enabled once the target chunk has a mesh collider.

using System.Collections;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Core;

namespace VoxelEngine.Player
{
    public class PlayerSpawner : MonoBehaviour
    {
        public static PlayerSpawner Instance { get; private set; }

        [Tooltip("Seconds to wait for the target chunk to gain a mesh collider before unfreezing the player anyway.")]
        public float maxWaitSeconds = 12f;
        [Tooltip("World-space radius to search around (0,0) when finding a fresh world-spawn.")]
        public int searchRadius = 32;
        [Tooltip("Maximum nearby terrain columns/directions tested when relocating an unsafe water spawn.")]
        [Range(8, 64)] public int drySpawnSearchAttempts = 24;

        private CharacterController _cc;
        private const float SpawnGroundClearance = 1.15f;
        private const float DrySeaClearance = 0.25f;
        private readonly RaycastHit[] _spawnRayHits = new RaycastHit[16];
        public  bool ReadyForPlayerControl { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _cc = GetComponent<CharacterController>();
        }

        private void Start()
        {
            var session = Menu.WorldSession.Instance;
            if (session != null) session.LoadSpawnSidecar();
            StartCoroutine(SpawnRoutine());
        }

        private IEnumerator SpawnRoutine()
        {
            ReadyForPlayerControl = false;

            // Give one frame for VoxelWorld / WorldStatePersistence to initialise.
            yield return null;

            var session = Menu.WorldSession.Instance;

            // 11.4 Offline survival — consume cryobed O₂ based on offline time.
            // This runs after WorldStatePersistence has restored placed cryobeds/biofarms.
            bool offlineDied = false;
            string offlineReason = "";

            OfflineSurvivalService.EnsureInstance();
            // Give one extra frame for grid blocks to restore from save before checking
            yield return null;

            try
            {
                if (OfflineSurvivalService.Instance != null)
                {
                    var offlineRes = OfflineSurvivalService.Instance.CheckOfflineSurvivalAndConsume();
                    if (offlineRes.hoursOffline > 0.01f)
                    {
                        Debug.Log($"[OfflineSurvival] {offlineRes.reason} (hadCryobed={offlineRes.hadCryobed}, hours={offlineRes.hoursOffline:0.0}, O2 consumed={offlineRes.oxygenConsumed:0})");
                        offlineReason = offlineRes.reason;
                        if (!offlineRes.survived)
                        {
                            offlineDied = true;
                            // Clear bed spawn so we fall back to world spawn
                            if (session != null)
                            {
                                session.hasBedSpawn = false;
                                session.SaveSpawnSidecar();
                            }
                            // Clear offline file so we don't re-apply death on next load
                            OfflineSurvivalService.Instance.ClearOfflineFile();
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[OfflineSurvival] Check exception: " + ex.Message);
            }

            bool hasSavedPos = TryReadSavedPlayerPosition(out Vector3 savedPos);
            // If we died offline, ignore saved pos — force world spawn path
            if (offlineDied) hasSavedPos = false;

            // Real-space safety (7.13.6): a save written during a launch/bug session can
            // restore the player INSIDE a planet (or so close to a core that they spawn
            // in solid terrain). Those saves are poisoned — reject them and fall back to
            // the surface/bed spawn path. Legit surface/orbit/deep-space saves pass.
            if (hasSavedPos && IsSavedPositionInsideBody(savedPos))
            {
                Debug.LogWarning("[PlayerSpawner] Saved position is inside a celestial body — rejecting poisoned save, spawning on the surface instead.");
                hasSavedPos = false;
            }

            // Determine the target position.
            Vector3 target;
            bool isFreshWorld = false;

            if (hasSavedPos)
            {
                target = savedPos;
                Debug.Log("[PlayerSpawner] Restoring saved player position: " + target);
            }
            else if (session != null && session.hasBedSpawn)
            {
                target = session.bedSpawnPoint;
                Debug.Log("[PlayerSpawner] Bed spawn: " + target);
                // A bed saved during the launch-era can also be a stale space coordinate.
                if (!IsValidSurfaceDestination(target, out Vector3 safeBed))
                {
                    Debug.LogWarning("[PlayerSpawner] Bed spawn invalid (in space / inside planet) — using a fresh surface point instead.");
                    target = safeBed;
                    session.hasBedSpawn = false;   // don't loop back to the poisoned bed
                }
            }
            else
            {
                isFreshWorld = true;
                // Fresh world. If a sphere body is active, spawn on its surface; else flat origin.
                var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
                if (body != null)
                {
                    // Choose one land target analytically before colliders exist. The old
                    // Physics-only scan ran before chunks had streamed, then the wet-spawn
                    // fallback visibly moved the player through many candidate locations.
                    bool foundLand = false;
                    Vector3 bestSpawn = body.transform.position + body.transform.up * (body.SurfaceRadius + 30f);
                    if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphereWorld &&
                        sphereWorld.TryFindDrySpawnPoint(drySpawnSearchAttempts, out Vector3 analyticGround))
                    {
                        bestSpawn = analyticGround + body.UpAt(analyticGround) * SpawnGroundClearance;
                        foundLand = true;
                    }

                    // Keep a small collider fallback for malformed/custom density assets, but
                    // only after the deterministic path declined to provide land.
                    if (!foundLand)
                    {
                        Vector3 equator = new Vector3(1f, 0f, 0f);
                        int initialSamples = Mathf.Min(8, Mathf.Max(4, drySpawnSearchAttempts));
                        for (int i = 0; i < initialSamples; i++)
                        {
                            float angle = i * (360f / initialSamples);
                            Vector3 sampleDir = Quaternion.AngleAxis(angle, body.transform.up) * (equator + body.transform.up * 0.55f);
                            sampleDir = math.normalizesafe(sampleDir, body.transform.up);
                            Vector3 rayFrom = body.transform.position + sampleDir * (body.SurfaceRadius + 250f);
                            if (!Physics.Raycast(rayFrom, -sampleDir, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            if (Vector3.Distance(hit.point, body.transform.position) <= body.SeaRadius + 3f) continue;
                            Vector3 candidate = hit.point + sampleDir * SpawnGroundClearance;
                            if (IsSpawnInWater(candidate)) continue;
                            bestSpawn = candidate;
                            foundLand = true;
                            break;
                        }
                    }

                    target = bestSpawn;
                    Debug.Log("[PlayerSpawner] Fresh SPHERE world — selected one dry surface target at " + target);
                }
                else
                {
                    target = new Vector3(0, 250, 0);
                    Debug.Log("[PlayerSpawner] Fresh FLAT world — placing player above origin to trigger chunk streaming.");
                }
            }

            // Park the CharacterController-disabled player at the (X,Z) of target with a HIGH Y.
            // This forces VoxelWorld's streamer to start loading chunks around the spawn site.
            DisableController();

            // Frame preparation for the spawn target (covers saved positions, beds, and
            // space beds): pin the scene frame + streaming to the destination's dominant
            // body WITHOUT a velocity delta, and suppress auto frame switches until the
            // player is at rest and in control. Prevents any spawn-time sideways kick.
            var spawnOrigin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            if (spawnOrigin != null) spawnOrigin.suppressAutoFrameSwitches = true;
            PrepareRespawnFrame(target);
            // On a sphere, DON'T force Y to 250 — that would park the player far above the
            // body's surface (which could be at Y=700+). Use the target Y directly so chunks
            // around the surface start streaming immediately.
            float parkY = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null
                          ? target.y
                          : Mathf.Max(target.y, 250f);
            SetPosition(new Vector3(target.x, parkY, target.z));

            // A saved high-altitude/space location has no terrain column to wait for.
            // Waiting there would unnecessarily freeze control before an orbital logout
            // — this includes REAL-SPACE deep-space logouts (no active body at all).
            var activeBody = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            bool savedInSpace = hasSavedPos && (VoxelEngine.Cosmos.GravityProvider.IsDeepSpace
                || (activeBody != null
                    && Vector3.Distance(target, activeBody.transform.position) > activeBody.SurfaceRadius + 80f));
            if (!savedInSpace)
                yield return WaitForChunkAt(VoxelCoordOf(target), maxWaitSeconds);

            // Saved positions near terrain can be from an older build that wrote the
            // controller slightly inside the voxel surface. Lift them to the first
            // surface below the player before enabling the CharacterController.
            if (hasSavedPos && !savedInSpace)
            {
                target = LiftSavedPositionOutOfGround(target);
                SetPosition(target);
            }

            // For flat fresh worlds, find the actual top-of-ground position.
            // We intentionally compute the ground target BEFORE snapping but DEFER saving
            // worldSpawn until AFTER the final raycast snap, so worldSpawnPoint always
            // equals the actual walkable ground, never the 0,250,0 parking placeholder.
            bool isSphere = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null;
            if (isFreshWorld && !isSphere)
            {
                Vector3 ground = FindFreshSpawnNearby(target);
                target = ground;
                // Don't save yet — wait until after final SnapToGround for accuracy.
            }

            // A saved position is authoritative: it can be on terrain, in atmosphere,
            // or in space. Ground snapping it would destroy valid sky/orbit logout state.
            bool snapped = hasSavedPos;
            if (!hasSavedPos)
            {
                // Snap a fresh/bed spawn to actual ground via raycast — keep retrying until we get a real hit,
                // because the mesh collider may take a frame or two to activate after the chunk
                // mesh is uploaded.
                float groundT0 = Time.time;
                while (!snapped && Time.time - groundT0 < 5f)
                {
                    // On a sphere, raycast RADIAL-DOWN (toward the body core) instead of world-down.
                    var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
                    Vector3 from, dir, lift;
                    if (body != null)
                    {
                        Vector3 bup = body.UpAt(transform.position);
                        from = transform.position + bup * 100f;
                        dir  = -bup;
                        lift = bup;
                    }
                    else
                    {
                        from = new Vector3(target.x, target.y + 100f, target.z);
                        dir  = Vector3.down;
                        lift = Vector3.up;
                    }
                    if (Physics.Raycast(from, dir, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        SetPosition(hit.point + lift * SpawnGroundClearance);
                        snapped = true;
                        break;
                    }
                    yield return null;
                }
                if (!snapped)
                {
                    Debug.LogWarning("[PlayerSpawner] Could not raycast to ground after 5s — placing at target surface.");
                    SetPosition(target);
                }
            }

            // One more frame to let physics settle, then run one final terrain-lift
            // pass at the exact release pose. This prevents the CharacterController
            // from enabling while its feet are still intersecting a late-updated mesh.
            yield return null;
            SetPosition(LiftSavedPositionOutOfGround(transform.position));
            yield return null;

            // A save, bed, or fresh-world target must never release the player into
            // a water volume. When the selected column is wet, relocate while the
            // controller remains disabled and chunks continue streaming.
            if (!savedInSpace)
                yield return EnsureDrySpawn(transform.position);

            // For any fresh-world first spawn (flat OR sphere), persist the FINAL grounded
            // position as the true world spawn. This fixes the 0,250,0 bug where the death
            // screen and initial spawn fell back to the parking placeholder instead of the
            // computed safe ground.
            if (isFreshWorld && session != null)
            {
                // If we snapped to ground, transform.position is now the real ground.
                // If we couldn't snap, fall back to the best target we computed.
                Vector3 finalSpawn = transform.position;
                if (finalSpawn.y < -1000f || finalSpawn.y > 200000f) finalSpawn = target;
                // Only override if we have a sane position and haven't already initialized
                // during this same run, or if the old stored spawn was still the default.
                bool shouldSave = !session.worldSpawnInitialized ||
                                  session.worldSpawnPoint.sqrMagnitude < 0.1f ||
                                  (Mathf.Abs(session.worldSpawnPoint.x) < 0.1f &&
                                   Mathf.Abs(session.worldSpawnPoint.z) < 0.1f &&
                                   session.worldSpawnPoint.y >= 249f && session.worldSpawnPoint.y <= 251f);
                if (shouldSave)
                {
                    session.RecordWorldSpawn(finalSpawn, VoxelEngine.Cosmos.GravityProvider.ActiveBody);
                    session.SaveSpawnSidecar();
                    Debug.Log("[PlayerSpawner] World spawn initialized at " + finalSpawn +
                              (string.IsNullOrEmpty(session.worldSpawnBodyName) ? "" :
                               $" (anchored to '{session.worldSpawnBodyName}')"));
                }
            }

            // Load-position assurance (9.5.4): a restored position hanging in open space
            // over a body (frame drift between sessions — "loaded the world and spawned
            // above the planet") is pulled down to the surface below. Genuine deep-space
            // saves (no gravity frame) are untouched.
            var frameBody = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (frameBody != null && transform.parent == null)   // seated/grid players are exempt
            {
                float altitude = Vector3.Distance(transform.position, frameBody.transform.position)
                                 - frameBody.SurfaceRadius;
                if (altitude > 300f && altitude < 100000f)
                {
                    Debug.LogWarning($"[PlayerSpawner] Restored position hangs {altitude:0} m above " +
                                     $"'{frameBody.DisplayName}' — snapping to the surface below.");
                    Vector3 upLoad = frameBody.UpAt(transform.position);
                    Vector3 radialGround = frameBody.transform.position +
                                           upLoad * (frameBody.SurfaceRadius + SpawnGroundClearance);
                    SetPosition(SnapToGround(radialGround));
                    yield return WaitForChunkAt(VoxelCoordOf(transform.position), 8f);
                    SetPosition(SnapToGround(transform.position));
                }
            }

            EnableController();
            // Spawn must start at REST — clear any residual velocity (frame deltas,
            // stale physics) so the player can never be "launched" by old state.
            ZeroPlayerVelocity();
            var pcSpawn = GetComponent<PlayerController>();
            if (pcSpawn != null) pcSpawn.BeginSpawnGrace();
            ReadyForPlayerControl = true;
            // Re-enable automatic frame switches now that the player is at rest in the
            // correct frame.
            var originEnd = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            if (originEnd != null) originEnd.suppressAutoFrameSwitches = false;
            if (!VoxelEngine.UI.UIState.IsBlocking)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Debug.Log("[PlayerSpawner] Player control enabled at " + transform.position);

            // 11.4 Offline death — if we died while offline, kill player now and show death screen
            if (offlineDied)
            {
                // Give one frame for UI to mount
                yield return null;
                var stats = GetComponent<PlayerStats>();
                if (stats != null)
                {
                    // Show feedback before death screen
                    VoxelEngine.UI.BuildFeedbackHud.Show("Offline Death", offlineReason, null, new Color(0.95f, 0.25f, 0.20f));
                    // Delay death by 0.5s so feedback is readable
                    yield return new WaitForSeconds(0.6f);
                    stats.TakeDamage(9999f); // will trigger Die() -> DeathScreen with world spawn
                }
            }
        }

        private void OnDisable()
        {
            // Save while the player still owns a valid transform and Inventory. This
            // runs before scene teardown, unlike persistence-manager destruction.
            if (ReadyForPlayerControl)
                VoxelEngine.Persistence.WorldStatePersistence.Instance?.SaveAll();
        }

        // ============================================================
        //                     DEATH RESPAWN
        // ============================================================
        public void Respawn()
        {
            var session = Menu.WorldSession.Instance;
            Vector3 dest;
            if (session != null)
            {
                // Prefer the explicit linked bed spawn; otherwise the world spawn is
                // reconstructed from its BODY ANCHOR (9.2.0): the stored scene point goes
                // stale whenever the floating origin re-anchors (orbits, planet hops),
                // which used to respawn the player in empty space. The body-local offset
                // is transformed by the body's CURRENT transform instead.
                if (session.hasBedSpawn) dest = session.bedSpawnPoint;
                else if (session.TryResolveWorldSpawn(out Vector3 resolvedSpawn)) dest = resolvedSpawn;
                else dest = transform.position;

                // 9.4.0 — a world spawn WITHOUT a body anchor is never trusted: raw
                // scene points go stale with every floating-origin re-anchor (THE
                // "respawn in space" bug, final form). Also reject any anchored point
                // that still resolves to open space. Either way: compute a fresh dry
                // surface point on the active world and heal the save.
                bool anchorMissing = string.IsNullOrEmpty(session.worldSpawnBodyName);
                if (!session.hasBedSpawn && (anchorMissing || IsOpenSpacePosition(dest)))
                {
                    // Heal path A: the streamed world can search a dry point (only valid
                    // while its body is assigned — dying in deep space leaves it null).
                    bool healed = false;
                    if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphereWorld &&
                        sphereWorld.body != null &&
                        sphereWorld.TryFindDrySpawnPoint(drySpawnSearchAttempts, out Vector3 freshGround))
                    {
                        dest = freshGround;
                        healed = true;
                    }
                    // Heal path B (9.5.0): ANALYTIC spawn on the HOME body straight from
                    // PlanetField — needs no streamed chunks and works from any frame
                    // (deep-space deaths, other-planet deaths).
                    else
                    {
                        var home = VoxelEngine.Cosmos.GravityProvider.ActiveBody
                                   ?? (VoxelEngine.Cosmos.CosmosBootstrap.Instance != null
                                       ? VoxelEngine.Cosmos.CosmosBootstrap.Instance.HomeBody : null);
                        if (TryComputeAnalyticSpawn(home, out Vector3 analytic))
                        {
                            dest = analytic;
                            healed = true;
                        }
                    }

                    if (healed)
                    {
                        Debug.LogWarning("[PlayerSpawner] World spawn was " +
                                         (anchorMissing ? "un-anchored (legacy save)" : "resolving to open space") +
                                         " — recomputed a fresh surface spawn and healed the save. dest=" + dest);
                        session.RecordWorldSpawn(dest, VoxelEngine.Cosmos.GravityProvider.ActiveBody);
                        session.SaveSpawnSidecar();
                    }
                    else
                    {
                        Debug.LogError("[PlayerSpawner] Could not heal the world spawn (no usable body) — dest=" + dest);
                    }
                }
                Debug.Log($"[PlayerSpawner] Respawn → dest={dest} anchor='{session.worldSpawnBodyName}' bed={session.hasBedSpawn}");
            }
            else
            {
                dest = new Vector3(0, 250, 0);
            }
            StartCoroutine(RespawnRoutine(dest, allowSpaceDestination: session != null && session.hasBedSpawn));
        }

        /// <summary>
        /// Analytic surface spawn straight from the planetary field — no chunks, no
        /// colliders, valid from any reference frame. Scans deterministic directions
        /// for dry land (surface above sea) and returns a point just above it.
        /// </summary>
        private static bool TryComputeAnalyticSpawn(VoxelEngine.Cosmos.CelestialBody body, out Vector3 scenePos)
        {
            scenePos = default;
            if (body == null || body.settings == null) return false;
            var prm = body.genParams;
            if (prm.radiusWorld < 10f) return false;

            var rng = new System.Random(prm.seed ^ 0x5F3759DF);
            for (int i = 0; i < 64; i++)
            {
                var d = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
                if (d.sqrMagnitude < 0.01f) continue;
                d.Normalize();

                float surf = VoxelEngine.GpuVoxel.PlanetField.SurfaceRadius(
                    prm.seed, new Unity.Mathematics.float3(d.x, d.y, d.z),
                    prm.radiusWorld, prm.baseHeight, prm.seaRadius,
                    prm.continentScaleDir, prm.mountainScale);
                if (surf <= prm.seaRadius + 2f) continue;   // wet — keep looking

                scenePos = body.transform.position + d * (surf + SpawnGroundClearance);
                return true;
            }
            return false;
        }

        /// <summary>
        /// True when a scene position is far above EVERY celestial body's surface
        /// (more than 800 m of altitude everywhere) — i.e. genuinely in open space.
        /// </summary>
        private static bool IsOpenSpacePosition(Vector3 scenePos)
        {
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (registry == null || registry.SceneBodies == null) return false;
            foreach (var kv in registry.SceneBodies)
            {
                var body = kv.Value;
                if (body == null) continue;
                float altitude = Vector3.Distance(scenePos, body.transform.position) - body.SurfaceRadius;
                if (altitude < 800f) return false;
            }
            return true;
        }

        public void RespawnAt(Vector3 destination)
        {
            // Explicit destinations (beds, cryobeds, stations) may be in space by design.
            StartCoroutine(RespawnRoutine(destination, allowSpaceDestination: true));
        }

        private IEnumerator RespawnRoutine(Vector3 dest, bool allowSpaceDestination = false)
        {
            ReadyForPlayerControl = false;
            DisableController();

            // Death-loop breaker: only destinations INSIDE a planet are rejected (a save
            // written mid-launch). Spawns in space are intentional — a bed / cryobed in
            // orbit or on a station must spawn you next to it (design decision).
            if (!IsValidSurfaceDestination(dest, out Vector3 safeDest))
            {
                Debug.LogWarning("[PlayerSpawner] Respawn destination is inside a planet — respawning on a fresh surface point instead: " + dest);
                dest = safeDest;
            }

            // Respawn frame preparation: pin the scene frame to the destination's dominant
            // body (or deep space) WITHOUT a velocity delta, and suppress automatic frame
            // switches until control handover. This is what stopped the sideways launch:
            // a frame switch landing between the teleport and velocity-zeroing used to
            // kick the freshly-respawned player with the frame-velocity delta.
            var spawnOrigin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            if (spawnOrigin != null) spawnOrigin.suppressAutoFrameSwitches = true;
            PrepareRespawnFrame(dest);

            // Mirror the first-spawn routine: on a spherical body the parked position
            // must sit at the TRUE destination height (9.5.2: the legacy flat-world
            // "park at Y ≥ 250" trick parked deep-space deaths on a nonsense plane —
            // the destination knows its own height; park slightly above it instead).
            Vector3 parkUp = Vector3.up;
            var destBody = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (destBody != null) parkUp = destBody.UpAt(dest);
            SetPosition(dest + parkUp * 12f);
            Debug.Log($"[PlayerSpawner] Respawn parked at {transform.position} (dest {dest}), waiting for chunks…");
            yield return WaitForChunkAt(VoxelCoordOf(dest), 8f);
            SetPosition(SnapToGround(dest));
            yield return null;
            yield return null;
            SetPosition(SnapToGround(transform.position));
            yield return EnsureDrySpawn(transform.position);

            // Collider assurance (9.5.4 — "went through the ground on respawn"): hold the
            // handover until REAL ground collision exists under the player (mesh collider
            // baking lags mesh visuals by a few frames under streaming load).
            float colliderWait = 6f;
            while (colliderWait > 0f)
            {
                Vector3 upNow = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null
                    ? VoxelEngine.Cosmos.GravityProvider.ActiveBody.UpAt(transform.position)
                    : Vector3.up;
                if (Physics.Raycast(transform.position + upNow * 40f, -upNow, out var groundHit,
                                    400f, ~0, QueryTriggerInteraction.Ignore) &&
                    groundHit.collider.GetComponentInParent<VoxelEngine.Cosmos.PlanetSafetyCollider>() == null)
                {
                    SetPosition(groundHit.point + upNow * SpawnGroundClearance);
                    break;
                }
                colliderWait -= 0.25f;
                yield return new WaitForSeconds(0.25f);
            }

            // Final clamp (9.5.2): if everything above still left us floating in open
            // space (chunk wait timeout, failed snap), force the analytic surface point.
            // Bed/station spawns may be in space by design and are never clamped.
            if (!allowSpaceDestination && IsOpenSpacePosition(transform.position))
            {
                var clampBody = VoxelEngine.Cosmos.GravityProvider.ActiveBody
                                ?? (VoxelEngine.Cosmos.CosmosBootstrap.Instance != null
                                    ? VoxelEngine.Cosmos.CosmosBootstrap.Instance.HomeBody : null);
                if (TryComputeAnalyticSpawn(clampBody, out Vector3 clamped))
                {
                    Debug.LogWarning("[PlayerSpawner] Respawn ended in open space — hard-clamped to analytic surface " + clamped);
                    SetPosition(clamped);
                }
            }
            Debug.Log($"[PlayerSpawner] Respawn complete at {transform.position}");
            EnableController();
            ZeroPlayerVelocity();
            // Brief fall-damage grace so the physics settle at spawn can never insta-kill.
            var pc = GetComponent<PlayerController>();
            if (pc != null) pc.BeginSpawnGrace();
            ReadyForPlayerControl = true;
            // Re-enable automatic frame switches now that control has fully handed over
            // and the player is at rest in the correct frame.
            var originEnd = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            if (originEnd != null) originEnd.suppressAutoFrameSwitches = false;
        }

        /// <summary>
        /// Pins the scene reference frame to the dominant body at a scene position (or the
        /// star frame in deep space) so a respawn/teleport starts at rest relative to where
        /// the player is actually going. Uses SetFrame directly — no velocity delta — and
        /// forces the streaming systems onto the destination body.
        /// </summary>
        private void PrepareRespawnFrame(Vector3 sceneDest)
        {
            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return;

            double3 cosmic = origin.GetCosmicKm(sceneDest);
            var dominant = registry.GetDominantBody(cosmic, out _);
            VoxelEngine.Cosmos.CelestialBody frame = null;
            if (dominant != null) registry.SceneBodies.TryGetValue(dominant, out frame);

            origin.SetFrame(frame);
            var bootstrap = VoxelEngine.Cosmos.CosmosBootstrap.Instance;
            if (bootstrap != null) bootstrap.ForceStreamingBody(frame);
        }

        /// <summary>
        /// True when a scene position is a usable spawn destination: NOT inside a planet
        /// (a launch-era save can bury you in solid terrain). Positions in space are VALID —
        /// a bed/cryobed in orbit or on a station must spawn you next to it. When invalid,
        /// outputs a fresh deterministic dry surface spawn.
        /// </summary>
        private bool IsValidSurfaceDestination(Vector3 scenePos, out Vector3 safePoint)
        {
            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;

            // Check EVERY celestial body, not just the active one (a destination near
            // another planet's core must also be rejected).
            if (origin != null && registry != null && registry.IsReady)
            {
                double3 cosmic = origin.GetCosmicKm(scenePos);
                foreach (var kv in registry.SceneBodies)
                {
                    if (kv.Key == null || kv.Key.settings == null || kv.Value == null) continue;
                    double3 bodyCosmic = registry.CosmicPositionOf(kv.Key);
                    double d = math.length(bodyCosmic - cosmic);
                    if (d < kv.Key.settings.radiusKm * 0.95d)
                    {
                        // Recompute a fresh surface point on THIS body from the density field.
                        var body = kv.Value;
                        Vector3 ground = body.transform.position + body.transform.up * (body.SurfaceRadius + 30f);
                        if (VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphereWorld &&
                            sphereWorld.TryFindDrySpawnPoint(drySpawnSearchAttempts, out Vector3 analyticGround))
                        {
                            ground = analyticGround;
                        }
                        safePoint = ground + body.UpAt(ground) * SpawnGroundClearance;
                        return false;
                    }
                }
            }

            safePoint = scenePos;
            return true;
        }

        /// <summary>
        /// True when a saved scene position would restore the player INSIDE a celestial
        /// body (distance from any body's core below its surface radius). Such saves are
        /// the result of launch/tunnel bugs and must never be restored as-is.
        /// </summary>
        private static bool IsSavedPositionInsideBody(Vector3 scenePos)
        {
            var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
            var registry = VoxelEngine.Cosmos.CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return false;

            double3 cosmic = origin.GetCosmicKm(scenePos);
            foreach (var kv in registry.SceneBodies)
            {
                if (kv.Key == null || kv.Value == null || kv.Key.settings == null) continue;
                double3 bodyCosmic = registry.CosmicPositionOf(kv.Key);
                double d = math.length(bodyCosmic - cosmic);
                double rSurface = kv.Key.settings.radiusKm;
                // Strictly inside the crust (5% margin below the surface).
                if (d < rSurface * 0.95d) return true;
            }
            return false;
        }

        /// <summary>Brings the player to a full stop before control handover.</summary>
        private void ZeroPlayerVelocity()
        {
            var pc = GetComponent<PlayerController>();
            if (pc != null) pc.ResetVelocity();
            var rb = GetComponentInChildren<Rigidbody>();
            if (rb != null) rb.linearVelocity = Vector3.zero;
        }

        // ============================================================
        //                       HELPERS
        // ============================================================
        private bool TryReadSavedPlayerPosition(out Vector3 pos)
        {
            pos = default;
            var session = Menu.WorldSession.Instance;
            if (session == null) return false;
            string path = System.IO.Path.Combine(
                Application.persistentDataPath, "VoxelWorlds", session.worldName, "world_state.json");
            if (!System.IO.File.Exists(path)) return false;
            try
            {
                string txt = System.IO.File.ReadAllText(path);
                // Verbatim string: doubled "" for literal quotes, \{ is a regex-escaped brace.
                const string pattern = @"""player""\s*:\s*\{\s*""pos""\s*:\s*\{\s*""x""\s*:\s*(-?[0-9.eE+-]+)\s*,\s*""y""\s*:\s*(-?[0-9.eE+-]+)\s*,\s*""z""\s*:\s*(-?[0-9.eE+-]+)";
                var m = System.Text.RegularExpressions.Regex.Match(txt, pattern);
                if (!m.Success) return false;
                float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                pos = new Vector3(x, y, z);
                if (!IsSafeSavedPosition(pos))
                {
                    Debug.LogWarning("[PlayerSpawner] Ignored an invalid saved player position: " + pos);
                    pos = default;
                    return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Rejects corrupt coordinates before they can freeze streaming.
        /// Legitimate high-atmosphere and space positions remain valid.</summary>
        private static bool IsSafeSavedPosition(Vector3 pos)
        {
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)
                || float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
                return false;

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null) return Mathf.Abs(pos.x) < 100000f && Mathf.Abs(pos.y) < 100000f && Mathf.Abs(pos.z) < 100000f;

            float radialDistance = Vector3.Distance(pos, body.transform.position);
            // A player may legitimately log out in atmosphere, orbit, or deep space.
            // Only positions inside the solid planetary body are invalid; do not clamp
            // high-altitude positions back to the surface.
            return radialDistance >= body.SurfaceRadius * 0.70f;
        }

        private static Vector3Int VoxelCoordOf(Vector3 pos)
            => new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));

        private IEnumerator WaitForChunkAt(Vector3Int worldVoxel, float timeoutSec)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) yield break;

            // CRITICAL: convert the world-space voxel position to chunk coords via the WORLD's
            // own WorldToChunk method. On a flat world this is just position/chunkSize, but on a
            // SPHERE the body is offset/rotated, so the chunk dictionary uses BODY-LOCAL coords.
            // Computing chunk coords from raw world voxel position gives wrong keys → timeout.
            Vector3 worldPos = new Vector3(worldVoxel.x, worldVoxel.y, worldVoxel.z);
            Vector3Int centerChunk = world.WorldToChunk(worldPos);

            float t0 = Time.time;
            while (Time.time - t0 < timeoutSec)
            {
                bool anyMeshed = false;
                // Search ±4 chunks vertically (covers the surface ±128 voxels).
                for (int dy = -4; dy <= 4; dy++)
                {
                    var checkCoord = new Vector3Int(centerChunk.x, centerChunk.y + dy, centerChunk.z);
                    if (world.TryGetChunk(checkCoord, out var c)
                        && c != null && c.isGenerated
                        && c.meshCollider != null && c.meshCollider.sharedMesh != null)
                    {
                        anyMeshed = true; break;
                    }
                }
                if (anyMeshed) yield break;
                yield return null;
            }
            Debug.LogWarning($"[PlayerSpawner] Timed out waiting for chunks at {centerChunk}.");
        }

        private void DisableController() { if (_cc != null) _cc.enabled = false; }
        private void EnableController()  { if (_cc != null) _cc.enabled = true;  }
        private void SetPosition(Vector3 p) { transform.position = p; }

        /// <summary>
        /// Ensures a fresh, bed, saved, or respawn location never releases the
        /// player inside water. The controller stays disabled while candidate chunks
        /// stream and each candidate is raycast onto dry, walkable ground.
        /// </summary>
        private IEnumerator EnsureDrySpawn(Vector3 preferred)
        {
            if (!IsSpawnInWater(transform.position)) yield break;

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body != null && VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld sphereWorld &&
                sphereWorld.TryFindDrySpawnPoint(drySpawnSearchAttempts, out Vector3 analyticGround))
            {
                // One deterministic relocation replaces the old visible sequence of trial
                // teleports. Keep the controller disabled while this single target streams.
                Vector3 analyticSpawn = analyticGround + body.UpAt(analyticGround) * SpawnGroundClearance;
                SetPosition(GetSpawnParkingPosition(analyticSpawn));
                yield return WaitForChunkAt(VoxelCoordOf(analyticSpawn), 3f);
                if (TryFindDryGround(analyticSpawn, out Vector3 dryGround)) analyticSpawn = dryGround;
                SetPosition(analyticSpawn);
                PersistDrySpawnRelocation(preferred, analyticSpawn);
                Debug.Log("[PlayerSpawner] Replaced wet spawn with one deterministic dry surface target at " + analyticSpawn);
                yield break;
            }
            if (body != null)
            {
                // A fully oceanic/custom body has no density-confirmed dry land. Do not make
                // the player watch a long sequence of trial teleports; hold one safe point
                // above the selected water column and report the authoring issue once.
                Vector3 surfaceUp = body.UpAt(preferred);
                SetPosition(preferred + (surfaceUp.sqrMagnitude > 0.0001f ? surfaceUp.normalized : Vector3.up) * 4f);
                Debug.LogError("[PlayerSpawner] No dry spherical spawn was found in the authored density field; held player above the selected water column without relocation hopping.");
                yield break;
            }

            int attempts = Mathf.Max(8, drySpawnSearchAttempts);
            Debug.LogWarning("[PlayerSpawner] Selected spawn is wet; searching nearby dry ground.");
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector3 candidate = GetDrySpawnCandidate(preferred, attempt);
                if (attempt > 0)
                {
                    SetPosition(GetSpawnParkingPosition(candidate));
                    yield return WaitForChunkAt(VoxelCoordOf(candidate), 1.5f);
                }

                if (!TryFindDryGround(candidate, out Vector3 dryGround)) continue;
                SetPosition(dryGround);
                yield return null;
                if (IsSpawnInWater(transform.position)) continue;

                PersistDrySpawnRelocation(preferred, transform.position);
                Debug.Log("[PlayerSpawner] Relocated wet spawn to dry ground at " + transform.position);
                yield break;
            }

            // A fully oceanic body has no valid terrain candidate. Keep the player
            // above the water rather than deliberately placing them inside it and
            // leave a clear error for world-authoring diagnostics.
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null
                ? VoxelEngine.Cosmos.GravityProvider.ActiveBody.UpAt(preferred)
                : Vector3.up;
            SetPosition(preferred + up * 4f);
            Debug.LogError("[PlayerSpawner] No dry spawn terrain was found after the bounded safety search. Player was held above the selected water column; add reachable land to this world.");
        }

        private Vector3 GetDrySpawnCandidate(Vector3 preferred, int attempt)
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null)
            {
                if (attempt == 0) return preferred;
                int n = attempt - 1;
                float ring = Mathf.Min(Mathf.Max(4f, searchRadius), 4f + (n / 8 + 1) * 4f);
                float angle = n * 137.507764f * Mathf.Deg2Rad;
                return new Vector3(
                    preferred.x + Mathf.Cos(angle) * ring,
                    Mathf.Max(preferred.y, 250f),
                    preferred.z + Mathf.Sin(angle) * ring);
            }

            if (attempt == 0) return preferred;
            Vector3 center = body.transform.position;
            Vector3 preferredDirection = preferred - center;
            if (preferredDirection.sqrMagnitude < 0.0001f) preferredDirection = body.transform.up;
            preferredDirection.Normalize();

            Vector3 reference = Mathf.Abs(Vector3.Dot(preferredDirection, body.transform.up)) > 0.9f
                ? body.transform.right
                : body.transform.up;
            Vector3 tangentA = Vector3.Cross(reference, preferredDirection).normalized;
            Vector3 tangentB = Vector3.Cross(preferredDirection, tangentA).normalized;
            int sample = attempt - 1;
            float polar = Mathf.Min(1.30f, 0.10f + 0.14f * Mathf.Sqrt(sample + 1f));
            float azimuth = sample * 2.39996323f;
            Vector3 ringDirection = tangentA * Mathf.Cos(azimuth) + tangentB * Mathf.Sin(azimuth);
            Vector3 direction = (preferredDirection * Mathf.Cos(polar) + ringDirection * Mathf.Sin(polar)).normalized;
            return center + direction * (body.SurfaceRadius + 40f);
        }

        private Vector3 GetSpawnParkingPosition(Vector3 candidate)
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null) return new Vector3(candidate.x, Mathf.Max(candidate.y, 250f), candidate.z);
            Vector3 up = body.UpAt(candidate);
            return body.transform.position + up * (body.SurfaceRadius + 40f);
        }

        private void PersistDrySpawnRelocation(Vector3 previousSpawn, Vector3 drySpawn)
        {
            var session = Menu.WorldSession.Instance;
            if (session == null) return;

            // The original saved/bed/world target may differ from the snapped feet
            // position by the controller clearance, so use a modest tolerance.
            const float MatchDistance = 10f;
            bool changed = false;
            if (session.hasBedSpawn && Vector3.Distance(session.bedSpawnPoint, previousSpawn) <= MatchDistance)
            {
                session.bedSpawnPoint = drySpawn;
                changed = true;
            }
            else if (session.worldSpawnInitialized && Vector3.Distance(session.worldSpawnPoint, previousSpawn) <= MatchDistance)
            {
                session.RecordWorldSpawn(drySpawn, VoxelEngine.Cosmos.GravityProvider.ActiveBody);
                changed = true;
            }

            if (changed) session.SaveSpawnSidecar();
        }

        private bool TryFindDryGround(Vector3 candidate, out Vector3 dryGround)
        {
            dryGround = candidate;
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            Vector3 up = body != null ? body.UpAt(candidate) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 origin = candidate + up * 120f;
            float rayDistance = body != null ? 320f : 512f;
            int count = Physics.RaycastNonAlloc(origin, -up, _spawnRayHits, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = _spawnRayHits[i];
                if (hit.collider == null || IsOwnCollider(hit.collider) || IsLiquidSurfaceCollider(hit.collider)) continue;
                if (Vector3.Dot(hit.normal, up) < 0.12f || hit.distance >= nearest) continue;

                Vector3 testSpawn = hit.point + up * SpawnGroundClearance;
                if (IsSpawnInWater(testSpawn)) continue;

                nearest = hit.distance;
                dryGround = testSpawn;
            }
            return nearest < float.PositiveInfinity;
        }

        private bool IsSpawnInWater(Vector3 feetPosition)
        {
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            Vector3 up = body != null ? body.UpAt(feetPosition) : Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            if (body != null && Vector3.Distance(feetPosition, body.transform.position) <= body.SeaRadius + DrySeaClearance)
                return true;

            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return false;
            float height = Mathf.Max(_cc != null ? _cc.height : 1.85f, 1.85f);
            for (int i = 0; i <= 4; i++)
            {
                Vector3 sample = feetPosition + up * (height * i / 4f + 0.05f);
                if (IsLiquidVoxel(world.GetVoxelWorld(world.WorldToVoxel(sample)))) return true;
            }
            return false;
        }

        private bool IsOwnCollider(Collider collider)
        {
            if (collider == null) return false;
            return collider.transform == transform || collider.transform.IsChildOf(transform) || transform.IsChildOf(collider.transform);
        }

        private static bool IsLiquidSurfaceCollider(Collider collider)
        {
            if (collider == null) return false;
            string name = collider.gameObject.name;
            return name.IndexOf("LiquidSurface", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("WaterSurface", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Ocean", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLiquidVoxel(Voxel voxel)
        {
            return voxel.waterLevel > 0
                || voxel.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel
                || voxel.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid
                || voxel.material == (byte)VoxelEngine.Materials.MaterialId.CrudeOil;
        }

        /// <summary>
        /// Raycast straight down from above to find the top of the meshed terrain.
        /// Returns target unchanged if nothing is hit.
        /// </summary>
        private Vector3 SnapToGround(Vector3 target)
        {
            if (TryFindDryGround(target, out Vector3 dryGround)) return dryGround;

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            Vector3 from, dir, lift;
            if (body != null)
            {
                Vector3 bup = body.UpAt(target);
                from = target + bup * 100f;
                dir  = -bup;
                lift = bup;
            }
            else
            {
                from = new Vector3(target.x, target.y + 100f, target.z);
                dir  = Vector3.down;
                lift = Vector3.up;
            }
            if (Physics.Raycast(from, dir, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + lift * SpawnGroundClearance;
            return target + lift * 0.5f;
        }

        private Vector3 LiftSavedPositionOutOfGround(Vector3 saved)
        {
            Vector3 up;
            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body != null) up = body.UpAt(saved);
            else up = Vector3.up;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 from = saved + up * 8f;
            if (Physics.Raycast(from, -up, out var hit, 16f, ~0, QueryTriggerInteraction.Ignore))
            {
                float heightAboveSurface = Vector3.Dot(saved - hit.point, up);
                if (heightAboveSurface < SpawnGroundClearance)
                    return hit.point + up * SpawnGroundClearance;
            }
            return saved;
        }

        /// <summary>
        /// Scan a square around <paramref name="origin"/> for the closest column whose top
        /// voxel is solid AND above sea level.
        /// </summary>
        private Vector3 FindFreshSpawnNearby(Vector3 origin)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return origin;
            int seaLevel = world.SeaLevel;

            int ox = Mathf.FloorToInt(origin.x);
            int oz = Mathf.FloorToInt(origin.z);

            // Search outward in expanding rings.
            for (int r = 0; r <= searchRadius; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                    int wx = ox + dx;
                    int wz = oz + dz;

                    int topY = -1;
                    for (int wy = VoxelConstants.WORLD_HEIGHT_VOXELS - 1; wy >= 1; wy--)
                    {
                        var v = world.GetVoxelWorld(new Vector3Int(wx, wy, wz));
                        if (v.density > 0)
                        {
                            // Skip every liquid representation, including a
                            // waterLevel carried by an otherwise legacy voxel.
                            if (IsLiquidVoxel(v)) continue;
                            topY = wy; break;
                        }
                    }
                    if (topY < 0) continue;
                    if (topY < seaLevel) continue;

                    // A solid seabed can still have a water column above it. Test
                    // the complete controller volume, not only the top solid voxel.
                    Vector3 candidate = new Vector3(wx + 0.5f, topY + SpawnGroundClearance, wz + 0.5f);
                    if (IsSpawnInWater(candidate)) continue;

                    // Found a valid spot — return immediately (closest-first scan guarantees this is nearest).
                    return candidate;
                }
            }
            // Fall back: just above the origin's XZ at sea-level + buffer.
            return new Vector3(origin.x, seaLevel + 30f, origin.z);
        }
    }
}
