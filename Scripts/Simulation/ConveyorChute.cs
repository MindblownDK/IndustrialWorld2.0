// Assets/Scripts/VoxelEngine/Simulation/ConveyorChute.cs
//
// Vertical item transport with authored-prefab reuse, fallback visuals, and
// pooled item representations. Existing setup-generated visuals are never
// duplicated at runtime.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Transport;

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
        private readonly List<GameObject> _authoredVisuals = new(12);
        private MaterialPropertyBlock _itemProperties;
        private GameObject _runtimeShapeRoot;
        private float _scanTimer;
        private float _pullTimer;

        public IReadOnlyList<ChuteItem> Items => _items;

        private void Awake()
        {
            _itemProperties = new MaterialPropertyBlock();
            CacheAuthoredVisuals();
            EnsureVisuals();
        }

        internal void SetBuildShape(ChuteShape buildShape)
        {
            if (shape == buildShape) return;
            shape = buildShape;
            EnsureVisuals();
            ScanConnections();
        }

        private void OnEnable()
        {
            ScanConnections();
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
            const float connectionOffset = 1f;
            downstreamTarget = FindEndpointAt(transform.position - transform.up * connectionOffset, provider: false);
            upstreamSource = FindEndpointAt(transform.position + transform.up * connectionOffset, provider: true);
        }

        private MonoBehaviour FindEndpointAt(Vector3 worldPosition, bool provider)
        {
            const float connectionRadius = 1.05f;
            var hits = Physics.OverlapSphere(worldPosition, connectionRadius);
            MonoBehaviour nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var col in hits)
            {
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var behaviours = col.GetComponentsInParent<MonoBehaviour>(true);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null || behaviour == this) continue;
                    if (!CanUseEndpoint(behaviour, provider)) continue;

                    float distance = (behaviour.transform.position - worldPosition).sqrMagnitude;
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    nearest = behaviour;
                }
            }
            return nearest;
        }

        private bool CanUseEndpoint(MonoBehaviour behaviour, bool provider)
        {
            if (provider && behaviour is IItemProvider) return true;
            if (!provider && behaviour is IItemConsumer) return true;

            // Port-aware machines must expose the face that physically points at
            // the chute. This takes priority over their broader inventory interface.
            if (behaviour is IItemPortHost portHost && portHost.PortConfig != null)
            {
                PortDirection direction = provider ? PortDirection.Output : PortDirection.Input;
                if (!portHost.PortConfig.GetMatchingFace(transform.position, direction).HasValue) return false;
                var containers = portHost.GetPortContainers();
                if (containers == null) return false;
                foreach (var port in containers)
                {
                    if (port.Container == null) continue;
                    if (provider && port.CanOutput) return true;
                    if (!provider && port.CanInput) return true;
                }
                return false;
            }

            if (behaviour is IInventoryInterface inventory)
            {
                if (provider && inventory.HasOutputReady && inventory.GetOutputContainer() != null) return true;
                if (!provider && inventory.CanAcceptInput && inventory.GetInputContainer() != null) return true;
            }

            return false;
        }

        private bool TryHandOff(ref ChuteItem chuteItem)
        {
            int accepted = PushToEndpoint(downstreamTarget, chuteItem.item, chuteItem.count);
            chuteItem.count -= accepted;
            return chuteItem.count <= 0;
        }

        private void TryPullFromUpstream()
        {
            if (upstreamSource == null) return;

            if (upstreamSource is IItemProvider provider)
            {
                var item = provider.PeekOutput(out int available);
                if (item == null || available <= 0) return;

                int wanted = Mathf.Min(available, maxItems - _items.Count);
                int received = provider.TryExtract(item, wanted);
                AddReceivedItems(item, received);
                return;
            }

            ItemContainer container = null;
            if (upstreamSource is IInventoryInterface inventory && inventory.HasOutputReady)
                container = inventory.GetOutputContainer();
            else if (upstreamSource is IItemPortHost portHost)
                container = FindPortContainer(portHost, canOutput: true);

            if (container == null) return;
            for (int i = 0; i < container.Size; i++)
            {
                var stack = container.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item == null) continue;

                int wanted = Mathf.Min(stack.count, maxItems - _items.Count);
                int received = container.Remove(stack.item, wanted);
                AddReceivedItems(stack.item, received);
                return;
            }
        }

        private void AddReceivedItems(ItemDefinition item, int count)
        {
            if (item == null || count <= 0) return;
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
        }

        private int PushToEndpoint(MonoBehaviour endpoint, ItemDefinition item, int count)
        {
            if (endpoint == null || item == null || count <= 0) return 0;

            if (endpoint is IItemConsumer consumer)
            {
                int capacity = consumer.GetInputCapacity(item);
                return capacity > 0 ? consumer.TryInsert(item, Mathf.Min(capacity, count)) : 0;
            }

            var routing = endpoint.GetComponent<ItemPortRouting>();
            if (routing != null)
                return routing.TryAcceptFromPipe(transform.position, item, count);

            ItemContainer container = null;
            if (endpoint is IInventoryInterface inventory && inventory.CanAcceptInput)
                container = inventory.GetInputContainer();
            else if (endpoint is IItemPortHost portHost)
                container = FindPortContainer(portHost, canInput: true);

            if (container == null) return 0;
            int before = container.CountOf(item);
            container.Insert(new ItemStack(item, count));
            return container.CountOf(item) - before;
        }

        private static ItemContainer FindPortContainer(IItemPortHost host, bool canInput = false, bool canOutput = false)
        {
            if (host == null) return null;
            var containers = host.GetPortContainers();
            if (containers == null) return null;
            foreach (var port in containers)
            {
                if (port.Container == null) continue;
                if (canInput && port.CanInput) return port.Container;
                if (canOutput && port.CanOutput) return port.Container;
            }
            return null;
        }

        private void CacheAuthoredVisuals()
        {
            _authoredVisuals.Clear();
            var children = GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                if (child == null || child == transform) continue;
                if (child.name.StartsWith("Generated_", System.StringComparison.Ordinal))
                    _authoredVisuals.Add(child.gameObject);
            }
        }

        private void SetAuthoredVisualsActive(bool active)
        {
            for (int i = 0; i < _authoredVisuals.Count; i++)
            {
                var visual = _authoredVisuals[i];
                if (visual != null) visual.SetActive(active);
            }
        }

        private void EnsureVisuals()
        {
            if (_runtimeShapeRoot != null)
            {
                Destroy(_runtimeShapeRoot);
                _runtimeShapeRoot = null;
            }

            if (shape == ChuteShape.Straight)
            {
                SetAuthoredVisualsActive(true);
                if (_authoredVisuals.Count > 0)
                {
                    var indicatorTransform = transform.Find("Generated_DownArrow");
                    var indicatorRenderer = indicatorTransform != null ? indicatorTransform.GetComponent<Renderer>() : null;
                    if (indicatorRenderer != null)
                        GetComponent<FactoryStatusIndicator>()?.SetRuntimeRenderer(indicatorRenderer);
                    return;
                }
                BuildFallbackVisuals();
                return;
            }

            SetAuthoredVisualsActive(false);
            BuildRuntimeShapeVisuals();
        }

        private void BuildFallbackVisuals()
        {
            var existing = transform.Find("RuntimeFallbackVisuals");
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                return;
            }

            var visualRoot = new GameObject("RuntimeFallbackVisuals");
            visualRoot.transform.SetParent(transform, false);
            _authoredVisuals.Add(visualRoot);

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

        private void BuildRuntimeShapeVisuals()
        {
            _runtimeShapeRoot = new GameObject(shape == ChuteShape.Corner
                ? "RuntimeCornerChute"
                : "RuntimeSpiralChute");
            _runtimeShapeRoot.transform.SetParent(transform, false);

            int segmentCount = shape == ChuteShape.Spiral ? 22 : 12;
            Vector3 previous = GetLocalPathPosition(0f);
            for (int i = 1; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                Vector3 current = GetLocalPathPosition(t);
                CreateRuntimeChuteSegment(previous, current, i == segmentCount / 2);
                previous = current;
            }

            CreateRuntimeCollar("TopCollar", GetLocalPathPosition(0f));
            CreateRuntimeCollar("BottomCollar", GetLocalPathPosition(1f));
        }

        private void CreateRuntimeChuteSegment(Vector3 start, Vector3 end, bool statusSegment)
        {
            Vector3 tangent = end - start;
            float length = tangent.magnitude;
            if (length < 0.001f) return;
            Vector3 direction = tangent / length;
            Vector3 center = (start + end) * 0.5f;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward, direction);

            var channel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            channel.name = "ChuteChannelSegment";
            channel.transform.SetParent(_runtimeShapeRoot.transform, false);
            channel.transform.localPosition = center;
            channel.transform.localRotation = rotation;
            channel.transform.localScale = new Vector3(0.56f, 0.07f, length + 0.025f);
            Destroy(channel.GetComponent<Collider>());
            channel.GetComponent<MeshRenderer>().sharedMaterial = GetFallbackChannelMaterial();

            Vector3 side = Vector3.Cross(direction, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();
            CreateRuntimeRail(center + side * 0.31f, rotation, length);
            CreateRuntimeRail(center - side * 0.31f, rotation, length);

            if (!statusSegment) return;
            var status = GameObject.CreatePrimitive(PrimitiveType.Cube);
            status.name = "Runtime_ChuteStatusLine";
            status.transform.SetParent(_runtimeShapeRoot.transform, false);
            status.transform.localPosition = center + Vector3.up * 0.055f;
            status.transform.localRotation = rotation;
            status.transform.localScale = new Vector3(0.05f, 0.025f, length + 0.02f);
            Destroy(status.GetComponent<Collider>());
            var renderer = status.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetFallbackShellMaterial();
            GetComponent<FactoryStatusIndicator>()?.SetRuntimeRenderer(renderer);
        }

        private void CreateRuntimeRail(Vector3 position, Quaternion rotation, float length)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "ChuteGuardRail";
            rail.transform.SetParent(_runtimeShapeRoot.transform, false);
            rail.transform.localPosition = position;
            rail.transform.localRotation = rotation;
            rail.transform.localScale = new Vector3(0.055f, 0.12f, length + 0.03f);
            Destroy(rail.GetComponent<Collider>());
            rail.GetComponent<MeshRenderer>().sharedMaterial = GetFallbackShellMaterial();
        }

        private void CreateRuntimeCollar(string objectName, Vector3 position)
        {
            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = objectName;
            collar.transform.SetParent(_runtimeShapeRoot.transform, false);
            collar.transform.localPosition = position;
            collar.transform.localScale = new Vector3(0.38f, 0.045f, 0.38f);
            Destroy(collar.GetComponent<Collider>());
            collar.GetComponent<MeshRenderer>().sharedMaterial = GetFallbackShellMaterial();
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
            return transform.TransformPoint(GetLocalPathPosition(progress));
        }

        private Vector3 GetLocalPathPosition(float progress)
        {
            float t = Mathf.Clamp01(progress);
            switch (shape)
            {
                case ChuteShape.Corner:
                    {
                        Vector3 start = new(0f, 0.85f, 0f);
                        Vector3 control = new(0.48f, 0.30f, 0.36f);
                        Vector3 end = new(0f, -0.2f, 0f);
                        float inverse = 1f - t;
                        return inverse * inverse * start + 2f * inverse * t * control + t * t * end;
                    }
                case ChuteShape.Spiral:
                    {
                        float angle = t * Mathf.PI * 4f;
                        float radius = Mathf.Sin(t * Mathf.PI) * 0.28f;
                        return new Vector3(
                            Mathf.Cos(angle) * radius,
                            Mathf.Lerp(0.85f, -0.2f, t),
                            Mathf.Sin(angle) * radius);
                    }
                default:
                    return new Vector3(0f, Mathf.Lerp(0.85f, -0.2f, t), 0f);
            }
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
            if (_itemProperties == null) _itemProperties = new MaterialPropertyBlock();

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
