// Assets/Scripts/VoxelEngine/Building/RailDisplayScreen.cs
//
// THE DISPLAY FAMILY - split-flap boards, nixie readouts and analog dials, steampunk.
//
// ONE COMPONENT, EVERY SCREEN
// The request was modular: the same hardware family in several housings (a grid screen
// driven by a shaft, a station cabinet, a hanging departure board, a small nixie readout)
// showing whatever the player points it at. So the kind of display and the data it shows
// are settings on one component, and the prefabs authored by setup step 95 are just
// housings with different defaults.
//
// POWER, PER THE BRIEF
//   • Stationary housings carry a PowerConsumer: mains electricity, and the flavour is
//     honest - a small electric engine inside turns the flap drums.
//   • Grid housings carry NO consumer: they tap ROTATIONAL power. Any shaft, gearbox or
//     engine on the grid spinning above idle keeps the screen alive; park the engine and
//     the board goes dark. That is the Create-mod contract, expressed against this
//     project's mechanical blocks without dragging the screen into the propulsion jobs.
//
// THE SOUND IS THE FEATURE
// A split-flap row that changes turns over with Sfx.SplitFlapFlip - click, slap, rattle -
// staggered per row so a board updating sounds like a board updating, not like one clap.

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Building
{
    public enum ScreenKind
    {
        /// <summary>Rows of turning cards. The departure-board look.</summary>
        SplitFlap = 0,
        /// <summary>Glowing gas-discharge digits in a brass cage.</summary>
        Nixie = 1,
        /// <summary>A needle over a dial, because some numbers want a hand.</summary>
        Analog = 2,
    }

    public enum ScreenSource
    {
        /// <summary>Speed of the train this screen rides on (or the nearest one).</summary>
        TrainSpeed = 0,
        /// <summary>Consist mass against rated load.</summary>
        ConsistLoad = 1,
        /// <summary>Next departures from the station this screen stands at.</summary>
        Departures = 2,
        /// <summary>Hold usage and role of the station this screen stands at.</summary>
        StationStatus = 3,
        /// <summary>Whatever the player typed.</summary>
        CustomText = 4,
    }

    [DisallowMultipleComponent]
    public class RailDisplayScreen : MonoBehaviour
    {
        [Header("Display")]
        public ScreenKind kind = ScreenKind.SplitFlap;
        public ScreenSource source = ScreenSource.TrainSpeed;

        [Tooltip("Shown when the source is Custom Text. Split onto rows at line breaks.")]
        public string customText = "INDUSTRIAL WORLD";

        [Tooltip("Split-flap rows. Departure boards author four, small screens two.")]
        [Range(1, 4)] public int rows = 4;

        public ScreenKind Kind => kind;
        public ScreenSource Source => source;
        public bool IsPowered => _powered;

        // ── Runtime state ──────────────────────────────────────────────────────
        private Transform _screenRoot;
        private readonly List<TextMesh> _rowTexts = new(4);
        private readonly List<Transform> _rowCards = new(4);
        private TextMesh _nixieText;
        private Material _nixieMat;
        private Transform _needlePivot;
        private TextMesh _analogLabel;
        private float _analogValue;
        private float _analogTarget;

        private readonly string[] _shown = new string[4];
        private bool _powered = true;
        private float _nextPoll;

        private VoxelEngine.Power.PowerConsumer _consumer;
        private Component _rpmSource;
        private PropertyInfo _rpmProperty;
        private float _nextRpmScan;

        private static readonly Dictionary<System.Type, PropertyInfo> _rpmCache = new();

        private static readonly Color Brass = new(0.72f, 0.51f, 0.22f);
        private static readonly Color DarkIron = new(0.10f, 0.09f, 0.08f);
        private static readonly Color CardFace = new(0.13f, 0.12f, 0.11f);
        private static readonly Color FlapText = new(0.93f, 0.88f, 0.75f);
        private static readonly Color NixieGlow = new(1.00f, 0.55f, 0.15f);

        // ════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ════════════════════════════════════════════════════════════════

        private void OnEnable()
        {
            _consumer = GetComponent<VoxelEngine.Power.PowerConsumer>();
            BuildVisuals();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        /// <summary>Applies a configuration change from the console. Rebuilds the hardware
        /// when the kind changes; a source change only needs a refresh.</summary>
        public void Configure(ScreenKind newKind, ScreenSource newSource, string custom)
        {
            bool rebuild = newKind != kind;
            kind = newKind;
            source = newSource;
            if (custom != null) customText = custom;

            if (rebuild) BuildVisuals();
            for (int i = 0; i < _shown.Length; i++) _shown[i] = null;
            _nextPoll = 0f;
        }

        // ════════════════════════════════════════════════════════════════
        //  HARDWARE
        // ════════════════════════════════════════════════════════════════

        private void BuildVisuals()
        {
            if (_screenRoot != null)
            {
                var old = _screenRoot.gameObject;
                _screenRoot = null;
                _rowTexts.Clear(); _rowCards.Clear();
                _nixieText = null; _nixieMat = null; _needlePivot = null; _analogLabel = null;
                if (old != null) Destroy(old);
            }

            var root = new GameObject("ScreenHardware") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            _screenRoot = root.transform;

            // Brass bezel: the frame every kind shares, because steampunk is a frame law.
            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bezel.name = "Bezel";
            bezel.transform.SetParent(_screenRoot, false);
            bezel.transform.localPosition = new Vector3(0f, 0f, -0.03f);
            bezel.transform.localScale = new Vector3(1.10f, 0.72f, 0.06f);
            Paint(bezel, Mat("Mat_DisplayBrass", Brass, metallic: 0.7f));
            KillCollider(bezel);

            switch (kind)
            {
                case ScreenKind.SplitFlap: BuildSplitFlap(); break;
                case ScreenKind.Nixie:     BuildNixie(); break;
                case ScreenKind.Analog:    BuildAnalog(); break;
            }
        }

        private void BuildSplitFlap()
        {
            int n = Mathf.Clamp(rows, 1, 4);
            float top = 0.24f;
            float step = 0.16f;

            for (int i = 0; i < n; i++)
            {
                float y = top - i * step;

                var card = GameObject.CreatePrimitive(PrimitiveType.Cube);
                card.name = "FlapRow" + i;
                card.transform.SetParent(_screenRoot, false);
                card.transform.localPosition = new Vector3(0f, y, 0f);
                card.transform.localScale = new Vector3(0.98f, 0.14f, 0.02f);
                Paint(card, Mat("Mat_DisplayCard", CardFace, metallic: 0f));
                KillCollider(card);
                _rowCards.Add(card.transform);

                var label = NewText("RowText" + i, FlapText, 0.085f, TextAnchor.MiddleLeft, TextAlignment.Left);
                label.transform.SetParent(_screenRoot, false);
                label.transform.localPosition = new Vector3(-0.44f, y, -0.045f);
                label.transform.localRotation = Quaternion.identity;
                _rowTexts.Add(label);
            }
        }

        private void BuildNixie()
        {
            // Brass cage: three thin bars across a recessed dark window.
            var window = GameObject.CreatePrimitive(PrimitiveType.Cube);
            window.name = "NixieWindow";
            window.transform.SetParent(_screenRoot, false);
            window.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            window.transform.localScale = new Vector3(1.00f, 0.50f, 0.03f);
            Paint(window, Mat("Mat_DisplayDark", DarkIron, metallic: 0.2f));
            KillCollider(window);

            for (int i = 0; i < 3; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bar.name = "CageBar" + i;
                bar.transform.SetParent(_screenRoot, false);
                bar.transform.localPosition = new Vector3(-0.33f + i * 0.33f, 0f, -0.045f);
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                bar.transform.localScale = new Vector3(0.015f, 0.25f, 0.015f);
                Paint(bar, Mat("Mat_DisplayBrass", Brass, metallic: 0.7f));
                KillCollider(bar);
            }

            _nixieMat = Mat("Mat_NixieGlow", NixieGlow, metallic: 0f);
            if (_nixieMat != null)
            {
                _nixieMat.EnableKeyword("_EMISSION");
                _nixieMat.SetColor("_EmissionColor", NixieGlow * 1.6f);
                _nixieMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            _nixieText = NewText("NixieDigits", NixieGlow, 0.22f, TextAnchor.MiddleCenter, TextAlignment.Center);
            _nixieText.transform.SetParent(_screenRoot, false);
            _nixieText.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            _nixieText.transform.localRotation = Quaternion.identity;
            if (_nixieMat != null) _nixieText.GetComponent<MeshRenderer>().sharedMaterial = _nixieMat;
        }

        private void BuildAnalog()
        {
            var dial = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dial.name = "Dial";
            dial.transform.SetParent(_screenRoot, false);
            dial.transform.localPosition = new Vector3(0f, 0.05f, -0.01f);
            dial.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            dial.transform.localScale = new Vector3(0.62f, 0.02f, 0.62f);
            Paint(dial, Mat("Mat_DisplayFace", new Color(0.88f, 0.83f, 0.70f), metallic: 0f));
            KillCollider(dial);

            var pivot = new GameObject("NeedlePivot") { hideFlags = HideFlags.DontSave };
            pivot.transform.SetParent(_screenRoot, false);
            pivot.transform.localPosition = new Vector3(0f, 0.05f, -0.035f);
            _needlePivot = pivot.transform;

            var needle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            needle.name = "Needle";
            needle.transform.SetParent(_needlePivot, false);
            needle.transform.localPosition = new Vector3(0f, 0.14f, 0f);
            needle.transform.localScale = new Vector3(0.018f, 0.30f, 0.012f);
            Paint(needle, Mat("Mat_DisplayNeedle", new Color(0.55f, 0.12f, 0.10f), metallic: 0f));
            KillCollider(needle);

            _analogLabel = NewText("AnalogValue", DarkIron, 0.09f, TextAnchor.MiddleCenter, TextAlignment.Center);
            _analogLabel.transform.SetParent(_screenRoot, false);
            _analogLabel.transform.localPosition = new Vector3(0f, -0.24f, -0.03f);
            _analogLabel.transform.localRotation = Quaternion.identity;
        }

        // ════════════════════════════════════════════════════════════════
        //  DATA
        // ════════════════════════════════════════════════════════════════

        private void Update()
        {
            PollPower();

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + 0.25f;
                RefreshContent();
            }

            // The needle eases every frame; a dial that snaps is a dial that lies.
            if (_needlePivot != null)
            {
                _analogValue = Mathf.MoveTowards(_analogValue, _powered ? _analogTarget : 0f, Time.deltaTime * 0.8f);
                float angle = Mathf.Lerp(-120f, 120f, Mathf.Clamp01(_analogValue));
                _needlePivot.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void PollPower()
        {
            if (_consumer != null)
            {
                _powered = _consumer.IsPowered;
                return;
            }

            // Rotational tap: any mechanical block on this grid with a live shaft RPM.
            if (Time.time < _nextRpmScan) { }
            else if (_rpmSource == null)
            {
                _nextRpmScan = Time.time + 5f;
                ScanForShaft();
            }

            if (_rpmSource == null) { _powered = false; return; }
            float rpm = (float)_rpmProperty.GetValue(_rpmSource);
            _powered = rpm > 5f;
        }

        private void ScanForShaft()
        {
            // Scan from the GRID ROOT, not this transform: a screen block is one child of
            // the train, and the shaft or engine feeding it sits somewhere else entirely.
            var comps = transform.root.GetComponentsInChildren<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null || c == this) continue;

                var type = c.GetType();
                if (!_rpmCache.TryGetValue(type, out var prop))
                {
                    prop = type.GetProperty("CurrentRPM", BindingFlags.Public | BindingFlags.Instance);
                    if (prop != null && prop.PropertyType != typeof(float)) prop = null;
                    _rpmCache[type] = prop;
                }
                if (prop == null) continue;

                _rpmSource = c;
                _rpmProperty = prop;
                return;
            }
        }

        private void RefreshContent()
        {
            var lines = new string[4];
            float gauge = 0f;

            if (!_powered)
            {
                for (int i = 0; i < 4; i++) lines[i] = kind == ScreenKind.SplitFlap ? "---- ---- ----" : "";
            }
            else
            {
                switch (source)
                {
                    case ScreenSource.TrainSpeed:
                    {
                        var bogie = NearestBogie();
                        if (bogie == null) { lines[0] = "NO TRAIN"; lines[1] = "IN RANGE"; }
                        else
                        {
                            float kmh = bogie.Speed * 3.6f;
                            lines[0] = $"SPEED  {kmh,3:0} KM/H";
                            lines[1] = bogie.BlockedReason == "" ? "LINE   CLEAR" : Trunc(bogie.BlockedReason, 16);
                            lines[2] = bogie.Destination != null ? "TO     " + Trunc(bogie.Destination.name, 10) : "";
                            gauge = bogie.maxSpeed > 0.01f ? bogie.Speed / bogie.maxSpeed : 0f;
                        }
                        break;
                    }
                    case ScreenSource.ConsistLoad:
                    {
                        var bogie = NearestBogie();
                        if (bogie == null) { lines[0] = "NO TRAIN"; lines[1] = "IN RANGE"; }
                        else
                        {
                            float tonnes = bogie.ConsistMassKg / 1000f;
                            lines[0] = $"LOAD   {tonnes,4:0.0} T";
                            lines[1] = $"FACTOR {(bogie.LoadSpeedFactor() * 100f),3:0} PCT";
                            gauge = 1f - bogie.LoadSpeedFactor();
                        }
                        break;
                    }
                    case ScreenSource.Departures:
                    {
                        var station = NearestStation();
                        lines[0] = station == null ? "NO STATION" : "DEP " + Trunc(station.StationName, 12);
                        int row = 1;
                        if (station != null)
                        {
                            var services = GridTrainScheduleBlock.All;
                            for (int i = 0; i < services.Count && row < 4; i++)
                            {
                                var svc = services[i];
                                if (svc == null || svc.schedule.IsEmpty) continue;
                                string dest = null;
                                string status = "IDLE";
                                var entries = svc.schedule.entries;
                                for (int e = 0; e < entries.Count; e++)
                                {
                                    if (!string.Equals(entries[e].stationName, station.StationName,
                                        System.StringComparison.OrdinalIgnoreCase)) continue;
                                    dest = entries[(e + 1) % entries.Count].stationName;
                                    status = svc.IsWaiting ? "BOARDING" : "EN ROUTE";
                                    break;
                                }
                                if (dest == null) continue;
                                lines[row++] = Trunc(svc.TrainName, 7) + " " + Trunc(dest, 9) + " " + status;
                            }
                            if (row == 1) lines[1] = "NO SERVICES";
                        }
                        break;
                    }
                    case ScreenSource.StationStatus:
                    {
                        var station = NearestStation();
                        if (station == null) { lines[0] = "NO STATION"; }
                        else
                        {
                            int used = 0;
                            if (station.Hold != null)
                                for (int i = 0; i < station.Hold.Size; i++)
                                    if (!station.Hold.GetSlot(i).IsEmpty) used++;
                            lines[0] = Trunc(station.StationName, 16);
                            lines[1] = station.Hold != null ? $"HOLD   {used,2:0}/{station.Hold.Size,2:0}" : "NO HOLD";
                            lines[2] = "ROLE   " + station.RoleLabel;
                            gauge = station.Hold != null && station.Hold.Size > 0 ? used / (float)station.Hold.Size : 0f;
                        }
                        break;
                    }
                    default:
                    {
                        var parts = (customText ?? "").Split('\n');
                        for (int i = 0; i < 4; i++)
                            lines[i] = i < parts.Length ? Trunc(parts[i], 16) : "";
                        break;
                    }
                }
            }

            _analogTarget = Mathf.Clamp01(gauge);
            Apply(lines);
        }

        private void Apply(string[] lines)
        {
            switch (kind)
            {
                case ScreenKind.SplitFlap:
                {
                    for (int i = 0; i < _rowTexts.Count; i++)
                    {
                        string line = lines[i] ?? "";
                        if (_shown[i] == line) continue;
                        _shown[i] = line;
                        if (_powered) StartCoroutine(FlipRow(i, line));
                        else _rowTexts[i].text = line;
                    }
                    break;
                }
                case ScreenKind.Nixie:
                {
                    string all = string.Join("  ", System.Linq.Enumerable.ToArray(
                        System.Linq.Enumerable.Where(lines, l => !string.IsNullOrEmpty(l))));
                    if (all == "") all = _powered ? "0" : "";
                    if (_shown[0] == all) break;
                    _shown[0] = all;
                    if (_nixieText != null)
                    {
                        _nixieText.text = Trunc(all, 24);
                        if (_powered) StartCoroutine(NixieFlicker());
                    }
                    break;
                }
                case ScreenKind.Analog:
                {
                    string label = lines[0] ?? "";
                    if (_shown[0] != label && _analogLabel != null)
                    {
                        _shown[0] = label;
                        _analogLabel.text = label;
                    }
                    break;
                }
            }
        }

        /// <summary>The card tips edge-on, the text swaps while it is invisible, then it
        /// lands - with the flip sound on the landing, staggered so rows cascade.</summary>
        private System.Collections.IEnumerator FlipRow(int row, string text)
        {
            if (row >= _rowCards.Count || row >= _rowTexts.Count) yield break;
            var card = _rowCards[row];
            var label = _rowTexts[row];
            var labelRenderer = label.GetComponent<MeshRenderer>();

            yield return new WaitForSeconds(0.04f * row);

            if (labelRenderer != null) labelRenderer.enabled = false;
            float t = 0f;
            while (t < 0.05f)
            {
                t += Time.deltaTime;
                card.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 88f, t / 0.05f), 0f, 0f);
                yield return null;
            }

            label.text = text;
            if (labelRenderer != null) labelRenderer.enabled = true;
            VoxelEngine.FX.AudioManager.PlayAt(
                VoxelEngine.FX.SfxLibrary.GetVariant(VoxelEngine.FX.Sfx.SplitFlapFlip, 3),
                transform.position, volume: 0.45f,
                pitch: Random.Range(0.94f, 1.06f), maxDistance: 14f);

            t = 0f;
            while (t < 0.07f)
            {
                t += Time.deltaTime;
                card.localRotation = Quaternion.Euler(Mathf.Lerp(88f, 0f, t / 0.07f), 0f, 0f);
                yield return null;
            }
            card.localRotation = Quaternion.identity;
        }

        private System.Collections.IEnumerator NixieFlicker()
        {
            if (_nixieMat == null) yield break;
            for (int i = 0; i < 3; i++)
            {
                _nixieMat.SetColor("_EmissionColor", NixieGlow * (i % 2 == 0 ? 0.4f : 1.6f));
                yield return new WaitForSeconds(0.03f);
            }
            _nixieMat.SetColor("_EmissionColor", NixieGlow * 1.6f);
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════

        private GridRailBogie NearestBogie()
        {
            var own = GetComponentInParent<GridRailBogie>();
            if (own != null) return own;

            GridRailBogie best = null;
            float bestSq = 40f * 40f;
            var all = GridRailBogie.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null) continue;
                float d = (b.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = b; }
            }
            return best;
        }

        private RailStation NearestStation()
        {
            RailStation best = null;
            float bestSq = 25f * 25f;
            var all = RailStation.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null) continue;
                float d = (s.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            return best;
        }

        private static string Trunc(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private TextMesh NewText(string name, Color color, float worldHeight, TextAnchor anchor, TextAlignment align)
        {
            // Legacy TextMesh on purpose: the runtime assembly has no TextMeshPro
            // reference, and GridScreenBlock already renders world text this way.
            // World glyph height = fontSize * characterSize * 0.1.
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = 48;
            tm.characterSize = worldHeight / 4.8f;
            tm.anchor = anchor;
            tm.alignment = align;
            tm.color = color;
            tm.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tm;
        }

        private static readonly Dictionary<string, Material> _mats = new();

        private static Material Mat(string name, Color color, float metallic)
        {
            if (_mats.TryGetValue(name, out var cached) && cached != null) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { hideFlags = HideFlags.DontSave, name = name, color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            _mats[name] = mat;
            return mat;
        }

        private static void KillCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private static void Paint(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }
    }
}
