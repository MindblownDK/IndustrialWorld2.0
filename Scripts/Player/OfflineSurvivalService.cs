// Assets/Scripts/VoxelEngine/Player/OfflineSurvivalService.cs
//
// 11.4 Offline Survival — player requires active cryobed or oxygen-rich environment while offline.
// If oxygen depletes or no valid condition, player dies offline.
//
// Flow:
//   • On SaveAll / OnApplicationQuit we write offline_state.json with UTC timestamp, player pos,
//     and claimed cryobed info (pos + name).
//   • On next login PlayerSpawner calls CheckOfflineSurvival() which:
//       - Computes offline hours
//       - If claimed cryobed exists and is GridCryobed, consumes oxygenStored = offlineHours * offlineOxygenPerHour
//       - If enough O₂, survives; else O₂ → 0 and dies
//       - If no claimed cryobed, checks oxygen-rich environment at logout pos (near powered biofarm / O₂ tank / cryobed)
//       - If neither, dies offline
//
// Save-compatible: additive file, no schema change to world_state.json.

using UnityEngine;
using System;
using System.IO;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Fluids;
using VoxelEngine.Gas;

namespace VoxelEngine.Player
{
    public class OfflineSurvivalService : MonoBehaviour
    {
        public static OfflineSurvivalService Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("OfflineSurvivalService");
            Instance = go.AddComponent<OfflineSurvivalService>();
            DontDestroyOnLoad(go);
        }

        [Serializable]
        private class OfflineStateFile
        {
            public string lastLogoutUtcIso;
            public Vector3 lastPos;
            public bool hasClaimedCryobed;
            public Vector3 claimedCryobedPos;
            public string claimedCryobedName;
        }

        public struct OfflineResult
        {
            public bool survived;
            public float hoursOffline;
            public float oxygenConsumed;
            public string reason;
            public bool hadCryobed;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private string OfflinePath
        {
            get
            {
                var session = Menu.WorldSession.Instance;
                if (session == null) return null;
                string folder = session.WorldFolderPath(session.worldName);
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "offline_state.json");
            }
        }

        public void SaveOfflineState(Vector3 playerPos)
        {
            try
            {
                var session = Menu.WorldSession.Instance;
                if (session == null) return;
                string path = OfflinePath;
                if (string.IsNullOrEmpty(path)) return;

                var file = new OfflineStateFile
                {
                    lastLogoutUtcIso = DateTime.UtcNow.ToString("o"),
                    lastPos = playerPos,
                    hasClaimedCryobed = session.hasBedSpawn,
                    claimedCryobedPos = session.hasBedSpawn ? session.bedSpawnPoint : Vector3.zero,
                    claimedCryobedName = ""
                };

                // Try to resolve claimed cryobed name for UX
                if (session.hasBedSpawn)
                {
                    file.claimedCryobedName = ResolveCryobedNameAt(session.bedSpawnPoint);
                }

                string json = JsonUtility.ToJson(file, true);
                File.WriteAllText(path, json);
                Debug.Log($"[OfflineSurvival] Saved offline state at {file.lastLogoutUtcIso} pos {playerPos} cryobed {(file.hasClaimedCryobed ? file.claimedCryobedPos.ToString() : "none")}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OfflineSurvival] SaveOfflineState failed: " + ex.Message);
            }
        }

