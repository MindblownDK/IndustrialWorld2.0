// Assets/Scripts/VoxelEngine/FX/VacuumAudio.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║              INDUSTRIAL WORLD — VACUUM AUDIO DUCKING           ║
// ║                                                                  ║
// ║  One global answer to \"how much exterior sound survives here?\"  ║
// ║  Sampled at the listener (camera): full air reads 1, hard        ║
// ║  vacuum reads 0, thin atmospheres land in between. Sealed,       ║
// ║  pressurised ship and station rooms count as air, so a pilot     ║
// ║  in a cockpit hears the ship while an EVA engineer outside       ║
// ║  hears nothing but suit, UI and music.                           ║
// ║                                                                  ║
// ║  Exterior emitters (machine loops, thrusters, ambience, weather, ║
// ║  positional one-shots, tool hits) multiply by Exterior01. UI,    ║
// ║  music and pickup cues bypass it entirely.                       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Pressure;

namespace VoxelEngine.FX
{
    public static class VacuumAudio
    {
        // Pressure (atm) at or above which exterior sound is full; fades
        // linearly down to silence at 0 atm.
        private const float FullSoundAtm = 0.3f;

        // Smoothing rates (per second): duck out fast, swell back slower.
        private const float DuckRate   = 1.4f;
        private const float ReturnRate = 0.7f;

        private static Transform _listener;
        private static float _sampleTimer;
        private static float _target = 1f;

        /// <summary>Smoothed 0..1 exterior audibility (1 = full air, 0 = hard vacuum).</summary>
        public static float Exterior01 { get; private set; } = 1f;

        /// <summary>Smoothed 0..1 vacuum depth (inverse of <see cref="Exterior01"/>).</summary>
        public static float Vacuum01 => 1f - Exterior01;

        /// <summary>True once exterior sound is effectively gone.</summary>
        public static bool IsVacuum => Exterior01 < 0.02f;

        /// <summary>
        /// Re-samples the listener environment a few times a second and eases
        /// <see cref="Exterior01"/> toward it every call. Driven by
        /// <see cref="WorldAudioBootstrap"/> in the gameplay scene.
        /// </summary>
        public static void Tick()
        {
            if (_listener == null)
            {
                var cam = Camera.main;
                if (cam != null) _listener = cam.transform;
            }
            _sampleTimer += Time.unscaledDeltaTime;
            if (_sampleTimer >= 0.25f || _listener == null)
            {
                _sampleTimer = 0f;
                _target = SampleTarget(_listener != null ? _listener.position : Vector3.zero);
            }
            float rate = _target < Exterior01 ? DuckRate : ReturnRate;
            Exterior01 = Mathf.MoveTowards(Exterior01, _target, Time.unscaledDeltaTime * rate);
        }

        private static float SampleTarget(Vector3 listenerPos)
        {
            // Best air available at the listener: open sky, ship room or station room.
            float air = PressureRules.SampleAmbient(listenerPos).PressureAtm;
            var room = RoomAtmosphereService.RoomAt(listenerPos);
            if (room != null) air = Mathf.Max(air, room.PressureAtm);
            var station = StationRoomSolver.RoomAt(listenerPos);
            if (station != null) air = Mathf.Max(air, station.Fill01 * PressureRules.NominalPressureAtm);
            return Mathf.InverseLerp(0f, FullSoundAtm, air);
        }
    }
}
