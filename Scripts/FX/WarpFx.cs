// Assets/Scripts/VoxelEngine/FX/WarpFx.cs
//
// The warp jump effect: the ship is swallowed by an energy bubble, streaks
// stretch past the hull, the screen tunnels and the FOV punches wide — then
// the ship drops out at the destination and the bubble pops.
//
// Fully procedural (shader + generated textures + synthesized audio), no assets.
//
// Sequencing is async: GridWarpDrive.TryWarp initiates, WarpFx plays the short
// pre-phase, THEN invokes the teleport callback, then plays transit + fade-out.
// Screen FX (overlay, FOV, shake, audio) only run for the grid the local player
// is aboard; the bubble + streak particles play for every jumping grid, so any
// observer sees the ship warp.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Player;

namespace VoxelEngine.FX
{
    public static class WarpFx
    {
        public const float PreSeconds = 0.55f;
        public const float TransitSeconds = 2.4f;
        public const float OutSeconds = 0.9f;
        private const float TransitFovKick = 12f;
        private const float StreakRate = 200f;

        private class Jump
        {
            public GridWarpDrive drive;
            public GridEntity grid;
            public Action onJump;
            public float t;
            public bool jumped;
            public bool screenFx;
            public GameObject bubble;
            public Material bubbleMat;
            public float bubbleDiameter;
            public ParticleSystem streaks;
            public float nextRumbleAt;
        }

        private static readonly List<Jump> _active = new();
        private static readonly HashSet<GridWarpDrive> _pending = new();
        private static WarpFxDriver _driver;

        // ── Screen overlay (UIToolkit, mounted into the HUD layer) ──
        private static VisualElement _overlayRoot;
        private static VisualElement _overlay;
        private static VisualElement _edgeLayer;
        private static VisualElement _streakLayer;
        private static VisualElement _flashLayer;
        private static Texture2D _streakTex;
        private static Texture2D _edgeTex;
        private static float _flash;

        /// <summary>True while a drive's jump is initiated but the teleport hasn't fired.</summary>
        public static bool IsPending(GridWarpDrive drive) => drive != null && _pending.Contains(drive);

