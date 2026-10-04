// Assets/Scripts/VoxelEngine/Combat/BannerCloth.cs
//
// 14.37.1-dev - THE banner visual, built once in code and reused by every
// 3D display site (placed banner block, grid banner block). The FRAME is
// always identical - gold pole, gold crossbar, gold cross finial - only
// the cloth image and the three text lines vary, and those come from
// TeamBannerRegistry per team. The component subscribes to the registry
// and repaints live when the team's banner is edited.
//
// 14.37.2 layout and motion, from the second field report:
//
//   • The two cloths hang CENTERED on the pole, front and back - two
//     parallel sheets sandwiching the pole in line, not side by side.
//     Each sheet faces its own way, so the banner reads correctly from
//     both directions; the back face of each sheet carries mirrored U
//     coordinates for the moments the wind lets you peek behind one.
//   • The flutter clock is a continuously integrated phase. The old code
//     computed sin(time x frequency) while frequency stepped on the slow
//     environment clock - every step snapped the wave and the cloth
//     visibly stuttered. Wind and air are now slewed per frame and the
//     phase accumulates, so a gust changes the pace smoothly and the
//     fabric never jumps.
//   • Flutter follows wind + the carrier's own speed, scaled by local
//     atmospheric density - in vacuum the cloth hangs dead still.
//
// The swallow-tail is still geometry - the bottom rows simply hang less
// at the center columns. No transparency tricks.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networking;

namespace VoxelEngine.Combat
{
    public class BannerCloth : MonoBehaviour
    {
        [Header("Cloth dimensions (meters) - two sheets, front and back of the pole")]
        public float clothWidth = 0.84f;
        public float clothHeight = 1.30f;
        [Tooltip("Depth of the swallow-tail notch as a fraction of cloth height.")]
        [Range(0.05f, 0.4f)] public float notchDepth01 = 0.18f;
        public float poleHeight = 2.30f;
        [Tooltip("Distance between the two cloth sheets - the pole runs between them.")]
        public float clothGap = 0.11f;

        [Header("Flutter")]
        [Tooltip("Maximum sideways swing of the cloth tail at full wind, in meters.")]
        public float maxFlutter = 0.045f;

        // Vertex grid resolution per cloth (verts, not segments).
        private const int Cols = 7;
        private const int Rows = 11;
        private const float EnvSampleInterval = 0.5f;
        private const float SleepDistance = 70f;

        private string _teamId = "";
        private bool _built;
        private Material _clothMaterial;

        // ── cloth panels ──────────────────────────────────────────────────
        private sealed class ClothPanel
        {
            public Mesh mesh;
            public Vector3[] baseVerts;   // rest pose, never mutated
            public Vector3[] work;        // per-frame scratch
            public float[] hang01;        // 0 at the bar, 1 at the tail, per vertex
            public float[] xNorm;         // 0..1 across the cloth, per vertex
        }

        private readonly List<ClothPanel> _panels = new();

        // ── text lines (one outward-facing set per sheet) ─────────────────
        private sealed class ClothTextAnchor
        {
            public Transform tf;
            public float baseZ;           // rest-pose local Z on its sheet
            public float hang01;          // where the line sits down the cloth
        }

        private readonly List<TextMesh> _topTexts = new();
        private readonly List<TextMesh> _middleTexts = new();
        private readonly List<TextMesh> _bottomTexts = new();
        private readonly List<ClothTextAnchor> _textAnchors = new();

        // ── flutter state ─────────────────────────────────────────────────
        private Rigidbody _carrierBody;   // the ship under a GridBannerBlock, if any
        private float _envTimer;
        private float _envWind;           // sampled target: wind + carrier speed, m/s
        private float _envAir;            // sampled target: atmospheric density 0..1
        private float _windSmooth;        // per-frame slewed copies - no steps, no stutter
        private float _airSmooth;
        private float _wavePhase;         // integrated flutter clock (radians)
        private float _amp;               // smoothed current amplitude, meters
        private bool _flat = true;        // mesh currently in rest pose

