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
                // Fresh world — move the player ABOVE the search area first so chunks load.
                // The actual ground-finding happens after chunks stream in.
                target = new Vector3(0, 250, 0);
                Debug.Log("[PlayerSpawner] Fresh world — placing player above origin to trigger chunk streaming.");
            }

            // Park the CharacterController-disabled player at the (X,Z) of target with a HIGH Y.
            // This forces VoxelWorld's streamer to start loading chunks around the spawn site.
            DisableController();
            SetPosition(new Vector3(target.x, Mathf.Max(target.y, 250f), target.z));

            // Now wait for the chunk column at (target.x, target.z) to be generated AND meshed.
            yield return WaitForChunkAt(VoxelCoordOf(target), maxWaitSeconds);

            // For fresh worlds, NOW find the actual top-of-ground position.
            if (!hasSavedPos && !(session != null && session.hasBedSpawn))
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
                Vector3 from = new Vector3(target.x, target.y + 100f, target.z);
                if (Physics.Raycast(from, Vector3.down, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                {
                    SetPosition(hit.point + Vector3.up * 1.0f);
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
                const string pattern = @"""pos""\s*:\s*\{\s*""x""\s*:\s*(-?[0-9.eE+-]+)\s*,\s*""y""\s*:\s*(-?[0-9.eE+-]+)\s*,\s*""z""\s*:\s*(-?[0-9.eE+-]+)";
                var m = System.Text.RegularExpressions.Regex.Match(txt, pattern);
                if (!m.Success) return false;
                float x = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                pos = new Vector3(x, y, z);
                return true;
            }
            catch { return false; }
        }

        private static Vector3Int VoxelCoordOf(Vector3 pos)
            => new Vector3Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));

        private IEnumerator WaitForChunkAt(Vector3Int worldVoxel, float timeoutSec)
        {
            var world = VoxelWorld.Instance;
            if (world == null) yield break;

            int cs = VoxelConstants.CHUNK_SIZE;
            // Check all Y-stacks at this XZ column — any meshed chunk in that column is enough.
            int cx = Mathf.FloorToInt(worldVoxel.x / (float)cs);
            int cz = Mathf.FloorToInt(worldVoxel.z / (float)cs);

            float t0 = Time.time;
            while (Time.time - t0 < timeoutSec)
            {
                bool anyMeshed = false;
                for (int cy = 0; cy < VoxelConstants.WORLD_HEIGHT_CHUNKS; cy++)
                {
                    if (world.TryGetChunk(new Vector3Int(cx, cy, cz), out var c)
                        && c != null && c.isGenerated
                        && c.meshCollider != null && c.meshCollider.sharedMesh != null)
                    {
                        anyMeshed = true; break;
                    }
                }
                if (anyMeshed) yield break;
                yield return null;
            }
            Debug.LogWarning($"[PlayerSpawner] Timed out waiting for chunks at column ({cx}, ?, {cz}).");
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
            var world = VoxelWorld.Instance;
            if (world == null) return origin;
            int seaLevel = world.planet != null ? world.planet.seaLevel : 96;

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