        /// <summary>Drop a drive's jump entirely (block removed mid-effect).</summary>
        public static void CancelFor(GridWarpDrive drive)
        {
            if (drive == null) return;
            _pending.Remove(drive);
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].drive == drive || _active[i].drive == null)
                {
                    Teardown(_active[i]);
                    _active.RemoveAt(i);
                }
            }
        }

        private static GridEntity _arrivalGrid;
        private static string _arrivalLine = "";
        private static float _arrivalAt = -999f;
        private const float ArrivalHoldSeconds = 25f;

        /// <summary>Remember the arrival readout for one grid's cockpit LCD.</summary>
        public static void ReportArrival(GridEntity grid, string line)
        {
            _arrivalGrid = grid;
            _arrivalLine = line ?? "";
            _arrivalAt = Time.unscaledTime;
        }

        /// <summary>Fresh arrival line for this grid, or empty when expired.</summary>
        public static string ArrivalLineFor(GridEntity grid)
        {
            if (grid == null || grid != _arrivalGrid) return "";
            if (Time.unscaledTime - _arrivalAt > ArrivalHoldSeconds) return "";
            return _arrivalLine;
        }

        /// <summary>
        /// Start the jump sequence: pre-phase, then onJump (the teleport), then
        /// transit + fade-out. Returns false when this drive already has a jump
        /// in flight (the caller treats that as "jump in progress").
        /// </summary>
        public static bool PlayJump(GridWarpDrive drive, Action onJump)
        {
            if (drive == null || drive.Grid == null || onJump == null) return false;
            if (_pending.Contains(drive)) return false;
            _pending.Add(drive);
            EnsureDriver();

            var grid = drive.Grid;
            var j = new Jump
            {
                drive = drive,
                grid = grid,
                onJump = onJump,
                screenFx = grid.IsControlled,
                bubbleDiameter = EstimateDiameter(grid)
            };

            var shader = Shader.Find("VoxelEngine/WarpBubble");
            if (shader != null)
            {
                j.bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                j.bubble.name = "~WarpBubble";
                var col = j.bubble.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);
                j.bubble.transform.SetParent(grid.transform, false);
                j.bubble.transform.position = grid.GetGridCenter();
                j.bubble.transform.localScale = Vector3.one * 0.01f;
                j.bubbleMat = new Material(shader);
                var rend = j.bubble.GetComponent<MeshRenderer>();
                rend.sharedMaterial = j.bubbleMat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            j.streaks = BuildStreaks(grid, j.bubbleDiameter);

            _active.Add(j);
            if (j.screenFx) AudioManager.PlayUI(RiserClip, 0.5f);
            return true;
        }

        public static void EnsureOverlayMounted(VisualElement uiRoot)
        {
            if (_overlayRoot == uiRoot && _overlay != null && _overlay.parent == uiRoot) return;
            _overlayRoot = uiRoot;
            if (_overlay != null) _overlay.RemoveFromHierarchy();

            EnsureTextures();
            _overlay = new VisualElement { name = "WarpFxOverlay" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.style.display = DisplayStyle.None;
            _overlay.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_overlay);

            _edgeLayer = FullLayer("WarpEdge", _edgeTex);
            _streakLayer = FullLayer("WarpStreaks", _streakTex);
            _flashLayer = new VisualElement { name = "WarpFlash" };
            _flashLayer.style.position = Position.Absolute;
            _flashLayer.style.left = 0;
            _flashLayer.style.top = 0;
            _flashLayer.style.right = 0;
            _flashLayer.style.bottom = 0;
            _flashLayer.style.backgroundColor = new StyleColor(Color.white);
            _flashLayer.style.opacity = 0f;
            _flashLayer.pickingMode = PickingMode.Ignore;
            _overlay.Add(_edgeLayer);
            _overlay.Add(_streakLayer);
            _overlay.Add(_flashLayer);
        }

        internal static void Tick(float dt)
        {
            float streakTarget = 0f, edgeTarget = 0f;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var j = _active[i];
                if (j.drive == null || j.grid == null) { Teardown(j); _active.RemoveAt(i); continue; }
                j.t += dt;

                if (!j.jumped && j.t >= PreSeconds)
                {
                    j.jumped = true;
                    _pending.Remove(j.drive);
                    try { j.onJump?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    if (j.grid != null)
                    {
                        Vector3 center = j.grid.GetGridCenter();
                        if (j.bubble != null) j.bubble.transform.position = center;
                        if (j.streaks != null) j.streaks.transform.position = center;
                    }
                    if (j.screenFx)
                    {
                        _flash = 1f;
                        CameraFeedback.AddShake(0.75f);
                        AudioManager.PlayUI(WhooshClip, 0.55f);
                        j.nextRumbleAt = j.t + 0.45f;
                    }
                }

                float total = PreSeconds + TransitSeconds + OutSeconds;
                if (j.t >= total) { Teardown(j); _active.RemoveAt(i); continue; }

                // Bubble: inflate through pre, hold + breathe through transit, pop out.
                if (j.bubbleMat != null)
                {
                    float env, inten;
                    if (j.t < PreSeconds)
                    {
                        float k = j.t / PreSeconds;
                        env = 1f - (1f - k) * (1f - k);
                        inten = 0.7f + 0.8f * k;
                    }
                    else if (j.t < PreSeconds + TransitSeconds)
                    {
                        env = 1f;
                        inten = 1.5f + 0.35f * Mathf.Sin(j.t * 9f);
                    }
                    else
                    {
                        float k = (j.t - PreSeconds - TransitSeconds) / OutSeconds;
                        env = 1f + 0.3f * k;
                        inten = 1.5f * (1f - k);
                    }
                    if (j.bubble != null)
                    {
                        float gs = j.grid.transform.lossyScale.x;
                        if (Mathf.Abs(gs) < 0.001f) gs = 1f;
                        j.bubble.transform.localScale = Vector3.one * (j.bubbleDiameter * env / gs);
                    }
                    j.bubbleMat.SetFloat("_Intensity", Mathf.Max(0f, inten));
                }

                // Streaks: spin up through pre, full blast in transit, die in out.
                if (j.streaks != null)
                {
                    var emission = j.streaks.emission;
                    if (j.t < PreSeconds) emission.rateOverTime = StreakRate * (j.t / PreSeconds);
                    else if (j.t < PreSeconds + TransitSeconds) emission.rateOverTime = StreakRate;
                    else emission.rateOverTime = 0f;
                }

                if (j.screenFx)
                {
                    float s, e;
                    if (j.t < PreSeconds)
                    {
                        float k = j.t / PreSeconds;
                        s = 0.20f * k;
                        e = 0.32f * k;
                    }
                    else if (j.t < PreSeconds + TransitSeconds)
                    {
                        s = 0.8f;
                        e = 0.32f + 0.08f * Mathf.Sin(j.t * 7f);
                    }
                    else
                    {
                        float k = (j.t - PreSeconds - TransitSeconds) / OutSeconds;
                        s = 0.8f * (1f - k);
                        e = 0.32f * (1f - k);
                    }
                    if (s > streakTarget) streakTarget = s;
                    if (e > edgeTarget) edgeTarget = e;
                    if (j.jumped && j.t < PreSeconds + TransitSeconds)
                    {
                        // Hold the wide FOV through transit (it decays unless re-applied).
                        float ramp = Mathf.Min(1f, (j.t - PreSeconds) / 0.35f);
                        CameraFeedback.AddFovSqueeze(TransitFovKick * ramp);
                        if (j.t >= j.nextRumbleAt)
                        {
                            j.nextRumbleAt = j.t + 0.45f;
                            CameraFeedback.AddShake(0.3f);
                        }
                    }
                }
            }
            _flash = Mathf.Max(0f, _flash - dt * 4f);
            ApplyOverlay(streakTarget, edgeTarget);
        }

        private static void Teardown(Jump j)
        {
            _pending.Remove(j.drive);
            if (j.bubble != null) UnityEngine.Object.Destroy(j.bubble);
            if (j.bubbleMat != null) UnityEngine.Object.Destroy(j.bubbleMat);
            if (j.streaks != null) UnityEngine.Object.Destroy(j.streaks.gameObject);
        }

        private static void EnsureDriver()
        {
            if (_driver != null) return;
            var go = new GameObject("~WarpFx");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            _driver = go.AddComponent<WarpFxDriver>();
        }

        private static float EstimateDiameter(GridEntity grid)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var c in grid.GetComponentsInChildren<Collider>())
            {
                if (c == null) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            float d = any ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : 10f;
            return Mathf.Clamp(d + 6f, 12f, 220f);
        }

        private static void ApplyOverlay(float streak, float edge)
        {
            if (_overlay == null) return;
            bool show = streak > 0.003f || edge > 0.003f || _flash > 0.003f;
            _overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _overlay.BringToFront();
            _streakLayer.style.opacity = Mathf.Clamp01(streak);
            float zoom = 1f + 0.30f * Mathf.Clamp01(streak);
            _streakLayer.style.scale = new StyleScale(new Scale(new Vector2(zoom, zoom)));
            _edgeLayer.style.opacity = Mathf.Clamp01(edge);
            _flashLayer.style.opacity = Mathf.Clamp01(_flash);
        }

        private static VisualElement FullLayer(string name, Texture2D tex)
        {
            var v = new VisualElement { name = name };
            v.style.position = Position.Absolute;
            v.style.left = 0;
            v.style.top = 0;
            v.style.right = 0;
            v.style.bottom = 0;
            v.style.backgroundImage = new StyleBackground(tex);
            v.style.opacity = 0f;
            v.pickingMode = PickingMode.Ignore;
            return v;
        }

        // ── Generated textures ──
        private static void EnsureTextures()
        {
            if (_streakTex == null) _streakTex = BuildStreakTexture();
            if (_edgeTex == null) _edgeTex = BuildEdgeTexture();
            if (_dotTex == null) _dotTex = BuildDotTexture();
        }

        private static Texture2D BuildStreakTexture()
        {
            const int S = 512;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[S * S];
            var rng = new System.Random(1337);
            for (int i = 0; i < 220; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2.0;
                float dx = (float)Math.Cos(ang), dy = (float)Math.Sin(ang);
                float r0 = 8f + (float)rng.NextDouble() * 70f;
                float len = 30f + (float)rng.NextDouble() * 120f;
                int hw = rng.Next(0, 10) == 0 ? 1 : 0;
                float a = 0.10f + (float)rng.NextDouble() * 0.32f;
                int steps = (int)len;
                for (int s = 0; s < steps; s++)
                {
                    float fade = 1f - (float)s / steps;
                    int cx = (int)(S / 2 + dx * (r0 + s));
                    int cy = (int)(S / 2 + dy * (r0 + s));
                    for (int ox = -hw; ox <= hw; ox++)
                    {
                        for (int oy = -hw; oy <= hw; oy++)
                        {
                            int x = cx + ox, y = cy + oy;
                            if ((uint)x >= S || (uint)y >= S) continue;
                            float add = a * fade;
                            int idx = y * S + x;
                            var dst = px[idx];
                            px[idx] = new Color(
                                Mathf.Max(dst.r, 0.78f * add),
                                Mathf.Max(dst.g, 0.93f * add),
                                Mathf.Max(dst.b, 1f * add),
                                Mathf.Max(dst.a, add));
                        }
                    }
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D BuildEdgeTexture()
        {
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x / (float)(S - 1) * 2f - 1f;
                    float dy = y / (float)(S - 1) * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.Clamp01((d - 0.62f) / 0.43f);
                    t = t * t * (3f - 2f * t);
                    px[y * S + x] = new Color(0.45f, 0.78f, 1f, t * 0.38f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D _dotTex;
        private static Texture2D BuildDotTexture()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x / (float)(S - 1) * 2f - 1f;
                    float dy = y / (float)(S - 1) * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d * 1.7f);
                    px[y * S + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        // ── Streak particles (world space around the hull) ──
        private static Material _streakMat;
        private static Material StreakMaterial
        {
            get
            {
                if (_streakMat == null)
                {
                    // Same fallback chain as Explosion: runtime particle systems need a
                    // material or they render magenta.
                    Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("Unlit/Color");
                    EnsureTextures();
                    _streakMat = new Material(sh) { color = Color.white };
                    if (_streakMat.HasProperty("_BaseMap")) _streakMat.SetTexture("_BaseMap", _dotTex);
                    if (_streakMat.HasProperty("_MainTex")) _streakMat.SetTexture("_MainTex", _dotTex);
                    if (_streakMat.HasProperty("_Surface")) _streakMat.SetFloat("_Surface", 1f);
                    if (_streakMat.HasProperty("_Blend")) _streakMat.SetFloat("_Blend", 0f);
                    _streakMat.SetOverrideTag("RenderType", "Transparent");
                    _streakMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _streakMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    _streakMat.SetInt("_ZWrite", 0);
                    _streakMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
                return _streakMat;
            }
        }

        private static ParticleSystem BuildStreaks(GridEntity grid, float diameter)
        {
            var go = new GameObject("~WarpStreaks");
            go.transform.SetParent(grid.transform, false);
            go.transform.position = grid.GetGridCenter();
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.7f;
            main.startSpeed = 0f;
            main.startSize = 0.32f;
            main.startColor = new Color(0.65f, 0.9f, 1f, 0.55f);
            main.maxParticles = 500;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(diameter, diameter, diameter * 1.6f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(-diameter * 3f);
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = 1.4f;
            rend.velocityScale = 0.025f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = StreakMaterial;
            ps.Play();
            return ps;
        }

        // ── Synthesized audio (no clips to author) ──
        private const int SynthRate = 22050;
        private static AudioClip _riser;
        private static AudioClip _whoosh;
        private static AudioClip RiserClip => _riser != null ? _riser : (_riser = BuildRiser());
        private static AudioClip WhooshClip => _whoosh != null ? _whoosh : (_whoosh = BuildWhoosh());

        private static AudioClip BuildRiser()
        {
            int n = (int)(SynthRate * 0.6f);
            var data = new float[n];
            var rng = new System.Random(4242);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                double freq = 180.0 + (1400.0 - 180.0) * k * k;
                phase += 2.0 * Math.PI * freq / SynthRate;
                float tone = (float)Math.Sin(phase);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = Mathf.Clamp((tone * 0.75f + noise * 0.25f) * k * k * 0.8f, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("~WarpRiser", n, 1, SynthRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildWhoosh()
        {
            int n = (int)(SynthRate * 2.6f);
            var data = new float[n];
            var rng = new System.Random(777);
            double phase = 0.0;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                float env = Mathf.Min(1f, k / 0.08f) * Mathf.Exp(-2.2f * k);
                double freq = 90.0 - 55.0 * k;
                phase += 2.0 * Math.PI * freq / SynthRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cutoff = 0.25f - 0.18f * k;
                lp += cutoff * (noise - lp);
                data[i] = Mathf.Clamp((lp * 1.6f + (float)Math.Sin(phase) * 0.45f) * env * 0.7f, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("~WarpWhoosh", n, 1, SynthRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>Unscaled-time driver for WarpFx (lives on a DontDestroyOnLoad root).</summary>
    internal sealed class WarpFxDriver : MonoBehaviour
    {
        private void Update() => WarpFx.Tick(Time.unscaledDeltaTime);
    }
}
