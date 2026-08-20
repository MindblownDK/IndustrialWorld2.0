// Assets/Scripts/VoxelEngine/Cosmos/QuasarSettings.cs
//
// Designer settings for the REAL quasar body of a solar system (Phase 5).
//
// A quasar is a supermassive black hole mid-feast: the same event horizon + accretion
// disc as the black hole body, PLUS two relativistic polar jets that shear anything
// crossing them. It is a separate body type — the registry places it at a seeded
// deep-space position even farther out than the black hole.
//
// The legacy backdrop fields (skyDirection, apparentSize, …) are kept for the old
// billboard renderer; `realBody` promotes the quasar to a genuine flyable body.
using System;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [Serializable]
    public class QuasarSettings
    {
        [Tooltip("Master enable for this quasar (body and/or backdrop).")]
        public bool enabled = true;

        [Tooltip("TRUE = the quasar is a REAL distant cosmic body (Phase 5) with gravity, a flyable " +
                 "event horizon, and lethal jets. FALSE = legacy pinned backdrop only.")]
        public bool realBody = true;

        [Tooltip("True after Step 52 has initialized the real-body fields. Editor-only gate so the " +
                 "setup step can fill defaults ONCE without ever overwriting authored values.")]
        public bool configured;

        [Header("Physics (real body)")]
        [Tooltip("Gravitational parameter μ (km³/s²). 500,000 ≈ 2,778× the system star — the dominant " +
                 "pull across its whole deep-space neighbourhood.")]
        public double gravitationalParamKm3S2 = 500000d;

        [Tooltip("Hard cap on the acceleration (m/s²) the simulation applies.")]
        public float maxAccelMps2 = 1200f;

        [Tooltip("Event-horizon radius (km). Visual black sphere AND the crush boundary.")]
        public float eventHorizonRadiusKm = 60f;

        [Header("Placement (deep space, beyond the black hole)")]
        [Tooltip("Seeded distance range from the star (km).")]
        public Vector2 distanceFromStarKm = new Vector2(800000f, 1200000f);

        [Header("Hazard Zones")]
        [Tooltip("First warning + light damage inside this horizon distance (km). 0 = auto (150× horizon).")]
        public float warningRadiusKm = 8000f;

        [Tooltip("Damage ramps steeply inside this horizon distance (km). 0 = auto (40× horizon).")]
        public float lethalRadiusKm = 2200f;

        [Tooltip("Tidal damage per second at the lethal boundary (ramps up inside).")]
        public float tidalDamagePerSecond = 40f;

        [Tooltip("Warp-drive arrival standoff from the horizon (km).")]
        public float standoffArrivalKm = 15000f;

        [Header("Accretion Disc (real body)")]
        [Tooltip("Authored disc normal in cosmic space (unit vector; seeded jitter added at layout).")]
        public Vector3 discAxis = Vector3.forward;

        [Tooltip("Disc inner radius in horizon radii.")]
        public float discInnerRadiusFactor = 1.5f;

        [Tooltip("Disc outer radius (km).")]
        public float discOuterRadiusKm = 450f;

        [Range(0f, 2f)]
        [Tooltip("Disc rotation speed (rad/s).")]
        public float discSpeed = 0.22f;

        [Header("Relativistic Jets")]
        [Tooltip("Jet length (km) along ±disc normal.")]
        public float jetLengthKm = 3200f;

        [Tooltip("Lethal core radius of each jet (km). Inside: radiation shear damage.")]
        public float jetCoreRadiusKm = 130f;

        [Tooltip("Damage per second at the jet core axis (falls off toward the edge).")]
        public float jetDamagePerSecond = 45f;

        [Tooltip("Visual width of the jet beams (km).")]
        public float jetVisualWidthKm = 60f;

        [Header("Appearance")]
        [Range(0.2f, 5f)]
        [Tooltip("Overall visual brightness.")]
        public float brightness = 1.8f;

        [Tooltip("Bright accretion-disc core colour.")]
        public Color coreColor = new Color(0.62f, 0.82f, 1.0f, 1f);

        [Tooltip("Colour of the relativistic polar jets.")]
        public Color jetColor = new Color(0.40f, 0.60f, 1.0f, 0.9f);

        [Tooltip("Disc mid colour.")]
        public Color midColor = new Color(0.85f, 0.55f, 0.25f, 1f);

        [Tooltip("Disc outer colour.")]
        public Color outerColor = new Color(0.45f, 0.12f, 0.06f, 1f);

        [Range(0.5f, 4f)]
        [Tooltip("Photon-ring brightness multiplier.")]
        public float photonRingBrightness = 2.4f;

        [Header("Legacy backdrop (billboard renderer)")]
        [Tooltip("Direction (from origin) the legacy backdrop sits at on the skybox. Unused when realBody.")]
        public Vector3 skyDirection = new Vector3(0.3f, 0.4f, 1f);

        [Range(0.1f, 4f)]
        [Tooltip("Apparent angular size of the legacy backdrop core on screen. Unused when realBody.")]
        public float apparentSize = 1.2f;
    }
}
