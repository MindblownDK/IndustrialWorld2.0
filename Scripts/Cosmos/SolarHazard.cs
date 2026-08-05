// Assets/Scripts/VoxelEngine/Cosmos/SolarHazard.cs
//
// The star is DEADLY, and it tells you so before it kills you. This component watches the
// player's cosmic distance to the sun and applies a heat warning / damage ramp:
//
//   • > warningRadiusKm    — nothing (safe).
//   • warning→lethal band  — HUD warnings ("SOL APPROACH — HEAT RISING"), damage ramps in.
//   • < lethalRadiusKm     — heat damage scales up steeply; inside the corona it is lethal.
//
// Flying into the sun is therefore possible but never accidental: the player gets clear
// warnings and can turn back. Grids are not damaged (ships shield the pilot at distance;
// only direct player exposure burns).
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Cosmos
{
    public class SolarHazard : MonoBehaviour
    {
        [Header("Hazard Zones (km from the star)")]
        [Tooltip("Inside this radius the first warning fires and light heat damage begins.")]
        public float warningRadiusKm = 2200f;

        [Tooltip("Inside this radius heat damage ramps steeply to lethal.")]
        public float lethalRadiusKm = 1100f;

        [Tooltip("Heat damage per second at the lethal boundary.")]
        public float lethalDamagePerSecond = 60f;

        [Header("References")]
        public Transform player;

        private float _nextWarnTime;
        private bool _warned;

        private void Update()
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Sun == null) return;

            var origin = SpaceOrigin.Instance;
            if (origin == null) return;

            // Resolve the real player (also covers late spawns).
            if (player == null || player.GetComponent<PlayerController>() == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) player = pc.transform;
                if (player == null) return;
            }

            double3 cosmic = origin.GetCosmicKm(player.position);
            double distKm = math.length(cosmic - registry.Sun.positionKmD);

            float warn = warningRadiusKm;
            float lethal = Mathf.Min(lethalRadiusKm, warn * 0.55f);

            if (distKm > warn) { _warned = false; return; }

            // Inside the warning zone.
            float t = Mathf.Clamp01((float)((warn - distKm) / (warn - lethal))); // 0 at warn, 1 at lethal
            if (!_warned)
            {
                _warned = true;
                _nextWarnTime = 0f;
            }

            if (Time.unscaledTime >= _nextWarnTime)
            {
                _nextWarnTime = Time.unscaledTime + 2f;
                string msg = t < 0.35f
                    ? "SOL APPROACH — HEAT RISING"
                    : t < 0.75f
                        ? "SOL FLARE — CRITICAL HEAT, TURN BACK"
                        : "SOL CORONA — CERTAIN DEATH";
                VoxelEngine.UI.BuildFeedbackHud.Show("☀ " + msg,
                    $"{(int)distKm} km from star", null,
                    Color.Lerp(new Color(1f, 0.72f, 0.25f), new Color(1f, 0.2f, 0.1f), t));
            }

            // Damage: ramps from 0 at the warning edge to lethal at the lethal boundary.
            float dmgPerSec = t <= 0f ? 0f : Mathf.Pow(t, 1.6f) * lethalDamagePerSecond;
            if (dmgPerSec > 0f)
            {
                var stats = PlayerStats.Instance != null ? PlayerStats.Instance : player.GetComponent<PlayerStats>();
                if (stats != null) stats.TakeDamage(dmgPerSec * Time.deltaTime);
            }
        }
    }
}
