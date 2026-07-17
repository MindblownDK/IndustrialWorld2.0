// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.cs
//
// Premium configurable digital screen for large grid ships.
// v5.47.0-dev — Multi-source: one screen can show data from any number of providers.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.GridSystem
{
    public enum ScreenDataMode
    {
        Summary, Power, Inventory, Speed, System, Bars, Custom
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

            string text = FormattedDisplay;
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
                if (_statusText != null) { _statusText.text = "LIVE"; _statusText.color = new Color(textColor.r * 0.5f, textColor.g * 0.5f, textColor.b * 0.5f); }
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

            var root = new GameObject("DisplayRoot");
            root.transform.SetParent(surface, false);
            root.transform.localPosition = new Vector3(0, 0, -0.015f);

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

            var dObj = new GameObject("ScreenDisplayText");
            dObj.transform.SetParent(root.transform, false);
            dObj.transform.localPosition = Vector3.zero;
            _screenText = dObj.AddComponent<TextMesh>();
            _screenText.text = "STARTING"; _screenText.fontSize = 24;
            _screenText.characterSize = charSize; _screenText.color = textColor;
            _screenText.anchor = TextAnchor.MiddleCenter; _screenText.alignment = TextAlignment.Center;

            var sObj = new GameObject("ScreenStatusText");
            sObj.transform.SetParent(root.transform, false);
            sObj.transform.localPosition = new Vector3(dw * 0.42f, -dh * 0.38f, 0);
            _statusText = sObj.AddComponent<TextMesh>();
            _statusText.text = "BOOT"; _statusText.fontSize = 12;
            _statusText.characterSize = charSize;
            _statusText.color = new Color(0.30f, 0.35f, 0.45f);
            _statusText.anchor = TextAnchor.LowerRight; _statusText.alignment = TextAlignment.Right;
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
                    provider = block as IGridDataProvider;
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
                if (kv.Value == null || kv.Value == this || !(kv.Value is IGridDataProvider p)) continue;
                float d = Vector3.Distance(kv.Value.transform.position, transform.position);
                if (d < bd) { bd = d; best = p; bp = kv.Key; }
            }
            if (best != null)
                ToggleSource(bp, (best as GridBlock)?.GetInstanceID() ?? 0);
        }

        public List<(Vector3Int pos, IGridDataProvider provider)> GetAvailableSources()
        {
            var list = new List<(Vector3Int, IGridDataProvider)>();
            if (Grid == null) return list;
            foreach (var kv in Grid.Blocks)
                if (kv.Value != null && kv.Value != this && kv.Value is IGridDataProvider p)
                    list.Add((kv.Key, p));
            return list;
        }

        public override void OnPlaced()
        {
            base.OnPlaced(); blockName = "Screen (" + screenSize + ")";
            _initialized = false; AutoLinkToNearestProvider();
        }
    }
}
