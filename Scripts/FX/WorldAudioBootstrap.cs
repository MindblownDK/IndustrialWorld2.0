// Assets/Scripts/VoxelEngine/FX/WorldAudioBootstrap.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — WORLD AUDIO BOOTSTRAP              ║
// ║                                                                  ║
// ║  Spawns the ambience controller and continually wires looping    ║
// ║  3D sound emitters onto every machine in the world — no scene    ║
// ║  setup, no per-machine code. A periodic sweep finds any machine  ║
// ║  that doesn't yet have a MachineAudio and configures it with the  ║
// ║  right sound + activity driver.                                  ║
// ║                                                                  ║
// ║  Auto-created at scene load (RuntimeInitializeOnLoad) ONLY in    ║
// ║  the gameplay scene (skips the main menu, which has no world).   ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Gas;
using VoxelEngine.GridSystem;
using VoxelEngine.Nuclear;
using VoxelEngine.Power;
using VoxelEngine.Transport;

namespace VoxelEngine.FX
{
    public class WorldAudioBootstrap : MonoBehaviour
    {
        private static WorldAudioBootstrap _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // Only run where there's an actual voxel world (the gameplay scene).
            // The main menu has no VoxelWorld, so we skip it to stay silent there.
            if (Core.ActiveWorld.Current == null)
                return;
            if (_instance != null) return;

            var go = new GameObject("~WorldAudio");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<WorldAudioBootstrap>();
            go.AddComponent<AmbienceController>();
        }

        private float _scanTimer;
        private const float SCAN_INTERVAL = 1.5f;   // re-sweep cadence for new machines

        private void Update()
        {
            _scanTimer += Time.unscaledDeltaTime;
            if (_scanTimer < SCAN_INTERVAL) return;
            _scanTimer = 0f;
            SweepMachines();
        }

        /// <summary>
        /// Finds machines lacking a <see cref="MachineAudio"/> and attaches one,
        /// configured with the correct loop + activity driver.
        /// </summary>
        private void SweepMachines()
        {
            // ── Crafting / processing ──────────────────────────────
            foreach (var m in FindObjectsByType<Furnace>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.FurnaceBurn, () => m.IsBurning ? (m.Current != null ? 1f : 0.5f) : 0f, vol: 0.6f, dist: 18f);

            foreach (var m in FindObjectsByType<ElectricFurnace>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.MachineHum, () => (m.IsOnline && m.Current != null) ? 1f : 0f, vol: 0.55f, dist: 18f);

            foreach (var m in FindObjectsByType<OilRefinery>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.MachineHum, () => (m.IsOnline && m.Current != null) ? 1f : 0f, vol: 0.6f, dist: 20f, basePitch: 0.85f);

            foreach (var m in FindObjectsByType<Pumpjack>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.EngineRumble, () => (m.IsOnline && m.HasReservoir) ? 1f : 0f, vol: 0.6f, dist: 24f, basePitch: 0.8f, pitchSpread: 0.18f);

            // ── Power ──────────────────────────────────────────────
            foreach (var m in FindObjectsByType<CoalGeneratorFuel>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.EngineRumble, () => m.IsBurning ? 1f : 0f, vol: 0.6f, dist: 22f);

            // ── Gas ────────────────────────────────────────────────
            foreach (var m in FindObjectsByType<HydrogenEngine>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.EngineRumble, () => m.IsRunning ? 1f : 0f, vol: 0.65f, dist: 24f, basePitch: 1.1f);

            foreach (var m in FindObjectsByType<Electrolyser>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.ElectricWhine, () => m.IsRunning ? 1f : 0f, vol: 0.5f, dist: 18f);

            // ── Nuclear ────────────────────────────────────────────
            foreach (var m in FindObjectsByType<ReactorCore>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.ReactorThrum, () => m.IsOnline ? (m.IsOverheating ? 1f : 0.7f) : 0f, vol: 0.7f, dist: 30f);

            foreach (var m in FindObjectsByType<SteamTurbine>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.SteamHiss, () => m.IsRunning ? Mathf.Lerp(0.4f, 1f, m.SteamFill01) : 0f, vol: 0.6f, dist: 24f);

            // ── Transport ──────────────────────────────────────────
            foreach (var m in FindObjectsByType<Quarry>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.QuarryGrind, () => m.IsMining ? 1f : 0f, vol: 0.7f, dist: 32f, pitchSpread: 0.1f);

            // ── Ship/vehicle grid blocks ───────────────────────────
            // GridThruster now has its own immersive audio/visual system built in, so we
            // skip it here to avoid double-spatialised thruster sounds.

            foreach (var m in FindObjectsByType<GridDrill>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.DrillSpin, () => m.IsActive ? 1f : 0f, vol: 0.6f, dist: 24f, pitchSpread: 0.2f);

            foreach (var m in FindObjectsByType<GridWheel>(FindObjectsInactive.Exclude))
                Attach(m, Sfx.WheelMotor, () =>
                {
                    if (!m.IsGrounded || m.Grid == null) return 0f;
                    return Mathf.Clamp01(Mathf.Abs(m.Grid.ThrustInput.z));
                }, vol: 0.4f, dist: 18f, pitchSpread: 0.25f);
        }

        /// <summary>Attach a configured MachineAudio if the target doesn't have one.</summary>
        private static void Attach(Component target, Sfx sfx, System.Func<float> activity,
            float vol = 0.7f, float dist = 24f, float basePitch = 1f, float pitchSpread = 0.12f)
        {
            if (target == null) return;
            if (target.GetComponent<MachineAudio>() != null) return;
            target.gameObject.AddComponent<MachineAudio>()
                  .Configure(sfx, activity, vol, dist, basePitch, pitchSpread);
        }
    }
}
