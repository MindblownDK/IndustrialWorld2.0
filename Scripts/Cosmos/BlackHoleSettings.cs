// Assets/Scripts/VoxelEngine/Cosmos/BlackHoleSettings.cs
//
// Designer settings for the REAL black hole body of a solar system (Phase 5).
//
// Unlike the old quasar backdrop, this is a genuine cosmic object: the registry places it
// at a seeded deep-space position far beyond the planets, its mass contributes to the
// N-body gravity field, and flying to it is a real (and lethal) journey.
//
// All radii are GAME-SCALED for playability — the gravitational parameter drives the pull
// (real inverse-square law), while the event horizon is the visual/crush boundary the
// player can actually fly toward.
using System;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [Serializable]
    public class BlackHoleSettings
    {
        [Tooltip("Whether this system features a real black hole body.")]
        public bool enabled = true;

        [Tooltip("True after Step 52 has initialized this block. Editor-only gate so the setup " +
                 "step can fill defaults ONCE without ever overwriting authored values.")]
        public bool configured;

        [Header("Physics")]
        [Tooltip("Gravitational parameter μ (km³/s²). 80,000 ≈ 444× the system star — a pull you " +
                 "feel from ~4,000 km and can't out-thrust below ~300 km. Real inverse-square law.")]
        public double gravitationalParamKm3S2 = 80000d;

        [Tooltip("Hard cap on the acceleration (m/s²) the simulation applies — keeps physics stable at close range.")]
        public float maxAccelMps2 = 900f;

        [Tooltip("Event-horizon radius (km). This is the visual black sphere AND the crush boundary.")]
        public float eventHorizonRadiusKm = 40f;

        [Header("Placement (deep space, beyond every planet)")]
        [Tooltip("Seeded distance range from the star (km). The black hole sits outside the whole planet system.")]
        public Vector2 distanceFromStarKm = new Vector2(400000f, 600000f);

        [Header("Hazard Zones")]
        [Tooltip("First warning + light damage inside this horizon distance (km). 0 = auto (150× horizon).")]
        public float warningRadiusKm = 6000f;

        [Tooltip("Damage ramps steeply inside this horizon distance (km). 0 = auto (40× horizon).")]
        public float lethalRadiusKm = 1600f;

        [Tooltip("Tidal damage per second at the lethal boundary (ramps up inside).")]
        public float tidalDamagePerSecond = 35f;

        [Tooltip("Warp-drive arrival standoff from the horizon (km). Safe, gentle-pull distance.")]
        public float standoffArrivalKm = 12000f;

        [Header("Accretion Disc")]
        [Tooltip("Authored disc normal in cosmic space (unit vector; seeded jitter is added at layout).")]
        public Vector3 discAxis = Vector3.up;

        [Tooltip("Disc inner radius in horizon radii (photon ring sits at the inner edge).")]
        public float discInnerRadiusFactor = 1.5f;

        [Tooltip("Disc outer radius (km).")]
        public float discOuterRadiusKm = 300f;

        [Range(0f, 2f)]
        [Tooltip("Disc rotation speed (rad/s).")]
        public float discSpeed = 0.35f;

        [Range(0.2f, 5f)]
        [Tooltip("Overall visual brightness.")]
        public float brightness = 1.6f;

        [Tooltip("Inner (white-hot) disc colour.")]
        public Color coreColor = new Color(1.0f, 0.96f, 0.86f, 1f);

        [Tooltip("Mid-disc colour.")]
        public Color midColor = new Color(1.0f, 0.55f, 0.16f, 1f);

        [Tooltip("Outer (deep red) disc colour.")]
        public Color outerColor = new Color(0.62f, 0.10f, 0.04f, 1f);

        [Range(0.5f, 4f)]
        [Tooltip("Photon-ring brightness multiplier.")]
        public float photonRingBrightness = 2.2f;
    }
}