        /// <summary>Show this team's banner (empty/unknown id = default emblem).</summary>
        public void Bind(string teamId)
        {
            _teamId = teamId ?? "";
            if (!_built) Build();
            Repaint();
        }

        public string BoundTeamId => _teamId;

        private void OnEnable()
        {
            TeamBannerRegistry.OnBannerChanged += OnBannerChanged;
            if (_built) Repaint();
        }

        private void OnDisable()
        {
            TeamBannerRegistry.OnBannerChanged -= OnBannerChanged;
        }

        private void OnBannerChanged(string teamId)
        {
            if (!_built) return;
            if (string.IsNullOrEmpty(teamId) || teamId == _teamId) Repaint();
        }

        private void Repaint()
        {
            if (_clothMaterial != null)
            {
                var tex = TeamBannerRegistry.ClothTexture(_teamId);
                _clothMaterial.mainTexture = tex;
                if (_clothMaterial.HasProperty("_BaseMap")) _clothMaterial.SetTexture("_BaseMap", tex);
                if (_clothMaterial.HasProperty("_MainTex")) _clothMaterial.SetTexture("_MainTex", tex);
            }
            TeamBannerRegistry.TextsOf(_teamId, out string top, out string middle, out string bottom);
            foreach (var tm in _topTexts) if (tm != null) { tm.text = top; FitToCloth(tm); }
            foreach (var tm in _middleTexts) if (tm != null) { tm.text = middle; FitToCloth(tm); }
            foreach (var tm in _bottomTexts) if (tm != null) { tm.text = bottom; FitToCloth(tm); }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Text fitting (14.47.2)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Base character size per line, recorded at build time so
        /// every refit starts from the designed size, never from a previous
        /// shrink.</summary>
        private readonly Dictionary<TextMesh, float> _baseCharSize = new();

        /// <summary>14.47.2 - a line must LIVE on the cloth: text wider than
        /// the banner shrinks to fit instead of sticking out past the edges
        /// or being cut off. Short lines keep their designed size.</summary>
        private void FitToCloth(TextMesh tm)
        {
            if (tm == null) return;
            if (!_baseCharSize.TryGetValue(tm, out float baseSize) || baseSize <= 0f)
                baseSize = tm.characterSize;
            tm.characterSize = baseSize;
            if (string.IsNullOrEmpty(tm.text)) return;

            float maxWidth = clothWidth * 0.92f;   // a whisper of margin each side
            float width = MeasureWidth(tm);
            if (width > maxWidth && width > 0.0001f)
                tm.characterSize = baseSize * (maxWidth / width);
        }

        /// <summary>Rendered width of a TextMesh line in local units,
        /// measured from the font's own glyph advances (TextMesh draws at
        /// advance x characterSize x 0.1). Falls back to a bold-glyph
        /// estimate when no font can be asked.</summary>
        private static float MeasureWidth(TextMesh tm)
        {
            int size = tm.fontSize > 0 ? tm.fontSize : 13;
            var font = tm.font;
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (System.Exception) { font = null; }
            }
            if (font != null)
            {
                font.RequestCharactersInTexture(tm.text, size, tm.fontStyle);
                float advance = 0f;
                bool complete = true;
                foreach (char c in tm.text)
                {
                    if (font.GetCharacterInfo(c, out var info, size, tm.fontStyle)) advance += info.advance;
                    else { complete = false; break; }
                }
                if (complete) return advance * tm.characterSize * 0.1f;
            }
            // No font to ask: a bold glyph averages about 55% of its point size.
            return tm.text.Length * size * 0.55f * tm.characterSize * 0.1f;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Construction
        // ─────────────────────────────────────────────────────────────────

        private void Build()
        {
            _built = true;
            var gold = MakeMaterial(new Color(0.85f, 0.68f, 0.21f), metallic: 0.75f, smoothness: 0.62f);

            // Pole: base at local origin, finial on top.
            float poleR = 0.035f;
            AddPart(PrimitiveType.Cylinder, gold, new Vector3(0f, poleHeight * 0.5f, 0f),
                Vector3.zero, new Vector3(poleR * 2f, poleHeight * 0.5f, poleR * 2f), "Banner_Pole");

            // Crossbar the cloths hang from, just under the finial.
            float barY = poleHeight - 0.16f;
            float barLen = clothWidth + 0.16f;
            AddPart(PrimitiveType.Cylinder, gold, new Vector3(0f, barY, 0f),
                new Vector3(0f, 0f, 90f), new Vector3(0.05f, barLen * 0.5f, 0.05f), "Banner_Crossbar");
            AddPart(PrimitiveType.Sphere, gold, new Vector3(-barLen * 0.5f, barY, 0f),
                Vector3.zero, Vector3.one * 0.085f, "Banner_BarTipL");
            AddPart(PrimitiveType.Sphere, gold, new Vector3(barLen * 0.5f, barY, 0f),
                Vector3.zero, Vector3.one * 0.085f, "Banner_BarTipR");

            // Cross finial above the crossbar - the emblem of the crusade.
            float crossY = poleHeight + 0.10f;
            AddPart(PrimitiveType.Cube, gold, new Vector3(0f, crossY, 0f),
                Vector3.zero, new Vector3(0.045f, 0.26f, 0.045f), "Banner_CrossV");
            AddPart(PrimitiveType.Cube, gold, new Vector3(0f, crossY + 0.045f, 0f),
                Vector3.zero, new Vector3(0.17f, 0.045f, 0.045f), "Banner_CrossH");

            // One shared cloth material - both sheets show the same banner.
            _clothMaterial = MakeMaterial(Color.white, metallic: 0f, smoothness: 0.25f);
            _clothMaterial.name = "BannerCloth_Runtime";

            // Two sheets, CENTERED on the pole, one in front and one behind -
            // the pole runs between them, in line, never through the fabric.
            // Both sheets ride the same wave so they move in parallel and the
            // gap between them never collapses.
            float topY = barY - 0.045f;
            BuildClothPanel(new Vector3(0f, topY, -clothGap * 0.5f), outward: -1, "Banner_ClothFront");
            BuildClothPanel(new Vector3(0f, topY, clothGap * 0.5f), outward: +1, "Banner_ClothBack");

            // The ship (or vehicle) carrying this banner, for motion-driven
            // flutter. Static banners have no rigidbody parent - that's fine.
            _carrierBody = GetComponentInParent<Rigidbody>();

            // Banners read the global wind simulation; make sure it runs even
            // in worlds that never placed a turbine.
            VoxelEngine.Power.Wind.WindSystem.EnsureInstance();

            Repaint();
        }

        /// <summary>One swallow-tail cloth as a Cols x Rows vertex grid with a
        /// duplicated, U-mirrored set for the reverse face. The grid is what
        /// lets the cloth bend in the wind; the mirror keeps the image honest
        /// if the reverse of a sheet ever catches the eye.</summary>
        private void BuildClothPanel(Vector3 localPos, int outward, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;

            float w = clothWidth, h = clothHeight, notch = h * notchDepth01;
            int n = Cols * Rows;

            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var hang = new float[n * 2];
            var xn = new float[n * 2];

            for (int r = 0; r < Rows; r++)
            {
                float hang01 = r / (float)(Rows - 1);
                for (int c = 0; c < Cols; c++)
                {
                    float xNorm = c / (float)(Cols - 1);
                    // Swallow-tail: center columns hang less (triangular notch).
                    float colLen = h - notch * (1f - Mathf.Abs(xNorm * 2f - 1f));
                    float x = -w * 0.5f + xNorm * w;
                    float y = -colLen * hang01;

                    int i = r * Cols + c;
                    verts[i] = new Vector3(x, y, 0f);
                    verts[i + n] = verts[i];
                    // The -Z face samples the texture straight (reads correctly
                    // from the front); the +Z face mirrors U (reads correctly
                    // from behind). That one rule serves BOTH sheets - each
                    // sheet's outward face is automatically the readable one.
                    uvs[i] = new Vector2(xNorm, (y + h) / h);
                    uvs[i + n] = new Vector2(1f - xNorm, (y + h) / h);
                    hang[i] = hang01; hang[i + n] = hang01;
                    xn[i] = xNorm; xn[i + n] = xNorm;
                }
            }

            var tris = new List<int>(Cols * Rows * 12);
            for (int r = 0; r < Rows - 1; r++)
            {
                for (int c = 0; c < Cols - 1; c++)
                {
                    int a = r * Cols + c;          // top-left
                    int b = a + 1;                 // top-right
                    int e = a + Cols;              // bottom-left
                    int d = e + 1;                 // bottom-right
                    // Faces -Z (the front sheet's outward side).
                    tris.Add(a); tris.Add(b); tris.Add(e);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                    // Faces +Z (duplicate set, reversed winding).
                    tris.Add(a + n); tris.Add(e + n); tris.Add(b + n);
                    tris.Add(b + n); tris.Add(e + n); tris.Add(d + n);
                }
            }

            var mesh = new Mesh { name = "BannerCloth_SwallowTail" };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            // The tail swings; pad the bounds once so Unity never culls mid-flap.
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, 0f, maxFlutter * 2.5f));
            mesh.bounds = bounds;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _clothMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            _panels.Add(new ClothPanel
            {
                mesh = mesh,
                baseVerts = verts,
                work = (Vector3[])verts.Clone(),
                hang01 = hang,
                xNorm = xn,
            });

