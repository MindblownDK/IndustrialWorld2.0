// Assets/Scripts/VoxelEngine/GridSystem/GridScreenBlock.cs
//
// A configurable digital screen mounted on a ship grid.
// Displays live data from any IGridDataProvider block on the same grid.
// Right-click to configure: select data source, choose what info to show,
// customize colours and text size.
//
// v5.43.0-dev — Grid Screens & Displays.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    /// <summary>What aspect of the data source to display.</summary>
    public enum ScreenDataMode
    {
        Summary,    // auto picks the most useful info
        Power,      // power generation / consumption / battery
        Inventory,  // cargo fill / item counts
        Speed,      // current speed / velocity
        System,     // grid state, mass, block count
        Custom      // raw GetDisplayData() output
    }

    /// <summary>Screen physical size variant.</summary>
    public enum ScreenSize
    {
        Small,       // 1x1 grid cell
        Medium,      // 2x2 grid cells
        Large,       // 4x4 grid cells
        ExtraLarge,  // 8x8 grid cells
        Wide         // 2x1 grid cells, banner style
    }

    public class GridScreenBlock : GridBlock
    {
        [Header("Screen")]
        public ScreenSize screenSize = ScreenSize.Small;
        public ScreenDataMode dataMode = ScreenDataMode.Summary;
        public Color textColor = new Color(0.18f, 0.72f, 0.88f);
        public Color backgroundColor = new Color(0.035f, 0.04f, 0.06f, 0.92f);
        public float textSize = 1f;

        [Header("Data Source")]
        [Tooltip("Grid position of the data source block. Zero means no source selected.")]
        public Vector3Int dataSourceGridPos;
        [Tooltip("Instance ID of the data source block (for validation).")]
        public int dataSourceInstanceId;

        /// <summary>Cached reference to the data provider (resolved each frame from gridPos).</summary>
        private IGridDataProvider _cachedProvider;
        private Vector3Int _lastCheckedPos;

        /// <summary>Last resolved display text.</summary>
        public string CurrentDisplayText { get; private set; } = "NO SIGNAL";

        /// <summary>Current formatted data string shown on screen.</summary>
        public string FormattedDisplay
        {
            get
            {
                var provider = ResolveProvider();
                if (provider == null) return "NO SIGNAL";

                string raw = provider.GetDisplayData();
                if (string.IsNullOrEmpty(raw)) return "--";

                switch (dataMode)
                {
                    case ScreenDataMode.Summary:
                        return provider.SourceName + "\n" + raw.Split('\n')[0];
                    case ScreenDataMode.Power:
                        return ExtractSection(raw, "POWER", "⚡");
                    case ScreenDataMode.Inventory:
                        return ExtractSection(raw, "CARGO", "📦");
                    case ScreenDataMode.Speed:
                        return ExtractSection(raw, "SPEED", "🚀");
                    case ScreenDataMode.System:
                        return ExtractSection(raw, "SYS", "⚙");
                    case ScreenDataMode.Custom:
                    default:
                        return raw;
                }
            }
        }

        private string ExtractSection(string raw, string sectionName, string icon)
        {
            if (raw.Contains(sectionName, System.StringComparison.OrdinalIgnoreCase))
            {
                var lines = raw.Split('\n');
                var result = new System.Text.StringBuilder();
                result.AppendLine($"{icon} {sectionName}");
                foreach (var line in lines)
                {
                    if (!line.Contains(sectionName, System.StringComparison.OrdinalIgnoreCase))
                        result.AppendLine(" " + line.Trim());
                }
                return result.ToString().Trim();
            }
            // Fallback: show first 3 lines
            var all = raw.Split('\n');
            int take = Mathf.Min(3, all.Length);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < take; i++)
                sb.AppendLine(all[i].Trim());
            return sb.ToString().Trim();
        }

        /// <summary>Tries to find a data provider for the configured source.</summary>
        public IGridDataProvider ResolveProvider()
        {
            if (Grid == null) return null;

            // Re-check if position changed
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

            // Verify instance ID still matches
            if (_cachedProvider != null && Grid.Blocks.TryGetValue(dataSourceGridPos, out var current))
            {
                if (current.GetInstanceID() != dataSourceInstanceId)
                    _cachedProvider = null;
            }

            return _cachedProvider;
        }

        /// <summary>Scan for the closest IGridDataProvider on the same grid and link to it.</summary>
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
                if (d < bestDist)
                {
                    bestDist = d;
                    best = provider;
                    bestPos = kv.Key;
                }
            }

            if (best != null)
            {
                dataSourceGridPos = bestPos;
                dataSourceInstanceId = (best as GridBlock)?.GetInstanceID() ?? 0;
                _cachedProvider = best;
                _lastCheckedPos = bestPos;
            }
        }

        /// <summary>Enumerate all potential data sources on this grid.</summary>
        public List<(Vector3Int pos, IGridDataProvider provider)> GetAvailableSources()
        {
            var list = new List<(Vector3Int, IGridDataProvider)>();
            if (Grid == null) return list;

            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value == null || kv.Value == this) continue;
                if (kv.Value is IGridDataProvider provider)
                    list.Add((kv.Key, provider));
            }
            return list;
        }

        public void SetDataSource(Vector3Int gridPos, int instanceId)
        {
            dataSourceGridPos = gridPos;
            dataSourceInstanceId = instanceId;
            _cachedProvider = null; // force re-resolve
            _lastCheckedPos = gridPos;
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = $"Screen ({screenSize})";
            // Auto-link to nearest data source after placement
            AutoLinkToNearestProvider();
        }
    }
}
