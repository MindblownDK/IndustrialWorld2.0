// Assets/Scripts/VoxelEngine/GridSystem/GridCockpitMfd.cs
// Live runtime instrumentation for the authored cockpit displays.

using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Pressure;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public sealed class GridCockpitMfd : MonoBehaviour
    {
        private GridCockpit _cockpit;
        private Transform _cockpitCabin;
        private Transform _speedPanel;
        private Transform _airPanel;
        private Transform _planetPanel;
        private TextMesh _speedDisplayText;
        private TextMesh _airDisplayText;
        private TextMesh _planetNameText;
        private Transform _planetGlobe;
        private Renderer _planetGlobeRenderer;
        private Material _planetGlobeMaterial;
        private Texture2D _planetGlobeTexture;
        private CelestialBody _displayedGlobeBody;
        private string _displayedPlanetName;
        private int _planetGlobeSignature;
        private int _lastSpeedTenths = int.MinValue;
        private int _lastAirPercent = int.MinValue;
        private string _lastAirState;
        private bool _hasPlanetGlobeSignature;
        private float _nextCockpitDisplayUpdate;
        private bool _mfdTextFitPending;
        private int _fitTextAfterFrame;

        private void Awake()
        {
            _cockpit = GetComponent<GridCockpit>();
        }

        private void OnEnable()
        {
            EnsureCockpitDisplays();
        }

        private void Update()
        {
            // Every frame, not on the telemetry timer: a dynamic font's atlas can be
            // replaced at any point after a caption is set, and the caption must not be
            // seen sampling the old one even once.
            VoxelEngine.Rendering.WorldTextMaterial.SyncAtlas(_speedDisplayText);
            VoxelEngine.Rendering.WorldTextMaterial.SyncAtlas(_airDisplayText);
            VoxelEngine.Rendering.WorldTextMaterial.SyncAtlas(_planetNameText);

            if (_mfdTextFitPending && Time.frameCount >= _fitTextAfterFrame)
                RefreshMfdTextFits();
            TickCockpitDisplays();
        }

        private void OnDestroy()
        {
            ReleasePlanetGlobeResources();
        }

        private void EnsureCockpitDisplays()
        {
            if (_cockpitCabin == null)
            {
                Transform shell = transform.Find(CockpitShellBuilder.ShellRootName);
                _cockpitCabin = shell != null ? shell.Find(CockpitShellBuilder.InteriorName) : null;
            }
            if (_cockpitCabin == null) return;

            _airPanel = _cockpitCabin.Find("MFD_Screen_0");
            _speedPanel = _cockpitCabin.Find("MFD_Screen_1");
            _planetPanel = _cockpitCabin.Find("MFD_Screen_2");
            if (_speedPanel == null || _airPanel == null || _planetPanel == null) return;

            DisableStaticMfdDecorations();

            _speedDisplayText = EnsureMfdText("MFD_Runtime_Speed", _speedPanel,
                TextAnchor.MiddleCenter, TextAlignment.Center);
            _airDisplayText = EnsureMfdText("MFD_Runtime_Air", _airPanel,
                TextAnchor.MiddleLeft, TextAlignment.Left);
            _planetNameText = EnsureMfdText("MFD_Runtime_Planet_Name", _planetPanel,
                TextAnchor.MiddleCenter, TextAlignment.Center);

            if (_planetGlobe == null)
            {
                GameObject globe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                globe.name = "MFD_Runtime_Planet_Globe";
                globe.transform.SetParent(_cockpitCabin, false);
                Collider collider = globe.GetComponent<Collider>();
                if (collider != null) Destroy(collider);

                _planetGlobe = globe.transform;
                _planetGlobeRenderer = globe.GetComponent<Renderer>();
                _planetGlobe.localScale = Vector3.one * 0.18f;
            }

            PlaceMfdText(_speedDisplayText, _speedPanel,
                new Vector3(0f, 0f, _speedPanel.localScale.z * 0.5f + 0.010f));
            PlaceMfdText(_airDisplayText, _airPanel,
                new Vector3(_airPanel.localScale.x * 0.40f, 0f, _airPanel.localScale.z * 0.5f + 0.010f));
            PlaceMfdText(_planetNameText, _planetPanel,
                new Vector3(0f, _planetPanel.localScale.y * 0.39f, _planetPanel.localScale.z * 0.5f + 0.012f));
            PlacePlanetGlobe();
            ScheduleMfdTextFit();
        }

        private TextMesh EnsureMfdText(string objectName, Transform panel, TextAnchor anchor, TextAlignment alignment)
        {
            Transform existing = _cockpitCabin.Find(objectName);
            TextMesh text = existing != null ? existing.GetComponent<TextMesh>() : null;
            if (text == null)
            {
                GameObject go = new GameObject(objectName);
                go.transform.SetParent(_cockpitCabin, false);
                text = go.AddComponent<TextMesh>();
            }

            text.anchor = anchor;
            text.alignment = alignment;
            text.fontSize = 32;
            text.characterSize = 1f;
            text.lineSpacing = 0.95f;
            text.richText = false;
            text.color = new Color(0.48f, 0.96f, 1f, 1f);
            MeshRenderer meshRenderer = text.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.receiveShadows = false;
                meshRenderer.sortingOrder = 5;
            }

            // Keep newly authored text conservative until Unity has rebuilt its mesh.
            // Measuring it immediately after assigning text can read the old short
            // placeholder bounds, which scales a full telemetry label off the panel.
            if (string.IsNullOrEmpty(text.text)) text.text = "--";
            VoxelEngine.Rendering.WorldTextMaterial.Apply(text);
            text.transform.localScale = new Vector3(-0.02f, 0.02f, 0.02f);
            PlaceMfdText(text, panel, new Vector3(0f, 0f, panel.localScale.z * 0.5f + 0.010f));
            return text;
        }

        private static void PlaceMfdText(TextMesh text, Transform panel, Vector3 panelLocalOffset)
        {
            if (text == null || panel == null) return;
            text.transform.localPosition = panel.localPosition + panel.localRotation * panelLocalOffset;
            // Keep the text coplanar with the authored display. The cabin's facing
            // reverses its horizontal basis relative to the seated view, so FitMfdText
            // mirrors glyph X while retaining this front-facing panel rotation.
            text.transform.localRotation = panel.localRotation;
        }

        private void ScheduleMfdTextFit()
        {
            _mfdTextFitPending = true;
            _fitTextAfterFrame = Mathf.Max(_fitTextAfterFrame, Time.frameCount + 1);
        }

        private void RefreshMfdTextFits()
        {
            bool speedReady = FitMfdText(_speedDisplayText, _speedPanel, 0.88f, 0.74f);
            bool airReady = FitMfdText(_airDisplayText, _airPanel, 0.88f, 0.76f);
            bool nameReady = FitMfdText(_planetNameText, _planetPanel, 0.88f, 0.22f);
            _mfdTextFitPending = !(speedReady && airReady && nameReady);
            if (_mfdTextFitPending) _fitTextAfterFrame = Time.frameCount + 1;
        }

        private void SetMfdText(TextMesh text, string value)
        {
            if (text == null || text.text == value) return;
            text.text = value;
            // Avoid showing an unmeasured long string at the scale of the prior short one.
            text.transform.localScale = new Vector3(-0.02f, 0.02f, 0.02f);
            ScheduleMfdTextFit();
        }

        private static bool FitMfdText(TextMesh text, Transform panel, float widthFraction, float heightFraction)
        {
            if (text == null || panel == null) return false;
            MeshFilter filter = text.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return false;

            Vector3 bounds = mesh.bounds.size;
            if (bounds.x <= 0.0001f || bounds.y <= 0.0001f) return false;
            float fitX = panel.localScale.x * widthFraction / bounds.x;
            float fitY = panel.localScale.y * heightFraction / bounds.y;
            float fit = Mathf.Min(fitX, fitY);
            if (float.IsNaN(fit) || float.IsInfinity(fit) || fit <= 0f) return false;
            text.transform.localScale = new Vector3(-fit, fit, fit);
            return true;
        }

        private void DisableStaticMfdDecorations()
        {
            for (int i = 0; i < _cockpitCabin.childCount; i++)
            {
                Transform child = _cockpitCabin.GetChild(i);
                string childName = child.name;
                if (childName.StartsWith("MFD_Bar_") || childName.StartsWith("MFD_Tick_") ||
                    childName.StartsWith("MFD_Graph_"))
                    child.gameObject.SetActive(false);
            }
        }

        private void PlacePlanetGlobe()
        {
            if (_planetGlobe == null || _planetPanel == null) return;
            const float radius = 0.09f;
            _planetGlobe.localPosition = _planetPanel.localPosition + _planetPanel.localRotation *
                new Vector3(0f, -0.045f, _planetPanel.localScale.z * 0.5f + radius);
            _planetGlobe.localRotation = _planetPanel.localRotation;
        }

        private void TickCockpitDisplays()
        {
            if (!Application.isPlaying || Time.unscaledTime < _nextCockpitDisplayUpdate) return;
            _nextCockpitDisplayUpdate = Time.unscaledTime + 0.20f;

            if (_speedDisplayText == null || _airDisplayText == null || _planetNameText == null || _planetGlobe == null)
                EnsureCockpitDisplays();
            if (_speedDisplayText == null || _airDisplayText == null || _planetNameText == null || _planetGlobe == null)
                return;

            GridEntity grid = _cockpit != null ? _cockpit.Grid : null;
            float speed = grid != null && grid.Body != null
                ? grid.Body.linearVelocity.magnitude
                : 0f;
            if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 0f;
            int speedTenths = Mathf.RoundToInt(Mathf.Clamp(speed, 0f, 99999f) * 10f);
            if (speedTenths != _lastSpeedTenths)
            {
                _lastSpeedTenths = speedTenths;
                SetMfdText(_speedDisplayText,
                    "SPEED\n" + (speedTenths / 10f).ToString("0.0") + " m/s");
            }

            SampleCockpitAir(out float pressureAtm, out bool oxygenBearing);
            int breathablePercent = oxygenBearing
                ? Mathf.RoundToInt(Mathf.Clamp01(pressureAtm / PressureRules.NominalPressureAtm) * 100f)
                : 0;
            string airState = breathablePercent >= PressureRules.BreathablePressureAtm * 100f
                ? "BREATHABLE"
                : breathablePercent > 0 ? "LOW AIR" : "NO O2";
            if (breathablePercent != _lastAirPercent || airState != _lastAirState)
            {
                _lastAirPercent = breathablePercent;
                _lastAirState = airState;
                SetMfdText(_airDisplayText, "AIR\n" + breathablePercent + "%\n" + airState);
            }

            UpdatePlanetGlobe(GravityProvider.ActiveBody);
        }

        private void SampleCockpitAir(out float pressureAtm, out bool oxygenBearing)
        {
            Vector3 position = transform.position;
            GridRoom gridRoom = RoomAtmosphereService.RoomAt(position);
            if (gridRoom != null)
            {
                pressureAtm = gridRoom.PressureAtm;
                oxygenBearing = gridRoom.IsSealed
                    ? pressureAtm > 0.001f
                    : gridRoom.Ambient.IsOxygenBearing;
                return;
            }

            StationRoom stationRoom = StationRoomSolver.RoomAt(position);
            if (stationRoom != null)
            {
                pressureAtm = stationRoom.Fill01 * PressureRules.NominalPressureAtm;
                oxygenBearing = true;
                return;
            }

            AmbientAir ambient = PressureRules.SampleAmbient(position);
            pressureAtm = ambient.PressureAtm;
            oxygenBearing = ambient.IsOxygenBearing;
        }

        private void UpdatePlanetGlobe(CelestialBody body)
        {
            if (body == null)
            {
                _displayedGlobeBody = null;
                _hasPlanetGlobeSignature = false;
                _planetGlobe.gameObject.SetActive(false);
                if (_displayedPlanetName != "NO PLANET")
                {
                    _displayedPlanetName = "NO PLANET";
                    SetMfdText(_planetNameText, _displayedPlanetName);
                }
                return;
            }

            _planetGlobe.gameObject.SetActive(true);
            string bodyName = string.IsNullOrWhiteSpace(body.DisplayName) ? "PLANET" : body.DisplayName;
            if (_displayedPlanetName != bodyName)
            {
                _displayedPlanetName = bodyName;
                SetMfdText(_planetNameText, bodyName.ToUpperInvariant());
            }

            BodySettings settings = body.settings;
            int signature = settings == null ? 0 : ComputePlanetGlobeSignature(settings);
            if (_displayedGlobeBody != body || !_hasPlanetGlobeSignature || _planetGlobeSignature != signature)
            {
                RebuildPlanetGlobe(body);
                _displayedGlobeBody = body;
                _planetGlobeSignature = signature;
                _hasPlanetGlobeSignature = true;
            }

            _planetGlobe.localRotation = _planetPanel.localRotation * Quaternion.Euler(12f, Time.unscaledTime * 8f, 0f);
        }

        private static int ComputePlanetGlobeSignature(BodySettings settings)
        {
            unchecked
            {
                int signature = settings.seed;
                signature = signature * 397 ^ settings.waterLevel;
                signature = signature * 397 ^ Mathf.RoundToInt(settings.waterVolume * 100f);
                signature = signature * 397 ^ Mathf.RoundToInt(settings.temperature * 10f);
                signature = signature * 397 ^ Mathf.RoundToInt(settings.oxygenLevel * 100f);
                signature = signature * 397 ^ (int)settings.surfaceMode;
                signature = signature * 397 ^ Mathf.RoundToInt(settings.mountainScale * 100f);
                signature = signature * 397 ^ Mathf.RoundToInt(settings.continentScaleFactor * 100f);
                signature = signature * 397 ^ (settings.enableGrass ? 1 : 0);
                signature = signature * 397 ^ (settings.WeatherAllowed ? 1 : 0);
                signature = signature * 397 ^ Mathf.RoundToInt(settings.radiationLevel * 100f);
                signature = signature * 397 ^ settings.displayColor.GetHashCode();
                return signature;
            }
        }

        private void RebuildPlanetGlobe(CelestialBody body)
        {
            if (_planetGlobeRenderer == null) return;
            if (_planetGlobeTexture != null) Destroy(_planetGlobeTexture);
            if (_planetGlobeMaterial != null) Destroy(_planetGlobeMaterial);

            // A small equirectangular atlas is enough for a cockpit screen, but the
            // old 96x48, two-noise pass read as a blank ball. This higher-resolution
            // globe uses the body's terrain, water, climate, atmosphere and weather
            // settings to draw recognizable continents, coasts, relief, ice and clouds.
            const int width = 256;
            const int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "CockpitPlanetGlobeTexture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2,
            };
            Color[] pixels = new Color[width * height];

            BodySettings settings = body.settings;
            int seed = settings != null ? settings.seed
                : (body.DisplayName ?? "Body").GetHashCode();
            float seedX = (seed & 1023) * 0.0031f + 2.7f;
            float seedY = ((seed >> 10) & 1023) * 0.0037f + 5.3f;
            float continentScale = settings != null
                ? Mathf.Clamp(settings.continentScaleFactor, 0.35f, 3.5f) : 1f;
            float mountainScale = settings != null
                ? Mathf.Clamp(settings.mountainScale, 0.1f, 5f) : 1f;
            float temperature = settings != null ? settings.temperature : 10f;
            float waterLevel = settings != null ? settings.waterLevel : 96f;
            float waterVolume = settings != null ? Mathf.Clamp01(settings.waterVolume / 2f) : 0.5f;
            bool oxygenated = settings != null && settings.HasOxygen;
            bool grass = settings == null || settings.enableGrass;
            bool weather = settings != null && settings.WeatherAllowed;
            bool oilWorld = settings != null && settings.surfaceMode == SurfaceMode.OilOnly;
            bool waterWorld = settings != null && settings.surfaceMode == SurfaceMode.WaterOnlyWithSubsurface;
            bool hasOceans = oilWorld || waterWorld || waterLevel > 0f;

            float landThreshold = settings != null
                ? Mathf.Lerp(0.36f, 0.68f, Mathf.Clamp01(waterLevel / 250f))
                : 0.52f;
            if (waterLevel <= 0f && !oilWorld && !waterWorld) landThreshold = 0f;

            Color deepOcean = oilWorld
                ? new Color(0.20f, 0.105f, 0.035f)
                : new Color(0.018f, 0.075f, 0.22f);
            Color shallowOcean = oilWorld
                ? new Color(0.47f, 0.245f, 0.065f)
                : Color.Lerp(new Color(0.035f, 0.22f, 0.38f), new Color(0.055f, 0.42f, 0.54f), waterVolume);

            Color landBase = oxygenated
                ? (grass ? new Color(0.17f, 0.40f, 0.23f) : new Color(0.34f, 0.37f, 0.32f))
                : new Color(0.48f, 0.34f, 0.22f);
            if (temperature > 35f) landBase = Color.Lerp(landBase, new Color(0.69f, 0.48f, 0.22f), 0.68f);
            if (temperature < -12f) landBase = Color.Lerp(landBase, new Color(0.60f, 0.68f, 0.73f), 0.72f);
            if (settings != null && settings.displayColor.a > 0.05f)
                landBase = Color.Lerp(landBase, settings.displayColor, 0.28f);
            Color rock = temperature < -12f
                ? new Color(0.72f, 0.78f, 0.82f)
                : new Color(0.42f, 0.39f, 0.34f);
            Color cloudColor = new Color(0.82f, 0.91f, 0.98f);
            Color gridColor = new Color(0.62f, 0.87f, 0.96f);

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float latitudeDegrees = 90f - v * 180f;
                float latitude = Mathf.Abs(latitudeDegrees) / 90f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float broad = SampleGlobeNoise(u, v, 2.25f * continentScale, seedX, seedY);
                    float regional = SampleGlobeNoise(u, v, 5.1f * continentScale, seedY + 11.7f, seedX + 4.2f);
                    float detail = SampleGlobeNoise(u, v, 12.8f * continentScale, seedX + 18.4f, seedY + 29.1f);
                    float terrain = broad * 0.65f + regional * 0.25f + detail * 0.10f;
                    float ridge = 1f - Mathf.Abs(regional * 2f - 1f);

                    float landCoverage;
                    if (waterWorld || oilWorld) landCoverage = 0f;
                    else if (!hasOceans) landCoverage = 1f;
                    else landCoverage = Mathf.SmoothStep(landThreshold - 0.025f, landThreshold + 0.035f, terrain);

                    float oceanDepth = Mathf.Clamp01((landThreshold - terrain + 0.08f) * 2.8f);
                    Color oceanColor = Color.Lerp(shallowOcean, deepOcean, oceanDepth);
                    if (oilWorld)
                    {
                        float slick = Mathf.SmoothStep(0.60f, 0.88f, detail);
                        oceanColor = Color.Lerp(oceanColor, new Color(0.68f, 0.39f, 0.085f), slick * 0.42f);
                    }

                    float highland = Mathf.Clamp01((terrain - landThreshold) * 3f
                        + (ridge - 0.58f) * mountainScale * 0.85f);
                    Color landColor = Color.Lerp(landBase, rock, highland * 0.72f);
                    float polarIce = Mathf.Clamp01((latitude - 0.73f) / 0.22f)
                        * Mathf.Clamp01((8f - temperature) / 32f);
                    landColor = Color.Lerp(landColor, new Color(0.78f, 0.87f, 0.91f), polarIce);
                    Color color = Color.Lerp(oceanColor, landColor, landCoverage);

                    // Cloud swirls are tied to the authored weather profile; a thin
                    // oxygenated atmosphere keeps only a restrained haze of wisps.
                    if (settings != null && settings.HasAtmosphere)
                    {
                        float cloudBroad = SampleGlobeNoise(u, v, 7.2f, seedY + 43.2f, seedX + 16.8f);
                        float cloudDetail = SampleGlobeNoise(u, v, 16.5f, seedX + 61.1f, seedY + 8.6f);
                        float cloudField = cloudBroad * 0.72f + cloudDetail * 0.28f;
                        float cloudAmount = weather
                            ? Mathf.SmoothStep(0.63f, 0.79f, cloudField) * 0.62f
                            : Mathf.SmoothStep(0.78f, 0.91f, cloudField) * 0.12f;
                        color = Color.Lerp(color, cloudColor, cloudAmount);
                    }

                    // A faint latitude/longitude graticule makes the MFD read as an
                    // instrument globe instead of a bare textured sphere.
                    float latDistance = Mathf.Abs(Mathf.Repeat(latitudeDegrees + 15f, 30f) - 15f);
                    float longitudeDegrees = u * 360f;
                    float lonDistance = Mathf.Abs(Mathf.Repeat(longitudeDegrees + 15f, 30f) - 15f);
                    bool parallel = latDistance < 0.65f && latitude < 0.96f;
                    bool meridian = lonDistance < 0.55f && latitude < 0.96f;
                    if (parallel || meridian)
                        color = Color.Lerp(color, gridColor, 0.16f);

                    float reliefShade = 0.82f + detail * 0.20f;
                    if (settings != null && settings.radiationLevel > 0.5f)
                        color = Color.Lerp(color, new Color(0.50f, 0.27f, 0.55f),
                            Mathf.Clamp01(settings.radiationLevel / 8f) * 0.18f);
                    pixels[y * width + x] = color * reliefShade;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Destroy(texture);
                return;
            }

            Material material = new Material(shader) { name = "CockpitPlanetGlobeMaterial" };
            material.mainTexture = texture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.04f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.48f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(0.075f, 0.095f, 0.11f, 1f));
            }

            _planetGlobeTexture = texture;
            _planetGlobeMaterial = material;
            _planetGlobeRenderer.sharedMaterial = material;
        }

        private static float SampleGlobeNoise(float u, float v, float frequency, float offsetX, float offsetY)
        {
            // Sample around a circle rather than a flat horizontal interval so the
            // equirectangular texture joins cleanly at the globe's longitude seam.
            float angle = u * Mathf.PI * 2f;
            float x = offsetX + frequency + Mathf.Cos(angle) * frequency;
            float y = offsetY + frequency * 0.7f + v * frequency * 1.5f
                + Mathf.Sin(angle) * frequency * 0.35f;
            return Mathf.PerlinNoise(x, y);
        }

        private void ReleasePlanetGlobeResources()
        {
            if (_planetGlobeTexture != null) Destroy(_planetGlobeTexture);
            if (_planetGlobeMaterial != null) Destroy(_planetGlobeMaterial);
            _planetGlobeTexture = null;
            _planetGlobeMaterial = null;
            _planetGlobeRenderer = null;
        }

    }
}