            // Three text lines on the OUTWARD face of this sheet. The inner
            // faces stare at each other across the pole - no reader there.
            AddTextLine(go.transform, _topTexts, "Top", 0.14f, 0.60f, outward);
            AddTextLine(go.transform, _middleTexts, "Middle", 0.50f, 0.75f, outward);
            AddTextLine(go.transform, _bottomTexts, "Bottom", 0.72f, 0.60f, outward);
        }

        private void AddTextLine(Transform cloth, List<TextMesh> bucket, string line,
            float down01, float scale01, int outward)
        {
            float z = 0.018f * outward;
            var tm = MakeText("Banner_Text" + line, cloth,
                new Vector3(0f, -clothHeight * down01, z), scale01);
            if (outward > 0) tm.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
            bucket.Add(tm);
            _textAnchors.Add(new ClothTextAnchor { tf = tm.transform, baseZ = z, hang01 = down01 });
        }

        private TextMesh MakeText(string name, Transform parent, Vector3 localPos, float scale01)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 48;
            tm.characterSize = 0.012f * scale01 * (clothWidth / 0.84f);
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(0.14f, 0.10f, 0.08f);
            _baseCharSize[tm] = tm.characterSize;   // 14.47.2 - the refit baseline
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            // World text must sit IN the world: the stock font shader draws
            // over everything (ZTest Always), which let banner text shine
            // through terrain and blocks. Swap to the depth-tested variant.
            Rendering.WorldTextMaterial.Apply(tm);
            return tm;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Flutter
        // ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_built || _panels.Count == 0) return;
            float dt = Time.deltaTime;

