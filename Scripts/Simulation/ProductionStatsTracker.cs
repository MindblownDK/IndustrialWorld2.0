// Assets/Scripts/VoxelEngine/Simulation/ProductionStatsTracker.cs
//
// Save-free rolling production statistics for factory visibility. Machines call
// RecordConsumed / RecordProduced when a batch completes; UI panels can display
// per-minute throughput and lifetime totals without touching save schemas.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    public sealed class ProductionStatsTracker : MonoBehaviour
    {
        public readonly struct ItemStats
        {
            public readonly ItemDefinition Item;
            public readonly int ProducedTotal;
            public readonly int ConsumedTotal;
            public readonly float ProducedPerMinute;
            public readonly float ConsumedPerMinute;
            public int NetTotal => ProducedTotal - ConsumedTotal;
            public float NetPerMinute => ProducedPerMinute - ConsumedPerMinute;

            public ItemStats(ItemDefinition item, int producedTotal, int consumedTotal, float producedPerMinute, float consumedPerMinute)
            {
                Item = item;
                ProducedTotal = producedTotal;
                ConsumedTotal = consumedTotal;
                ProducedPerMinute = producedPerMinute;
                ConsumedPerMinute = consumedPerMinute;
            }
        }

        private struct EventSample
        {
            public ItemDefinition item;
            public int count;
            public float time;
            public bool produced;
        }

        private const float WindowSeconds = 60f;
        private const float RetainSeconds = 300f;
        private static ProductionStatsTracker _instance;

        private readonly List<EventSample> _events = new(256);
        private readonly Dictionary<ItemDefinition, int> _producedTotals = new();
        private readonly Dictionary<ItemDefinition, int> _consumedTotals = new();

        public static ProductionStatsTracker Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var existing = FindAnyObjectByType<ProductionStatsTracker>();
                if (existing != null) return _instance = existing;
                var go = new GameObject("ProductionStatsTracker");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<ProductionStatsTracker>();
                return _instance;
            }
        }

        public static void RecordProduced(ItemDefinition item, int count) => Instance.Record(item, count, produced: true);
        public static void RecordConsumed(ItemDefinition item, int count) => Instance.Record(item, count, produced: false);

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Record(ItemDefinition item, int count, bool produced)
        {
            if (item == null || count <= 0) return;
            var totals = produced ? _producedTotals : _consumedTotals;
            totals[item] = totals.TryGetValue(item, out int current) ? current + count : count;
            _events.Add(new EventSample { item = item, count = count, time = Time.time, produced = produced });
            TrimOldEvents();
        }

        public void Clear()
        {
            _events.Clear();
            _producedTotals.Clear();
            _consumedTotals.Clear();
        }

        public IReadOnlyList<ItemStats> GetSnapshot()
        {
            TrimOldEvents();
            float cutoff = Time.time - WindowSeconds;
            var producedMinute = new Dictionary<ItemDefinition, int>();
            var consumedMinute = new Dictionary<ItemDefinition, int>();
            foreach (var sample in _events)
            {
                if (sample.time < cutoff || sample.item == null) continue;
                var map = sample.produced ? producedMinute : consumedMinute;
                map[sample.item] = map.TryGetValue(sample.item, out int current) ? current + sample.count : sample.count;
            }

            var items = new HashSet<ItemDefinition>(_producedTotals.Keys);
            items.UnionWith(_consumedTotals.Keys);
            items.UnionWith(producedMinute.Keys);
            items.UnionWith(consumedMinute.Keys);

            return items
                .Where(item => item != null)
                .Select(item => new ItemStats(
                    item,
                    _producedTotals.TryGetValue(item, out int pt) ? pt : 0,
                    _consumedTotals.TryGetValue(item, out int ct) ? ct : 0,
                    producedMinute.TryGetValue(item, out int pm) ? pm : 0,
                    consumedMinute.TryGetValue(item, out int cm) ? cm : 0))
                .OrderByDescending(stat => Mathf.Abs(stat.NetPerMinute))
                .ThenBy(stat => stat.Item.displayName)
                .ToList();
        }

        private void TrimOldEvents()
        {
            float cutoff = Time.time - RetainSeconds;
            _events.RemoveAll(sample => sample.time < cutoff || sample.item == null);
        }
    }
}
