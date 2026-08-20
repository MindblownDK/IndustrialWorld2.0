// Assets/Scripts/VoxelEngine/Editor/SingularitySetup.cs
//
// Step 52 (Phase 5): SINGULARITY REMNANTS — non-destructive authoring of the real
// black hole and quasar bodies on every SolarSystemTemplate in the project.
//
//   • Creates the blackHole settings block when missing; initializes it ONCE
//     (configured flag) — never overwrites authored physics/visual values.
//   • Promotes the quasar to a real body the same way (realBody + configured flag).
//   • Enforces the ONE SUN policy at asset level: authored sunCount > 1 is normalized
//     to 1 (the simulation always runs a single star).
//   • Re-runnable. Idempotent. Existing saves keep working (layout is seed-derived).
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.EditorTools
{
    public static class SingularitySetup
    {
        public static void RunStep52()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 52 — Singularity Remnants (black hole + quasar) started.");

            string[] guids = AssetDatabase.FindAssets("t:SolarSystemTemplate");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[SingularitySetup] No SolarSystemTemplate assets found. " +
                                 "Run Step 21 first to author the solar system, then re-run Step 52.");
                return;
            }

            int templates = 0, bhCreated = 0, bhInitialized = 0, quasarCreated = 0,
                quasarInitialized = 0, sunsNormalized = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var system = AssetDatabase.LoadAssetAtPath<SolarSystemTemplate>(path);
                if (system == null) continue;
                templates++;

                // ── Black hole: create if missing, initialize once, never overwrite ──
                if (system.blackHole == null)
                {
                    system.blackHole = new BlackHoleSettings();
                    bhCreated++;
                }
                if (!system.blackHole.configured)
                {
                    system.blackHole.configured = true;
                    bhInitialized++;
                }

                // ── Quasar: real body by default; create if missing ──
                if (system.quasar == null)
                {
                    system.quasar = new QuasarSettings();
                    quasarCreated++;
                }
                if (!system.quasar.realBody)
                {
                    system.quasar.realBody = true;
                    quasarInitialized++;
                }
                if (!system.quasar.configured)
                {
                    system.quasar.configured = true;
                    quasarInitialized++;
                }

                // ── ONE SUN policy: normalize authored multi-star templates ──
                if (system.sun != null && system.sun.sunCount != 1)
                {
                    Debug.LogWarning($"[SingularitySetup] '{system.systemName}' authored {system.sun.sunCount} " +
                                     "stars — ONE SUN policy normalizes it to 1.");
                    system.sun.sunCount = 1;
                    sunsNormalized++;
                }

                EditorUtility.SetDirty(system);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SingularitySetup] Step 52 complete — {templates} system template(s) processed: " +
                      $"{bhCreated} black-hole blocks created, {bhInitialized} initialized; " +
                      $"{quasarCreated} quasar blocks created, {quasarInitialized} promoted/initialized; " +
                      $"{sunsNormalized} multi-sun templates normalized to one sun. " +
                      "Authored physics/visual values are preserved.");
        }
    }
}
#endif
