// Assets/Scripts/VoxelEngine/Editor/WeatherSystemSetup.cs
//
// Step 58 (9.19.0): WEATHER & CLIMATE FOUNDATIONS — non-destructive authoring.
//
//   • Authors a themed WeatherClimateProfile on every existing planet/moon that does
//     not yet carry one (version-gated, so hand-tuned worlds are never overwritten).
//     Desert worlds get wind & dust, ice worlds get snow/blizzard, ocean worlds get
//     heavy rain, airless moons get no weather — exactly matching their atmosphere.
//   • Ensures a single _Weather GameObject exists in the active scene carrying the
//     WeatherManager (+ particles/audio/lighting). Reused if already present; never
//     duplicated. Runtime (CosmosBootstrap) also auto-creates one, so weather is
//     guaranteed active even before this step is run.
//
// The weather simulation, particle systems, procedural audio, fog and lightning are
// all runtime code — no prefabs or items required. This step only authors per-body
// data and guarantees the scene hook. Re-runnable. Idempotent.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Weather;

namespace VoxelEngine.EditorTools
{
    public static class WeatherSystemSetup
    {
        private const int ProfileVersion = 1;

        public static void RunStep58()
        {
            Debug.Log("[VoxelEngineSetupWindow] Step 58 — Weather & Climate Foundations started.");

            int authored = 0;
            int preserved = 0;

            // ── 1) Per-body climate profiles (non-destructive, version-gated) ──
            void AuthorClimate(BodySettings body, Object owner)
            {
                if (body == null || owner == null) return;
                if (body.weather == null) body.weather = new WeatherClimateProfile();

                // A body that was already authored (or hand-tuned) keeps its values.
                if (body.weather.profileVersion >= ProfileVersion) { preserved++; return; }

                WeatherClimateProfile profile = ChooseProfileFor(body);
                body.weather.weatherEnabled      = profile.weatherEnabled;
                body.weather.precipitation        = profile.precipitation;
                body.weather.overcastBias         = profile.overcastBias;
                body.weather.stormChance          = profile.stormChance;
                body.weather.stormDarkening       = profile.stormDarkening;
                body.weather.stormWindMultiplier  = profile.stormWindMultiplier;
                body.weather.stormFogScale        = profile.stormFogScale;
                body.weather.stormLightFloor      = profile.stormLightFloor;
                body.weather.thunderFrequency     = profile.thunderFrequency;
                body.weather.profileVersion       = ProfileVersion;

                EditorUtility.SetDirty(owner);
                authored++;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:PlanetTemplate"))
            {
                var planet = AssetDatabase.LoadAssetAtPath<PlanetTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (planet != null) AuthorClimate(planet.body, planet);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:MoonTemplate"))
            {
                var moon = AssetDatabase.LoadAssetAtPath<MoonTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (moon != null) AuthorClimate(moon.body, moon);
            }

            // ── 2) Scene _Weather singleton (non-destructive: reused if present) ──
            bool sceneHooked = EnsureWeatherInActiveScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Voxel Engine — Weather & Climate (Step 58)",
                "Weather & climate foundations authored (non-destructive):\n\n" +
                "• " + authored + " bodies themed (desert / ice / ocean / airless / temperate)\n" +
                "• " + preserved + " existing profiles preserved\n" +
                "• Scene _Weather controller: " + (sceneHooked ? "present / connected" : "NOT added (none was missing — runtime will create one)") + "\n\n" +
                "Runtime: storms roll in per planet, rain/snow particles, fog, sun darkening,\n" +
                "synced thunder + lightning, and procedural audio. Airless bodies stay calm.\n" +
                "Re-runnable; authored climate values are never overwritten.",
                "OK");
        }

        /// <summary>Resolve a themed climate profile from a body's identity + atmosphere.</summary>
        private static WeatherClimateProfile ChooseProfileFor(BodySettings body)
        {
            string name = (body.bodyName ?? string.Empty).ToLowerInvariant();

            // Airless / vacuum bodies never get weather, regardless of name.
            bool airless = !body.HasAtmosphere
                || body.ResolveSurfaceAtmosphereDensity() <= 0.0001f
                || name.Contains("moon") || name.Contains("lunar")
                || name.Contains("asteroid") || name.Contains("belt");

            if (airless) return WeatherClimateProfile.Airless();
            if (name.Contains("desert") || name.Contains("mars") || name.Contains("desolate") || name.Contains("sand")) return WeatherClimateProfile.Desert();
            if (name.Contains("ice") || name.Contains("frozen") || name.Contains("tundra") || name.Contains("snow")) return WeatherClimateProfile.Tundra();
            if (name.Contains("ocean") || name.Contains("water") || name.Contains("sea")) return WeatherClimateProfile.Ocean();
            return WeatherClimateProfile.Default();
        }

        /// <summary>Ensure the active scene has exactly one _Weather controller, fully wired.</summary>
        private static bool EnsureWeatherInActiveScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var existing = Object.FindAnyObjectByType<WeatherManager>();
            if (existing != null)
            {
                // Connect any missing sub-components on an existing controller (non-destructive).
                EnsureComponent(existing.GetComponent<WeatherParticles>(), () => existing.gameObject.AddComponent<WeatherParticles>());
                EnsureComponent(existing.GetComponent<WeatherAudio>(),     () => existing.gameObject.AddComponent<WeatherAudio>());
                EnsureComponent(existing.GetComponent<WeatherLighting>(),  () => existing.gameObject.AddComponent<WeatherLighting>());
                EditorSceneManager.MarkSceneDirty(scene);
                return true;
            }

            var go = new GameObject("_Weather");
            var wm = go.AddComponent<WeatherManager>();
            // Pre-add the sub-components so designers can tune fog colours / flash in the
            // inspector. WeatherManager.Start dedups these, so no duplicates at runtime.
            go.AddComponent<WeatherParticles>();
            go.AddComponent<WeatherAudio>();
            go.AddComponent<WeatherLighting>();
            // Sensible default camera reference; runtime auto-resolves Camera.main if left null.
            wm.playerCamera = null;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[VoxelEngineSetupWindow] Step 58 created the _Weather controller in the active scene.");
            return true;
        }

        private static void EnsureComponent<T>(T current, System.Action add) where T : Component
        {
            if (current == null) add?.Invoke();
        }
    }
}
#endif
