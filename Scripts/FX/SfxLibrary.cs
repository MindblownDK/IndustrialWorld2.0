// Assets/Scripts/VoxelEngine/FX/SfxLibrary.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║          INDUSTRIAL WORLD — PROCEDURAL SFX LIBRARY            ║
// ║                                                                  ║
// ║  Every sound in the game is SYNTHESISED from code — zero audio  ║
// ║  files. This keeps the repo lean and lets us tune sounds like    ║
// ║  parameters. Mirrors the synthesis style pioneered in            ║
// ║  WeatherAudio.cs.                                                ║
// ║                                                                  ║
// ║  Two clip families:                                              ║
// ║   • LOOPS  — seamless machine hums, thruster roar, ambience.     ║
// ║   • ONE-SHOTS — mining impacts, clicks, UI blips.               ║
// ║                                                                  ║
// ║  All generators are cached so a given (kind,variant) is only     ║
// ║  built once, then shared by every AudioSource that needs it.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.FX
{
    /// <summary>Identifies every procedurally-generated sound the game can play.</summary>
    public enum Sfx
    {
        // ── Machine LOOPS ──────────────────────────────────────────
        MachineHum,        // generic electric machine (furnace, refinery…)
        ElectricWhine,     // high-tech assembler / processor
        QuarryGrind,       // heavy rock grinding + servo
        DrillSpin,         // rotary drill
        EngineRumble,      // combustion engine / generator
        ThrusterAtmo,      // atmospheric thruster — airy roar
        ThrusterIon,       // ion thruster — electric hiss
        ThrusterHydrogen,  // hydrogen thruster — deep jet
        SteamHiss,         // turbines / steam systems
        FurnaceBurn,       // crackling fire loop
        ConveyorClack,     // item transport
        ReactorThrum,      // nuclear core — ominous low thrum
        WheelMotor,        // vehicle wheels rolling

        // ── Mining ONE-SHOTS (material-specific) ───────────────────
        MineStone,
        MineDirt,
        MineSand,
        MineWood,
        MineMetal,         // ores / metal voxels
        MineIce,
        MineGeneric,

        // ── UI / feedback ONE-SHOTS ────────────────────────────────
        Place,
        Pickup,
        UiClick,
        UiHover,

        // ── Ambience LOOPS ─────────────────────────────────────────
        AmbDayBirds,
        AmbNightCrickets,
        AmbWindLight,
        AmbCaveDrips,
        AmbCaveRumble
    }

    public static class SfxLibrary
    {
        public const int SAMPLE_RATE = 44100;

        // Cache: one clip per (Sfx, variantSeed). Variants let us pre-bake a few
        // randomised one-shots so repeated mining doesn't sound identical.
        private static readonly Dictionary<int, AudioClip> _cache = new();

        /// <summary>Returns (creating on first use) the clip for a given sound.</summary>
        public static AudioClip Get(Sfx sfx, int variant = 0)
        {
            int key = ((int)sfx << 8) ^ (variant & 0xFF);
            if (_cache.TryGetValue(key, out var clip) && clip != null) return clip;

            // Deterministic per (sfx,variant) so a clip sounds the same each session.
            var prevState = Random.state;
            Random.InitState(0x5151 + key);

            clip = Build(sfx, variant);

            Random.state = prevState;
            _cache[key] = clip;
            return clip;
        }

        /// <summary>Pick one of N pre-baked variants at random (for one-shots).</summary>
        public static AudioClip GetVariant(Sfx sfx, int variantCount)
            => Get(sfx, Random.Range(0, Mathf.Max(1, variantCount)));

        /// <summary>Maps a voxel material byte to the most fitting mining sound.</summary>
        public static Sfx MiningSfxForMaterial(byte material)
        {
            switch ((Materials.MaterialId)material)
            {
                case Materials.MaterialId.Stone:
                case Materials.MaterialId.Clay:
                case Materials.MaterialId.LegacySolidFloor:
                    return Sfx.MineStone;
                case Materials.MaterialId.Sand:
                    return Sfx.MineSand;
                case Materials.MaterialId.Wood:
                    return Sfx.MineWood;
                case Materials.MaterialId.Ice:
                case Materials.MaterialId.WaterVoxel:
                    return Sfx.MineIce;
                case Materials.MaterialId.Iron:
                case Materials.MaterialId.Copper:
                case Materials.MaterialId.Coal:
                case Materials.MaterialId.Nickel:
                case Materials.MaterialId.Silicon:
                case Materials.MaterialId.Cobalt:
                case Materials.MaterialId.Silver:
                case Materials.MaterialId.Gold:
                case Materials.MaterialId.Magnesium:
                case Materials.MaterialId.Platinum:
                case Materials.MaterialId.Uranium:
                    return Sfx.MineMetal;
                default:
                    return Sfx.MineGeneric;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  DISPATCH
        // ════════════════════════════════════════════════════════════
        private static AudioClip Build(Sfx sfx, int variant)
        {
            switch (sfx)
            {
                // Loops
                case Sfx.MachineHum:       return Loop("Hum",       2f, MachineHum);
                case Sfx.ElectricWhine:    return Loop("Whine",     2f, ElectricWhine);
                case Sfx.QuarryGrind:      return Loop("Quarry",    2.5f, QuarryGrind);
                case Sfx.DrillSpin:        return Loop("Drill",     1.5f, DrillSpin);
                case Sfx.EngineRumble:     return Loop("Engine",    2f, EngineRumble);
                case Sfx.ThrusterAtmo:     return Loop("ThrAtmo",   2f, ThrusterAtmo);
                case Sfx.ThrusterIon:      return Loop("ThrIon",    2f, ThrusterIon);
                case Sfx.ThrusterHydrogen: return Loop("ThrH2",     2f, ThrusterHydrogen);
                case Sfx.SteamHiss:        return Loop("Steam",     2f, SteamHiss);
                case Sfx.FurnaceBurn:      return Loop("Fire",      2.5f, FurnaceBurn);
                case Sfx.ConveyorClack:    return Loop("Conveyor",  2f, ConveyorClack);
                case Sfx.ReactorThrum:     return Loop("Reactor",   3f, ReactorThrum);
                case Sfx.WheelMotor:       return Loop("Wheel",     1.5f, WheelMotor);

                // Mining one-shots
                case Sfx.MineStone:        return OneShot("MineStone",   0.22f, d => MineImpact(d, 220f, 0.55f, 0.30f));
                case Sfx.MineDirt:         return OneShot("MineDirt",    0.20f, d => MineImpact(d, 130f, 0.20f, 0.70f));
                case Sfx.MineSand:         return OneShot("MineSand",    0.18f, d => MineImpact(d, 90f,  0.10f, 0.90f));
                case Sfx.MineWood:         return OneShot("MineWood",    0.22f, d => MineImpact(d, 320f, 0.65f, 0.35f));
                case Sfx.MineMetal:        return OneShot("MineMetal",   0.30f, d => MineMetal(d));
                case Sfx.MineIce:          return OneShot("MineIce",     0.26f, d => MineImpact(d, 900f, 0.75f, 0.45f));
                case Sfx.MineGeneric:      return OneShot("MineHit",     0.20f, d => MineImpact(d, 200f, 0.40f, 0.50f));

                // UI
                case Sfx.Place:            return OneShot("Place",       0.16f, d => MineImpact(d, 180f, 0.35f, 0.40f));
                case Sfx.Pickup:           return OneShot("Pickup",      0.18f, Pickup);
                case Sfx.UiClick:          return OneShot("UiClick",     0.09f, UiClick);
                case Sfx.UiHover:          return OneShot("UiHover",     0.06f, UiHover);

                // Ambience
                case Sfx.AmbDayBirds:      return Loop("Birds",     6f, AmbDayBirds);
                case Sfx.AmbNightCrickets: return Loop("Crickets",  4f, AmbNightCrickets);
                case Sfx.AmbWindLight:     return Loop("WindLight", 5f, AmbWindLight);
                case Sfx.AmbCaveDrips:     return Loop("CaveDrips", 6f, AmbCaveDrips);
                case Sfx.AmbCaveRumble:    return Loop("CaveRumble",5f, AmbCaveRumble);
            }
            return OneShot("Empty", 0.05f, _ => { });
        }

        // ════════════════════════════════════════════════════════════
        //  CLIP BUILDERS
        // ════════════════════════════════════════════════════════════
        private delegate void Fill(float[] data);

        private static AudioClip Loop(string name, float dur, Fill fill)
        {
            int n = (int)(SAMPLE_RATE * dur);
            var data = new float[n];
            fill(data);
            CrossfadeSeam(data, 2048);   // make loops seamless
            Normalize(data, 0.9f);
            return MakeClip(name, data);
        }

        private static AudioClip OneShot(string name, float dur, Fill fill)
        {
            int n = (int)(SAMPLE_RATE * dur);
            var data = new float[n];
            fill(data);
            Normalize(data, 0.95f);
            return MakeClip(name, data);
        }

        // ════════════════════════════════════════════════════════════
        //  LOOP SYNTHESIS
        // ════════════════════════════════════════════════════════════

        // Generic electric machine — layered low sine + 50/100Hz mains buzz + airy noise.
        private static void MachineHum(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float body  = Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.35f;
                float buzz  = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.18f
                            + Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.08f;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.94f + white * 0.06f;
                float air = lp * 0.10f;
                float wobble = 1f + 0.05f * Mathf.Sin(2f * Mathf.PI * 1.3f * t);
                d[i] = (body + buzz + air) * 0.5f * wobble;
            }
        }

        // High-tech whine — bright detuned saws + shimmer.
        private static void ElectricWhine(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float a = Saw(420f, t), b = Saw(423f, t), c = Saw(840f, t);
                float shimmer = Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.05f;
                float lfo = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 0.8f * t);
                d[i] = ((a + b) * 0.18f + c * 0.08f + shimmer) * lfo;
            }
        }

        // Quarry — heavy crushing: low rumble + periodic rock-crunch bursts + servo whir.
        private static void QuarryGrind(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float rumble = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.3f
                             + Mathf.Sin(2f * Mathf.PI * 38f * t) * 0.2f;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.85f + white * 0.15f;
                // Crunch envelope ~3.3 Hz (grinding cadence).
                float crunchEnv = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 3.3f * t)), 4f);
                float crunch = lp * crunchEnv * 0.6f;
                float servo = Mathf.Sin(2f * Mathf.PI * 320f * t) * 0.05f * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 6f * t));
                d[i] = (rumble + crunch + servo) * 0.5f;
            }
        }

        // Drill — fast rotary motor + bit chatter.
        private static void DrillSpin(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float motor = Saw(160f, t) * 0.2f + Mathf.Sin(2f * Mathf.PI * 320f * t) * 0.15f;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.7f + white * 0.3f;
                float chatter = lp * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 40f * t)) * 0.25f;
                d[i] = (motor + chatter) * 0.55f;
            }
        }

        // Combustion engine — pulsing low cylinders.
        private static void EngineRumble(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                // Firing pulses ~18 Hz.
                float fire = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 18f * t)), 3f);
                float body = Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.3f;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.9f + white * 0.1f;
                d[i] = (body + fire * 0.5f + lp * 0.15f) * 0.55f;
            }
        }

        // Atmospheric thruster — airy roar (band-limited noise + low body).
        private static void ThrusterAtmo(float[] d)
        {
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.6f + white * 0.4f;       // low-pass
                hp = white - prev; prev = white;      // high-pass (hiss)
                float roar = lp * 0.5f + hp * 0.12f;
                float body = Mathf.Sin(2f * Mathf.PI * 48f * t) * 0.25f;
                d[i] = (roar + body) * 0.6f;
            }
        }

        // Ion thruster — electric crackle + airy shimmer.
        private static void ThrusterIon(float[] d)
        {
            float bp = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.5f + white * 0.5f;
                bp = bp * 0.8f + (white - lp) * 0.2f;
                float tone = Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.06f;
                float hum = Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.12f;
                d[i] = (bp * 0.4f + tone + hum) * 0.6f;
            }
        }

        // Hydrogen thruster — deep powerful jet.
        private static void ThrusterHydrogen(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.7f + white * 0.3f;
                float jet = lp * 0.55f;
                float sub = Mathf.Sin(2f * Mathf.PI * 34f * t) * 0.35f
                          + Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.15f;
                d[i] = (jet + sub) * 0.6f;
            }
        }

        // Steam hiss — filtered noise with gentle surge.
        private static void SteamHiss(float[] d)
        {
            float hp = 0, prev = 0, lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                hp = white - prev; prev = white;
                lp = lp * 0.3f + hp * 0.7f;
                float surge = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
                d[i] = lp * 0.4f * surge;
            }
        }

        // Furnace — crackling fire: brown noise bed + random pops.
        private static void FurnaceBurn(float[] d)
        {
            float brown = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float white = Random.Range(-1f, 1f);
                brown = (brown + white * 0.02f);
                brown = Mathf.Clamp(brown, -1f, 1f);
                d[i] = brown * 0.25f;
            }
            // Random crackle pops.
            int pops = (int)(d.Length / (float)SAMPLE_RATE * 22f);
            for (int p = 0; p < pops; p++)
            {
                int s = Random.Range(0, d.Length - 400);
                float amp = Random.Range(0.1f, 0.4f);
                int len = Random.Range(60, 300);
                for (int j = 0; j < len && s + j < d.Length; j++)
                {
                    float env = Mathf.Exp(-j / (len * 0.25f));
                    d[s + j] += Random.Range(-1f, 1f) * env * amp;
                }
            }
        }

        // Conveyor — rhythmic mechanical clack + belt rumble.
        private static void ConveyorClack(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.9f + white * 0.1f;
                d[i] = lp * 0.12f;
            }
            int clacks = (int)(d.Length / (float)SAMPLE_RATE * 6f); // 6/sec
            for (int c = 0; c < clacks; c++)
            {
                int s = (int)((c + 0.5f) / clacks * d.Length);
                int len = 500;
                for (int j = 0; j < len && s + j < d.Length; j++)
                {
                    float env = Mathf.Exp(-j / 90f);
                    d[s + j] += Mathf.Sin(2f * Mathf.PI * 240f * j / SAMPLE_RATE) * env * 0.3f;
                }
            }
        }

        // Reactor — ominous low thrum + slow pulse + faint geiger-ish ticks.
        private static void ReactorThrum(float[] d)
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float sub = Mathf.Sin(2f * Mathf.PI * 42f * t) * 0.3f
                          + Mathf.Sin(2f * Mathf.PI * 28f * t) * 0.2f;
                float pulse = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 0.4f * t);
                d[i] = sub * pulse * 0.5f;
            }
            int ticks = (int)(d.Length / (float)SAMPLE_RATE * 8f);
            for (int k = 0; k < ticks; k++)
            {
                int s = Random.Range(0, d.Length - 200);
                for (int j = 0; j < 120 && s + j < d.Length; j++)
                {
                    float env = Mathf.Exp(-j / 18f);
                    d[s + j] += Random.Range(-1f, 1f) * env * 0.07f;
                }
            }
        }

        // Vehicle wheel motor — rolling friction + servo.
        private static void WheelMotor(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.8f + white * 0.2f;
                float roll = lp * 0.25f;
                float servo = Saw(120f, t) * 0.1f;
                d[i] = (roll + servo) * 0.5f;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  ONE-SHOT SYNTHESIS
        // ════════════════════════════════════════════════════════════

        // Generic mining impact: a tonal "thock" + noise burst.
        // tone   = base resonance frequency
        // ring   = how tonal vs noisy (0 = pure noise thud, 1 = clear pitch)
        // crunch = amount of granular noise texture
        private static void MineImpact(float[] d, float tone, float ring, float crunch)
        {
            int n = d.Length;
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float env = Mathf.Exp(-i / (n * 0.22f));
                float body = Mathf.Sin(2f * Mathf.PI * tone * t) * ring;
                body += Mathf.Sin(2f * Mathf.PI * tone * 1.5f * t) * ring * 0.4f;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.6f + white * 0.4f;
                float noise = lp * crunch;
                d[i] = (body + noise) * env;
            }
        }

        // Metal mining: bright clang + metallic ring overtones.
        private static void MineMetal(float[] d)
        {
            int n = d.Length;
            float f0 = 1400f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float env = Mathf.Exp(-i / (n * 0.30f));
                float clang = Mathf.Sin(2f * Mathf.PI * f0 * t) * 0.5f
                            + Mathf.Sin(2f * Mathf.PI * f0 * 2.76f * t) * 0.3f   // inharmonic
                            + Mathf.Sin(2f * Mathf.PI * f0 * 5.4f * t) * 0.15f;
                float hit = (i < n * 0.05f) ? Random.Range(-1f, 1f) * 0.5f : 0f;
                d[i] = (clang * env) + hit * Mathf.Exp(-i / (n * 0.03f));
            }
        }

        // Pickup blip — short rising tone.
        private static void Pickup(float[] d)
        {
            int n = d.Length;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float env = Mathf.Exp(-i / (n * 0.4f));
                float freq = Mathf.Lerp(600f, 1100f, (float)i / n);
                d[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.5f;
            }
        }

        // UI click — crisp, premium two-tone "tick" with a fast decay.
        private static void UiClick(float[] d)
        {
            int n = d.Length;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float env = Mathf.Exp(-i / (n * 0.18f));
                // Two stacked sines give a clean, modern "tk".
                float tone = Mathf.Sin(2f * Mathf.PI * 1500f * t) * 0.5f
                           + Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.3f;
                // Tiny noise transient at the very start for "click" attack.
                float attack = (i < n * 0.04f) ? Random.Range(-1f, 1f) * 0.4f : 0f;
                d[i] = (tone + attack) * env * 0.5f;
            }
        }

        // UI hover — softer, higher, very subtle blip.
        private static void UiHover(float[] d)
        {
            int n = d.Length;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float env = Mathf.Exp(-i / (n * 0.16f));
                float freq = Mathf.Lerp(2200f, 2700f, (float)i / n); // gentle upward chirp
                d[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.28f;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  AMBIENCE SYNTHESIS
        // ════════════════════════════════════════════════════════════

        // Daytime — sparse procedural bird chirps over very soft air.
        private static void AmbDayBirds(float[] d)
        {
            // Faint air bed.
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.99f + white * 0.01f;
                d[i] = lp * 0.04f;
            }
            // Bird chirps: short FM warbles.
            int chirps = (int)(d.Length / (float)SAMPLE_RATE * 1.4f);
            for (int c = 0; c < chirps; c++)
            {
                int s = Random.Range(0, d.Length - SAMPLE_RATE / 2);
                int len = Random.Range(SAMPLE_RATE / 14, SAMPLE_RATE / 6);
                float baseF = Random.Range(1800f, 3600f);
                float vibRate = Random.Range(25f, 60f);
                float vibDepth = Random.Range(120f, 400f);
                int syllables = Random.Range(1, 4);
                for (int sy = 0; sy < syllables; sy++)
                {
                    int ss = s + sy * (len + len / 3);
                    for (int j = 0; j < len && ss + j < d.Length; j++)
                    {
                        float tt = (float)j / SAMPLE_RATE;
                        float env = Mathf.Sin(Mathf.PI * j / len);   // bell
                        env *= env;
                        float f = baseF + Mathf.Sin(2f * Mathf.PI * vibRate * tt) * vibDepth
                                + tt * 400f; // slight rise
                        d[ss + j] += Mathf.Sin(2f * Mathf.PI * f * tt) * env * 0.16f;
                    }
                }
            }
        }

        // Night — cricket pulses + low air.
        private static void AmbNightCrickets(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.995f + white * 0.005f;
                d[i] = lp * 0.03f;
            }
            // Several cricket "voices", each a fast amplitude-modulated tone.
            int voices = 5;
            for (int v = 0; v < voices; v++)
            {
                float freq = Random.Range(3800f, 4800f);
                float chirpRate = Random.Range(22f, 32f);
                float phase = Random.value * 10f;
                float pan = Random.Range(0.3f, 1f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = (float)i / SAMPLE_RATE;
                    // Chirp groups: on for a bit, off for a bit.
                    float group = Mathf.Sin(2f * Mathf.PI * 0.7f * t + phase) > 0.2f ? 1f : 0f;
                    float am = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * chirpRate * t)), 6f);
                    d[i] += Mathf.Sin(2f * Mathf.PI * freq * t) * am * group * 0.04f * pan;
                }
            }
        }

        // Gentle wind bed (outdoor neutral ambience).
        private static void AmbWindLight(float[] d)
        {
            float b0 = 0, b1 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                b0 = b0 * 0.985f + white * 0.015f;
                b1 = b1 * 0.95f + b0 * 0.05f;
                float mod = 0.5f + 0.5f * Mathf.Sin(t * 0.18f + Mathf.Sin(t * 0.05f) * 3f);
                d[i] = b1 * mod * 0.22f;
            }
        }

        // Cave — occasional water drips with reverb tail.
        private static void AmbCaveDrips(float[] d)
        {
            int drips = (int)(d.Length / (float)SAMPLE_RATE * 0.8f);
            for (int k = 0; k < drips; k++)
            {
                int s = Random.Range(0, d.Length - SAMPLE_RATE);
                float f = Random.Range(900f, 1900f);
                float amp = Random.Range(0.15f, 0.35f);
                int len = SAMPLE_RATE / 2;
                for (int j = 0; j < len && s + j < d.Length; j++)
                {
                    float tt = (float)j / SAMPLE_RATE;
                    // Pitch drops quickly (the "ploink").
                    float fr = f * Mathf.Exp(-tt * 6f);
                    float env = Mathf.Exp(-j / (len * 0.12f));
                    d[s + j] += Mathf.Sin(2f * Mathf.PI * fr * tt) * env * amp;
                }
            }
        }

        // Cave — deep ominous rumble bed.
        private static void AmbCaveRumble(float[] d)
        {
            float lp = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SAMPLE_RATE;
                float white = Random.Range(-1f, 1f);
                lp = lp * 0.997f + white * 0.003f;
                float sub = Mathf.Sin(2f * Mathf.PI * 32f * t) * 0.15f
                          + Mathf.Sin(2f * Mathf.PI * 21f * t) * 0.1f;
                float mod = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.12f * t);
                d[i] = (lp * 0.5f + sub) * mod * 0.4f;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  DSP UTILITIES
        // ════════════════════════════════════════════════════════════
        private static float Saw(float freq, float t)
        {
            float p = t * freq;
            return 2f * (p - Mathf.Floor(p + 0.5f));
        }

        /// <summary>Crossfade the clip's tail into its head so loops are seamless.</summary>
        private static void CrossfadeSeam(float[] d, int fade)
        {
            fade = Mathf.Min(fade, d.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;          // 0..1
                int tail = d.Length - fade + i;
                float blended = d[tail] * (1f - k) + d[i] * k;
                d[i]    = blended;                  // head becomes the blend
                d[tail] = blended;                  // tail mirrors it
            }
        }

        private static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > max) max = a; }
            if (max < 1e-4f) return;
            float g = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
        }

        private static AudioClip MakeClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SAMPLE_RATE, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
