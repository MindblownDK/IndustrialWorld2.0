// Assets/Scripts/VoxelEngine/Combat/BannerCloth.cs
//
// 14.37.1-dev - THE banner visual, built once in code and reused by every
// 3D display site (placed banner block, grid banner block). The FRAME is
// always identical - gold pole, gold crossbar, gold cross finial - only
// the cloth image and the three text lines vary, and those come from
// TeamBannerRegistry per team. The component subscribes to the registry
// and repaints live when the team's banner is edited.
//
// 14.37.1 reshapes the whole thing after the first field reports:
//
//   • TWO cloths now hang from the crossbar, one on each side of the pole,
//     so the pole no longer pierces the fabric - it looks like a real
//     processional banner instead of a flag on a skewer.
//   • Each cloth is a deformable vertex grid (not a stiff quad) and
//     FLUTTERS: amplitude follows the global wind simulation plus the
//     carrier's own speed (a banner on a moving ship streams), and the
//     whole effect is scaled by local atmospheric density - in vacuum or
//     space the cloth hangs perfectly still, because there is no air.
//   • The back face carries MIRRORED U coordinates, the way a real
//     printed banner shows its image through the weave - the emblem reads
//     correctly from both sides instead of appearing flipped from behind.
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
        [Header("Cloth dimensions (meters) - per cloth, one hangs each side of the pole")]
        public float clothWidth = 0.62f;
        public float clothHeight = 1.30f;
        [Tooltip("Depth of the swallow-tail notch as a fraction of cloth height.")]
        [Range(0.05f, 0.4f)] public float notchDepth01 = 0.18f;
        public float poleHeight = 2.30f;
        [Tooltip("Clear air between the two cloths, straddling the pole.")]
        public float poleGap = 0.17f;

        [Header("Flutter")]
        [Tooltip("Maximum sideways swing of the cloth tail at full wind, in meters.")]
        public float maxFlutter = 0.055f;

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
            public float phase;           // so the two cloths never swing in lockstep
        }

        private readonly List<ClothPanel> _panels = new();

        // ── text lines (front AND back of BOTH cloths = 4 copies per line) ─
        private sealed class ClothTextAnchor
        {
            public Transform tf;
            public float baseZ;           // rest-pose local Z (front or back of the cloth)
            public float hang01;          // where the line sits down the cloth
            public float phase;           // its panel's phase
        }

        private readonly List<TextMesh> _topTexts = new();
        private readonly List<TextMesh> _middleTexts = new();
        private readonly List<TextMesh> _bottomTexts = new();
        private readonly List<ClothTextAnchor> _textAnchors = new();

        // ── flutter state ─────────────────────────────────────────────────
        private Rigidbody _carrierBody;   // the ship under a GridBannerBlock, if any
        private float _envTimer;
        private float _envWind;           // wind + carrier speed, m/s
        private float _envAir;            // atmospheric density 0..1 (0 = vacuum)
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
            foreach (var tm in _topTexts) if (tm != null) tm.text = top;
            foreach (var tm in _middleTexts) if (tm != null) tm.text = middle;
            foreach (var tm in _bottomTexts) if (tm != null) tm.text = bottom;
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

            // Crossbar the cloths hang from, just under the finial. It spans
            // both cloths plus the pole gap.
            float barY = poleHeight - 0.16f;
            float barLen = poleGap + clothWidth * 2f + 0.14f;
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

            // One shared cloth material - both cloths show the same banner.
            _clothMaterial = MakeMaterial(Color.white, metallic: 0f, smoothness: 0.25f);
            _clothMaterial.name = "BannerCloth_Runtime";

            // Two cloths, one each side of the pole, hanging from the bar.
            float topY = barY - 0.045f;
            float xOffset = poleGap * 0.5f + clothWidth * 0.5f;
            BuildClothPanel(new Vector3(-xOffset, topY, 0f), phase: 0f, "Banner_ClothL");
            BuildClothPanel(new Vector3(xOffset, topY, 0f), phase: 1.9f, "Banner_ClothR");

            // The ship (or vehicle) carrying this banner, for motion-driven
            // flutter. Static banners have no rigidbody parent - that's fine.
            _carrierBody = GetComponentInParent<Rigidbody>();

            // Banners read the global wind simulation; make sure it runs even
            // in worlds that never placed a turbine.
            VoxelEngine.Power.Wind.WindSystem.EnsureInstance();

            Repaint();
        }

        /// <summary>One swallow-tail cloth as a Cols x Rows vertex grid with a
        /// duplicated, U-mirrored set for the back face. The grid is what lets
        /// the cloth bend in the wind; the mirror is what makes the image read
        /// correctly from behind.</summary>
        private void BuildClothPanel(Vector3 localPos, float phase, string name)
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
                    // Front samples the texture straight; the back mirrors U so
                    // the banner is readable from both sides.
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
                    // Front (normal -Z, the side the texts face).
                    tris.Add(a); tris.Add(b); tris.Add(e);
                    tris.Add(b); tris.Add(d); tris.Add(e);
                    // Back (duplicate set, reversed winding).
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
                phase = phase,
            });

            BuildPanelTexts(go.transform, phase);
        }

        /// <summary>Three text lines on the FRONT and three mirrored copies on
        /// the BACK of one cloth - a banner must read from both sides. The
        /// anchors remember where each line hangs so the letters ride the
        /// flutter instead of floating in front of a moving cloth.</summary>
        private void BuildPanelTexts(Transform cloth, float phase)
        {
            AddTextPair(cloth, _topTexts, "Top", 0.14f, 0.60f, phase);
            AddTextPair(cloth, _middleTexts, "Middle", 0.50f, 0.75f, phase);
            AddTextPair(cloth, _bottomTexts, "Bottom", 0.72f, 0.60f, phase);
        }

        private void AddTextPair(Transform cloth, List<TextMesh> bucket, string line,
            float down01, float scale01, float phase)
        {
            float y = -clothHeight * down01;

            var front = MakeText("Banner_Text" + line, cloth, new Vector3(0f, y, -0.018f), scale01);
            bucket.Add(front);
            _textAnchors.Add(new ClothTextAnchor { tf = front.transform, baseZ = -0.018f, hang01 = down01, phase = phase });

            var back = MakeText("Banner_Text" + line + "_Back", cloth, new Vector3(0f, y, 0.018f), scale01);
            back.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
            bucket.Add(back);
            _textAnchors.Add(new ClothTextAnchor { tf = back.transform, baseZ = 0.018f, hang01 = down01, phase = phase });
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
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return tm;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Flutter
        // ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_built || _panels.Count == 0) return;

            // Environment is sampled on a slow clock - wind, air and carrier
            // speed do not change meaningfully frame to frame.
            _envTimer -= Time.deltaTime;
            if (_envTimer <= 0f)
            {
                _envTimer = EnvSampleInterval;
                SampleEnvironment();
            }

            // Far away (or on a dedicated host with no camera): rest the cloth.
            var cam = Camera.main;
            bool sleeping = cam == null
                || (cam.transform.position - transform.position).sqrMagnitude > SleepDistance * SleepDistance;

            // Target amplitude: wind + carrier speed, choked by air density.
            // Vacuum (space, airless moons) means zero flutter - no air, no flag-waving.
            float target = sleeping ? 0f
                : maxFlutter * Mathf.Clamp01(_envWind / 16f) * _envAir;
            _amp = Mathf.MoveTowards(_amp, target, Time.deltaTime * 0.08f);

            if (_amp < 0.002f)
            {
                if (!_flat) RestoreRestPose();
                return;
            }
            _flat = false;

            // Stronger wind also flaps faster.
            float freq = 1.7f + Mathf.Min(_envWind, 30f) * 0.14f;
            float t = Time.time;

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
                    float wave = Mathf.Sin(t * freq + panel.phase
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
                float wave = Mathf.Sin(t * freq + anchor.phase + 0.5f * 2.1f + anchor.hang01 * 3.4f);
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
