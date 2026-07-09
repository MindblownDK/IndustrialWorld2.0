// Assets/Scripts/VoxelEngine/Simulation/ConveyorChute.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — CONVEYOR CHUTE                              ║
// ║  Drops items from one elevation to another. Items slide         ║
// ║  visually and audibly through the chute channel.                ║
// ║  Variants: Straight drop, Corner drop, Spiral (future).         ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>Chute shape variant.</summary>
    public enum ChuteShape { Straight, Corner, Spiral }

    /// <summary>
    /// A single item sliding through the chute.
    /// </summary>
    [System.Serializable]
    public struct ChuteItem
    {
        public ItemDefinition item;
        public int count;
        /// <summary>0 = top entry, 1 = bottom exit.</summary>
        public float slideProgress;
    }

    /// <summary>
    /// Vertical/diagonal item transport. Accepts items from belts or machines
    /// above, slides them down, and deposits onto a belt or machine below.
    /// </summary>
    public class ConveyorChute : MonoBehaviour, IItemConsumer, IItemProvider
    {
        [Header("Chute Configuration")]
        public ChuteShape shape = ChuteShape.Straight;

        [Header("Capacity")]
        [Tooltip("Maximum items sliding through the chute at once.")]
        public int maxItems = 6;

        [Header("Speed")]
        [Tooltip("Slide speed multiplier. Higher = faster descent.")]
        public float slideSpeed = 3f;

        [Header("Connections (auto-detected)")]
        public MonoBehaviour upstreamSource;
        public MonoBehaviour downstreamTarget;

        // ── Runtime ───────────────────────────────────────────────────

        private readonly List<ChuteItem> _items = new(12);
        private float _scanTimer;
        private float _pullTimer;

        /// <summary>Read-only view of items currently in the chute.</summary>
        public IReadOnlyList<ChuteItem> Items => _items;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            BuildChuteVisuals();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // Advance items down the chute.
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var ci = _items[i];
                ci.slideProgress += slideSpeed * dt;

                if (ci.slideProgress >= 1f)
                {
                    if (TryHandOff(ref ci))
                    {
                        _items.RemoveAt(i);
                        continue;
                    }
                    ci.slideProgress = 1f; // wait at exit
                }
                _items[i] = ci;
            }

            // Scan connections periodically.
            _scanTimer += dt;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                ScanConnections();
            }

            // Pull from upstream.
            _pullTimer += dt;
            if (_pullTimer >= 0.3f && _items.Count < maxItems && upstreamSource != null)
            {
                _pullTimer = 0f;
                TryPullFromUpstream();
            }
        }

        // ── IItemConsumer ─────────────────────────────────────────────

        public int GetInputCapacity(ItemDefinition item)
        {
            if (item == null) return 0;
            return Mathf.Max(0, maxItems - _items.Count);
        }

        public int TryInsert(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;
            int accepted = Mathf.Min(count, maxItems - _items.Count);
            for (int i = 0; i < accepted; i++)
            {
                _items.Add(new ChuteItem
                {
                    item = item,
                    count = 1,
                    slideProgress = 0f
                });
            }
            return accepted;
        }

        // ── IItemProvider ─────────────────────────────────────────────

        public ItemDefinition PeekOutput(out int count)
        {
            count = 0;
            if (_items.Count == 0) return null;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].slideProgress >= 0.9f)
                {
                    count = _items[i].count;
                    return _items[i].item;
                }
            }
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var ci = _items[i];
                if (ci.item != item || ci.slideProgress < 0.9f) continue;

                int take = Mathf.Min(count, ci.count);
                ci.count -= take;
                count -= take;

                if (ci.count <= 0) _items.RemoveAt(i);
                else _items[i] = ci;

                if (count <= 0) return take;
            }
            return 0;
        }

        // ── Connections ───────────────────────────────────────────────

        private void ScanConnections()
        {
            // Downstream = below the chute exit.
            Vector3 exitPos = transform.position + Vector3.down * 1.2f;
            var hits = Physics.OverlapSphere(exitPos, 0.8f);
            downstreamTarget = null;
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var consumer = col.GetComponentInParent<MonoBehaviour>() as IItemConsumer;
                if (consumer != null)
                {
                    downstreamTarget = consumer as MonoBehaviour;
                    break;
                }
            }

            // Upstream = above the chute entry.
            Vector3 entryPos = transform.position + Vector3.up * 1.2f;
            hits = Physics.OverlapSphere(entryPos, 0.8f);
            upstreamSource = null;
            foreach (var col in hits)
            {
                if (col.gameObject == gameObject) continue;
                var provider = col.GetComponentInParent<MonoBehaviour>() as IItemProvider;
                if (provider != null)
                {
                    upstreamSource = provider as MonoBehaviour;
                    break;
                }
            }
        }

        private bool TryHandOff(ref ChuteItem ci)
        {
            if (downstreamTarget == null) return false;
            var consumer = downstreamTarget as IItemConsumer;
            if (consumer == null) return false;

            int cap = consumer.GetInputCapacity(ci.item);
            if (cap <= 0) return false;

            int sent = Mathf.Min(cap, ci.count);
            int accepted = consumer.TryInsert(ci.item, sent);
            ci.count -= accepted;
            return ci.count <= 0;
        }

        private void TryPullFromUpstream()
        {
            var provider = upstreamSource as IItemProvider;
            if (provider == null) return;

            var item = provider.PeekOutput(out int available);
            if (item == null || available <= 0) return;

            int want = Mathf.Min(available, maxItems - _items.Count);
            int got = provider.TryExtract(item, want);

            for (int i = 0; i < got; i++)
            {
                _items.Add(new ChuteItem
                {
                    item = item,
                    count = 1,
                    slideProgress = 0f
                });
            }
        }

        // ── Visuals ───────────────────────────────────────────────────

        private void BuildChuteVisuals()
        {
            // Main chute body — angled channel.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "ChuteBody";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.6f, 1.0f, 0.6f);

            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = body.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = new Color(0.30f, 0.33f, 0.38f);
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Smoothness", 0.4f);
            mr.material = mat;

            // Inner channel — slightly transparent to see items sliding.
            var inner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            inner.name = "ChuteChannel";
            inner.transform.SetParent(transform, false);
            inner.transform.localPosition = Vector3.zero;
            inner.transform.localScale = new Vector3(0.45f, 0.95f, 0.45f);

            var icol = inner.GetComponent<Collider>();
            if (icol != null) Destroy(icol);

            var imr = inner.GetComponent<MeshRenderer>();
            var imat = new Material(shader);
            imat.color = new Color(0.12f, 0.13f, 0.16f, 0.6f);
            imat.SetFloat("_Metallic", 0.2f);
            imat.SetFloat("_Smoothness", 0.8f);
            imr.material = imat;
        }
    }
}
