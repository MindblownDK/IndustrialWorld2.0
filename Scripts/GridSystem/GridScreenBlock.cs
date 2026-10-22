// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.cs
//
// Premium configurable digital screen for large grid ships.
// v5.57.3-dev — Screen text depth fix without self-occluding the display.

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VoxelEngine.GridSystem.UI;
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
        MiningCargo,
        // 17.40.0-dev — farm and navigation detail pages, appended to preserve saved enum values.
        AutoRunPilotInfo, RouteRecorderInfo, FarmPlots
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
        [Range(1, 3), Tooltip("Number of compact history plots on the Power Graph page: grid power, solar output and battery transfer.")]
        public int powerGraphPanels = 3;
        [Tooltip("Show each battery's current charge/discharge and its rated maximum instead of the compact battery-cell grid.")]
        public bool showBatteryPowerRates;
        // ── 17.42.0-dev — the remake's page options. All additive: older saves load with
        // the defaults below and get the reference layouts.
        [Tooltip("Custom heading shared by the Manifest, Fill Bars and Storage Monitor pages. Blank falls back to the container or grid name.")]
        public string manifestTitle = "";
        [Tooltip("Power Graph panel 1 series (usage, usageScaled, battery, batteryIn, batteryOut, batteryInOut, batteryInOutAuto, solar, reactor, h2engine, storedOxygen, storedHydrogen).")]
        public string graphSeriesA = "usage";
        [Tooltip("Power Graph panel 2 series.")]
        public string graphSeriesB = "solar";
        [Tooltip("Power Graph panel 3 series.")]
        public string graphSeriesC = "batteryInOut";
        [Tooltip("Power Graph time span: 0=5 min, 1=15 min, 2=30 min, 3=1 hour, 4=12 hours, 5=24 hours, 6=all recorded.")]
        public int powerGraphSpan = 1;
        [Tooltip("Show the fill bar across the top of the Inventory Grid page.")]
        public bool showInventoryFill = true;
        [Tooltip("Show each item's mass under its count on the Storage Monitor page.")]
        public bool showStorageWeight;
        [Tooltip("Free line printed at the bottom-left of the Storage Monitor page.")]
        public string storageNote = "";
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
        // 17.42.0-dev — one mesh per ink, stacked on the same cell grid. _screenText is
        // the Primary layer and still owns the measurement; the other layers borrow its
        // transform so every glyph stays in its own cell.
        private readonly TextMesh[] _inkLayers = new TextMesh[5];
        private readonly string[] _inkTexts = new string[5];
        private Transform _bodyAnchor;
        private ScreenPalette _palette;
        private int _paletteForMode = -1;
        private Color _paletteBase;
        // Frame the text was last changed on, so the fit can re-run against the rebuilt
        // mesh (fitting against a stale mesh is how a screen ends up with tiny type).
        private int _textChangedFrame = -1;
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
        // Last strings the text was fitted to are gone (17.42.0-dev): the fit now keys on
        // the frame the text changed on and re-runs a frame later against the rebuilt mesh.
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

                // 17.42.0-dev — every remaining mode has a managed page above. A mode that
                // somehow escapes both says so plainly rather than dumping provider text
                // onto the glass at whatever size the fit happens to give it.
                return "NO PAGE";
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
            // 17.42.0-dev — THE DENSITY OF THE GLASS.
            //
            // The old counts (3, 4, 6, 10, 16) were chosen when pages were a handful of
            // lines each. On a wall panel that made the page "a tiny part of the grid":
            // a sixteen-row canvas over a sixteen-metre screen, with content occupying a
            // corner of it. These counts let a page actually fill its panel - graphs get
            // shape, monitors get a real item grid - while a Small screen still prints
            // four readable lines rather than sixteen unreadable ones.
            ScreenSize.Small => 4, ScreenSize.Wide => 5,
            ScreenSize.Medium => 8, ScreenSize.Large => 14,
            ScreenSize.ExtraLarge => 20, _ => 4
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

            // The power history is kept whether or not the graph page is showing - a
            // chart that only records while watched has nothing to draw when opened.
            SampleGraphHistory();

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

            // 17.42.0-dev — THE TEXT IS A STACK OF INKS.
            //
            // Managed pages paint every ink layer; legacy pages (Custom, banner, camera)
            // speak in one colour and leave the other layers empty. The layers share one
            // anchor and one scale, so a warn-red label still sits in its own cell.
            string text = showCameraFeed ? string.Empty : StyleText(FormattedDisplay, fontStyle);
            bool textChanged = false;
            for (int ink = 0; ink < _inkTexts.Length; ink++)
            {
                // A managed page published its five layers already; the body layer gets
                // its own share, never the merged page - a merged page here would ghost
                // every glyph in white under its real colour (and drift apart on a
                // tilted glass, so names print twice). Only a page that never painted
                // inks (Custom text, a bare status line) rides the body layer whole.
                string layer = ink == 0
                    ? (_pageInksPublished ? (_inkTexts[0] ?? string.Empty) : (text ?? string.Empty))
                    : (_inkTexts[ink] ?? string.Empty);
                if (_inkLayers[ink] == null) continue;
                if (_inkLayers[ink].text != layer)
                {
                    _inkLayers[ink].text = layer;
                    textChanged = true;
                }
            }
            if (textChanged)
            {
                _iconsDirty = true;
                _textChangedFrame = Time.frameCount;
            }

            if (_power != null) _power.wattsPerSecond = Enabled ? PowerDraw : 0f;

            var (dw, dh) = GlassSize();
            RefreshPalette();

            if (!Enabled)
            {
                Color d = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                ApplyBodyColour(d);
                if (_titleText != null) _titleText.color = d;
                if (_statusText != null) { _statusText.text = "OFF"; _statusText.color = d; }
            }
            else if (!IsPowered)
            {
                Color w = new Color(0.8f, 0.15f, 0.10f, 0.6f);
                ApplyBodyColour(w);
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
                ApplyBodyColour(ink);
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
                ApplyBodyColour(i);
                if (_titleText != null) { _titleText.text = "NO SOURCE"; _titleText.color = i; }
                if (_statusText != null) { _statusText.text = "IDLE"; _statusText.color = i; }
            }
            else
            {
                // A slow breath on the alpha only - the page's own colours are the
                // message; the pulse is just proof the panel is alive.
                float pulse = 0.88f + 0.12f * Mathf.Sin(Time.realtimeSinceStartup * 1.8f);
                ApplyPaletteLayers(pulse);
                var sources = ResolveAllProviders();
                if (_titleText != null)
                {
                    // A managed page names itself in its own grid, so the strip above it
                    // stays empty - the page's title and the strip's copy of it read as a
                    // double print. A split panel is named after its layout; the panes
                    // name themselves inside.
                    _titleText.text = IsSplit
                        ? "SPLIT x" + SplitPaneCount
                        : IsManagedPage(dataMode)
                            ? string.Empty
                            : sources.Count + " source" + (sources.Count != 1 ? "s" : "");
                    _titleText.color = new Color(_palette.Accent.r, _palette.Accent.g, _palette.Accent.b, 0.72f);
                }
                if (_statusText != null)
                {
                    _statusText.text = dataMode == ScreenDataMode.Camera ? "CAM" : "LIVE";
                    _statusText.color = new Color(_palette.Dim.r * 1.35f, _palette.Dim.g * 1.35f, _palette.Dim.b * 1.35f);
                }
            }

            if (_titleText != null) _titleText.transform.localPosition = new Vector3(0, dh * 0.455f, 0);
            if (_statusText != null) _statusText.transform.localPosition = new Vector3(dw * 0.455f, -dh * 0.455f, 0);
            // The body anchor belongs to the fit pass, which centres the printed block on
            // the glass. Pinning it to a corner offset here - as this used to do every
            // frame - put the page back in the top-left corner immediately after the fit
            // had centred it, which is exactly how every page ended up hugging the corner
            // of its panel with dead glass to the right and below.

            // The type is measured against the glass after the text is set, so it fits
            // the panel it is actually on rather than a fixed guess - and fitted again a
            // frame later, once Unity has rebuilt the meshes.
            RefreshTextFit();
            // Item pictures are placed from that same measurement, so a picture can never
            // drift away from the count it belongs to.
            RefreshPageIcons();
        }

        /// <summary>The page's palette: its own signature for a managed page, the screen's
        /// text colour for Custom, and the instrument family when a split mixes pages.</summary>
        private void RefreshPalette()
        {
            int modeKey = IsSplit ? -2 : (int)dataMode;
            if (modeKey == _paletteForMode && _paletteBase == textColor) return;
            _paletteForMode = modeKey;
            _paletteBase = textColor;
            _palette = IsSplit
                ? ScreenPalette.Monochrome(textColor)
                : ScreenPalette.ForPage(dataMode, textColor);
        }

        private void ApplyBodyColour(Color colour)
        {
            for (int i = 0; i < _inkLayers.Length; i++)
                if (_inkLayers[i] != null) _inkLayers[i].color = colour;
        }

        private void ApplyPaletteLayers(float alphaScale)
        {
            for (int i = 0; i < _inkLayers.Length; i++)
            {
                if (_inkLayers[i] == null) continue;
                Color c = _palette.For((ScreenInk)i);
                _inkLayers[i].color = new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * alphaScale));
            }
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

            // 17.42.0-dev — ONE ANCHOR, FIVE INKS.
            //
            // The anchor carries the page's corner; every ink layer is a TextMesh at the
            // anchor's origin with its own colour. Because they share one parent and one
            // fitted scale, a red warning and a green reading sit in the same character
            // grid - which is the entire point of drawing pages on a grid.
            //
            // LEFT-ALIGNED, FROM THE LEFT EDGE: a page is a character grid, and
            // centre-aligned text centres EACH LINE separately, so two rows of different
            // lengths would no longer share a column grid and item pictures would have
            // nothing to line up with.
            _bodyAnchor = new GameObject("BodyAnchor").transform;
            _bodyAnchor.SetParent(root.transform, false);
            _bodyAnchor.localPosition = new Vector3(-_glassWidth * 0.47f, _glassHeight * 0.34f, 0f);

            for (int i = 0; i < _inkLayers.Length; i++)
            {
                var layerObj = new GameObject("InkLayer_" + (ScreenInk)i);
                layerObj.transform.SetParent(_bodyAnchor, false);
                layerObj.transform.localPosition = Vector3.zero;
                var layer = layerObj.AddComponent<TextMesh>();
                layer.text = string.Empty;
                layer.fontSize = 24;
                layer.characterSize = charSize;
                layer.anchor = TextAnchor.UpperLeft;
                layer.alignment = TextAlignment.Left;
                MakeTextOpaque(layer);
                _inkLayers[i] = layer;
                _inkTexts[i] = string.Empty;
            }
            _screenText = _inkLayers[0];

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

            // A cube is 1 m at scale 1, scale.
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

        // 17.42.0-dev — fitted geometry, shared with the icon pass so a picture lands
        // on the same cell its number was printed in.
        private float _fittedLineHeight;
        private float _fittedCharWidth;

        /// <summary>
        /// Fit the page to its glass: the drawn ink box is mapped onto the panel.
        ///
        /// Every earlier attempt solved this from CHARACTER COUNTS - a cell measured as
        /// mesh bounds over the line count and the longest line, scaled until
        /// rows x cell fitted the box. It cannot work reliably, because those counts are
        /// not the page: the canvas pads every line out to its full width and every layer
        /// out to its full height, so the count says twenty-four columns and seven rows
        /// where the panel has ink on fourteen and two. Divide by that and the page is
        /// scaled to a fraction of the glass - the "content is a small part of the screen"
        /// report - or blown past its edges, depending on which way the padding fell.
        ///
        /// The ink box has no such problem. A text mesh's bounds cover exactly the glyphs
        /// that were drawn, they are expressed in the layer's own space, and they do not
        /// depend on the transform's scale - so one union of all five layers is a
        /// scale-independent measurement of what the page actually looks like, for ANY
        /// panel size and any number of rows. Mapping that onto the panel is one division:
        ///
        ///   scale = min(boxW / inkW, boxH / inkH)         - fills the glass on one axis,
        ///   capped so a two-row page cannot become two    - never past the other, and
        ///   letters tall                                    - never absurd when sparse.
        ///
        /// Runs every frame and is idempotent, so a page is re-fitted the moment its
        /// content or its panel changes.
        /// </summary>
        private void RefreshTextFit() => FitScreenText();

        /// <summary>The scale the ink layers are drawn at, so a change can dirty the
        /// pictures and nothing else has to know.</summary>
        private float _appliedFitScale = 1f;

        /// <summary>The printed block in the body anchor's own space, kept for the icon
        /// pass so a picture is placed against what was drawn.</summary>
        private float _blockLeft, _blockTop, _blockWidth, _blockHeight;

        /// <summary>The fewest lines a page is allowed to fill: a page of one or two rows
        /// is drawn at a third of the glass rather than as two enormous letters.</summary>
        private const float MinLinesForFill = 3f;

        private bool FitScreenText()
        {
            if (_bodyAnchor == null) return false;

            var (gw, gh) = GlassSize();
            if (gw <= 0.0001f || gh <= 0.0001f) return false;
            float stretch = StyleStretch(fontStyle);
            // Edge to edge: the panel's own margin, not a page-sized one.
            float boxW = gw * 0.98f;
            float boxH = gh * 0.94f;

            // ── The ink box: the union of every layer's mesh, in the anchor's space.
            // Mesh bounds are independent of the transform's scale, so this measures the
            // page at scale one whatever the layers are currently scaled to.
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int printedRows = 0, longestLine = 0, firstRow = int.MaxValue;
            bool measured = false;

            for (int i = 0; i < _inkLayers.Length; i++)
            {
                var layer = _inkLayers[i];
                if (layer == null || string.IsNullOrEmpty(layer.text)) continue;

                // Bounds from the RENDERER, not from a MeshFilter: a TextMesh carries an
                // internal mesh and no filter, which is why this pass used to measure
                // nothing on every layer and give up - leaving the page at its creation
                // scale in the corner of the glass.
                if (!VoxelEngine.Rendering.WorldTextMaterial.TryGetTextBounds(layer, out Bounds b))
                    continue;

                // The layer sits at the anchor's origin, so its mesh box is already in the
                // anchor's space; only the layer's own offset needs adding.
                Vector3 o = layer.transform.localPosition;
                float layerMinX = b.min.x + o.x, layerMaxX = b.max.x + o.x;
                float layerMinY = b.min.y + o.y, layerMaxY = b.max.y + o.y;

                if (layerMinX < minX) minX = layerMinX;
                if (layerMaxX > maxX) maxX = layerMaxX;
                if (layerMinY < minY) minY = layerMinY;
                if (layerMaxY > maxY) maxY = layerMaxY;
                measured = true;

                PrintedMetrics(layer.text, out int span, out int first, out int longest);
                if (span > printedRows) printedRows = span;
                if (first < firstRow) firstRow = first;
                if (longest > longestLine) longestLine = longest;
            }

            if (!measured)
            {
                // Nothing measurable: the font has not built its mesh yet. Lay the page out
                // from the canvas's own line height so it is centred and sized from the
                // first frame instead of sitting at the corner until a mesh appears.
                var fallback = PageGrid();
                float lineH = Mathf.Max(0.0001f, CharHeight());
                float cellW2 = lineH * 0.52f * stretch;
                _fittedLineHeight = lineH;
                _fittedCharWidth = cellW2;
                _blockLeft = 0f;
                _blockTop = 0f;
                _blockWidth = fallback.columns * cellW2;
                _blockHeight = fallback.rows * lineH;
                var fallbackAnchor = new Vector3(-_blockWidth * 0.5f, _blockHeight * 0.5f, 0f);
                if (_bodyAnchor.localPosition != fallbackAnchor)
                {
                    _bodyAnchor.localPosition = fallbackAnchor;
                    _iconsDirty = true;
                }
                return false;
            }

            float inkW = Mathf.Max(0.00001f, maxX - minX);
            float inkH = Mathf.Max(0.00001f, maxY - minY);
            if (printedRows <= 0) printedRows = 1;
            if (longestLine <= 0) longestLine = 1;
            if (firstRow == int.MaxValue) firstRow = 0;

            // ── Solve. The two box terms fill the glass; the third is the sparse-page cap:
            // a page that printed fewer than MinLinesForFill rows is allowed to use that
            // fraction of the height instead of the whole of it.
            float byWidth = (boxW / inkW) / Mathf.Max(0.01f, stretch);
            float byHeight = boxH / inkH;
            float sparseCap = byHeight * Mathf.Clamp01(printedRows / MinLinesForFill);
            float scale = Mathf.Min(byWidth, Mathf.Min(byHeight, sparseCap));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f) return false;
            // Only a sanity stop: a text mesh whose character size is a fraction of a cell
            // legitimately needs a large scale, so this may not be a design ceiling.
            scale = Mathf.Clamp(scale, 0.0005f, 500f);

            bool scaleChanged = !Mathf.Approximately(_appliedFitScale, scale);
            _appliedFitScale = scale;

            var applied = new Vector3(scale * stretch, scale, scale);
            for (int i = 0; i < _inkLayers.Length; i++)
                if (_inkLayers[i] != null && _inkLayers[i].transform.localScale != applied)
                    _inkLayers[i].transform.localScale = applied;

            // ── Cell metrics, for the pages that lay themselves out on a character grid:
            // the ink box over what was printed, which stays honest even when the canvas
            // pads its text out to full width and full height.
            float cellH = Mathf.Max(0.0001f, inkH / printedRows);
            float cellW = Mathf.Max(0.0001f, inkW / longestLine);
            _fittedLineHeight = Mathf.Max(0.0001f, cellH * scale);
            _fittedCharWidth = Mathf.Max(0.0001f, cellW * stretch * scale);

            // ── Centre the page on the glass. The two axes are not the same question.
            //
            // HORIZONTALLY the canvas is the answer, not the ink: a page that centres its
            // lines does it by padding them out to the canvas width (see NoSources), so the
            // ink box is narrower than the canvas and sits left of its centre. Centring on
            // that ink box pushes the whole page to the right - which is exactly what a
            // "no data sources" page did.
            //
            // VERTICALLY the ink is the answer, not the canvas: a page that begins partway
            // down its grid has blank rows above it, and centring on the canvas would leave
            // that page high in the panel - or, worse, pushed off the top of it.
            var pageGrid = PageGrid();
            float canvasWidth = pageGrid.columns * cellW * stretch * scale;

            // Which of the two is the page's centre depends on how the page was written.
            // A page that centres its lines is centred in the CANVAS, and its ink box is
            // only off the canvas centre by the whole character its padding rounds by - so
            // that page wants the canvas centre, and gets it when the two agree to within a
            // couple of cells. A page that prints left-aligned uses only the columns its
            // text needs, and its ink box sits well left of the canvas centre; centring such
            // a page on the canvas hangs it off the left edge of its panel, which is what
            // the farm and custom pages did. So: snap to the canvas when the page is
            // centred in it, otherwise centre the ink that is actually there.
            float inkCentreX = (minX * scale * stretch) + (inkW * scale * stretch) * 0.5f;
            float canvasCentreX = canvasWidth * 0.5f;
            float snap = cellW * stretch * scale * 2f;
            float centreX = Mathf.Abs(inkCentreX - canvasCentreX) <= snap
                ? canvasCentreX
                : inkCentreX;
            float centreY = (maxY * scale) - (inkH * scale) * 0.5f;

            // A short page rests a little below the middle rather than dead on it: three
            // lines centred exactly read as floating, because the eye takes the empty space
            // below them as part of the page.
            float restingBias = printedRows <= 4 ? -boxH * 0.06f : 0f;
            var anchorPosition = new Vector3(-centreX, -centreY + restingBias, 0f);

            // ── The block the PICTURES are placed against. Two extents matter and they are
            // not the same: the fit maps the INK onto the glass, while a picture belongs to
            // a cell of the CANVAS - including the blank cells beside a short line. Placing
            // pictures as fractions of the ink box would slide them inwards on every page
            // whose longest line is not its last column, so the canvas grid is published
            // here and the ink box stays a local of this method.
            _blockLeft = 0f;
            // The canvas's row zero sits above the first printed row, by however many blank
            // rows the page left at the top.
            _blockTop = (maxY + firstRow * cellH) * scale;
            _blockWidth = pageGrid.columns * cellW * stretch * scale;
            _blockHeight = pageGrid.rows * cellH * scale;
            if (_bodyAnchor.localPosition != anchorPosition)
            {
                _bodyAnchor.localPosition = anchorPosition;
                _iconsDirty = true;
            }
            if (scaleChanged) _iconsDirty = true;
            return true;
        }

        /// <summary>The printed shape of a layer's text: how many rows its glyphs span
        /// from first to last (blank rows between counted, blank rows beyond ignored),
        /// and the longest line with trailing spaces ignored. String truth, no mesh.</summary>
        private static void PrintedMetrics(string text, out int span, out int longest)
            => PrintedMetrics(text, out span, out _, out longest);

        /// <summary>As above, with the first printed row: a page that starts part-way down
        /// its grid has to be measured from where its ink actually begins.</summary>
        private static void PrintedMetrics(string text, out int span, out int first, out int longest)
        {
            span = 0;
            longest = 0;
            first = 0;
            if (string.IsNullOrEmpty(text)) return;

            int firstPrinted = -1, last = -1, row = 0, lineLength = 0, linePrinted = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                char c = i < text.Length ? text[i] : '\n';
                if (c == '\n')
                {
                    if (linePrinted > 0)
                    {
                        if (firstPrinted < 0) firstPrinted = row;
                        last = row;
                        if (linePrinted > longest) longest = linePrinted;
                    }
                    row++;
                    lineLength = 0;
                    linePrinted = 0;
                    continue;
                }
                lineLength++;
                if (c != ' ' && c != '\r') linePrinted = lineLength;
            }
            if (last >= 0)
            {
                first = Mathf.Max(0, firstPrinted);
                span = last - firstPrinted + 1;
            }
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

            // One nudge against the true glyph box: mesh bounds times the applied scale,
            // in parent-local units where maxWidth/maxHeight live. Never the world
            // axis-aligned renderer box - a tilted screen inflates it and shrank the
            // title type on every angled glass.
            Bounds box = mesh.bounds;
            if (box.size.x > 0.0001f && box.size.y > 0.0001f)
            {
                float widthLocal = box.size.x * scale * Mathf.Max(0.01f, xStretch);
                float heightLocal = box.size.y * scale;
                float corr = Mathf.Min(maxWidth / Mathf.Max(0.0001f, widthLocal),
                                       maxHeight / Mathf.Max(0.0001f, heightLocal));
                corr = Mathf.Clamp(corr, 0.2f, 5f);
                if (Mathf.Abs(corr - 1f) > 0.02f)
                    tm.transform.localScale = new Vector3(
                        scale * Mathf.Max(0.01f, xStretch) * corr, scale * corr, scale * corr);
            }
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

            // The case's glow strip used to double as a border; seen through the glass it
            // is just a stray bar lying over the page. The frame draws on the glass now.
            ApplyRendererAccent(_glowStripRenderer, false, baseColor, emission);
            for (int i = 0; i < _cornerDotRenderers.Count; i++)
                ApplyRendererAccent(_cornerDotRenderers[i], borderVisible && safeBorderStyle >= 2, baseColor, emission);

            // 17.36.0-dev: the border is drawn ON the glass, not only on the case. The old
            // border setting tinted the case glow strip and the corner dots and nothing else,
            // which from the front read as "the border button does nothing".
            UpdateBorderFrame(safeBorderStyle, accent);

            // The atlas can be replaced the moment a page prints a new letter; every
            // mesh follows it (see SyncFontAtlas) before anything is drawn this frame.
            for (int i = 0; i < _inkLayers.Length; i++) SyncFontAtlas(_inkLayers[i]);
            SyncFontAtlas(_titleText);
            SyncFontAtlas(_statusText);

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
            // Thin lines, like the reference frames: the border marks the glass, it does
            // not eat it. Thin 0.5, Thick 1.0, Glow 1.6 percent of the panel.
            float thickness = style == 1 ? gw * 0.005f : style == 2 ? gw * 0.010f : gw * 0.016f;
            thickness = Mathf.Max(0.008f, thickness);
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
            // EVERY ink layer, not just the primary one: layers with different fontSize or
            // lineSpacing build meshes with different row heights, and the five inks stop
            // sharing a cell grid - a warn label drifts half a row from the reading it
            // annotates. One style, five layers, no drift.
            for (int i = 1; i < _inkLayers.Length; i++)
            {
                if (_inkLayers[i] == null) continue;
                _inkLayers[i].fontSize = displayFontSize;
                _inkLayers[i].fontStyle = displayStyle;
                _inkLayers[i].lineSpacing = lineSpacing;
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
            for (int i = 0; i < _inkLayers.Length; i++)
            {
                if (_inkLayers[i] == null) continue;
                var renderer = _inkLayers[i].GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = visible;
            }
        }

        /// <summary>Ensures TextMesh respects depth so screen text cannot render through terrain or blocks.</summary>
        private static Font _screenFont;

        /// <summary>
        /// The screens speak in a monospace face. The whole page design is a character
        /// grid - ink layers overlay cell for cell, icons sit on column boundaries - and
        /// a proportional font slides every layer under its neighbours (thirteen spaces
        /// are not the width of "HOLD 0%", so column 13 prints on top of column 7).
        /// OS mono faces in preference order; the built-in face stays as the last word.
        /// </summary>
        /// <summary>The face the wall draws in, for the editor's live preview: the page is a
        /// character grid, and a preview in any other font cannot show that grid.</summary>
        public static Font DisplayFont => ScreenFont;

        private static Font ScreenFont
        {
            get
            {
                if (_screenFont == null)
                    _screenFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "Cascadia Mono", "Consolas", "Courier New", "Liberation Mono", "DejaVu Sans Mono" },
                        24);
                return _screenFont;
            }
        }

        /// <summary>
        /// A dynamic font replaces its whole glyph atlas when new characters appear, and a
        /// text mesh baked against the old atlas samples one letter's slice for another.
        /// The shared helper tracks the atlas by version and re-bakes on any change; a
        /// screen calls it through here for every ink layer and every caption.
        /// </summary>
        private static void SyncFontAtlas(TextMesh tm)
            => VoxelEngine.Rendering.WorldTextMaterial.SyncAtlas(tm);

        private void MakeTextOpaque(TextMesh tm)
        {
            // The font first: Apply() below reads the renderer's material, which the
            // font assignment points at the face's own glyph atlas.
            var mono = ScreenFont;
            if (mono != null && tm.font != mono) tm.font = mono;
            SyncFontAtlas(tm);

            // The old copy-the-font-material approach set _ZTest properties
            // that the stock font shader never reads - its ZTest Always is
            // hard-coded, which is exactly why screen text kept shining
            // through terrain and blocks. The shared depth-tested world-text
            // material fixes it for real (14.37.2).
            VoxelEngine.Rendering.WorldTextMaterial.Apply(tm);
            SyncFontAtlas(tm);
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
                    provider = ProviderFor(block);
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
                return ProviderFor(block);
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
                var provider = ProviderFor(block);
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

        /// <summary>
        /// The reading a screen gets from a block: its own provider if it has one, and a
        /// plain readout if it does not.
        ///
        /// Half the construct's interesting blocks never implemented IGridDataProvider -
        /// a solar panel above all - so they could not be picked as a screen's data
        /// source at all, and a Power page could only ever be pointed at the blocks that
        /// happened to speak the interface. Any block with a power reading now reports
        /// one, which is exactly the set a power or machine board wants to be pointed at.
        /// </summary>
        private IGridDataProvider ProviderFor(GridBlock block)
        {
            if (block == null) return null;

            var provider = ResolveProviderComponent(block);
            if (provider != null) return provider;
            if (!IsReadableBlock(block)) return null;

            if (!_readouts.TryGetValue(block, out var readout) || readout == null)
            {
                readout = new BlockReadout(block);
                _readouts[block] = readout;
            }
            return readout;
        }

        private readonly Dictionary<GridBlock, BlockReadout> _readouts = new();

        /// <summary>A block worth pointing a screen at even though it cannot describe
        /// itself: it makes power, it takes power, or it is a rated machine.</summary>
        private static bool IsReadableBlock(GridBlock block)
        {
            switch (block)
            {
                case GridSolarPanel solar when solar.maxOutput > 0.01f: return true;
                case GridHydrogenEngine h2 when h2.wattsOutput > 0.01f: return true;
                case GridPortableReactor reactor when reactor.wattsOutput > 0.01f: return true;
            }
            return block.PowerOutput > 0.01f
                || block.PowerDraw > 0.01f
                || block.RatedPowerDraw > 0.01f;
        }

        /// <summary>
        /// A block's reading, for blocks that predate the provider interface. It says what
        /// the block is and what it is drawing or making, in the same shape the provider
        /// pages expect: one reading per line.
        /// </summary>
        private sealed class BlockReadout : IGridDataProvider
        {
            public readonly GridBlock Block;

            public BlockReadout(GridBlock block) => Block = block;

            public string SourceName =>
                Block != null && !string.IsNullOrEmpty(Block.blockName) ? Block.blockName : "BLOCK";

            public string DataCategory =>
                Block != null && (Block.PowerOutput > 0.01f || IsRatedGenerator(Block))
                    ? "Power" : "Machine";

            private static bool IsRatedGenerator(GridBlock block) => block switch
            {
                GridSolarPanel solar => solar.maxOutput > 0.01f,
                GridHydrogenEngine h2 => h2.wattsOutput > 0.01f,
                GridPortableReactor reactor => reactor.wattsOutput > 0.01f,
                _ => false,
            };

            public string GetDisplayData()
            {
                if (Block == null) return string.Empty;
                var sb = new System.Text.StringBuilder(48);
                sb.Append("OUT ").Append(Format(Block.PowerOutput));
                sb.Append("  USE ").Append(Format(Block.PowerDraw));
                return sb.ToString();
            }

            private static string Format(float watts)
            {
                if (watts >= 1000f) return (watts / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " kW";
                return watts.ToString("0", CultureInfo.InvariantCulture) + " W";
            }
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
                var provider = ProviderFor(block);
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
