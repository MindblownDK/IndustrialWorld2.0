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
        // TeamBanner (14.37.0) sits at the end: the mode persists as its
        // enum NAME, so appending is save-compatible either way.
        Summary, Power, Inventory, Speed, System, Bars, Custom, Camera, TeamBanner,
        // 17.35.0-dev — the second wave of pages, APPENDED for the same reason.
        // ScreenPageCatalog describes every one of them to the picker; the ones this
        // block renders itself are in GridScreenBlock.Pages.cs.
        Environment, BatteryBank, PowerGraph, Manifest, FillBars, Farm, Mining,
        InventoryGrid, PlanetMap,
        // 17.36.0-dev — the second mining page, for pairing across a split panel.
        MiningCargo
    }

    public enum ScreenSize
    {
        Small, Medium, Large, ExtraLarge, Wide
    }

    public partial class GridScreenBlock : GridBlock, IGridDataProvider
    {
        [Header("Screen")]
        public ScreenSize screenSize = ScreenSize.Small;
        public ScreenDataMode dataMode = ScreenDataMode.Summary;
        public Color textColor = new Color(0.18f, 0.72f, 0.88f);

        /// <summary>
        /// 17.36.0-dev - one glass, up to four pages. 0 is the whole panel on one page;
        /// 1 is two side by side, 2 is two stacked, 3 is four quarters. The panes are equal
        /// by design: a screen is an instrument, and an instrument with an uneven split is
        /// just a window with a mullion in the wrong place.
        /// </summary>
        [Tooltip("Split layout: 0=One page, 1=Two side by side, 2=Two stacked, 3=Four quarters")]
        [Range(0, 3)] public int splitMode;

        [Tooltip("Page in pane 1 (left, or top when stacked)")]
        public ScreenDataMode splitPageA = ScreenDataMode.PowerGraph;
        [Tooltip("Page in pane 2 (right, or bottom when stacked)")]
        public ScreenDataMode splitPageB = ScreenDataMode.BatteryBank;
        [Tooltip("Page in pane 3 (bottom left, quadrants only)")]
        public ScreenDataMode splitPageC = ScreenDataMode.Manifest;
        [Tooltip("Page in pane 4 (bottom right, quadrants only)")]
        public ScreenDataMode splitPageD = ScreenDataMode.Environment;
        public string customText = "CUSTOM DISPLAY";
        /// <summary>TeamBanner mode (14.37.0): whose banner the screen shows.
        /// Set when the mode is chosen in the config panel - the banner itself
        /// is edited in PAUSE > TEAMS, never here. "" = default emblem.</summary>
        public string bannerTeamId = "";
        [Header("Appearance")]
        [Tooltip("Border style: 0=None, 1=Thin, 2=Thick, 3=Glow")]
        public int borderStyle = 1;
        [Tooltip("Font style: 0=Default, 1=Bold, 2=Wide, 3=Compact")]
        public int fontStyle = 0;

        [Header("Data Sources")]
        public List<Vector3Int> dataSourcePositions = new();
        public List<EntityId> dataSourceInstanceIds = new(); // session-local handles; re-bound by ResolveAllProviders()

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
        // 17.35.1-dev — the glass, in block-local metres, and where its front face is.
        // Read from the surface renderer rather than assumed: the surface cube is 1 m at
        // scale 1, so its actual size is what the text has to fit inside.
        private float _glassWidth;
        private float _glassHeight;
        private float _faceZ = -0.02f;
        // Last strings the text was fitted to, so a fit is only recomputed when the
        // characters actually change (Update runs every frame).
        private string _fittedBody, _fittedTitle, _fittedStatus;
        private bool _textFitDirty;
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
        /// <summary>True when this screen has something to show: a linked source, or a page
        /// that reads the construct and the world by itself. Which pages those are is the
        /// catalogue's answer, not a list kept here.</summary>
        public bool HasAnySource => IsSplit
                                 || ResolveAllProviders().Count > 0
                                 || !ScreenPageCatalog.NeedsSource(dataMode);
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
                // A split panel shows its panes; customText belongs to a single-page screen.
                if (dataMode == ScreenDataMode.Custom && !IsSplit)
                    return string.IsNullOrEmpty(customText) ? "(empty)" : customText;
                // 17.35.0-dev — the pages that read the construct and the world directly.
                // GridScreenBlock.Pages.cs owns them; RenderManagedPage returns null for every
                // page it does not own, so the older formatters below are untouched.
                string managed = RenderManagedPage(dataMode);
                if (managed != null) return managed;
                if (dataMode == ScreenDataMode.Camera) return CameraStatusDisplay();
                if (dataMode == ScreenDataMode.TeamBanner) return BannerTextDisplay();
                if (dataMode == ScreenDataMode.Power) return PowerDisplay();

                // 17.36.0-dev — LESS FURNITURE, MORE READING.
                //
                // The old page drew a `──┘` rule between every source and repeated the
                // section name in capitals above each one. On a Small panel that was half
                // the glass spent on decoration, and on a Large panel it still read as a
                // form rather than an instrument. A source is now introduced by its own
                // name and nothing else, and the rules are gone.
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

                    int remaining = max - CountLines(result.ToString());
                    if (remaining <= 0) break;

                    switch (dataMode)
                    {
                        case ScreenDataMode.Bars:
                            result.AppendLine(FormatBars(raw, p.SourceName, Mathf.Min(linesPerSource, remaining)));
                            break;
                        case ScreenDataMode.System:
                            result.AppendLine(FormatSection(raw, p.SourceName.ToUpperInvariant(), Mathf.Min(linesPerSource, remaining)));
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

        /// <summary>
        /// The instant power reading, with the two figures drawn against the larger of them
        /// so a deficit is visible before the number is read. The graph page next to it in
        /// the picker is the same question over time; this one is "right now".
        /// </summary>
        private string PowerDisplay()
        {
            if (Grid == null)
                return "POWER\nNO GRID";

            float gain = Mathf.Max(0f, Grid.PowerGenerated);
            float loss = Mathf.Max(0f, Grid.PowerConsumed);
            float net = gain - loss;
            float scale = Mathf.Max(1f, Mathf.Max(gain, loss));
            string state = net >= -0.1f ? "STABLE" : "DEFICIT";

            return "POWER " + state + "\n" +
                   "GEN " + PlainBar(gain / scale) + " " + FormatWatts(gain) + "\n" +
                   "USE " + PlainBar(loss / scale) + " " + FormatWatts(loss) + "\n" +
                   "NET " + (net >= 0f ? "+" : "-") + FormatWatts(Mathf.Abs(net));
        }

        private static string FormatWatts(float watts)
        {
            float abs = Mathf.Abs(watts);
            if (abs >= 1000000f) return (watts / 1000000f).ToString("0.##") + " MW";
            if (abs >= 1000f) return (watts / 1000f).ToString("0.#") + " kW";
            return watts.ToString("0") + " W";
        }

        // ── Formatting ────────────────────────────────────────────────
        /// <summary>One source's lines under its own name, with no decoration around them.</summary>
        private string FormatSection(string raw, string section, int max)
        {
            var lines = raw.Split('\n');
            var r = new System.Text.StringBuilder();
            int c = 0;
            foreach (var l in lines)
            {
                string t = l.Trim();
                if (t.Length == 0) continue;
                r.AppendLine(t);
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

        /// <summary>Ten block steps and nothing else - no percentage, for the rows that
        /// already print their own number.</summary>
        private static string PlainBar(float v01)
        {
            int full = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(v01) * 10f), 0, 10);
            var sb = new System.Text.StringBuilder(10);
            for (int i = 0; i < full; i++) sb.Append('\u2588');
            for (int i = full; i < 10; i++) sb.Append('\u2591');
            return sb.ToString();
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
                    // Legacy int instance ids can't map onto EntityIds (and were
                    // session-local anyway) — seed None; ResolveAllProviders()
                    // re-binds the live id on the next read.
                    dataSourceInstanceIds.Add(EntityId.None);
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
            // Team banner (14.37.0): the cloth fills the surface through the
            // same quad the camera feed uses; the three banner text lines ride
            // the main TextMesh on top, dark so they read against the cloth.
            bool showBanner = !showCameraFeed && dataMode == ScreenDataMode.TeamBanner && IsPowered;
            if (showBanner) ApplyBannerTexture();
            else ApplyCameraFeed(cameraProvider, showCameraFeed);
            SetMainTextVisible(!showCameraFeed);
            ApplyLiveAppearance(showCameraFeed || showBanner);

            string text = showCameraFeed ? string.Empty : StyleText(FormattedDisplay, fontStyle);
            if (_screenText.text != text)
            {
                _screenText.text = text;
                _textFitDirty = true;
                _iconsDirty = true;
            }

            if (_power != null) _power.wattsPerSecond = Enabled ? PowerDraw : 0f;

            var (dw, dh) = GlassSize();

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
            else if (showBanner)
            {
                Color ink = new Color(0.14f, 0.10f, 0.08f);
                _screenText.color = ink;
                Color gold = new Color(0.85f, 0.68f, 0.21f);
                if (_titleText != null)
                {
                    var bannerTeam = VoxelEngine.Networking.TeamRegistry.TeamById(bannerTeamId);
                    _titleText.text = bannerTeam != null ? bannerTeam.name.ToUpperInvariant() : "TEAM BANNER";
                    _titleText.color = new Color(gold.r, gold.g, gold.b, 0.95f);
                }
                if (_statusText != null)
                {
                    _statusText.text = "LIVE";
                    _statusText.color = new Color(gold.r, gold.g, gold.b, 0.9f);
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
                    // 17.35.0-dev: a managed page names itself along the top. "2 sources"
                    // told a player nothing about what they were looking at; "BATTERIES"
                    // does, and the page is the same on every screen that shows it.
                    // A split panel is named after its layout, not after one of its pages:
                    // the pages name themselves inside their own corners.
                    _titleText.text = IsSplit
                        ? "SPLIT x" + SplitPaneCount
                        : IsManagedPage(dataMode)
                            ? ScreenPageCatalog.For(dataMode).Label.ToUpperInvariant()
                            : sources.Count + " source" + (sources.Count != 1 ? "s" : "");
                    _titleText.color = new Color(textColor.r * 0.7f, textColor.g * 0.7f, textColor.b * 0.7f, 0.8f);
                }
                if (_statusText != null) { _statusText.text = dataMode == ScreenDataMode.Camera ? "CAM" : "LIVE"; _statusText.color = new Color(textColor.r * 0.5f, textColor.g * 0.5f, textColor.b * 0.5f); }
            }

            if (_titleText != null) _titleText.transform.localPosition = new Vector3(0, dh * 0.455f, 0);
            if (_statusText != null) _statusText.transform.localPosition = new Vector3(dw * 0.455f, -dh * 0.455f, 0);
            if (_screenText != null) _screenText.transform.localPosition = new Vector3(-dw * 0.47f, 0f, 0f);

            // 17.35.1-dev: the type is measured against the glass after the text is set,
            // so it fits the panel it is actually on rather than a fixed guess.
            RefreshTextFit();
            // 17.36.0-dev: item pictures are placed from that same measurement, so a
            // picture can never drift away from the count it belongs to.
            RefreshPageIcons();
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
                // A screen without its glass has nothing to show on: build one at the size
                // this block's ScreenSize promises, so an authored or migrated prefab that
                // lost the surface still renders instead of swallowing its own text.
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.name = "Generated_ScreenSurface";
                s.transform.SetParent(transform, false);
                var (fw, fh) = DisplayArea();
                s.transform.localScale = new Vector3(fw, fh, 0.06f);
                var c = s.GetComponent<Collider>();
                if (c != null) Destroy(c);
                surface = s.transform;
            }

            var staleRoot = surface.Find("DisplayRoot");
            if (staleRoot != null) DestroyImmediate(staleRoot.gameObject);
            staleRoot = transform.Find("DisplayRoot");
            if (staleRoot != null) DestroyImmediate(staleRoot.gameObject);

            _screenSurfaceRenderer = surface.GetComponent<Renderer>();
            if (_screenSurfaceRenderer != null && _screenSurfaceBaseMaterial == null)
                _screenSurfaceBaseMaterial = _screenSurfaceRenderer.sharedMaterial;
            CacheAppearanceRenderers();
            EnsureGlassProudOfFrame(surface);

            // The root exists before the glass is measured, because measuring is what puts
            // it on the front face - a root created afterwards would sit at the block's
            // origin, inside the frame, with its text buried in the middle of the block.
            var root = new GameObject("DisplayRoot");
            root.transform.SetParent(transform, false);
            root.transform.localRotation = surface.localRotation;
            root.transform.localScale = Vector3.one;
            MeasureGlass(surface);

            float charSize = CharHeight() / 24f;
            _baseCharSize = charSize;

            var tObj = new GameObject("ScreenTitleText");
            tObj.transform.SetParent(root.transform, false);
            tObj.transform.localPosition = new Vector3(0, _glassHeight * 0.44f, 0);
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
            // 17.36.0-dev — LEFT-ALIGNED, FROM THE LEFT EDGE.
            //
            // A page is a character grid: a bar starts at column zero and an item picture
            // sits above the column its count was printed in. Centre-aligned text centres
            // EACH LINE separately, so two rows of different lengths no longer share a
            // column grid and the pictures have nothing to line up with. The body is now
            // anchored at the left edge of the glass, which is also how a monitor reads.
            dObj.transform.localPosition = new Vector3(-_glassWidth * 0.47f, 0f, 0f);
            _screenText = dObj.AddComponent<TextMesh>();
            _screenText.text = "STARTING"; _screenText.fontSize = 24;
            _screenText.characterSize = charSize; _screenText.color = textColor;
            _screenText.anchor = TextAnchor.MiddleLeft; _screenText.alignment = TextAlignment.Left;
            MakeTextOpaque(_screenText);

            var sObj = new GameObject("ScreenStatusText");
            sObj.transform.SetParent(root.transform, false);
            sObj.transform.localPosition = new Vector3(_glassWidth * 0.455f, -_glassHeight * 0.44f, 0);
            _statusText = sObj.AddComponent<TextMesh>();
            _statusText.text = "BOOT"; _statusText.fontSize = 12;
            _statusText.characterSize = charSize;
            _statusText.color = new Color(0.30f, 0.35f, 0.45f);
            _statusText.anchor = TextAnchor.LowerRight; _statusText.alignment = TextAlignment.Right;
            MakeTextOpaque(_statusText);

            // Everything above was created at the right scale for the first time; the fit
            // pass below then measures the meshes and corrects them.
            _textFitDirty = true;
            FitScreenText();
        }

        // ── Fitting the text to the glass ───────────────────────────────────────
        // WHY THIS EXISTS (17.35.1-dev): the DisplayRoot used to be parented to the
        // Generated_ScreenSurface cube, which is scaled up to the size of the panel. Every
        // child inherited that scale, so a Medium screen drew text four times too large that
        // spilled out of the frame and hung in the air beside it. Two things were wrong and
        // both are fixed here: the root no longer lives under a scaled object, and the size
        // of the type is now MEASURED from the mesh it produced instead of guessed from a
        // font-metric constant. Whatever the font does, the text lands on the glass.

        /// <summary>
        /// The generated frame is a SOLID CUBE, not a bezel with a hole in it. The setup
        /// script mounts the glass 5 mm "recessed" inside it, which on a solid cube does not
        /// mean recessed - it means buried. That is the real reason a screen showed nothing
        /// but a dark box: the glass, the glow strip and the corner dots were all sitting
        /// behind an opaque wall, and the only thing that ever escaped was text that was so
        /// oversized it spilled out past the frame's silhouette.
        ///
        /// The fix is a nudge, done at runtime so every prefab ever generated is corrected
        /// without being rebuilt: whatever is meant to be seen is moved until its front face
        /// clears the frame's by a few millimetres.
        /// </summary>
        private void EnsureGlassProudOfFrame(Transform surface)
        {
            Transform frame = transform.Find("Generated_Frame");
            if (frame == null) return;
            var frameRenderer = frame.GetComponent<Renderer>();
            if (frameRenderer == null) return;

            float frameFront = FrontZ(frame, frameRenderer);
            // Layered, not stacked on one plane: the glow strip and the corner dots are
            // decoration on top of the glass, so each one clears the last by a few
            // millimetres. At 60 Hz a shared plane is a shimmering mess.
            BringProud(surface, frameFront, 0.004f);
            var glow = transform.Find("Generated_GlowStrip");
            if (glow != null) BringProud(glow, frameFront, 0.008f);

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.name == "Generated_CornerDot") BringProud(r.transform, frameFront, 0.011f);
            }
        }

        /// <summary>The most forward (most negative) local Z of a child's geometry.</summary>
        private static float FrontZ(Transform child, Renderer renderer)
            => child.localPosition.z - Mathf.Abs(child.localScale.z) * renderer.localBounds.size.z * 0.5f;

        /// <summary>Slide a child forward along its local Z until its front clears the glass
        /// plane by <paramref name="clearance"/>; never slide it backwards.</summary>
        private static void BringProud(Transform child, float frameFront, float clearance)
        {
            var renderer = child.GetComponent<Renderer>();
            if (renderer == null) return;

            float wanted = frameFront - clearance;
            float current = FrontZ(child, renderer);
            if (current <= wanted) return;

            Vector3 local = child.localPosition;
            local.z -= (current - wanted);
            child.localPosition = local;
        }

        /// <summary>Read the real glass size and front face off the surface renderer, in
        /// block-local metres, and put the display root just proud of that face.</summary>
        private void MeasureGlass(Transform surface)
        {
            var (fallbackW, fallbackH) = DisplayArea();
            _glassWidth = fallbackW;
            _glassHeight = fallbackH;

            var renderer = surface.GetComponent<Renderer>();
            Vector3 boundsSize = renderer != null ? renderer.localBounds.size : new Vector3(1f, 1f, 0.06f);
            if (boundsSize.x < 0.0001f || boundsSize.y < 0.0001f)
                boundsSize = new Vector3(1f, 1f, 0.06f);

            Vector3 lossy = surface.lossyScale;
            Vector3 blockScale = transform.lossyScale;
            float blockX = Mathf.Max(0.0001f, Mathf.Abs(blockScale.x));
            float blockY = Mathf.Max(0.0001f, Mathf.Abs(blockScale.y));

            // A cube is 1 m at scale 1, so size-on-screen is localBounds times lossy scale.
            _glassWidth = Mathf.Max(0.05f, Mathf.Abs(lossy.x) * boundsSize.x / blockX);
            _glassHeight = Mathf.Max(0.05f, Mathf.Abs(lossy.y) * boundsSize.y / blockY);

            // Front face centre, converted through the surface's own transform so a rotated
            // or offset screen still puts its text on its own glass.
            Vector3 faceWorld = surface.TransformPoint(new Vector3(0f, 0f, -boundsSize.z * 0.5f));
            Vector3 faceLocal = transform.InverseTransformPoint(faceWorld);
            // 3 cm proud of the glass: in front of the glow strip, the corner dots and the
            // camera-feed quad, and still visually part of the panel.
            Vector3 outward = transform.InverseTransformDirection(
                surface.TransformDirection(Vector3.back)).normalized;
            Vector3 rootLocal = faceLocal + outward * 0.03f;
            _faceZ = rootLocal.z;

            var root = transform.Find("DisplayRoot");
            if (root != null)
            {
                root.localPosition = rootLocal;
                root.localRotation = surface.localRotation;
            }
        }

        private (float w, float h) GlassSize()
        {
            var (fw, fh) = DisplayArea();
            return (_glassWidth > 0.001f ? _glassWidth : fw,
                    _glassHeight > 0.001f ? _glassHeight : fh);
        }

        /// <summary>Refit the three text meshes when their character content has changed.</summary>
        private void RefreshTextFit()
        {
            string body = _screenText != null ? _screenText.text : null;
            string title = _titleText != null ? _titleText.text : null;
            string status = _statusText != null ? _statusText.text : null;

            if (!_textFitDirty
                && body == _fittedBody && title == _fittedTitle && status == _fittedStatus) return;

            _textFitDirty = false;
            if (!FitScreenText())
            {
                // The TextMesh had not built its mesh yet this frame; ask again next frame
                // rather than recording a fit that never happened.
                _textFitDirty = true;
                return;
            }
            _fittedBody = body; _fittedTitle = title; _fittedStatus = status;
        }

        /// <summary>Fit all three texts; returns false when the body mesh was not ready.</summary>
        private bool FitScreenText()
        {
            var (gw, gh) = GlassSize();
            float stretch = StyleStretch(fontStyle);

            // 17.36.1-dev — THE LINE CAP COUNTS THE LINES THAT ARE THERE.
            //
            // It used to divide the panel's maximum line count (3, 4, 6, 10, 16) into the
            // body band, which meant a two-line page on a sixteen-line panel was capped at
            // one sixteenth of the glass and printed as a caption floating in a black box.
            // The cap exists to stop a two-word page filling the panel with giant letters,
            // so it has to count the page's own lines.
            int bodyLines = _screenText != null ? Mathf.Max(1, CountLines(_screenText.text)) : 1;
            bool fitted = FitTextMesh(_screenText, gw * 0.94f, gh * 0.64f,
                gh * 0.62f / bodyLines, stretch);
            FitTextMesh(_titleText, gw * 0.94f, gh * 0.11f);
            FitTextMesh(_statusText, gw * 0.36f, gh * 0.10f);
            return fitted;
        }

        /// <summary>
        /// Scale one TextMesh so its rendered mesh fits a box. The mesh is measured after
        /// the text has been built, which is the only number that is true for the actual
        /// font: no constant about how many metres a character is, no assumption that
        /// fontSize 24 means anything in particular.
        /// </summary>
        /// <param name="lineCapHeight">When positive, no single line may be taller than
        /// this - that is what stops a two-word page from filling the panel with giant
        /// letters while a twelve-line page is squeezed to fit.</param>
        private static bool FitTextMesh(TextMesh tm, float maxWidth, float maxHeight,
            float lineCapHeight = 0f, float xStretch = 1f)
        {
            if (tm == null) return false;

            var filter = tm.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return false;

            Bounds bounds = mesh.bounds;
            if (bounds.size.x <= 0.0001f || bounds.size.y <= 0.0001f) return false;

            // The style may stretch the glyphs horizontally; fit the stretched width.
            float scale = Mathf.Min(maxWidth / (bounds.size.x * Mathf.Max(0.01f, xStretch)),
                                    maxHeight / bounds.size.y);

            if (lineCapHeight > 0.0001f)
            {
                int lines = Mathf.Max(1, CountLines(tm.text));
                float perLine = bounds.size.y / lines;
                if (perLine > 0.0001f) scale = Mathf.Min(scale, lineCapHeight / perLine);
            }

            scale = Mathf.Max(0.0001f, scale);
            tm.transform.localScale = new Vector3(scale * Mathf.Max(0.01f, xStretch), scale, scale);
            return true;
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

            // 17.36.0-dev: the border is drawn ON the glass, not only on the case. The old
            // border setting tinted the case glow strip and the corner dots and nothing else,
            // which from the front read as "the border button does nothing".
            UpdateBorderFrame(safeBorderStyle, accent);

            ApplyFontStyle();
        }

        // ── The border on the glass (17.36.0-dev) ───────────────────────────────

        private readonly Renderer[] _borderQuads = new Renderer[4];
        private Material _borderMaterial;

        /// <summary>
        /// Four thin quads around the edge of the glass, in the player's own text colour.
        /// A screen's border is the one piece of styling you can read from across a room, so
        /// it belongs on the panel where the page is, at the weight the player picked:
        /// Thin 0.8 percent of the panel, Thick 1.6, Glow 2.4 and fully lit.
        /// </summary>
        private void UpdateBorderFrame(int style, Color accent)
        {
            Transform root = transform.Find("DisplayRoot");
            if (root == null) return;

            if (style <= 0)
            {
                for (int i = 0; i < _borderQuads.Length; i++)
                    if (_borderQuads[i] != null) _borderQuads[i].enabled = false;
                return;
            }

            EnsureBorderQuads(root);

            var (gw, gh) = GlassSize();
            float thickness = style == 1 ? gw * 0.008f : style == 2 ? gw * 0.016f : gw * 0.024f;
            thickness = Mathf.Max(0.012f, thickness);
            float inset = thickness * 0.5f;

            float alpha = style == 3 ? 1f : style == 2 ? 0.85f : 0.6f;
            Color colour = new Color(accent.r, accent.g, accent.b, alpha);
            ApplyBorderMaterial(colour, style == 3);

            float halfW = gw * 0.5f, halfH = gh * 0.5f;
            PlaceBorder(_borderQuads[0], new Vector3(0f, halfH - inset, 0.02f),
                new Vector3(gw, thickness, 1f));                              // top
            PlaceBorder(_borderQuads[1], new Vector3(0f, -halfH + inset, 0.02f),
                new Vector3(gw, thickness, 1f));                              // bottom
            PlaceBorder(_borderQuads[2], new Vector3(-halfW + inset, 0f, 0.02f),
                new Vector3(thickness, gh, 1f));                              // left
            PlaceBorder(_borderQuads[3], new Vector3(halfW - inset, 0f, 0.02f),
                new Vector3(thickness, gh, 1f));                              // right
        }

        private void EnsureBorderQuads(Transform root)
        {
            Transform holder = root.Find("PageBorder");
            if (holder == null)
            {
                var go = new GameObject("PageBorder");
                go.transform.SetParent(root, false);
                holder = go.transform;
            }

            for (int i = 0; i < _borderQuads.Length; i++)
            {
                if (_borderQuads[i] != null) continue;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "BorderEdge" + i;
                quad.transform.SetParent(holder, false);
                var collider = quad.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                _borderQuads[i] = renderer;
            }
        }

        private void PlaceBorder(Renderer quad, Vector3 localPosition, Vector3 localScale)
        {
            if (quad == null) return;
            quad.transform.localPosition = localPosition;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = localScale;
            quad.enabled = true;
        }

        private void ApplyBorderMaterial(Color colour, bool glow)
        {
            if (_borderMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("Standard");
                _borderMaterial = new Material(shader) { name = "ScreenBorder_Runtime" };
                _borderMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _borderMaterial.SetOverrideTag("RenderType", "Transparent");
                if (_borderMaterial.HasProperty("_Surface")) _borderMaterial.SetFloat("_Surface", 1f);
                _borderMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _borderMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _borderMaterial.SetInt("_ZWrite", 0);
                _borderMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            _borderMaterial.color = colour;
            if (_borderMaterial.HasProperty("_BaseColor")) _borderMaterial.SetColor("_BaseColor", colour);
            if (_borderMaterial.HasProperty("_EmissionColor"))
            {
                if (glow) _borderMaterial.EnableKeyword("_EMISSION");
                _borderMaterial.SetColor("_EmissionColor", glow ? colour * 1.6f : Color.black);
            }

            for (int i = 0; i < _borderQuads.Length; i++)
                if (_borderQuads[i] != null) _borderQuads[i].sharedMaterial = _borderMaterial;
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

        private int _appliedFontStyle = -1;

        /// <summary>
        /// 17.36.0-dev — FONT STYLES THAT ARE ACTUALLY DIFFERENT.
        ///
        /// The old four options changed the TextMesh font size by two points and the line
        /// spacing by a tenth. The fit pass then normalised the size back to the glass, so
        /// all four landed on the panel looking identical - which is why "the font doesn't
        /// work" was exactly right. A screen font has only three dials that survive being
        /// fitted: CASE, WEIGHT and PROPORTION. Those are the dials used here.
        ///
        ///   Default   mixed case, regular, natural width
        ///   Bold      mixed case, heavy, a little tracking
        ///   Wide      UPPERCASE, regular, wide and airy - a display panel
        ///   Compact   UPPERCASE, bold, condensed and tight - a status terminal
        /// </summary>

        /// <summary>The text a font style actually prints. Public because the config
        /// panel's preview has to show the same glyphs the wall will.</summary>
        public static string StyleText(string text, int style)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return style == 2 || style == 3 ? text.ToUpperInvariant() : text;
        }

        /// <summary>How much wider the style draws its glyphs, as a multiplier on the fit.
        /// Fit compensates for it, so a wide font is still guaranteed to fit the panel.</summary>
        public static float StyleStretch(int style) => style switch
        {
            1 => 1.06f,
            2 => 1.22f,
            3 => 0.90f,
            _ => 1f,
        };

        private void ApplyFontStyle()
        {
            int safeFontStyle = Mathf.Clamp(fontStyle, 0, 3);
            // ApplyLiveAppearance calls this every frame; rebuilding three TextMeshes at
            // 60 Hz costs more than the whole page renderer does.
            if (safeFontStyle == _appliedFontStyle) return;
            _appliedFontStyle = safeFontStyle;
            _textFitDirty = true;

            bool bold = safeFontStyle == 1 || safeFontStyle == 3;
            FontStyle displayStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            float lineSpacing = safeFontStyle switch
            {
                1 => 0.90f,
                2 => 1.05f,
                3 => 0.82f,
                _ => 1.00f,
            };

            // fontSize is the glyph resolution in the atlas, not the size on the panel -
            // the fit pass decides that. A higher value gives the bold/uppercase styles
            // cleaner edges, which is the only thing this number is good for here.
            int displayFontSize = bold ? 28 : 24;

            if (_screenText != null)
            {
                _screenText.fontSize = displayFontSize;
                _screenText.fontStyle = displayStyle;
                _screenText.lineSpacing = lineSpacing;
            }
            if (_titleText != null)
            {
                _titleText.fontSize = 22;
                _titleText.fontStyle = bold ? FontStyle.Bold : FontStyle.Bold;
            }
            if (_statusText != null)
            {
                _statusText.fontSize = 14;
                _statusText.fontStyle = displayStyle;
            }
        }

        /// <summary>The three banner text lines as the screen's text block.
        /// Empty lines collapse; a fully untitled banner shows nothing - the
        /// cloth is the message.</summary>
        private string BannerTextDisplay()
        {
            VoxelEngine.Networking.TeamBannerRegistry.TextsOf(bannerTeamId,
                out string top, out string middle, out string bottom);
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(top)) sb.Append(top);
            if (!string.IsNullOrEmpty(middle)) { if (sb.Length > 0) sb.Append('\n'); sb.Append(middle); }
            if (!string.IsNullOrEmpty(bottom)) { if (sb.Length > 0) sb.Append('\n'); sb.Append(bottom); }
            return sb.ToString();
        }

        /// <summary>Paint the team's cloth across the display surface. Reuses
        /// the camera feed's quad and runtime material - the two modes are
        /// mutually exclusive, so the shared state never fights.</summary>
        private void ApplyBannerTexture()
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

            var cloth = VoxelEngine.Networking.TeamBannerRegistry.ClothTexture(bannerTeamId);
            if (cloth == null) return;

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

            _cameraFeedMaterial.mainTexture = cloth;
            if (_cameraFeedMaterial.HasProperty("_BaseMap")) _cameraFeedMaterial.SetTexture("_BaseMap", cloth);
            if (_cameraFeedMaterial.HasProperty("_MainTex")) _cameraFeedMaterial.SetTexture("_MainTex", cloth);
            if (_cameraFeedMaterial.HasProperty("_BaseColor")) _cameraFeedMaterial.SetColor("_BaseColor", Color.white);
            if (_cameraFeedMaterial.HasProperty("_Color")) _cameraFeedMaterial.SetColor("_Color", Color.white);
            if (_cameraFeedMaterial.HasProperty("_EmissionColor")) _cameraFeedMaterial.SetColor("_EmissionColor", Color.white * 0.18f);
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
            var (dw, dh) = GlassSize();
            // Just proud of the glass and behind the text: 1.2 cm clears the bezel without
            // the feed appearing to hover off the panel the way a -0.24 offset did.
            quad.transform.localPosition = new Vector3(0f, 0f, _faceZ + 0.012f);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(dw * 0.97f, dh * 0.97f, 1f);

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
            // The old copy-the-font-material approach set _ZTest properties
            // that the stock font shader never reads - its ZTest Always is
            // hard-coded, which is exactly why screen text kept shining
            // through terrain and blocks. The shared depth-tested world-text
            // material fixes it for real (14.37.2).
            VoxelEngine.Rendering.WorldTextMaterial.Apply(tm);
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
                EntityId storedId = i < dataSourceInstanceIds.Count ? dataSourceInstanceIds[i] : EntityId.None;

                // 17.36.0-dev: IDENTITY, NOT JUST ADDRESS.
                //
                // Two blocks can report the same grid address - a chest bolted to a wall
                // reports the wall's cell, and every one of them resolved to the first
                // match. That is why selecting one cargo container lit up all of them and
                // why a second click added a source instead of removing the first: the
                // selection was stored by address, so every container shared one entry.
                // The instance id is the thing the player actually clicked.
                IGridDataProvider provider = ResolveByInstanceId(storedId);
                if (provider == null && UnifiedGridTopology.TryResolveAddress(Grid, pos, out var block))
                {
                    provider = ResolveProviderComponent(block);
                    if (provider != null && block is GridBlock resolved && !IsDefaultId(storedId))
                        dataSourceInstanceIds[i] = resolved.GetEntityId();
                }

                if (provider != null)
                {
                    _cachedProviders.Add(provider);
                    continue;
                }

                // Source no longer valid - remove it.
                dataSourcePositions.RemoveAt(i);
                if (i < dataSourceInstanceIds.Count) dataSourceInstanceIds.RemoveAt(i);
            }
            return _cachedProviders;
        }

        /// <summary>The provider whose block carries this instance id, if it is still on the
        /// construct. The only lookup that can tell two blocks in one cell apart.</summary>
        private IGridDataProvider ResolveByInstanceId(EntityId id)
        {
            if (Grid == null || IsDefaultId(id)) return null;
            foreach (var block in Grid.AllBlocks)
            {
                if (block == null || block == this) continue;
                if (block.GetEntityId() != id) continue;
                return ResolveProviderComponent(block);
            }
            return null;
        }

        private static bool IsDefaultId(EntityId id) => id.Equals(EntityId.None);

        /// <summary>Is this exact block linked? Address-only, kept for the legacy callers
        /// and for a saved screen that predates instance ids.</summary>
        public bool HasSource(Vector3Int pos)
        {
            if (!_migrated) MigrateLegacy();
            return dataSourcePositions.Contains(pos);
        }

        /// <summary>Is this exact block linked? Address AND instance id, which is the only
        /// question that gives the right answer when two blocks share a cell.</summary>
        public bool HasSource(Vector3Int pos, EntityId instanceId)
        {
            if (!_migrated) MigrateLegacy();
            for (int i = 0; i < dataSourcePositions.Count; i++)
            {
                if (dataSourcePositions[i] != pos) continue;
                EntityId stored = i < dataSourceInstanceIds.Count ? dataSourceInstanceIds[i] : EntityId.None;
                if (IsDefaultId(stored) || stored == instanceId) return true;
            }
            return false;
        }

        public void ToggleSource(Vector3Int pos, EntityId instanceId)
        {
            if (!_migrated) MigrateLegacy();

            // Remove the one that matches BOTH address and identity; add only if nothing
            // matched. A container that shares a cell with three others now toggles itself.
            for (int i = dataSourcePositions.Count - 1; i >= 0; i--)
            {
                if (dataSourcePositions[i] != pos) continue;
                EntityId stored = i < dataSourceInstanceIds.Count ? dataSourceInstanceIds[i] : EntityId.None;
                if (!IsDefaultId(stored) && stored != instanceId) continue;

                dataSourcePositions.RemoveAt(i);
                if (i < dataSourceInstanceIds.Count) dataSourceInstanceIds.RemoveAt(i);
                return;
            }

            dataSourcePositions.Add(pos);
            dataSourceInstanceIds.Add(instanceId);
        }

        public void SetPrimarySource(Vector3Int pos, EntityId instanceId)
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

            IGridDataProvider best = null;
            GridBlock bestBlock = null;
            Vector3Int bestAddress = default;
            float bestDistance = float.MaxValue;
            foreach (var block in Grid.AllBlocks)
            {
                if (block == null || block == this) continue;
                if (VoxelEngine.GridSystem.UI.GridMasterTerminal.IsHiddenFromScreenConfig(block)) continue;
                var provider = ResolveProviderComponent(block);
                if (provider == null) continue;
                float distance = Vector3.Distance(block.transform.position, transform.position);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = provider;
                bestBlock = block;
                bestAddress = UnifiedGridTopology.AddressOf(block);
            }
            if (best != null && bestBlock != null)
                ToggleSource(bestAddress, bestBlock.GetEntityId());
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
            foreach (var block in Grid.AllBlocks)
            {
                if (block == null || block == this) continue;
                if (VoxelEngine.GridSystem.UI.GridMasterTerminal.IsHiddenFromScreenConfig(block)) continue;
                var provider = ResolveProviderComponent(block);
                if (provider != null)
                    list.Add((UnifiedGridTopology.AddressOf(block), provider));
            }
            return list;
        }

        private void OnDestroy()
        {
            if (_cameraFeedMaterial != null)
                Destroy(_cameraFeedMaterial);
            if (_borderMaterial != null) Destroy(_borderMaterial);
            ReleaseIconResources();
        }

        public override void OnPlaced()
        {
            base.OnPlaced(); blockName = "Screen (" + screenSize + ")";
            _initialized = false; AutoLinkToNearestProvider();
        }
    }
}