            // Environment is sampled on a slow clock - wind, air and carrier
            // speed do not change meaningfully frame to frame.
            _envTimer -= dt;
            if (_envTimer <= 0f)
            {
                _envTimer = EnvSampleInterval;
                SampleEnvironment();
            }

            // Slew the sampled targets per frame. Feeding stepped values into
            // the wave math is what made the cloth stutter on every sample
            // tick; slewed wind + an integrated phase keep it glassy.
            _windSmooth = Mathf.MoveTowards(_windSmooth, _envWind, dt * 8f);
            _airSmooth = Mathf.MoveTowards(_airSmooth, _envAir, dt * 1.2f);

            // The flutter clock accumulates - a gust speeds the wave up
            // smoothly instead of snapping it to a new timeline.
            float freq = 1.7f + Mathf.Min(_windSmooth, 30f) * 0.14f;
            _wavePhase += freq * dt;
            if (_wavePhase > 628.31853f) _wavePhase -= 628.31853f;   // 100 x 2pi, exact period

            // Far away (or on a dedicated host with no camera): rest the cloth.
            var cam = Camera.main;
            bool sleeping = cam == null
                || (cam.transform.position - transform.position).sqrMagnitude > SleepDistance * SleepDistance;

            // Target amplitude: wind + carrier speed, choked by air density.
            // Vacuum (space, airless moons) means zero flutter - no air, no flag-waving.
            float target = sleeping ? 0f
                : maxFlutter * Mathf.Clamp01(_windSmooth / 16f) * _airSmooth;
            _amp = Mathf.MoveTowards(_amp, target, dt * 0.08f);

