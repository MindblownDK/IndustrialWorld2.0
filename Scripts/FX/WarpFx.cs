// Assets/Scripts/VoxelEngine/FX/WarpFx.cs
//
// Warp transit: a forward tunnel of stretched stars, a brief drop-out flash,
// then the hull is at the destination. Procedural (no authored clips/textures).
//
// Screen overlay + FOV for the seated local player. Exterior tunnel + streaks
// for any observer. No camera shake — a rest-velocity cockpit must stay still.

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
        public const float PreSeconds = 0.85f;
        public const float TransitSeconds = 1.65f;
        public const float OutSeconds = 0.7f;
        private const float TransitFovKick = 7f;
        private const float StreakRate = 280f;

        private class Jump
        {
            public GridWarpDrive drive;
            public GridEntity grid;
            public Action onJump;
            public float t;
            public bool jumped;
            public bool screenFx;
            public GameObject tunnel;
            public Material tunnelMat;
            public float hullSpan;
            public ParticleSystem streaks;
        }

        private static readonly List<Jump> _active = new();
        private static readonly HashSet<GridWarpDrive> _pending = new();
        private static WarpFxDriver _driver;

        private static VisualElement _overlayRoot;
        private static VisualElement _overlay;
        private static VisualElement _edgeLayer;
        private static VisualElement _streakLayer;
        private static VisualElement _flashLayer;
        private static Texture2D _streakTex;
        private static Texture2D _edgeTex;
        private static Texture2D _dotTex;
        private static float _flash;

        public static bool IsPending(GridWarpDrive drive) => drive != null && _pending.Contains(drive);

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

        public static void ReportArrival(GridEntity grid, string line)
        {
            _arrivalGrid = grid;
            _arrivalLine = line ?? "";
            _arrivalAt = Time.unscaledTime;
        }

        public static string ArrivalLineFor(GridEntity grid)
        {
            if (grid == null || grid != _arrivalGrid) return "";
            if (Time.unscaledTime - _arrivalAt > ArrivalHoldSeconds) return "";
            return _arrivalLine;
        }

        public static bool PlayJump(GridWarpDrive drive, Action onJump)
        {
            if (drive == null || drive.Grid == null || onJump == null) return false;
            if (_pending.Contains(drive)) return false;
            var j = new Jump { drive = drive, grid = drive.Grid, onJump = onJump };
            if (!StartJump(j)) return false;
            _pending.Add(drive);
            return true;
        }

        /// <summary>Drive-less jump for gate transits: same tunnel, streaks and screen
        /// FX, nothing in the pending set — the gate owns its own one-transit window.</summary>
        public static bool PlayJump(GridEntity grid, Action onJump)
        {
            if (grid == null || onJump == null) return false;
            var j = new Jump { drive = null, grid = grid, onJump = onJump };
            return StartJump(j);
        }

        private static bool StartJump(Jump j)
        {
            EnsureDriver();
            j.screenFx = j.grid.IsControlled;
            j.hullSpan = EstimateDiameter(j.grid);

            try
            {
                j.tunnel = BuildTunnel(j.grid, j.hullSpan, out j.tunnelMat);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                j.tunnel = null;
                j.tunnelMat = null;
            }
            try { j.streaks = BuildStreaks(j.grid, j.hullSpan); }
            catch (Exception e)
            {
                Debug.LogException(e);
                j.streaks = null;
            }

            _active.Add(j);
            if (j.screenFx) AudioManager.PlayUI(RiserClip, 0.45f);
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
            _flashLayer.style.backgroundColor = new StyleColor(new Color(0.72f, 0.88f, 1f, 1f));
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
                if (j.grid == null) { Teardown(j); _active.RemoveAt(i); continue; }
                j.t += dt;

                if (!j.jumped && j.t >= PreSeconds)
                {
                    j.jumped = true;
                    _pending.Remove(j.drive);
                    try { j.onJump?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    Vector3 center = j.grid != null ? j.grid.GetGridCenter() : Vector3.zero;
                    if (j.tunnel != null) j.tunnel.transform.position = center;
                    if (j.streaks != null) j.streaks.transform.position = center;
                    if (j.screenFx)
                    {
                        _flash = 0.85f;
                        AudioManager.PlayUI(WhooshClip, 0.5f);
                    }
                }

                float total = PreSeconds + TransitSeconds + OutSeconds;
                if (j.t >= total) { Teardown(j); _active.RemoveAt(i); continue; }

                float env, inten;
                if (j.t < PreSeconds)
                {
                    float k = j.t / PreSeconds;
                    env = k * k;
                    inten = 0.35f + 1.1f * k;
                }
                else if (j.t < PreSeconds + TransitSeconds)
                {
                    float k = (j.t - PreSeconds) / TransitSeconds;
                    env = 1f + 0.15f * Mathf.Sin(k * Mathf.PI);
                    inten = 1.35f;
                }
                else
                {
                    float k = (j.t - PreSeconds - TransitSeconds) / OutSeconds;
                    env = 1.1f + 0.4f * k;
                    inten = 1.35f * (1f - k);
                }

                if (j.tunnel != null)
                {
                    float gs = j.grid.transform.lossyScale.x;
                    if (Mathf.Abs(gs) < 0.001f) gs = 1f;
                    float rad = (j.hullSpan * 0.55f * (0.35f + 0.65f * env)) / gs;
                    float len = (j.hullSpan * (2.4f + 5.5f * env)) / gs;
                    j.tunnel.transform.localScale = new Vector3(rad, len * 0.5f, rad);
                    j.tunnel.transform.position = j.grid.GetGridCenter();
                }
                if (j.tunnelMat != null)
                {
                    if (j.tunnelMat.HasProperty("_Intensity")) j.tunnelMat.SetFloat("_Intensity", Mathf.Max(0f, inten));
                    if (j.tunnelMat.HasProperty("_Color"))
                    {
                        Color c = new Color(0.45f, 0.78f, 1f, Mathf.Clamp01(inten * 0.35f));
                        j.tunnelMat.SetColor("_Color", c);
                    }
                    if (j.tunnelMat.HasProperty("_BaseColor"))
                        j.tunnelMat.SetColor("_BaseColor", new Color(0.45f, 0.78f, 1f, Mathf.Clamp01(inten * 0.28f)));
                }

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
                        s = 0.55f * k;
                        e = 0.40f * k;
                    }
                    else if (j.t < PreSeconds + TransitSeconds)
                    {
                        s = 0.92f;
                        e = 0.48f;
                    }
                    else
                    {
                        float k = (j.t - PreSeconds - TransitSeconds) / OutSeconds;
                        s = 0.92f * (1f - k);
                        e = 0.48f * (1f - k);
                    }
                    if (s > streakTarget) streakTarget = s;
                    if (e > edgeTarget) edgeTarget = e;
                    if (j.jumped && j.t < PreSeconds + TransitSeconds)
                    {
                        float ramp = Mathf.Min(1f, (j.t - PreSeconds) / 0.25f);
                        CameraFeedback.AddFovSqueeze(TransitFovKick * ramp);
                    }
                }
            }
            _flash = Mathf.Max(0f, _flash - dt * 5.5f);
            ApplyOverlay(streakTarget, edgeTarget);
        }

        private static void Teardown(Jump j)
        {
            _pending.Remove(j.drive);
            if (j.tunnel != null) UnityEngine.Object.Destroy(j.tunnel);
            if (j.tunnelMat != null) UnityEngine.Object.Destroy(j.tunnelMat);
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

        private static GameObject BuildTunnel(GridEntity grid, float span, out Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "~WarpTunnel";
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.transform.SetParent(grid.transform, false);
            go.transform.position = grid.GetGridCenter();
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var shader = Shader.Find("VoxelEngine/WarpBubble")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Sprites/Default")
                      ?? Shader.Find("Unlit/Color");
            mat = new Material(shader) { name = "Mat_WarpTunnel" };
            Color tint = new Color(0.40f, 0.75f, 1f, 0.22f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Intensity")) mat.SetFloat("_Intensity", 0.4f);
            var rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            go.transform.localScale = new Vector3(span * 0.2f, span * 0.4f, span * 0.2f);
            return go;
        }

        private static void ApplyOverlay(float streak, float edge)
        {
            if (_overlay == null) return;
            bool show = streak > 0.003f || edge > 0.003f || _flash > 0.003f;
            _overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _overlay.BringToFront();
            _streakLayer.style.opacity = Mathf.Clamp01(streak);
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
            var rng = new System.Random(2026);
            float cx = (S - 1) * 0.5f, cy = (S - 1) * 0.5f;
            for (int i = 0; i < 340; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2.0;
                float dx = (float)Math.Cos(ang), dy = (float)Math.Sin(ang);
                float r0 = 6f + (float)rng.NextDouble() * 40f;
                float len = 80f + (float)rng.NextDouble() * 200f;
                float a = 0.08f + (float)rng.NextDouble() * 0.28f;
                int hw = rng.Next(0, 14) == 0 ? 1 : 0;
                int steps = (int)len;
                for (int s = 0; s < steps; s++)
                {
                    float fade = 1f - (float)s / steps;
                    fade *= fade;
                    int x = (int)(cx + dx * (r0 + s));
                    int y = (int)(cy + dy * (r0 + s));
                    for (int ox = -hw; ox <= hw; ox++)
                    {
                        int xx = x + ox, yy = y;
                        if ((uint)xx >= S || (uint)yy >= S) continue;
                        float add = a * fade;
                        int idx = yy * S + xx;
                        var dst = px[idx];
                        px[idx] = new Color(
                            Mathf.Max(dst.r, 0.70f * add),
                            Mathf.Max(dst.g, 0.88f * add),
                            Mathf.Max(dst.b, 1f * add),
                            Mathf.Max(dst.a, add));
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
                    float t = Mathf.Clamp01((d - 0.55f) / 0.50f);
                    t = t * t * (3f - 2f * t);
                    px[y * S + x] = new Color(0.15f, 0.35f, 0.55f, t * 0.72f);
                }
            }
            tex.SetPixels(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

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

        private static Material _streakMat;
        private static Material StreakMaterial
        {
            get
            {
                if (_streakMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("Unlit/Color");
                    EnsureTextures();
                    _streakMat = new Material(sh) { color = Color.white };
                    if (_streakMat.HasProperty("_BaseMap")) _streakMat.SetTexture("_BaseMap", _dotTex);
                    if (_streakMat.HasProperty("_MainTex")) _streakMat.SetTexture("_MainTex", _dotTex);
                    if (_streakMat.HasProperty("_Surface")) _streakMat.SetFloat("_Surface", 1f);
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
            main.startLifetime = 0.55f;
            main.startSpeed = 0f;
            main.startSize = 0.16f;
            main.startColor = new Color(0.75f, 0.92f, 1f, 0.7f);
            main.maxParticles = 180;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = diameter * 0.35f;
            shape.length = diameter * 0.2f;
            shape.rotation = new Vector3(0f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            float along = -diameter * 10f;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(along);
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.lengthScale = 4.2f;
            rend.velocityScale = 0.04f;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = StreakMaterial;
            ps.Play();
            return ps;
        }

        private const int SynthRate = 22050;
        private static AudioClip _riser;
        private static AudioClip _whoosh;
        private static AudioClip RiserClip => _riser != null ? _riser : (_riser = BuildRiser());
        private static AudioClip WhooshClip => _whoosh != null ? _whoosh : (_whoosh = BuildWhoosh());

        private static AudioClip BuildRiser()
        {
            int n = (int)(SynthRate * 0.85f);
            var data = new float[n];
            var rng = new System.Random(4242);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                double freq = 90.0 + (900.0 - 90.0) * k * k;
                phase += 2.0 * Math.PI * freq / SynthRate;
                float tone = (float)Math.Sin(phase);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = Mathf.Clamp((tone * 0.7f + noise * 0.18f) * k * k * 0.7f, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("~WarpRiser", n, 1, SynthRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip BuildWhoosh()
        {
            int n = (int)(SynthRate * 1.8f);
            var data = new float[n];
            var rng = new System.Random(777);
            double phase = 0.0;
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                float env = Mathf.Min(1f, k / 0.06f) * Mathf.Exp(-2.8f * k);
                double freq = 70.0 - 40.0 * k;
                phase += 2.0 * Math.PI * freq / SynthRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cutoff = 0.22f - 0.14f * k;
                lp += cutoff * (noise - lp);
                data[i] = Mathf.Clamp((lp * 1.5f + (float)Math.Sin(phase) * 0.35f) * env * 0.65f, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("~WarpWhoosh", n, 1, SynthRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    internal sealed class WarpFxDriver : MonoBehaviour
    {
        private void Update() => WarpFx.Tick(Time.unscaledDeltaTime);
    }
}
