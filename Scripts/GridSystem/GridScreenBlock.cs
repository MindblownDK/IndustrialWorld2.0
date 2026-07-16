// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.cs
//
// Premium configurable digital screen for large grid ships.
// v5.44.0-dev — Fixed: text sizing now uses world-space units + proper charSize.

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

        [Header("Data Source")]
        public Vector3Int dataSourceGridPos;
        public int dataSourceInstanceId;

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

        private IGridDataProvider _cachedProvider;
        private Vector3Int _lastCheckedPos;
        private TextMesh _screenText;
        private TextMesh _titleText;
        private TextMesh _statusText;
        private bool _initialized;
        private PowerConsumer _power;

        // ── Display area in world units (fits within the screen surface cube) ──
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
            // Return a world-unit character height that fits MaxDisplayLines lines
            // into the display area with 85% fill + padding.
            var (_, h) = DisplayArea();
            int lines = Mathf.Max(1, MaxDisplayLines);
            return (h * 0.85f) / lines;
        }

        public bool IsPowered => Enabled && Grid != null && Grid.HasPower;
        public bool HasDataSource => ResolveProvider() != null || dataMode == ScreenDataMode.Custom;

        // ── IGridDataProvider ──────────────────────────────────────────
        public string SourceName => blockName;
        public string DataCategory => "Display";
        public string GetDisplayData() => FormattedDisplay;

        public string FormattedDisplay
        {
            get
            {
                if (!Enabled) return "DISABLED";
                if (!IsPowered) return "OFFLINE";
                if (dataMode == ScreenDataMode.Custom) return string.IsNullOrEmpty(customText) ? "(empty)" : customText;
                var provider = ResolveProvider();
                if (provider == null) return "ONLINE\nNO DATA\nRight-click to configure";
                string raw = provider.GetDisplayData();
                if (string.IsNullOrEmpty(raw)) return "--";
                int max = MaxDisplayLines;
                switch (dataMode)
                {
                    case ScreenDataMode.Summary: return TruncateLines(provider.SourceName + "\n" + raw, max);
                    case ScreenDataMode.Power:   return FormatSection(raw, "POWER", max);
                    case ScreenDataMode.Inventory: return FormatSection(raw, "CARGO", max);
                    case ScreenDataMode.Speed:   return FormatSection(raw, "SPEED", max);
                    case ScreenDataMode.System:  return FormatSection(raw, "SYS", max);
                    case ScreenDataMode.Bars:    return FormatBars(raw, provider.SourceName, max);
                    default: return string.IsNullOrEmpty(customText) ? "(empty)" : customText;
                }
            }
        }

        private int MaxDisplayLines => screenSize switch
        {
            ScreenSize.Small => 3, ScreenSize.Wide => 4,
            ScreenSize.Medium => 6, ScreenSize.Large => 10,
            ScreenSize.ExtraLarge => 16, _ => 3
        };

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

        /// <summary>Render percentage values from provider data as visual bar charts.</summary>
        private string FormatBars(string raw, string sourceName, int maxLines)
        {
            var result = new System.Text.StringBuilder();
            result.AppendLine("[" + sourceName.ToUpperInvariant() + "]");

            var lines = raw.Split('\n');
            int barsRendered = 0;

            foreach (var l in lines)
            {
                string t = l.Trim();
                if (t.Length == 0) continue;
                if (barsRendered >= maxLines - 1) break;

                // Try to find a percentage value in this line
                float pct = ExtractPercentage(t);
                if (pct >= 0f)
                {
                    // Draw a bar
                    result.AppendLine(" " + BarString(pct));
                    barsRendered++;
                }
                // Also show non-percentage data (like kWh, mode)
                else if (barsRendered < maxLines - 2)
                {
                    result.AppendLine(" " + t);
                    barsRendered++;
                }
            }

            // Ensure at least one bar filled
            if (result.ToString().Trim().Length <= sourceName.Length + 4)
                return raw;

            return result.ToString().Trim();
        }

        /// <summary>Extract a percentage value (e.g. "80%") from a string. Returns -1 if none found.</summary>
        private static float ExtractPercentage(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1f;
            int idx = text.IndexOf('%');
            if (idx < 1) return -1f;
            // Walk backwards to find the number start
            int start = idx - 1;
            while (start >= 0 && (char.IsDigit(text[start]) || text[start] == '.' || text[start] == ','))
                start--;
            start++;
            if (start >= idx) return -1f;
            string numStr = text.Substring(start, idx - start);
            if (float.TryParse(numStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out float val))
                return Mathf.Clamp01(val / 100f);
            return -1f;
        }

        /// <summary>Render a 0-1 value as a unicode bar (10 characters wide).</summary>
        private static string BarString(float v01)
        {
            int full = Mathf.FloorToInt(v01 * 10f);
            full = Mathf.Clamp(full, 0, 10);

            // Use unicode block characters █ ▓ ▒ ░
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < full; i++) sb.Append('█');
            // Fill remaining with light shade
            for (int i = full; i < 10; i++) sb.Append('░');

            sb.Append(" " + (v01 * 100f).ToString("0") + "%");
            return sb.ToString();
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

            // ── State colours ──
            if (!Enabled)
            {
                Color dim = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                _screenText.color = dim;
                if (_titleText != null) _titleText.color = dim;
                if (_statusText != null) { _statusText.text = "OFF"; _statusText.color = dim; }
            }
            else if (!IsPowered)
            {
                Color warn = new Color(0.8f, 0.15f, 0.10f, 0.6f);
                _screenText.color = warn;
                if (_titleText != null) _titleText.color = warn;
                if (_statusText != null) { _statusText.text = "NO PWR"; _statusText.color = warn; }
            }
            else if (!HasDataSource)
            {
                Color idle = new Color(0.40f, 0.44f, 0.52f);
                _screenText.color = idle;
                if (_titleText != null) { _titleText.text = "NO SOURCE"; _titleText.color = idle; }
                if (_statusText != null) { _statusText.text = "IDLE"; _statusText.color = idle; }
            }
            else
            {
                float pulse = 0.7f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 1.8f);
                _screenText.color = new Color(textColor.r, textColor.g, textColor.b, pulse);
                if (_titleText != null)
                {
                    _titleText.text = (ResolveProvider()?.SourceName ?? "CONNECTED").ToUpperInvariant();
                    _titleText.color = new Color(textColor.r * 0.7f, textColor.g * 0.7f, textColor.b * 0.7f, 0.8f);
                }
                if (_statusText != null)
                {
                    _statusText.text = "LIVE";
                    _statusText.color = new Color(textColor.r * 0.5f, textColor.g * 0.5f, textColor.b * 0.5f);
                }
            }

            // ── Keep text positions relative to current display area ──
            if (_titleText != null)
                _titleText.transform.localPosition = new Vector3(0, dh * 0.38f, 0);
            if (_statusText != null)
                _statusText.transform.localPosition = new Vector3(dw * 0.42f, -dh * 0.38f, 0);
        }

        private void EnsureDisplay()
        {
            if (_initialized) return;
            _initialized = true;

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

            // Destroy old display children
            var oldRoot = surface.Find("DisplayRoot");
            if (oldRoot != null) { DestroyImmediate(oldRoot.gameObject); }

            var (dw, dh) = DisplayArea();
            float charH = CharHeight();
            // characterSize maps 1 font-unit → this many world units
            float charSize = charH / 24f; // we target ~24px font

            var root = new GameObject("DisplayRoot");
            root.transform.SetParent(surface, false);
            root.transform.localPosition = new Vector3(0, 0, -0.015f);

            // ── Title ──
            var tObj = new GameObject("ScreenTitleText");
            tObj.transform.SetParent(root.transform, false);
            tObj.transform.localPosition = new Vector3(0, dh * 0.38f, 0);
            _titleText = tObj.AddComponent<TextMesh>();
            _titleText.text = "NO SOURCE";
            _titleText.fontSize = 20;
            _titleText.characterSize = charSize;
            _titleText.color = new Color(0.50f, 0.55f, 0.65f);
            _titleText.anchor = TextAnchor.UpperCenter;
            _titleText.alignment = TextAlignment.Center;
            _titleText.fontStyle = FontStyle.Bold;

            // ── Main data ──
            var dObj = new GameObject("ScreenDisplayText");
            dObj.transform.SetParent(root.transform, false);
            dObj.transform.localPosition = Vector3.zero;
            _screenText = dObj.AddComponent<TextMesh>();
            _screenText.text = "STARTING";
            _screenText.fontSize = 24;
            _screenText.characterSize = charSize;
            _screenText.color = textColor;
            _screenText.anchor = TextAnchor.MiddleCenter;
            _screenText.alignment = TextAlignment.Center;
            _screenText.fontStyle = FontStyle.Normal;

            // ── Status ──
            var sObj = new GameObject("ScreenStatusText");
            sObj.transform.SetParent(root.transform, false);
            sObj.transform.localPosition = new Vector3(dw * 0.42f, -dh * 0.38f, 0);
            _statusText = sObj.AddComponent<TextMesh>();
            _statusText.text = "BOOT";
            _statusText.fontSize = 12;
            _statusText.characterSize = charSize;
            _statusText.color = new Color(0.30f, 0.35f, 0.45f);
            _statusText.anchor = TextAnchor.LowerRight;
            _statusText.alignment = TextAlignment.Right;
        }

        public IGridDataProvider ResolveProvider()
        {
            if (Grid == null) return null;
            if (_lastCheckedPos != dataSourceGridPos || _cachedProvider == null)
            {
                _lastCheckedPos = dataSourceGridPos; _cachedProvider = null;
                if (Grid.Blocks.TryGetValue(dataSourceGridPos, out var b))
                { _cachedProvider = b as IGridDataProvider; if (_cachedProvider != null) dataSourceInstanceId = b.GetInstanceID(); }
            }
            if (_cachedProvider != null && Grid.Blocks.TryGetValue(dataSourceGridPos, out var c))
                if (c.GetInstanceID() != dataSourceInstanceId) _cachedProvider = null;
            return _cachedProvider;
        }

        public void AutoLinkToNearestProvider()
        {
            if (Grid == null) return;
            IGridDataProvider best = null; Vector3Int bp = default; float bd = float.MaxValue;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value == null || kv.Value == this || !(kv.Value is IGridDataProvider p)) continue;
                float d = Vector3.Distance(kv.Value.transform.position, transform.position);
                if (d < bd) { bd = d; best = p; bp = kv.Key; }
            }
            if (best != null) { dataSourceGridPos = bp; dataSourceInstanceId = (best as GridBlock)?.GetInstanceID() ?? 0; _cachedProvider = best; _lastCheckedPos = bp; }
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

        public void SetDataSource(Vector3Int gp, int id) { dataSourceGridPos = gp; dataSourceInstanceId = id; _cachedProvider = null; _lastCheckedPos = gp; }

        public override void OnPlaced()
        {
            base.OnPlaced(); blockName = "Screen (" + screenSize + ")";
            _initialized = false; AutoLinkToNearestProvider();
        }
    }
}
