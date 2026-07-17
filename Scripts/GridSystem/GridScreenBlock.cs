// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.cs
//
// Premium configurable digital screen for large grid ships.
// v5.57.3-dev — Screen text depth fix without self-occluding the display.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.GridSystem
{
    public enum ScreenDataMode
    {
        Summary, Power, Inventory, Speed, System, Bars, Custom, Camera
    }

    public enum ScreenSize
    {
        Small, Medium, Large, ExtraLarge, Wide
    }

    public class GridScreenBlock : GridBlock, IGridDataProvider
    {
        [Header("Screen")]
        public ScreenSize screenSize = ScreenSize.Small;
        public ScreenDataMode dataMode = ScreenDataMode.Summary;
        public Color textColor = new Color(0.18f, 0.72f, 0.88f);
        public string customText = "CUSTOM DISPLAY";
        [Header("Appearance")]
        [Tooltip("Border style: 0=None, 1=Thin, 2=Thick, 3=Glow")]
        public int borderStyle = 1;
        [Tooltip("Font style: 0=Default, 1=Monospace, 2=LCD, 3=Terminal")]
        public int fontStyle = 0;

        [Header("Data Sources")]
        public List<Vector3Int> dataSourcePositions = new();
        public List<int> dataSourceInstanceIds = new();

        // Legacy single-source fields — migrated into lists on first access
        public Vector3Int legacyGridPos;
        public int legacyInstanceId;
        private bool _migrated;

        public override float PowerDraw
        {
            get
            {
                if (!Enabled) return 0f;
                return screenSize switch
                {
                    ScreenSize.Small => 5f, ScreenSize.Wide => 10f,
                    ScreenSize.Medium => 25f, ScreenSize.Large => 50f,
                    ScreenSize.ExtraLarge => 100f, _ => 5f
                };
            }
        }

        private readonly List<IGridDataProvider> _cachedProviders = new();
        private readonly Dictionary<Vector3Int, int> _lastChecked = new();
        private TextMesh _screenText, _titleText, _statusText;
        private Renderer _screenSurfaceRenderer;
        private Renderer _glowStripRenderer;
        private readonly List<Renderer> _cornerDotRenderers = new();
        private Renderer _cameraFeedQuadRenderer;
        private Material _screenSurfaceBaseMaterial;
        private Material _cameraFeedMaterial;
        private MaterialPropertyBlock _appearanceBlock;
        private float _baseCharSize;
        private bool _cameraFeedVisible;
        private bool _initialized;
        private PowerConsumer _power;

        private (float w, float h) DisplayArea()
        {
            float cs = 2.5f;
            return screenSize switch
            {
                ScreenSize.Small => (cs * 0.65f, cs * 0.45f),
                ScreenSize.Wide => (cs * 1.40f, cs * 0.45f),
                ScreenSize.Medium => (cs * 1.40f, cs * 1.40f),
                ScreenSize.Large => (cs * 3.00f, cs * 3.00f),
                ScreenSize.ExtraLarge => (cs * 6.40f, cs * 6.40f),
                _ => (cs * 0.65f, cs * 0.45f)
            };
        }

        private float CharHeight()
        {
            var (_, h) = DisplayArea();
            int lines = Mathf.Max(1, MaxDisplayLines);
            return (h * 0.85f) / lines;
        }

        public bool IsPowered => Enabled && Grid != null && Grid.HasPower;
        public bool HasAnySource => ResolveAllProviders().Count > 0 || dataMode == ScreenDataMode.Custom;
        public int SourceCount
        {
            get
            {
                if (!_migrated) MigrateLegacy();
                return dataSourcePositions.Count;
            }
        }

        // ── IGridDataProvider ──────────────────────────────────────────
        public string SourceName => blockName + " (" + SourceCount + " src)";
        public string DataCategory => "Display";
        public string GetDisplayData() => FormattedDisplay;

        public string FormattedDisplay
        {
            get
            {
                if (!Enabled) return "DISABLED";
                if (!IsPowered) return "OFFLINE";
                if (dataMode == ScreenDataMode.Custom) return string.IsNullOrEmpty(customText) ? "(empty)" : customText;
                if (dataMode == ScreenDataMode.Camera) return CameraStatusDisplay();
                if (dataMode == ScreenDataMode.Power) return PowerDisplay();

                var sources = ResolveAllProviders();
                if (sources.Count == 0) return "ONLINE\nNO DATA\nRight-click to configure";

                int max = MaxDisplayLines;
                int linesPerSource = Mathf.Max(2, max / sources.Count);

                var result = new System.Text.StringBuilder();
                for (int s = 0; s < sources.Count; s++)
                {
                    var p = sources[s];
                    string raw = p.GetDisplayData();
                    if (string.IsNullOrEmpty(raw)) continue;

                    if (s > 0) result.AppendLine("──┘");
                    int remaining = max - CountLines(result.ToString());

                    switch (dataMode)
                    {
                        case ScreenDataMode.Bars:
                            result.AppendLine(FormatBars(raw, p.SourceName, Mathf.Min(linesPerSource, remaining)));
                            break;
                        case ScreenDataMode.Power:
                        case ScreenDataMode.Inventory:
                        case ScreenDataMode.Speed:
                        case ScreenDataMode.System:
                            result.AppendLine(FormatSection(raw, dataMode.ToString(), Mathf.Min(linesPerSource, remaining)));
                            break;
                        default:
                            result.AppendLine(TruncateLines(p.SourceName + "\n" + raw, Mathf.Min(linesPerSource, remaining)));
                            break;
                    }
                }
                return result.ToString().Trim();
            }
        }

        private static int CountLines(string t)
        {
            if (string.IsNullOrEmpty(t)) return 0;
            int c = 1;
            foreach (char ch in t) if (ch == '\n') c++;
            return c;
        }

        private int MaxDisplayLines => screenSize switch
        {
            ScreenSize.Small => 3, ScreenSize.Wide => 4,
            ScreenSize.Medium => 6, ScreenSize.Large => 10,
            ScreenSize.ExtraLarge => 16, _ => 3
        };

        private IGridCameraFeedProvider ResolveCameraProvider()
        {
            var sources = ResolveAllProviders();
            // Use the most recently selected camera when multiple camera sources are present,
            // so two cameras never fight for the same screen.
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                if (sources[i] is IGridCameraFeedProvider cameraProvider)
                    return cameraProvider;
            }
            return null;
        }

        private string CameraStatusDisplay()
        {
            var cameraProvider = ResolveCameraProvider();
            if (cameraProvider == null)
                return "CAMERA\nNO CAMERA SOURCE\nRight-click to configure";

            if (!cameraProvider.IsOnline)
                return "CAMERA\nSOURCE OFFLINE";

            return "CAMERA\n" + (cameraProvider.IsFeedInUse ? "LIVE FEED" : "READY") + "\n" + cameraProvider.SourceName;
        }

        private string PowerDisplay()
        {
            if (Grid == null)
                return "POWER\nNO GRID";

            float gain = Mathf.Max(0f, Grid.PowerGenerated);
            float loss = Mathf.Max(0f, Grid.PowerConsumed);
            float net = gain - loss;
            string state = net >= -0.1f ? "STABLE" : "DEFICIT";
            return "POWER " + state + "\n" +
                   "Gain +" + FormatWatts(gain) + "\n" +
                   "Loss -" + FormatWatts(loss) + "\n" +
                   "Net " + (net >= 0f ? "+" : "") + FormatWatts(net);
        }

        private static string FormatWatts(float watts)
        {
            float abs = Mathf.Abs(watts);
            if (abs >= 1000000f) return (watts / 1000000f).ToString("0.##") + " MW";
            if (abs >= 1000f) return (watts / 1000f).ToString("0.#") + " kW";
            return watts.ToString("0") + " W";
        }

        // ── Formatting ────────────────────────────────────────────────
        private string FormatSection(string raw, string section, int max)
        {
            var lines = raw.Split('\n');
            var r = new System.Text.StringBuilder();
            int c = 0;
            foreach (var l in lines)
            {
                string t = l.Trim();
                if (t.Length == 0) continue;
                if (c == 0 && !t.Contains(section, System.StringComparison.OrdinalIgnoreCase))
                    r.AppendLine("=" + section + "=");
                else r.AppendLine(" " + t);
                c++; if (c >= max) break;
            }
            return r.ToString().Trim();
        }

        private string TruncateLines(string text, int max)
        {
            var lines = text.Split('\n');
            if (lines.Length <= max) return text;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max; i++) sb.AppendLine(lines[i]);
            return sb.ToString().Trim();
        }

        private string FormatBars(string raw, string sourceName, int maxLines)
        {
            var result = new System.Text.StringBuilder();
            result.AppendLine("[" + sourceName.ToUpperInvariant() + "]");
            var lines = raw.Split('\n');
            int bars = 0;
            foreach (var l in lines)
            {
                string t = l.Trim();
                if (t.Length == 0) continue;
                if (bars >= maxLines - 1) break;
                float pct = ExtractPercentage(t);
                if (pct >= 0f) { result.AppendLine(" " + BarString(pct)); bars++; }
                else if (bars < maxLines - 2) { result.AppendLine(" " + t); bars++; }
            }
            if (result.ToString().Trim().Length <= sourceName.Length + 4) return raw;
            return result.ToString().Trim();
        }

        private static float ExtractPercentage(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1f;
            int idx = text.IndexOf('%');
            if (idx < 1) return -1f;
            int start = idx - 1;
            while (start >= 0 && (char.IsDigit(text[start]) || text[start] == '.' || text[start] == ',')) start--;
            start++;
            if (start >= idx) return -1f;
            if (float.TryParse(text.Substring(start, idx - start), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
                return Mathf.Clamp01(val / 100f);
            return -1f;
        }

        private static string BarString(float v01)
        {
            int full = Mathf.Clamp(Mathf.FloorToInt(v01 * 10f), 0, 10);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < full; i++) sb.Append('\u2588');
            for (int i = full; i < 10; i++) sb.Append('\u2591');
            sb.Append(" " + (v01 * 100f).ToString("0") + "%");
            return sb.ToString();
        }

        // ── Lifecycle ─────────────────────────────────────────────────
        private void MigrateLegacy()
        {
            if (_migrated) return;
            _migrated = true;
            if (legacyGridPos != Vector3Int.zero || legacyInstanceId != 0)
            {
                if (!dataSourcePositions.Contains(legacyGridPos))
                {
                    dataSourcePositions.Add(legacyGridPos);
                    dataSourceInstanceIds.Add(legacyInstanceId);
                }
                legacyGridPos = Vector3Int.zero;
                legacyInstanceId = 0;
            }
        }

        private void Start() => EnsureDisplay();

        private void Update()
        {
            if (!_initialized) { EnsureDisplay(); return; }
            if (_screenText == null) return;

            IGridCameraFeedProvider cameraProvider = dataMode == ScreenDataMode.Camera ? ResolveCameraProvider() : null;
            bool showCameraFeed = IsPowered && cameraProvider != null && cameraProvider.IsOnline;
            ApplyCameraFeed(cameraProvider, showCameraFeed);
            SetMainTextVisible(!showCameraFeed);
            ApplyLiveAppearance(showCameraFeed);

            string text = showCameraFeed ? string.Empty : FormattedDisplay;
            if (_screenText.text != text) _screenText.text = text;

            if (_power != null) _power.wattsPerSecond = Enabled ? PowerDraw : 0f;

            var (dw, dh) = DisplayArea();

            if (!Enabled)
            {
                Color d = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                _screenText.color = d;
                if (_titleText != null) _titleText.color = d;
                if (_statusText != null) { _statusText.text = "OFF"; _statusText.color = d; }
            }
            else if (!IsPowered)
            {
                Color w = new Color(0.8f, 0.15f, 0.10f, 0.6f);
                _screenText.color = w;
                if (_titleText != null) _titleText.color = w;
                if (_statusText != null) { _statusText.text = "NO PWR"; _statusText.color = w; }
            }
            else if (showCameraFeed)
            {
                Color live = new Color(0.18f, 0.95f, 0.38f);
                if (_titleText != null)
                {
                    _titleText.text = "CAMERA  " + cameraProvider.SourceName;
                    _titleText.color = new Color(live.r, live.g, live.b, 0.92f);
                }
                if (_statusText != null)
                {
                    _statusText.text = "LIVE";
                    _statusText.color = new Color(live.r, live.g, live.b, 0.95f);
                }
            }
            else if (!HasAnySource)
            {
                Color i = new Color(0.40f, 0.44f, 0.52f);
                _screenText.color = i;
                if (_titleText != null) { _titleText.text = "NO SOURCE"; _titleText.color = i; }
                if (_statusText != null) { _statusText.text = "IDLE"; _statusText.color = i; }
            }
            else
            {
                float pulse = 0.7f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 1.8f);
                _screenText.color = new Color(textColor.r, textColor.g, textColor.b, pulse);
                var sources = ResolveAllProviders();
                if (_titleText != null)
                {
                    _titleText.text = sources.Count + " source" + (sources.Count != 1 ? "s" : "");
                    _titleText.color = new Color(textColor.r * 0.7f, textColor.g * 0.7f, textColor.b * 0.7f, 0.8f);
                }
                if (_statusText != null) { _statusText.text = dataMode == ScreenDataMode.Camera ? "CAM" : "LIVE"; _statusText.color = new Color(textColor.r * 0.5f, textColor.g * 0.5f, textColor.b * 0.5f); }
            }

            if (_titleText != null) _titleText.transform.localPosition = new Vector3(0, dh * 0.38f, 0);
            if (_statusText != null) _statusText.transform.localPosition = new Vector3(dw * 0.42f, -dh * 0.38f, 0);
        }

        private void EnsureDisplay()
        {
            if (_initialized) return;
            _initialized = true;
            MigrateLegacy();

            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.wattsPerSecond = PowerDraw;
            _power.connectRadius = 1.6f;

            Transform surface = transform.Find("Generated_ScreenSurface");
            if (surface == null)
            {
                var s = new GameObject("Generated_ScreenSurface");
                s.transform.SetParent(transform, false);
                surface = s.transform;
            }

            var oldRoot = surface.Find("DisplayRoot");
            if (oldRoot != null) DestroyImmediate(oldRoot.gameObject);

            var (dw, dh) = DisplayArea();
            float charSize = CharHeight() / 24f;
            _baseCharSize = charSize;

            _screenSurfaceRenderer = surface.GetComponent<Renderer>();
            if (_screenSurfaceRenderer != null && _screenSurfaceBaseMaterial == null)
                _screenSurfaceBaseMaterial = _screenSurfaceRenderer.sharedMaterial;
            CacheAppearanceRenderers();

            var root = new GameObject("DisplayRoot");
            root.transform.SetParent(surface, false);
            // Keep text in front of the physical screen surface. After enabling depth testing,
            // the old tiny offset could sit inside the screen cube and be self-occluded.
            root.transform.localPosition = new Vector3(0, 0, -0.12f);

            var tObj = new GameObject("ScreenTitleText");
            tObj.transform.SetParent(root.transform, false);
            tObj.transform.localPosition = new Vector3(0, dh * 0.38f, 0);
            _titleText = tObj.AddComponent<TextMesh>();
            _titleText.text = "NO SOURCE"; _titleText.fontSize = 20;
            _titleText.characterSize = charSize;
            _titleText.color = new Color(0.50f, 0.55f, 0.65f);
            _titleText.anchor = TextAnchor.UpperCenter;
            _titleText.alignment = TextAlignment.Center;
            _titleText.fontStyle = FontStyle.Bold;
            MakeTextOpaque(_titleText);

            var dObj = new GameObject("ScreenDisplayText");
            dObj.transform.SetParent(root.transform, false);
            dObj.transform.localPosition = Vector3.zero;
            _screenText = dObj.AddComponent<TextMesh>();
            _screenText.text = "STARTING"; _screenText.fontSize = 24;
            _screenText.characterSize = charSize; _screenText.color = textColor;
            _screenText.anchor = TextAnchor.MiddleCenter; _screenText.alignment = TextAlignment.Center;
            MakeTextOpaque(_screenText);

            var sObj = new GameObject("ScreenStatusText");
            sObj.transform.SetParent(root.transform, false);
            sObj.transform.localPosition = new Vector3(dw * 0.42f, -dh * 0.38f, 0);
            _statusText = sObj.AddComponent<TextMesh>();
            _statusText.text = "BOOT"; _statusText.fontSize = 12;
            _statusText.characterSize = charSize;
            _statusText.color = new Color(0.30f, 0.35f, 0.45f);
            _statusText.anchor = TextAnchor.LowerRight; _statusText.alignment = TextAlignment.Right;
            MakeTextOpaque(_statusText);
        }

        private void CacheAppearanceRenderers()
        {
            Transform glow = transform.Find("Generated_GlowStrip");
            if (glow != null) _glowStripRenderer = glow.GetComponent<Renderer>();

            _cornerDotRenderers.Clear();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].name == "Generated_CornerDot")
                    _cornerDotRenderers.Add(renderers[i]);
            }

            _appearanceBlock ??= new MaterialPropertyBlock();
        }

        private void ApplyLiveAppearance(bool cameraFeedVisible)
        {
            if (_glowStripRenderer == null && _cornerDotRenderers.Count == 0)
                CacheAppearanceRenderers();

            int safeBorderStyle = Mathf.Clamp(borderStyle, 0, 3);
            bool borderVisible = safeBorderStyle > 0;
            Color accent = cameraFeedVisible ? new Color(0.18f, 0.95f, 0.38f) : textColor;
            float pulse = cameraFeedVisible ? 1.05f + Mathf.Sin(Time.realtimeSinceStartup * 4.5f) * 0.12f : 0.75f + Mathf.Sin(Time.realtimeSinceStartup * 1.8f) * 0.10f;
            float strength = safeBorderStyle == 1 ? 0.35f : safeBorderStyle == 2 ? 0.75f : 1.25f;
            Color baseColor = new Color(accent.r, accent.g, accent.b, borderVisible ? 1f : 0f);
            Color emission = accent * Mathf.Max(0.05f, strength * pulse);

            ApplyRendererAccent(_glowStripRenderer, borderVisible, baseColor, emission);
            for (int i = 0; i < _cornerDotRenderers.Count; i++)
                ApplyRendererAccent(_cornerDotRenderers[i], borderVisible && safeBorderStyle >= 2, baseColor, emission);

            ApplyFontStyle();
        }

        private void ApplyRendererAccent(Renderer renderer, bool visible, Color baseColor, Color emission)
        {
            if (renderer == null) return;
            renderer.enabled = visible;
            if (!visible) return;
            _appearanceBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_appearanceBlock);
            _appearanceBlock.SetColor("_Color", baseColor);
            _appearanceBlock.SetColor("_BaseColor", baseColor);
            _appearanceBlock.SetColor("_EmissionColor", emission);
            renderer.SetPropertyBlock(_appearanceBlock);
        }

        private void ApplyFontStyle()
        {
            float baseSize = _baseCharSize > 0f ? _baseCharSize : CharHeight() / 24f;
            int safeFontStyle = Mathf.Clamp(fontStyle, 0, 3);
            int displayFontSize = safeFontStyle switch
            {
                1 => 22,
                2 => 26,
                3 => 22,
                _ => 24
            };
            float charScale = safeFontStyle switch
            {
                1 => 0.92f,
                2 => 0.95f,
                3 => 0.86f,
                _ => 1f
            };
            FontStyle displayStyle = safeFontStyle switch
            {
                2 => FontStyle.Bold,
                3 => FontStyle.Bold,
                _ => FontStyle.Normal
            };
            float lineSpacing = safeFontStyle switch
            {
                1 => 0.85f,
                2 => 0.80f,
                3 => 0.90f,
                _ => 1.00f
            };

            if (_screenText != null)
            {
                _screenText.fontSize = displayFontSize;
                _screenText.characterSize = baseSize * charScale;
                _screenText.fontStyle = displayStyle;
                _screenText.lineSpacing = lineSpacing;
            }
            if (_titleText != null)
                _titleText.fontStyle = safeFontStyle == 0 ? FontStyle.Bold : displayStyle;
            if (_statusText != null)
                _statusText.fontStyle = safeFontStyle == 0 ? FontStyle.Normal : displayStyle;
        }

        private void ApplyCameraFeed(IGridCameraFeedProvider cameraProvider, bool showFeed)
        {
            if (_screenSurfaceRenderer == null)
            {
                Transform surface = transform.Find("Generated_ScreenSurface");
                if (surface != null)
                    _screenSurfaceRenderer = surface.GetComponent<Renderer>();
                if (_screenSurfaceRenderer != null && _screenSurfaceBaseMaterial == null)
                    _screenSurfaceBaseMaterial = _screenSurfaceRenderer.sharedMaterial;
            }

            if (_screenSurfaceRenderer == null) return;

            if (!showFeed || cameraProvider == null)
            {
                if (_cameraFeedVisible && _screenSurfaceBaseMaterial != null)
                    _screenSurfaceRenderer.sharedMaterial = _screenSurfaceBaseMaterial;
                if (_cameraFeedQuadRenderer != null)
                    _cameraFeedQuadRenderer.enabled = false;
                _cameraFeedVisible = false;
                return;
            }

            cameraProvider.RegisterFeedConsumer(this);
            var feedTexture = cameraProvider.FeedTexture;
            if (feedTexture == null)
                return;

            if (_cameraFeedMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Texture")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                _cameraFeedMaterial = new Material(shader) { name = "ScreenCameraFeed_Runtime" };
                if (_cameraFeedMaterial.HasProperty("_Metallic")) _cameraFeedMaterial.SetFloat("_Metallic", 0f);
                if (_cameraFeedMaterial.HasProperty("_Smoothness")) _cameraFeedMaterial.SetFloat("_Smoothness", 0.88f);
                _cameraFeedMaterial.EnableKeyword("_EMISSION");
            }

            _cameraFeedMaterial.mainTexture = feedTexture;
            if (_cameraFeedMaterial.HasProperty("_BaseMap")) _cameraFeedMaterial.SetTexture("_BaseMap", feedTexture);
            if (_cameraFeedMaterial.HasProperty("_MainTex")) _cameraFeedMaterial.SetTexture("_MainTex", feedTexture);
            if (_cameraFeedMaterial.HasProperty("_BaseColor")) _cameraFeedMaterial.SetColor("_BaseColor", Color.white);
            if (_cameraFeedMaterial.HasProperty("_Color")) _cameraFeedMaterial.SetColor("_Color", Color.white);
            if (_cameraFeedMaterial.HasProperty("_EmissionColor")) _cameraFeedMaterial.SetColor("_EmissionColor", new Color(0.16f, 0.75f, 0.92f) * 0.45f);
            if (_cameraFeedMaterial.HasProperty("_Cull")) _cameraFeedMaterial.SetFloat("_Cull", 0f);

            if (_screenSurfaceRenderer.sharedMaterial != _cameraFeedMaterial)
                _screenSurfaceRenderer.sharedMaterial = _cameraFeedMaterial;

            EnsureCameraFeedQuad();
            if (_cameraFeedQuadRenderer != null)
            {
                _cameraFeedQuadRenderer.enabled = true;
                _cameraFeedQuadRenderer.sharedMaterial = _cameraFeedMaterial;
            }
            _cameraFeedVisible = true;
        }

        private void EnsureCameraFeedQuad()
        {
            if (_cameraFeedQuadRenderer != null) return;

            Transform existing = transform.Find("CameraFeedQuad_Runtime");
            GameObject quad = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "CameraFeedQuad_Runtime";
            quad.transform.SetParent(transform, false);
            var (dw, dh) = DisplayArea();
            quad.transform.localPosition = new Vector3(0f, 0f, -0.24f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(dw * 0.96f, dh * 0.96f, 1f);

            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            _cameraFeedQuadRenderer = quad.GetComponent<Renderer>();
            if (_cameraFeedQuadRenderer != null)
            {
                _cameraFeedQuadRenderer.enabled = false;
                _cameraFeedQuadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cameraFeedQuadRenderer.receiveShadows = false;
            }
        }

        private void SetMainTextVisible(bool visible)
        {
            if (_screenText == null) return;
            var renderer = _screenText.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = visible;
        }

        /// <summary>Ensures TextMesh respects depth so screen text cannot render through terrain or blocks.</summary>
        private static void MakeTextOpaque(TextMesh tm)
        {
            if (tm == null) return;
            var renderer = tm.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var source = renderer.sharedMaterial;
            if (source == null) return;

            // Preserve Unity's working TextMesh font shader/material so glyph alpha stays visible.
            // Only change depth behavior; swapping to a generic cutout shader made some screens black.
            var mat = new Material(source) { name = "ScreenText_DepthTest" };
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (mat.HasProperty("unity_GUIZTestMode")) mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            renderer.sharedMaterial = mat;
            renderer.sortingOrder = 0;
        }

        // ── Multi-source management ───────────────────────────────────
        public List<IGridDataProvider> ResolveAllProviders()
        {
            _cachedProviders.Clear();
            if (Grid == null) return _cachedProviders;
            if (!_migrated) MigrateLegacy();

            for (int i = dataSourcePositions.Count - 1; i >= 0; i--)
            {
                var pos = dataSourcePositions[i];
                int storedId = i < dataSourceInstanceIds.Count ? dataSourceInstanceIds[i] : 0;

                IGridDataProvider provider = null;
                if (Grid.Blocks.TryGetValue(pos, out var block))
                {
                    provider = ResolveProviderComponent(block);
                    if (provider != null)
                    {
                        int currentId = block.GetInstanceID();
                        if (currentId != storedId)
                        {
                            // Block was replaced — update instance id
                            dataSourceInstanceIds[i] = currentId;
                        }
                        _cachedProviders.Add(provider);
                        continue;
                    }
                }

                // Source no longer valid — remove it
                dataSourcePositions.RemoveAt(i);
                if (i < dataSourceInstanceIds.Count) dataSourceInstanceIds.RemoveAt(i);
            }
            return _cachedProviders;
        }

        public bool HasSource(Vector3Int pos)
        {
            if (!_migrated) MigrateLegacy();
            return dataSourcePositions.Contains(pos);
        }

        public void ToggleSource(Vector3Int pos, int instanceId)
        {
            if (!_migrated) MigrateLegacy();
            if (dataSourcePositions.Contains(pos))
            {
                int idx = dataSourcePositions.IndexOf(pos);
                dataSourcePositions.RemoveAt(idx);
                if (idx < dataSourceInstanceIds.Count) dataSourceInstanceIds.RemoveAt(idx);
            }
            else
            {
                dataSourcePositions.Add(pos);
                dataSourceInstanceIds.Add(instanceId);
            }
        }

        public void SetPrimarySource(Vector3Int pos, int instanceId)
        {
            if (!_migrated) MigrateLegacy();
            dataSourcePositions.Clear();
            dataSourceInstanceIds.Clear();
            dataSourcePositions.Add(pos);
            dataSourceInstanceIds.Add(instanceId);
        }

        public void ClearSources()
        {
            if (!_migrated) MigrateLegacy();
            dataSourcePositions.Clear();
            dataSourceInstanceIds.Clear();
        }

        public void AutoLinkToNearestProvider()
        {
            if (Grid == null) return;
            if (!_migrated) MigrateLegacy();
            if (dataSourcePositions.Count > 0) return; // already has sources

            IGridDataProvider best = null; Vector3Int bp = default; float bd = float.MaxValue;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value == null || kv.Value == this) continue;
                if (VoxelEngine.GridSystem.UI.GridMasterTerminal.IsHiddenFromScreenConfig(kv.Value)) continue;
                var provider = ResolveProviderComponent(kv.Value);
                if (provider == null) continue;
                float d = Vector3.Distance(kv.Value.transform.position, transform.position);
                if (d < bd) { bd = d; best = provider; bp = kv.Key; }
            }
            if (best != null)
                ToggleSource(bp, (best as GridBlock)?.GetInstanceID() ?? 0);
        }

        private static IGridDataProvider ResolveProviderComponent(GridBlock block)
        {
            if (block == null) return null;
            if (block is IGridDataProvider direct) return direct;
            var behaviours = block.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IGridDataProvider provider)
                    return provider;
            }
            return null;
        }

        public List<(Vector3Int pos, IGridDataProvider provider)> GetAvailableSources()
        {
            var list = new List<(Vector3Int, IGridDataProvider)>();
            if (Grid == null) return list;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value == null || kv.Value == this) continue;
                if (VoxelEngine.GridSystem.UI.GridMasterTerminal.IsHiddenFromScreenConfig(kv.Value)) continue;
                var provider = ResolveProviderComponent(kv.Value);
                if (provider != null)
                    list.Add((kv.Key, provider));
            }
            return list;
        }

        private void OnDestroy()
        {
            if (_cameraFeedMaterial != null)
                Destroy(_cameraFeedMaterial);
        }

        public override void OnPlaced()
        {
            base.OnPlaced(); blockName = "Screen (" + screenSize + ")";
            _initialized = false; AutoLinkToNearestProvider();
        }
    }
}
