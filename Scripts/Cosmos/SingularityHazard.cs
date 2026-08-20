// Assets/Scripts/VoxelEngine/Cosmos/SingularityHazard.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║           SINGULARITY HAZARD — the danger is real (Phase 5)           ║
// ║                                                                       ║
// ║  The PULL is already real physics (CosmicRegistry.GetGravityMetersS2  ║
// ║  sums every singularity's inverse-square field). This component owns  ║
// ║  everything that makes approaching one feel like approaching one:     ║
// ║                                                                       ║
// ║   • HUD warning ladder (approach → critical → horizon)                ║
// ║   • Tidal damage that ramps steeply inside the lethal band            ║
// ║   • Camera squeeze + shake as spacetime shear climbs                  ║
// ║   • CRUSH DEATH at the event horizon (\"COMPRESSED BY THE SINGULARITY\")║
// ║   • Quasar jets: relativistic beams along the disc axis shear         ║
// ║     anything crossing them (separate, faster damage)                  ║
// ║                                                                       ║
// ║  Zones are measured from the EVENT HORIZON (warning → lethal → crush) ║
// ║  and authored per body in the system template.                        ║
// ╚══════════════════════════════════════════════════════════════════════╝
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public class SingularityHazard : MonoBehaviour
    {
        [Header("Tuning")]
        [Tooltip("Extra FOV squeeze (degrees, negative) at full shear.")]
        public float crushFovSqueezeDegrees = -6f;

        [Tooltip("Camera shake magnitude at full shear.")]
        public float crushShakeMagnitude = 0.45f;

        [Header("References")]
        public Transform player;

        private readonly HashSet<SingularityInstance> _warned = new HashSet<SingularityInstance>();
        private float _nextWarnTime;

        private void Update()
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Singularities == null) return;
            var origin = SpaceOrigin.Instance;
            if (origin == null) return;

            // Resolve the real player (also covers late spawns).
            if (player == null || player.GetComponent<PlayerController>() == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) player = pc.transform;
                if (player == null) return;
            }

            var stats = PlayerStats.Instance != null ? PlayerStats.Instance : player.GetComponent<PlayerStats>();
            if (stats == null || stats.IsDead) return;

            double3 cosmic = origin.GetCosmicKm(player.position);
            float totalShear = 0f;

            for (int i = 0; i < registry.Singularities.Count; i++)
            {
                var s = registry.Singularities[i];
                if (s == null) continue;

                double rH = s.HorizonDistanceKm(cosmic);
                double warn = s.WarningRadiusKm;
                double lethal = math.max(s.LethalRadiusKm, s.eventHorizonKm);
                if (lethal >= warn) lethal = warn * 0.5d;
                if (rH > warn) continue;

                float t = Mathf.Clamp01((float)((warn - rH) / (warn - lethal)));
                totalShear = Mathf.Max(totalShear, t);

                // ── HUD warning ladder ──
                if (!_warned.Contains(s) || Time.unscaledTime >= _nextWarnTime)
                {
                    _warned.Add(s);
                    _nextWarnTime = Time.unscaledTime + 2f;
                    string msg = t < 0.35f
                        ? "GRAVITY SHEAR RISING"
                        : t < 0.75f
                            ? "SPACETIME SHEAR — CRITICAL, TURN BACK"
                            : "EVENT HORIZON — NO ESCAPE";
                    VoxelEngine.UI.BuildFeedbackHud.Show(s.DisplayName.ToUpperInvariant() + " — " + msg,
                        $"{(int)rH} km to horizon", null,
                        Color.Lerp(new Color(0.75f, 0.45f, 1f), new Color(1f, 0.15f, 0.1f), t));
                }

                // ── Tidal compression damage ──
                float dmgPerSec = t <= 0f ? 0f : Mathf.Pow(t, 1.7f) * s.tidalDamagePerSecond;
                if (dmgPerSec > 0f)
                {
                    if (t >= 0.75f) PlayerStats.SetDeathCause("COMPRESSED BY THE SINGULARITY");
                    stats.TakeDamage(dmgPerSec * Time.deltaTime);
                }

                // ── Crossed the horizon: instant crush ──
                if (rH <= 0d)
                {
                    PlayerStats.SetDeathCause("COMPRESSED BY THE SINGULARITY");
                    stats.TakeDamage(100000f * Time.deltaTime);
                }

                // ── Quasar relativistic jets ──
                if (s.kind == SingularityKind.Quasar && s.jetDamagePerSecond > 0f)
                {
                    Vector3 axis = s.discAxis.sqrMagnitude > 0.001f ? s.discAxis.normalized : Vector3.up;
                    double3 toS = s.positionKmD - cosmic;
                    double axial = math.dot(toS, new double3(axis.x, axis.y, axis.z));
                    if (math.abs(axial) < s.jetLengthKm)
                    {
                        double3 onAxis = new double3(axis.x, axis.y, axis.z) * axial;
                        double radial = math.length(toS - onAxis);
                        if (radial < s.jetCoreRadiusKm)
                        {
                            float edge = 1f - (float)(radial / s.jetCoreRadiusKm);
                            float jetDps = s.jetDamagePerSecond * (0.25f + 0.75f * edge * edge);
                            PlayerStats.SetDeathCause("SHEARED BY A QUASAR JET");
                            stats.TakeDamage(jetDps * Time.deltaTime);
                            totalShear = Mathf.Max(totalShear, 0.55f + 0.45f * edge);
                            if (!_warned.Contains(s) || Time.unscaledTime >= _nextWarnTime)
                            {
                                _warned.Add(s);
                                _nextWarnTime = Time.unscaledTime + 2f;
                                VoxelEngine.UI.BuildFeedbackHud.Show("QUASAR JET — RELATIVISTIC SHEAR",
                                    "Inside the polar beam — get out", null,
                                    new Color(0.5f, 0.7f, 1f));
                            }
                        }
                    }
                }
            }

            // ── Camera compression feedback ──
            if (totalShear > 0.001f)
            {
                CameraFeedback.AddShake(totalShear * crushShakeMagnitude);
                CameraFeedback.AddFovSqueeze(totalShear * crushFovSqueezeDegrees);
            }
        }
    }
}
