// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.cs
//
// Premium configurable digital screen for large grid ships.
// v5.44.0-dev — Fixed: text positioning, power state, Enabled toggle, custom text display.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.GridSystem
{
    public enum ScreenDataMode
    {
        Summary,
        Power,
        Inventory,
        Speed,
        System,
        Custom
    }

    public enum ScreenSize
    {
        Small,
        Medium,
        Large,
        ExtraLarge,
        Wide
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

        // Power draw by screen size — overrides GridBlock.PowerDraw for grid power pool
        public override float PowerDraw
        {
            get
            {
                if (!Enabled) return 0f;
                return screenSize switch
                {
                    ScreenSize.Small => 5f,
                    ScreenSize.Wide => 10f,
                    ScreenSize.Medium => 25f,
                    ScreenSize.Large => 50f,
                    ScreenSize.ExtraLarge => 100f,
                    _ => 5f
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

        public bool IsPowered => Enabled && Grid != null && Grid.HasPower;
        public bool HasDataSource => ResolveProvider() != null || dataMode == ScreenDataMode.Custom;

        // -- IGridDataProvider -----------------------------------------
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

                int maxLines = MaxDisplayLines;

                switch (dataMode)
                {
                    case ScreenDataMode.Summary:
                        return TruncateLines(provider.SourceName + "\n" + raw, maxLines);
                    case ScreenDataMode.Power:
                        return FormatSection(raw, "POWER", maxLines);
                    case ScreenDataMode.Inventory:
                        return FormatSection(raw, "CARGO", maxLines);
                    case ScreenDataMode.Speed:
                        return FormatSection(raw, "SPEED", maxLines);
                    case ScreenDataMode.System:
                        return FormatSection(raw, "SYS", maxLines);
                    case ScreenDataMode.Custom:
                    default:
                        return string.IsNullOrEmpty(customText) ? "(empty)" : customText;
                }
            }
        }

        private int MaxDisplayLines => screenSize switch
        {
            ScreenSize.Small => 3,
            ScreenSize.Wide => 4,
            ScreenSize.Medium => 6,
            ScreenSize.Large => 10,
            ScreenSize.ExtraLarge => 16,
            _ => 3
        };

        private string FormatSection(string raw, string section, int maxLines)
        {
            var lines = raw.Split('\n');
            var result = new System.Text.StringBuilder();
            int count = 0;
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (count == 0 && !trimmed.Contains(section, System.StringComparison.OrdinalIgnoreCase))
                    result.AppendLine("=" + section + "=");
                else
                    result.AppendLine(" " + trimmed);
                count++;
                if (count >= maxLines) break;
            }
            return result.ToString().Trim();
        }

        private string TruncateLines(string text, int max)
        {
            var lines = text.Split('\n');
            if (lines.Length <= max) return text;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max; i++) sb.AppendLine(lines[i]);
            return sb.ToString().Trim();
        }

        private void Start() => EnsureDisplay();

        private void Update()
        {
            if (!_initialized) { EnsureDisplay(); return; }
            if (_screenText == null) return;

            string text = FormattedDisplay;
            if (_screenText.text != text)
                _screenText.text = text;

            // Update power draw every frame to reflect enabled state
            if (_power != null)
                _power.wattsPerSecond = Enabled ? PowerDraw : 0f;

            // Color states
            if (!Enabled)
            {
                _screenText.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
                if (_statusText != null) { _statusText.text = "OFF"; _statusText.color = new Color(0.4f, 0.4f, 0.4f, 0.3f); }
                if (_titleText != null) _titleText.color = new Color(0.3f, 0.3f, 0.3f, 0.3f);
                return;
            }

            if (!IsPowered)
            {
                _screenText.color = new Color(0.8f, 0.15f, 0.10f, 0.6f);
                if (_statusText != null) { _statusText.text = "NO PWR"; _statusText.color = new Color(0.8f, 0.15f, 0.10f, 0.6f); }
                if (_titleText != null) _titleText.color = new Color(0.5f, 0.1f, 0.1f, 0.6f);
            }
            else if (!HasDataSource)
            {
                _screenText.color = new Color(0.40f, 0.44f, 0.52f);
                if (_statusText != null) { _statusText.text = "IDLE"; _statusText.color = new Color(0.40f, 0.44f, 0.52f); }
                if (_titleText != null) { _titleText.text = "NO SOURCE"; _titleText.color = new Color(0.40f, 0.44f, 0.52f); }
            }
            else
            {
                float pulse = 0.6f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 1.8f);
                _screenText.color = new Color(textColor.r, textColor.g, textColor.b, pulse + 0.4f);
                if (_statusText != null) { _statusText.text = "LIVE"; _statusText.color = new Color(textColor.r * 0.5f, textColor.g * 0.5f, textColor.b * 0.5f); }
                if (_titleText != null)
                {
                    var provider = ResolveProvider();
                    _titleText.text = provider != null ? provider.SourceName.ToUpperInvariant() : "CONNECTED";
                    _titleText.color = new Color(textColor.r * 0.8f, textColor.g * 0.8f, textColor.b * 0.8f, 0.9f);
                }
            }
        }

        private void EnsureDisplay()
        {
            if (_initialized) return;
            _initialized = true;

            // Connect power consumer
            _power = GetComponent<PowerConsumer>();
            if (_power == null) _power = gameObject.AddComponent<PowerConsumer>();
            _power.wattsPerSecond = PowerDraw;
            _power.connectRadius = 1.6f;

            // Find the screen surface child
            Transform surface = transform.Find("Generated_ScreenSurface");
            if (surface == null)
            {
                var surf = new GameObject("Generated_ScreenSurface");
                surf.transform.SetParent(transform, false);
                surface = surf.transform;
            }

            // Use large grid cell size
            float cs = 2.5f;

            // Calculate display area dimensions based on screen size
            float displayW, displayH;
            switch (screenSize)
            {
                case ScreenSize.Small:  displayW = cs * 0.7f; displayH = cs * 0.5f; break;
                case ScreenSize.Wide:   displayW = cs * 1.5f; displayH = cs * 0.5f; break;
                case ScreenSize.Medium: displayW = cs * 1.5f; displayH = cs * 1.5f; break;
                case ScreenSize.Large:  displayW = cs * 3.2f; displayH = cs * 3.2f; break;
                case ScreenSize.ExtraLarge: displayW = cs * 6.6f; displayH = cs * 6.6f; break;
                default: displayW = cs * 0.7f; displayH = cs * 0.5f; break;
            }

            // Remove old text children
            var oldDisplay = surface.Find("ScreenDisplayText");
            if (oldDisplay != null) DestroyImmediate(oldDisplay.gameObject);
            var oldTitle = surface.Find("ScreenTitleText");
            if (oldTitle != null) DestroyImmediate(oldTitle.gameObject);
            var oldStatus = surface.Find("ScreenStatusText");
            if (oldStatus != null) DestroyImmediate(oldStatus.gameObject);

            // Create parent object for display elements so we can scale them uniformly
            var displayRoot = new GameObject("DisplayRoot");
            displayRoot.transform.SetParent(surface, false);
            displayRoot.transform.localPosition = new Vector3(0, 0, -0.01f);

            // Determine font size based on screen size
            int fontSize = screenSize switch
            {
                ScreenSize.Small => 20,
                ScreenSize.Wide => 24,
                ScreenSize.Medium => 28,
                ScreenSize.Large => 36,
                ScreenSize.ExtraLarge => 48,
                _ => 20
            };

            // Title text (top of screen)
            var titleObj = new GameObject("ScreenTitleText");
            titleObj.transform.SetParent(displayRoot.transform, false);
            titleObj.transform.localPosition = new Vector3(0, displayH * 0.38f, 0);
            _titleText = titleObj.AddComponent<TextMesh>();
            _titleText.text = "NO SOURCE";
            _titleText.fontSize = Mathf.RoundToInt(fontSize * 0.7f);
            _titleText.color = new Color(0.50f, 0.55f, 0.65f);
            _titleText.anchor = TextAnchor.UpperCenter;
            _titleText.alignment = TextAlignment.Center;
            _titleText.fontStyle = FontStyle.Bold;

            // Main data text (centered)
            var textObj = new GameObject("ScreenDisplayText");
            textObj.transform.SetParent(displayRoot.transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, 0);
            _screenText = textObj.AddComponent<TextMesh>();
            _screenText.text = "STARTING";
            _screenText.fontSize = fontSize;
            _screenText.color = textColor;
            _screenText.anchor = TextAnchor.MiddleCenter;
            _screenText.alignment = TextAlignment.Center;
            _screenText.fontStyle = FontStyle.Normal;

            // Status indicator (bottom-right)
            var statusObj = new GameObject("ScreenStatusText");
            statusObj.transform.SetParent(displayRoot.transform, false);
            statusObj.transform.localPosition = new Vector3(displayW * 0.42f, -displayH * 0.38f, 0);
            _statusText = statusObj.AddComponent<TextMesh>();
            _statusText.text = "BOOT";
            _statusText.fontSize = Mathf.RoundToInt(fontSize * 0.4f);
            _statusText.color = new Color(0.30f, 0.35f, 0.45f);
            _statusText.anchor = TextAnchor.LowerRight;
            _statusText.alignment = TextAlignment.Right;

            // Scale the display root so it fits the screen surface nicely.
            // TextMesh character size default is 1 unit per font-unit, which is
            // enormous at game scale. Set character size very small so text is readable.
            float baseScale = displayW / 5f;
            displayRoot.transform.localScale = new Vector3(baseScale, baseScale, 1f);
            _screenText.characterSize = 0.015f;
            _titleText.characterSize = 0.015f;
            _statusText.characterSize = 0.015f;

            // Rotate text to face outward (screen faces -Z by default in our prefab)
            displayRoot.transform.localRotation = Quaternion.identity;
        }

        public IGridDataProvider ResolveProvider()
        {
            if (Grid == null) return null;

            if (_lastCheckedPos != dataSourceGridPos || _cachedProvider == null)
            {
                _lastCheckedPos = dataSourceGridPos;
                _cachedProvider = null;
                if (Grid.Blocks.TryGetValue(dataSourceGridPos, out var block))
                {
                    _cachedProvider = block as IGridDataProvider;
                    if (_cachedProvider != null)
                        dataSourceInstanceId = block.GetInstanceID();
                }
            }

            if (_cachedProvider != null && Grid.Blocks.TryGetValue(dataSourceGridPos, out var current))
                if (current.GetInstanceID() != dataSourceInstanceId)
                    _cachedProvider = null;

            return _cachedProvider;
        }

        public void AutoLinkToNearestProvider()
        {
            if (Grid == null) return;
            IGridDataProvider best = null;
            Vector3Int bestPos = Vector3Int.zero;
            float bestDist = float.MaxValue;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value == null || kv.Value == this) continue;
                if (!(kv.Value is IGridDataProvider provider)) continue;
                float d = Vector3.Distance(kv.Value.transform.position, transform.position);
                if (d < bestDist) { bestDist = d; best = provider; bestPos = kv.Key; }
            }
            if (best != null)
            {
                dataSourceGridPos = bestPos;
                dataSourceInstanceId = (best as GridBlock)?.GetInstanceID() ?? 0;
                _cachedProvider = best; _lastCheckedPos = bestPos;
            }
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

        public void SetDataSource(Vector3Int gridPos, int instanceId)
        {
            dataSourceGridPos = gridPos;
            dataSourceInstanceId = instanceId;
            _cachedProvider = null;
            _lastCheckedPos = gridPos;
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Screen (" + screenSize + ")";
            _initialized = false;
            AutoLinkToNearestProvider();
        }
    }
}