            if (_amp < 0.002f)
            {
                if (!_flat) RestoreRestPose();
                return;
            }
            _flat = false;

            for (int p = 0; p < _panels.Count; p++)
            {
                var panel = _panels[p];
                var baseVerts = panel.baseVerts;
                var work = panel.work;
                for (int i = 0; i < work.Length; i++)
                {
                    // The bar edge is sewn on (hang01 = 0, no movement); the
                    // tail swings hardest. A travelling wave runs down and
                    // across the cloth.
                    float hangWeight = panel.hang01[i] * panel.hang01[i];
                    float wave = Mathf.Sin(_wavePhase
                        + panel.xNorm[i] * 2.1f + panel.hang01[i] * 3.4f);
                    var v = baseVerts[i];
                    v.z += _amp * hangWeight * wave;
                    work[i] = v;
                }
                panel.mesh.vertices = work;
                panel.mesh.RecalculateNormals();
            }

            // The painted letters ride their spot on the cloth.
            for (int i = 0; i < _textAnchors.Count; i++)
            {
                var anchor = _textAnchors[i];
                if (anchor.tf == null) continue;
                float hangWeight = anchor.hang01 * anchor.hang01;
                float wave = Mathf.Sin(_wavePhase + 0.5f * 2.1f + anchor.hang01 * 3.4f);
                var pos = anchor.tf.localPosition;
                pos.z = anchor.baseZ + _amp * hangWeight * wave;
                anchor.tf.localPosition = pos;
            }
        }

        private void SampleEnvironment()
        {
            // Air: in space or on an airless world the cloth must hang dead
            // still, however fast the ship moves - there is nothing to push it.
            var sample = VoxelEngine.GridSystem.AtmosphereManager.Sample(transform.position);
            _envAir = sample.IsInSpace ? 0f : Mathf.Clamp01(sample.Density01);

            // Wind: the global simulation (2-45 m/s, weather-coupled) ...
            var windSystem = VoxelEngine.Power.Wind.WindSystem.Instance;
            float wind = windSystem != null ? windSystem.GetWindSpeed() : 0f;

            // ... plus apparent wind from the carrier's own motion - a banner
            // on a sailing ship streams even on a dead-calm day.
            if (_carrierBody == null) _carrierBody = GetComponentInParent<Rigidbody>();
            if (_carrierBody != null) wind += _carrierBody.linearVelocity.magnitude * 0.9f;

            _envWind = wind;
        }

        private void RestoreRestPose()
        {
            _flat = true;
            foreach (var panel in _panels)
            {
                panel.mesh.vertices = panel.baseVerts;
                panel.mesh.RecalculateNormals();
            }
            foreach (var anchor in _textAnchors)
            {
                if (anchor.tf == null) continue;
                var pos = anchor.tf.localPosition;
                pos.z = anchor.baseZ;
                anchor.tf.localPosition = pos;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Parts
        // ─────────────────────────────────────────────────────────────────

        private GameObject AddPart(PrimitiveType type, Material mat, Vector3 pos, Vector3 euler, Vector3 scale, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            // The part is decoration; the owner (block/display) supplies the
            // collider. Destroy, not DestroyImmediate - builds can run inside
            // physics callbacks.
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Destroy(col); }
            return go;
        }

        private static Material MakeMaterial(Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }
    }
}
