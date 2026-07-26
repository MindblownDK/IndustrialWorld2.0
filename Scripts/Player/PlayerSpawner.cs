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

        private CharacterController _cc;
        private const float SpawnGroundClearance = 1.15f;
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
            bool hasSavedPos = TryReadSavedPlayerPosition(out Vector3 savedPos);

            // Determine the target position.
            Vector3 target;
            if (hasSavedPos)
            {
                target = savedPos;
                Debug.Log("[PlayerSpawner] Restoring saved player position: " + target);
            }
            else if (session != null && session.hasBedSpawn)
            {
                target = session.bedSpawnPoint;
                Debug.Log("[PlayerSpawner] Bed spawn: " + target);
            }
            else
            {
                // Fresh world. If a sphere body is active, spawn on its surface; else flat origin.
                var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
                if (body != null)
                {
                    // Scan around temperate latitude to find a valid land surface above sea level (not water).
                    Vector3 equator = new Vector3(1f, 0f, 0f);
                    bool foundLand = false;
                    Vector3 bestSpawn = body.transform.position + body.transform.up * (body.SurfaceRadius + 30f);

                    for (int i = 0; i < 16; i++)
                    {
                        float angle = i * (360f / 16f);
                        Vector3 sampleDir = Quaternion.AngleAxis(angle, body.transform.up) * (equator + body.transform.up * 0.55f);
                        sampleDir = math.normalizesafe(sampleDir, body.transform.up);

                        Vector3 rayFrom = body.transform.position + sampleDir * (body.SurfaceRadius + 250f);
                        Vector3 rayDir = -sampleDir;
                        if (Physics.Raycast(rayFrom, rayDir, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            float hitRadius = Vector3.Distance(hit.point, body.transform.position);
                            float seaRadius = body.SeaRadius;
                            if (hitRadius > seaRadius + 3f)
                            {
                                var world = VoxelEngine.Core.ActiveWorld.Current;
                                bool isWater = false;
                                if (world != null)
                                {
                                    var voxelCoord = world.WorldToVoxel(hit.point - sampleDir * 0.5f);
                                    var v = world.GetVoxelWorld(voxelCoord);
                                    if (v.material == (byte)VoxelEngine.Materials.MaterialId.WaterVoxel ||
                                        v.material == (byte)VoxelEngine.Materials.MaterialId.WaterLiquid ||
                                        v.waterLevel > 0)
                                    {
                                        isWater = true;
                                    }
                                }
                                if (!isWater)
                                {
                                    bestSpawn = hit.point + sampleDir * SpawnGroundClearance;
                                    foundLand = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!foundLand)
                    {
                        Vector3 surfDir = math.normalizesafe(equator + body.transform.up * 0.55f, body.transform.up);
                        bestSpawn = body.transform.position + surfDir * (body.SurfaceRadius + 25f);
                    }
                    target = bestSpawn;
                    Debug.Log("[PlayerSpawner] Fresh SPHERE world — spawning on land surface at " + target);
                }
                else
                {
                    target = new Vector3(0, 250, 0);
                    Debug.Log("[PlayerSpawner] Fresh world — placing player above origin to trigger chunk streaming.");
                }
            }

            // Park the CharacterController-disabled player at the (X,Z) of target with a HIGH Y.
            // This forces VoxelWorld's streamer to start loading chunks around the spawn site.
            DisableController();
            // On a sphere, DON'T force Y to 250 — that would park the player far above the
            // body's surface (which could be at Y=700+). Use the target Y directly so chunks
            // around the surface start streaming immediately.
            float parkY = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null
                          ? target.y
                          : Mathf.Max(target.y, 250f);
            SetPosition(new Vector3(target.x, parkY, target.z));

            // A saved high-altitude/space location has no terrain column to wait for.
            // Waiting there would unnecessarily freeze control before an orbital logout.
            var activeBody = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            bool savedInSpace = hasSavedPos && activeBody != null
                && Vector3.Distance(target, activeBody.transform.position) > activeBody.SurfaceRadius + 80f;
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

            // For fresh worlds, NOW find the actual top-of-ground position.
            // Skip on spheres — FindFreshSpawnNearby scans in world-space voxel coords which
            // is wrong for a body-offset sphere, and the radial raycast below handles ground
            // detection on planets anyway.
            bool isSphere = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null;
            if (!hasSavedPos && !(session != null && session.hasBedSpawn) && !isSphere)
            {
                Vector3 ground = FindFreshSpawnNearby(target);
                target = ground;
                if (session != null)
                {
                    session.worldSpawnPoint = target;
                    session.worldSpawnInitialized = true;
                    session.SaveSpawnSidecar();
                }
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

            EnableController();
            ReadyForPlayerControl = true;
            if (!VoxelEngine.UI.UIState.IsBlocking)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Debug.Log("[PlayerSpawner] Player control enabled at " + transform.position);
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
            Vector3 dest = session != null ? session.GetActiveSpawn() : new Vector3(0, 250, 0);
            StartCoroutine(RespawnRoutine(dest));
        }

        public void RespawnAt(Vector3 destination)
        {
            StartCoroutine(RespawnRoutine(destination));
        }

        private IEnumerator RespawnRoutine(Vector3 dest)
        {
            ReadyForPlayerControl = false;
            DisableController();
            // Mirror the first-spawn routine: on a spherical body the parked position
            // must sit at the TRUE destination height. The player transform drives chunk
            // streaming, so forcing Y up to 250 (a flat-world streaming trick) on a sphere
            // parks the viewer far below the surface spawn — the chunks around the cryobed
            // never stream in, WaitForChunkAt times out, and the player is dropped far from
            // the chosen respawn point.
            float parkY = VoxelEngine.Cosmos.GravityProvider.ActiveBody != null
                          ? dest.y
                          : Mathf.Max(dest.y, 250f);
            SetPosition(new Vector3(dest.x, parkY, dest.z));
            yield return WaitForChunkAt(VoxelCoordOf(dest), 8f);
            SetPosition(SnapToGround(dest));
            yield return null;
            yield return null;
            SetPosition(SnapToGround(transform.position));
            EnableController();
            ReadyForPlayerControl = true;
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
        /// Raycast straight down from above to find the top of the meshed terrain.
        /// Returns target unchanged if nothing is hit.
        /// </summary>
        private Vector3 SnapToGround(Vector3 target)
        {
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
                            // Skip water voxels.
                            if (v.material == (byte)Materials.MaterialId.WaterVoxel ||
                                v.material == (byte)Materials.MaterialId.WaterLiquid) continue;
                            topY = wy; break;
                        }
                    }
                    if (topY < 0) continue;
                    if (topY < seaLevel) continue;
                    // Found a valid spot — return immediately (closest-first scan guarantees this is nearest).
                    return new Vector3(wx + 0.5f, topY + 0.05f, wz + 0.5f);
                }
            }
            // Fall back: just above the origin's XZ at sea-level + buffer.
            return new Vector3(origin.x, seaLevel + 30f, origin.z);
        }
    }
}
