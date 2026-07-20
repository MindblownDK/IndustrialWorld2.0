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
                    // Spawn above the body's "north pole" (body-local +Y) so the player starts
                    // standing upright on the planet surface.
                    // Spawn at a TEMPERATE latitude (~30° from equator), NOT the freezing pole.
                    // The pole is the coldest point (latitude climate) → only ice/tundra.
                    // 30° gives a nice grassy/forest biome. Also spawn closer (8m above surface)
                    // so chunks load fast and the player doesn't fall while waiting.
                    Vector3 equator = new Vector3(1f, 0f, 0f);
                    // Rotate 30° from equator toward the pole to get a temperate climate.
                    Vector3 surfDir = math.normalizesafe(
                        equator + body.transform.up * 0.55f, body.transform.up);
                    target = body.transform.position + surfDir * (body.SurfaceRadius + 8f);
                    Debug.Log("[PlayerSpawner] Fresh SPHERE world — spawning on " + body.DisplayName +
                              " surface above north pole.");
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

            // Now wait for the chunk column at (target.x, target.z) to be generated AND meshed.
            yield return WaitForChunkAt(VoxelCoordOf(target), maxWaitSeconds);

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

            // Snap to actual ground via raycast — keep retrying until we get a real hit,
            // because the mesh collider may take a frame or two to activate after the chunk
            // mesh is uploaded.
            float groundT0 = Time.time;
            bool snapped = false;
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
                    SetPosition(hit.point + lift * 1.0f);
                    snapped = true;
                    break;
                }
                yield return null;
            }
            if (!snapped)
            {
                Debug.LogWarning("[PlayerSpawner] Could not raycast to ground after 5s — placing high above target.");
                SetPosition(new Vector3(target.x, target.y + 5f, target.z));
            }

            // One more frame to let physics settle.
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

        private IEnumerator RespawnRoutine(Vector3 dest)
        {
            ReadyForPlayerControl = false;
            DisableController();
            SetPosition(new Vector3(dest.x, Mathf.Max(dest.y, 250f), dest.z));
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

        /// <summary>Rejects corrupt/legacy coordinates before they can freeze streaming
        /// around an invalid location. Planet saves must remain close to the active surface.</summary>
        private static bool IsSafeSavedPosition(Vector3 pos)
        {
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)
                || float.IsInfinity(pos.x) || float.IsInfinity(pos.y) || float.IsInfinity(pos.z))
                return false;

            var body = VoxelEngine.Cosmos.GravityProvider.ActiveBody;
            if (body == null) return Mathf.Abs(pos.x) < 100000f && Mathf.Abs(pos.y) < 100000f && Mathf.Abs(pos.z) < 100000f;

            float radialDistance = Vector3.Distance(pos, body.transform.position);
            // Terrain varies around the authored surface, but a player far inside or
            // outside the body cannot stream a valid playable chunk column.
            float tolerance = Mathf.Max(160f, body.SurfaceRadius * 0.30f);
            return Mathf.Abs(radialDistance - body.SurfaceRadius) <= tolerance;
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
            Vector3 from = new Vector3(target.x, target.y + 100f, target.z);
            if (Physics.Raycast(from, Vector3.down, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 1.0f;     // stand 1m above hit
            return target + Vector3.up * 2f;
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
                    return new Vector3(wx + 0.5f, topY + 2f, wz + 0.5f);
                }
            }
            // Fall back: just above the origin's XZ at sea-level + buffer.
            return new Vector3(origin.x, seaLevel + 30f, origin.z);
        }
    }
}