        public OfflineResult CheckOfflineSurvivalAndConsume()
        {
            var result = new OfflineResult
            {
                survived = true,
                hoursOffline = 0f,
                oxygenConsumed = 0f,
                reason = "No previous offline state — fresh login",
                hadCryobed = false
            };

            try
            {
                string path = OfflinePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Debug.Log("[OfflineSurvival] No offline file — treating as fresh login, survived.");
                    return result;
                }

                string json = File.ReadAllText(path);
                var file = JsonUtility.FromJson<OfflineStateFile>(json);
                if (file == null || string.IsNullOrEmpty(file.lastLogoutUtcIso))
                {
                    Debug.Log("[OfflineSurvival] Invalid offline file — survived.");
                    return result;
                }

                DateTime lastUtc;
                if (!DateTime.TryParse(file.lastLogoutUtcIso, null, System.Globalization.DateTimeStyles.RoundtripKind, out lastUtc))
                {
                    Debug.LogWarning("[OfflineSurvival] Could not parse lastUtc — survived.");
                    return result;
                }

                TimeSpan offlineSpan = DateTime.UtcNow - lastUtc;
                float hours = (float)offlineSpan.TotalHours;
                // Clamp: negative (clock drift) → 0, max 30 days to prevent insane consumption
                if (hours < 0f) hours = 0f;
                if (hours > 720f) hours = 720f;

                result.hoursOffline = hours;

                // If offline < 2 minutes, don't consume — avoids save/load spam killing O₂
                if (hours < 0.033f)
                {
                    result.reason = $"Offline {hours*60f:0} min — too short, no O₂ consumed";
                    result.survived = true;
                    return result;
                }

                var session = Menu.WorldSession.Instance;
                bool hasClaimed = file.hasClaimedCryobed && session != null && session.hasBedSpawn;
                result.hadCryobed = hasClaimed;

                // Try to find the claimed cryobed object at file.claimedCryobedPos
                GridCryobed claimedGridBed = null;
                Cryobed claimedStaticBed = null;

                if (hasClaimed)
                {
                    claimedGridBed = FindGridCryobedNear(file.claimedCryobedPos);
                    if (claimedGridBed == null)
                        claimedStaticBed = FindStaticCryobedNear(file.claimedCryobedPos);
                }

                if (claimedGridBed != null)
                {
                    // Grid cryobed: consume O₂ from its internal buffer
                    float need = claimedGridBed.offlineOxygenPerHour * hours;
                    result.oxygenConsumed = need;

                    // Check power at logout time? We check current IsPowered — if unpowered now, treat as failure
                    if (!claimedGridBed.IsPowered)
                    {
                        result.survived = false;
                        result.reason = $"Grid Cryobed '{claimedGridBed.blockName}' has NO POWER after {hours:0.0}h offline — you died offline";
                        claimedGridBed.oxygenStored = Mathf.Max(0f, claimedGridBed.oxygenStored - need);
                        return result;
                    }

                    if (claimedGridBed.oxygenStored >= need - 0.01f)
                    {
                        claimedGridBed.oxygenStored -= need;
                        result.survived = true;
                        result.reason = $"Survived {hours:0.0}h offline in Grid Cryobed '{claimedGridBed.blockName}' — consumed {need:0} O₂, remaining {claimedGridBed.oxygenStored:0}";
                        return result;
                    }
                    else
                    {
                        // Not enough O₂ — depleted
                        float available = claimedGridBed.oxygenStored;
                        claimedGridBed.oxygenStored = 0f;
                        result.survived = false;
                        result.oxygenConsumed = available;
                        result.reason = $"Grid Cryobed '{claimedGridBed.blockName}' O₂ depleted after {available / Mathf.Max(0.01f, claimedGridBed.offlineOxygenPerHour):0.0}h of {hours:0.0}h offline — you died offline";
                        return result;
                    }
                }
                else if (claimedStaticBed != null)
                {
                    if (!claimedStaticBed.IsPowered)
                    {
                        result.survived = false;
                        result.reason = $"Static Cryobed '{claimedStaticBed.displayName}' has NO POWER after {hours:0.0}h offline — you died offline";
                        return result;
                    }
                    if (!claimedStaticBed.HasOxygenEnvironment)
                    {
                        result.survived = false;
                        result.reason = $"Static Cryobed '{claimedStaticBed.displayName}' has NO OXYGEN at {file.claimedCryobedPos} after {hours:0.0}h offline — you died offline";
                        return result;
                    }
                    result.survived = true;
                    result.reason = $"Survived {hours:0.0}h offline in Static Cryobed '{claimedStaticBed.displayName}' (room O₂ OK)";
                    return result;
                }
                else if (hasClaimed)
                {
                    // Had a claimed cryobed at logout but it's gone now (destroyed)
                    result.survived = false;
                    result.reason = $"Claimed cryobed at {file.claimedCryobedPos} not found on login (destroyed) after {hours:0.0}h offline — you died offline";
                    return result;
                }

                // No claimed cryobed — check oxygen-rich environment at logout pos
                if (IsOxygenRichEnvironment(file.lastPos))
                {
                    result.survived = true;
                    result.reason = $"Survived {hours:0.0}h offline in oxygen-rich environment near {file.lastPos} (biofarm / O₂ tank / powered cryobed)";
                    return result;
                }

                // No cryobed and no O₂-rich environment
                result.survived = false;
                result.reason = $"No active cryobed and no oxygen-rich environment at {file.lastPos} — died after {hours:0.0}h offline (need biofarm / O₂ tank / cryobed)";
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[OfflineSurvival] Check failed: " + ex.Message + " — treating as survived");
                result.survived = true;
                result.reason = "Check exception — survived";
                return result;
            }
        }

        public void ClearOfflineFile()
        {
            try
            {
                string path = OfflinePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private string ResolveCryobedNameAt(Vector3 spawnPos)
        {
            var gridBed = FindGridCryobedNear(spawnPos);
            if (gridBed != null) return gridBed.blockName;
            var staticBed = FindStaticCryobedNear(spawnPos);
            if (staticBed != null) return staticBed.displayName;
            return "";
        }

        private GridCryobed FindGridCryobedNear(Vector3 spawnPos, float tol = 2.5f)
        {
            float tolSq = tol * tol;
            foreach (var cryo in FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null) continue;
                if ((cryo.SpawnPoint - spawnPos).sqrMagnitude < tolSq) return cryo;
            }
            return null;
        }

        private Cryobed FindStaticCryobedNear(Vector3 spawnPos, float tol = 2.5f)
        {
            float tolSq = tol * tol;
            foreach (var cryo in FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null) continue;
                if ((cryo.SpawnPoint - spawnPos).sqrMagnitude < tolSq) return cryo;
            }
            return null;
        }

        // Oxygen-rich environment = near powered biofarm producing O₂, or O₂ tank with O₂, or powered cryobed with O₂
        private bool IsOxygenRichEnvironment(Vector3 pos)
        {
            const float biofarmRadius = 10f;
            const float tankRadius = 6f;
            const float cryobedRadius = 6f;

            // Static biofarms
            foreach (var bf in FindObjectsByType<Building.Biofarm>(FindObjectsInactive.Exclude))
            {
                if (bf == null || !bf.IsRunning) continue;
                if ((bf.transform.position - pos).sqrMagnitude <= biofarmRadius * biofarmRadius)
                    return true;
            }

            // Grid biofarms
            foreach (var bf in FindObjectsByType<GridBiofarm>(FindObjectsInactive.Exclude))
            {
                if (bf == null || !bf.IsProducing) continue;
                if ((bf.transform.position - pos).sqrMagnitude <= biofarmRadius * biofarmRadius)
                    return true;
            }

            // Static gas tanks with O₂
            foreach (var tank in FindObjectsByType<GasTank>(FindObjectsInactive.Exclude))
            {
                if (tank == null) continue;
                if (tank.storedGasType != GasType.Oxygen) continue;
                if (tank.storedAmount < 10f) continue;
                if ((tank.transform.position - pos).sqrMagnitude <= tankRadius * tankRadius)
                    return true;
            }

            // Grid gas tanks with O₂
            foreach (var tank in FindObjectsByType<GridGasTank>(FindObjectsInactive.Exclude))
            {
                if (tank == null) continue;
                if (tank.gasType != GasType.Oxygen) continue;
                if (tank.stored < 10f) continue;
                if ((tank.transform.position - pos).sqrMagnitude <= tankRadius * tankRadius)
                    return true;
            }

            // Grid cryobeds with O₂ (even if not claimed)
            foreach (var cryo in FindObjectsByType<GridCryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null || !cryo.HasOxygenEnvironment) continue;
                if (!cryo.IsPowered) continue;
                if ((cryo.transform.position - pos).sqrMagnitude <= cryobedRadius * cryobedRadius)
                    return true;
            }

            // Static cryobeds with O₂ (room O₂)
            foreach (var cryo in FindObjectsByType<Cryobed>(FindObjectsInactive.Exclude))
            {
                if (cryo == null || !cryo.IsAvailableForRespawn) continue;
                if ((cryo.transform.position - pos).sqrMagnitude <= cryobedRadius * cryobedRadius)
                    return true;
            }

            return false;
        }
    }
}
