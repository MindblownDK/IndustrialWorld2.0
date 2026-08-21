// Assets/Scripts/VoxelEngine/WaterSim/LiquidVisualProfile.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║        LIQUID VISUAL PROFILE — every liquid looks like ITSELF        ║
// ║                                                                      ║
// ║  The mesh builder creates one material instance per liquid from      ║
// ║  these profiles. Each liquid gets its own colours, wave character,   ║
// ║  gloss, iridescent sheen and emission — water shimmers with foam,    ║
// ║  crude oil sits black and glossy, refined products carry rainbow     ║
// ║  thin-film sheens, and engine coolant glows.                         ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    public struct LiquidVisualProfile
    {
        public Color ShallowColor;
        public Color DeepColor;
        public Color FoamColor;

        public float DeepWaveAmplitude;
        public float DeepWaveFrequency;
        public float DeepWaveSpeed;
        public float SecondaryWaveAmplitude;
        public float SecondaryWaveFrequency;
        public float SecondaryWaveSpeed;
        public float ShallowWaveAmplitude;
        public float ShallowWaveFrequency;
        public float ShallowWaveSpeed;
        public float WaveChop;
        public float NormalScale;
        public float Gloss;
        public float FresnelPower;
        public float RefractionStrength;
        public float CausticsIntensity;
        public float DepthFade;
        public float ShoreOpaqueDepth;
        public float ShoreFoamWidth;
        public float ShoreFoamIntensity;
        public float SssIntensity;
        public float FlowNormalStrength;
        public float FlowFoamStrength;
        public float TideStrength;

        // 9.16.0 — thin-film iridescence + emission (the "real texture" layer).
        public float IridescenceStrength;   // 0 = none (water), 1 = full rainbow (fuels/oils)
        public float IridescenceScale;      // how fast the hue cycles with the view angle
        public Color EmissionColor;
        public float EmissionStrength;      // 0 = none, >0 = glow (coolant)

        public static LiquidVisualProfile For(LiquidType t) => t switch
        {
            LiquidType.CrudeOil            => CrudeOil,
            LiquidType.RefinedOil          => RefinedOil,
            LiquidType.LiquidFuel          => LiquidFuel,
            LiquidType.HeavyFuelOil        => HeavyFuelOil,
            LiquidType.MarineGasOil        => MarineGasOil,
            LiquidType.MarineEngineCoolant => Coolant,
            _                              => Water,
        };

        // ── WATER — lively, foamy, blue-green ──────────────────────
        public static readonly LiquidVisualProfile Water = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.10f, 0.58f, 0.86f, 0.96f),
            DeepColor    = new Color(0.01f, 0.06f, 0.22f, 0.995f),
            FoamColor    = new Color(0.92f, 0.96f, 1.00f, 0.88f),
            DeepWaveAmplitude = 0.85f, DeepWaveFrequency = 0.22f, DeepWaveSpeed = 0.55f,
            SecondaryWaveAmplitude = 0.35f, SecondaryWaveFrequency = 0.47f, SecondaryWaveSpeed = 0.91f,
            ShallowWaveAmplitude = 0.16f, ShallowWaveFrequency = 1.65f, ShallowWaveSpeed = 1.8f,
            WaveChop = 0.28f, NormalScale = 2.4f, Gloss = 0.97f, FresnelPower = 4.2f,
            RefractionStrength = 0.045f, CausticsIntensity = 0.62f, DepthFade = 2.5f,
            ShoreOpaqueDepth = 1.5f, ShoreFoamWidth = 2.0f, ShoreFoamIntensity = 1.4f,
            SssIntensity = 0.62f, FlowNormalStrength = 1.0f, FlowFoamStrength = 0.8f,
            TideStrength = 0.22f,
            IridescenceStrength = 0.0f, IridescenceScale = 1.0f,
            EmissionColor = Color.black, EmissionStrength = 0f,
        };

        // ── CRUDE OIL — near-black, viscous, faint sickly sheen ────
        public static readonly LiquidVisualProfile CrudeOil = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.12f, 0.085f, 0.05f, 0.90f),
            DeepColor    = new Color(0.02f, 0.015f, 0.01f, 0.98f),
            FoamColor    = new Color(0.35f, 0.25f, 0.12f, 0.40f),
            DeepWaveAmplitude = 0.04f, DeepWaveFrequency = 0.40f, DeepWaveSpeed = 0.12f,
            SecondaryWaveAmplitude = 0.025f, SecondaryWaveFrequency = 0.85f, SecondaryWaveSpeed = 0.08f,
            ShallowWaveAmplitude = 0.015f, ShallowWaveFrequency = 1.1f, ShallowWaveSpeed = 0.16f,
            WaveChop = 0.06f, NormalScale = 0.45f, Gloss = 1.0f, FresnelPower = 4.0f,
            RefractionStrength = 0.004f, CausticsIntensity = 0.0f, DepthFade = 1.8f,
            ShoreOpaqueDepth = 1.0f, ShoreFoamWidth = 0.5f, ShoreFoamIntensity = 0.1f,
            SssIntensity = 0.0f, FlowNormalStrength = 0.3f, FlowFoamStrength = 0.2f,
            TideStrength = 0.04f,
            IridescenceStrength = 0.55f, IridescenceScale = 0.8f,
            EmissionColor = Color.black, EmissionStrength = 0f,
        };

        // ── REFINED OIL — amber, transparent, strong rainbow sheen ─
        public static readonly LiquidVisualProfile RefinedOil = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.62f, 0.42f, 0.16f, 0.92f),
            DeepColor    = new Color(0.28f, 0.17f, 0.05f, 0.985f),
            FoamColor    = new Color(0.75f, 0.62f, 0.32f, 0.55f),
            DeepWaveAmplitude = 0.10f, DeepWaveFrequency = 0.50f, DeepWaveSpeed = 0.22f,
            SecondaryWaveAmplitude = 0.06f, SecondaryWaveFrequency = 1.0f, SecondaryWaveSpeed = 0.15f,
            ShallowWaveAmplitude = 0.03f, ShallowWaveFrequency = 1.3f, ShallowWaveSpeed = 0.3f,
            WaveChop = 0.10f, NormalScale = 0.7f, Gloss = 1.0f, FresnelPower = 3.6f,
            RefractionStrength = 0.012f, CausticsIntensity = 0.08f, DepthFade = 1.6f,
            ShoreOpaqueDepth = 0.8f, ShoreFoamWidth = 0.6f, ShoreFoamIntensity = 0.35f,
            SssIntensity = 0.15f, FlowNormalStrength = 0.55f, FlowFoamStrength = 0.35f,
            TideStrength = 0.10f,
            IridescenceStrength = 1.0f, IridescenceScale = 1.6f,
            EmissionColor = Color.black, EmissionStrength = 0f,
        };

        // ── LIQUID FUEL — bright amber, volatile, fast shimmer ─────
        public static readonly LiquidVisualProfile LiquidFuel = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.95f, 0.62f, 0.16f, 0.94f),
            DeepColor    = new Color(0.55f, 0.28f, 0.05f, 0.99f),
            FoamColor    = new Color(1.00f, 0.85f, 0.55f, 0.60f),
            DeepWaveAmplitude = 0.16f, DeepWaveFrequency = 0.55f, DeepWaveSpeed = 0.34f,
            SecondaryWaveAmplitude = 0.09f, SecondaryWaveFrequency = 1.1f, SecondaryWaveSpeed = 0.24f,
            ShallowWaveAmplitude = 0.05f, ShallowWaveFrequency = 1.5f, ShallowWaveSpeed = 0.45f,
            WaveChop = 0.14f, NormalScale = 0.85f, Gloss = 1.0f, FresnelPower = 3.4f,
            RefractionStrength = 0.016f, CausticsIntensity = 0.05f, DepthFade = 1.4f,
            ShoreOpaqueDepth = 0.7f, ShoreFoamWidth = 0.7f, ShoreFoamIntensity = 0.5f,
            SssIntensity = 0.25f, FlowNormalStrength = 0.7f, FlowFoamStrength = 0.5f,
            TideStrength = 0.12f,
            IridescenceStrength = 1.0f, IridescenceScale = 2.0f,
            EmissionColor = Color.black, EmissionStrength = 0f,
        };

        // ── HEAVY FUEL OIL — tar black, matte, barely moves ────────
        public static readonly LiquidVisualProfile HeavyFuelOil = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.13f, 0.10f, 0.07f, 0.97f),
            DeepColor    = new Color(0.03f, 0.02f, 0.015f, 0.995f),
            FoamColor    = new Color(0.30f, 0.22f, 0.12f, 0.25f),
            DeepWaveAmplitude = 0.02f, DeepWaveFrequency = 0.35f, DeepWaveSpeed = 0.06f,
            SecondaryWaveAmplitude = 0.012f, SecondaryWaveFrequency = 0.7f, SecondaryWaveSpeed = 0.04f,
            ShallowWaveAmplitude = 0.008f, ShallowWaveFrequency = 0.9f, ShallowWaveSpeed = 0.08f,
            WaveChop = 0.03f, NormalScale = 0.30f, Gloss = 0.55f, FresnelPower = 5.0f,
            RefractionStrength = 0.002f, CausticsIntensity = 0.0f, DepthFade = 1.2f,
            ShoreOpaqueDepth = 0.6f, ShoreFoamWidth = 0.3f, ShoreFoamIntensity = 0.06f,
            SssIntensity = 0.0f, FlowNormalStrength = 0.15f, FlowFoamStrength = 0.1f,
            TideStrength = 0.02f,
            IridescenceStrength = 0.25f, IridescenceScale = 0.5f,
            EmissionColor = Color.black, EmissionStrength = 0f,
        };

        // ── MARINE GAS OIL — pale green-amber distillate, thin ─────
        public static readonly LiquidVisualProfile MarineGasOil = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.80f, 0.72f, 0.34f, 0.90f),
            DeepColor    = new Color(0.35f, 0.28f, 0.10f, 0.98f),
            FoamColor    = new Color(0.95f, 0.88f, 0.55f, 0.45f),
            DeepWaveAmplitude = 0.12f, DeepWaveFrequency = 0.50f, DeepWaveSpeed = 0.28f,
            SecondaryWaveAmplitude = 0.07f, SecondaryWaveFrequency = 1.0f, SecondaryWaveSpeed = 0.20f,
            ShallowWaveAmplitude = 0.04f, ShallowWaveFrequency = 1.4f, ShallowWaveSpeed = 0.38f,
            WaveChop = 0.12f, NormalScale = 0.75f, Gloss = 0.95f, FresnelPower = 3.8f,
            RefractionStrength = 0.014f, CausticsIntensity = 0.06f, DepthFade = 1.5f,
            ShoreOpaqueDepth = 0.7f, ShoreFoamWidth = 0.6f, ShoreFoamIntensity = 0.4f,
            SssIntensity = 0.2f, FlowNormalStrength = 0.6f, FlowFoamStrength = 0.4f,
            TideStrength = 0.1f,
            IridescenceStrength = 0.85f, IridescenceScale = 1.4f,
            EmissionColor = Color.black, EmissionStrength = 0f,
        };

        // ── COOLANT — bright cyan, emissive glow, watery motion ────
        public static readonly LiquidVisualProfile Coolant = new LiquidVisualProfile
        {
            ShallowColor = new Color(0.16f, 0.85f, 0.78f, 0.93f),
            DeepColor    = new Color(0.02f, 0.30f, 0.34f, 0.99f),
            FoamColor    = new Color(0.55f, 0.98f, 0.95f, 0.85f),
            DeepWaveAmplitude = 0.55f, DeepWaveFrequency = 0.30f, DeepWaveSpeed = 0.45f,
            SecondaryWaveAmplitude = 0.25f, SecondaryWaveFrequency = 0.55f, SecondaryWaveSpeed = 0.75f,
            ShallowWaveAmplitude = 0.12f, ShallowWaveFrequency = 1.5f, ShallowWaveSpeed = 1.5f,
            WaveChop = 0.20f, NormalScale = 1.7f, Gloss = 0.98f, FresnelPower = 3.9f,
            RefractionStrength = 0.030f, CausticsIntensity = 0.4f, DepthFade = 2.0f,
            ShoreOpaqueDepth = 1.1f, ShoreFoamWidth = 1.4f, ShoreFoamIntensity = 1.0f,
            SssIntensity = 0.45f, FlowNormalStrength = 0.85f, FlowFoamStrength = 0.7f,
            TideStrength = 0.15f,
            IridescenceStrength = 0.15f, IridescenceScale = 1.0f,
            EmissionColor = new Color(0.10f, 0.85f, 0.80f), EmissionStrength = 0.75f,
        };

        /// <summary>Applies this profile to a material instance (property-safe).</summary>
        public void ApplyTo(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_ShallowColor")) mat.SetColor("_ShallowColor", ShallowColor);
            if (mat.HasProperty("_DeepColor")) mat.SetColor("_DeepColor", DeepColor);
            if (mat.HasProperty("_FoamColor")) mat.SetColor("_FoamColor", FoamColor);
            if (mat.HasProperty("_DeepWaveAmplitude")) mat.SetFloat("_DeepWaveAmplitude", DeepWaveAmplitude);
            if (mat.HasProperty("_DeepWaveFrequency")) mat.SetFloat("_DeepWaveFrequency", DeepWaveFrequency);
            if (mat.HasProperty("_DeepWaveSpeed")) mat.SetFloat("_DeepWaveSpeed", DeepWaveSpeed);
            if (mat.HasProperty("_SecondaryWaveAmplitude")) mat.SetFloat("_SecondaryWaveAmplitude", SecondaryWaveAmplitude);
            if (mat.HasProperty("_SecondaryWaveFrequency")) mat.SetFloat("_SecondaryWaveFrequency", SecondaryWaveFrequency);
            if (mat.HasProperty("_SecondaryWaveSpeed")) mat.SetFloat("_SecondaryWaveSpeed", SecondaryWaveSpeed);
            if (mat.HasProperty("_ShallowWaveAmplitude")) mat.SetFloat("_ShallowWaveAmplitude", ShallowWaveAmplitude);
            if (mat.HasProperty("_ShallowWaveFrequency")) mat.SetFloat("_ShallowWaveFrequency", ShallowWaveFrequency);
            if (mat.HasProperty("_ShallowWaveSpeed")) mat.SetFloat("_ShallowWaveSpeed", ShallowWaveSpeed);
            if (mat.HasProperty("_WaveChop")) mat.SetFloat("_WaveChop", WaveChop);
            if (mat.HasProperty("_NormalScale")) mat.SetFloat("_NormalScale", NormalScale);
            if (mat.HasProperty("_Gloss")) mat.SetFloat("_Gloss", Gloss);
            if (mat.HasProperty("_FresnelPower")) mat.SetFloat("_FresnelPower", FresnelPower);
            if (mat.HasProperty("_RefractionStrength")) mat.SetFloat("_RefractionStrength", RefractionStrength);
            if (mat.HasProperty("_CausticsIntensity")) mat.SetFloat("_CausticsIntensity", CausticsIntensity);
            if (mat.HasProperty("_DepthFade")) mat.SetFloat("_DepthFade", DepthFade);
            if (mat.HasProperty("_ShoreOpaqueDepth")) mat.SetFloat("_ShoreOpaqueDepth", ShoreOpaqueDepth);
            if (mat.HasProperty("_ShoreFoamWidth")) mat.SetFloat("_ShoreFoamWidth", ShoreFoamWidth);
            if (mat.HasProperty("_ShoreFoamIntensity")) mat.SetFloat("_ShoreFoamIntensity", ShoreFoamIntensity);
            if (mat.HasProperty("_SSSIntensity")) mat.SetFloat("_SSSIntensity", SssIntensity);
            if (mat.HasProperty("_FlowNormalStrength")) mat.SetFloat("_FlowNormalStrength", FlowNormalStrength);
            if (mat.HasProperty("_FlowFoamStrength")) mat.SetFloat("_FlowFoamStrength", FlowFoamStrength);
            if (mat.HasProperty("_TideStrength")) mat.SetFloat("_TideStrength", TideStrength);
            if (mat.HasProperty("_IridescenceStrength")) mat.SetFloat("_IridescenceStrength", IridescenceStrength);
            if (mat.HasProperty("_IridescenceScale")) mat.SetFloat("_IridescenceScale", IridescenceScale);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", EmissionColor);
            if (mat.HasProperty("_EmissionStrength")) mat.SetFloat("_EmissionStrength", EmissionStrength);
        }
    }
}
