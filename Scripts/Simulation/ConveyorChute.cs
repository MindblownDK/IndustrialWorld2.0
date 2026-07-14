// Assets/Scripts/VoxelEngine/Simulation/ConveyorChute.cs
//
// Vertical item transport with authored-prefab reuse, fallback visuals, and
// pooled item representations. Existing setup-generated visuals are never
// duplicated at runtime.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    public enum ChuteShape { Straight, Corner, Spiral }

    [System.Serializable]
    public struct ChuteItem
    {
        public ItemDefinition item;
        public int count;
        public float slideProgress;
    }

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

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Material _fallbackShellMaterial;
        private static Material _fallbackChannelMaterial;
        private static Material _sharedItemMaterial;

        private readonly List<ChuteItem> _items = new(12);
        private readonly List<Transform> _itemVisuals = new(12);
        private readonly List<bool> _visualActive = new(12);
        private readonly MaterialPropertyBlock _itemProperties = new();
        private float _scanTimer;
        private float _pullTimer;

        public IReadOnlyList<ChuteItem> Items => _items;

        private void Awake()
        {
            EnsureVisuals();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var chuteItem = _items[i];
                chuteItem.slideProgress += slideSpeed * dt;

                if (chuteItem.slideProgress >= 1f)
                {
                    if (TryHandOff(ref chuteItem))
                    {
                        _items.RemoveAt(i);
                        continue;
                    }
                    chuteItem.slideProgress = 1f;
                }
                _items[i] = chuteItem;
            }

            _scanTimer += dt;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                ScanConnections();
            }

            _pullTimer += dt;
            if (_pullTimer >= 0.3f && _items.Count < maxItems && upstreamSource != null)
            {
                _pullTimer = 0f;
                TryPullFromUpstream();
            }

            UpdateItemVisuals();
        }

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

        public ItemDefinition PeekOutput(out int count)
        {
            count = 0;
            if (_items.Count == 0) return null;

            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].slideProgress < 0.9f) continue;
                count = _items[i].count;
                return _items[i].item;
            }
            return null;
        }

        public int TryExtract(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return 0;

            int remaining = count;
            int extracted = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var chuteItem = _items[i];
                if (chuteItem.item != item || chuteItem.slideProgress < 0.9f) continue;

                int take = Mathf.Min(remaining, chuteItem.count);
                chuteItem.count -= take;
                remaining -= take;
                extracted += take;

                if (chuteItem.count <= 0)
                    _items.RemoveAt(i);
                else
                    _items[i] = chuteItem;

                if (remaining <= 0) break;
            }
            return extracted;
        }

        private void ScanConnections()
        {
            downstreamTarget = FindInterfaceAt<IItemConsumer>(transform.position - transform.up * 1.2f);
            upstreamSource = FindInterfaceAt<IItemProvider>(transform.position + transform.up * 1.2f);
        }

        private MonoBehaviour FindInterfaceAt<T>(Vector3 worldPosition) where T : class
        {
            var hits = Physics.OverlapSphere(worldPosition, 0.8f);
            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour != null && behaviour != this && behaviour is T)
                        return behaviour;
                }
            }
            return null;
        }

        private bool TryHandOff(ref ChuteItem chuteItem)
        {
            if (!(downstreamTarget is IItemConsumer consumer)) return false;

            int capacity = consumer.GetInputCapacity(chuteItem.item);
            if (capacity <= 0) return false;

            int sent = Mathf.Min(capacity, chuteItem.count);
            int accepted = consumer.TryInsert(chuteItem.item, sent);
            chuteItem.count -= accepted;
            return chuteItem.count <= 0;
        }

        private void TryPullFromUpstream()
        {
            if (!(upstreamSource is IItemProvider provider)) return;

            var item = provider.PeekOutput(out int available);
            if (item == null || available <= 0) return;

            int wanted = Mathf.Min(available, maxItems - _items.Count);
            int received = provider.TryExtract(item, wanted);
            for (int i = 0; i < received; i++)
            {
                _items.Add(new ChuteItem
                {
                    item = item,
                    count = 1,
                    slideProgress = 0f
                });
            }
        }

        private void EnsureVisuals()
        {
            if (transform.Find("Generated_SquareRim") != null) return;
            if (transform.Find("RuntimeFallbackVisuals") != null) return;

            var visualRoot = new GameObject("RuntimeFallbackVisuals");
            visualRoot.transform.SetParent(transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "ChuteBody";
            body.transform.SetParent(visualRoot.transform, false);
            body.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            Destroy(body.GetComponent<Collider>());
            body.GetComponent<MeshRenderer>().sharedMaterial = GetFallbackShellMaterial();

            var channel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            channel.name = "ChuteChannel";
            channel.transform.SetParent(visualRoot.transform, false);
            channel.transform.localScale = new Vector3(0.45f, 0.95f, 0.45f);
            Destroy(channel.GetComponent<Collider>());
            channel.GetComponent<MeshRenderer>().sharedMaterial = GetFallbackChannelMaterial();
        }

        private void UpdateItemVisuals()
        {
            while (_itemVisuals.Count < _items.Count)
            {
                _itemVisuals.Add(CreateItemVisual());
                _visualActive.Add(false);
            }

            for (int i = 0; i < _itemVisuals.Count; i++)
            {
                if (i < _items.Count)
                {
                    var chuteItem = _items[i];
                    var visual = _itemVisuals[i];
                    visual.position = GetWorldPosition(chuteItem.slideProgress);
                    visual.gameObject.SetActive(true);
                    _visualActive[i] = true;
                    SetItemColor(visual, chuteItem.item);
                }
                else if (_visualActive[i])
                {
                    _itemVisuals[i].gameObject.SetActive(false);
                    _visualActive[i] = false;
                }
            }
        }

        private Vector3 GetWorldPosition(float progress)
        {
            float t = Mathf.Clamp01(progress);
            Vector3 localPosition;
            switch (shape)
            {
                case ChuteShape.Corner:
                    {
                        Vector3 start = new(0f, 0.85f, -0.3f);
                        Vector3 control = new(0f, 0.3f, 0f);
                        Vector3 end = new(0.3f, -0.2f, 0f);
                        float inverse = 1f - t;
                        localPosition = inverse * inverse * start + 2f * inverse * t * control + t * t * end;
                        break;
                    }
                case ChuteShape.Spiral:
                    {
                        float angle = t * Mathf.PI * 3f;
                        localPosition = new Vector3(Mathf.Cos(angle) * 0.22f, Mathf.Lerp(0.85f, -0.2f, t), Mathf.Sin(angle) * 0.22f);
                        break;
                    }
                default:
                    localPosition = new Vector3(0f, Mathf.Lerp(0.85f, -0.2f, t), 0f);
                    break;
            }
            return transform.TransformPoint(localPosition);
        }

        private Transform CreateItemVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ChuteItemVisual";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.2f;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = GetSharedItemMaterial();
            go.SetActive(false);
            return go.transform;
        }

        private void SetItemColor(Transform visual, ItemDefinition item)
        {
            if (visual == null || item == null) return;
            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            renderer.GetPropertyBlock(_itemProperties);
            _itemProperties.SetColor(BaseColorId, item.iconTint);
            _itemProperties.SetColor(ColorId, item.iconTint);
            renderer.SetPropertyBlock(_itemProperties);
        }

        private static Material GetFallbackShellMaterial()
        {
            if (_fallbackShellMaterial != null) return _fallbackShellMaterial;
            _fallbackShellMaterial = new Material(GetRequiredShader()) { color = new Color(0.30f, 0.33f, 0.38f) };
            _fallbackShellMaterial.SetFloat("_Metallic", 0.6f);
            _fallbackShellMaterial.SetFloat("_Smoothness", 0.4f);
            return _fallbackShellMaterial;
        }

        private static Material GetFallbackChannelMaterial()
        {
            if (_fallbackChannelMaterial != null) return _fallbackChannelMaterial;
            _fallbackChannelMaterial = new Material(GetRequiredShader()) { color = new Color(0.12f, 0.13f, 0.16f) };
            _fallbackChannelMaterial.SetFloat("_Metallic", 0.2f);
            _fallbackChannelMaterial.SetFloat("_Smoothness", 0.8f);
            return _fallbackChannelMaterial;
        }

        private static Material GetSharedItemMaterial()
        {
            if (_sharedItemMaterial != null) return _sharedItemMaterial;
            _sharedItemMaterial = new Material(GetRequiredShader()) { color = Color.white };
            _sharedItemMaterial.SetFloat("_Metallic", 0.15f);
            _sharedItemMaterial.SetFloat("_Smoothness", 0.3f);
            return _sharedItemMaterial;
        }

        private static Shader GetRequiredShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Hidden/InternalErrorShader");
        }
    }
}
