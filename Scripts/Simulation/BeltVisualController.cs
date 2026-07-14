using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    [RequireComponent(typeof(ConveyorBelt))]
    public class BeltVisualController : MonoBehaviour
    {
        [Header("Visual Settings")]
        public float uvScrollSpeed = 2f;
        public Color beltColor = new(0.15f, 0.16f, 0.18f, 1f);
        public Color railColor = new(0.35f, 0.38f, 0.42f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static Material _sharedBeltMaterial;
        private static Material _sharedRailMaterial;
        private static Material _sharedItemMaterial;

        private readonly List<Transform> _itemVisuals = new(16);
        private readonly List<bool> _visualActive = new(16);
        private readonly List<GameObject> _authoredVisuals = new(16);
        private MaterialPropertyBlock _itemProperties;

        private ConveyorBelt _belt;
        private GameObject _meshRoot;
        private MeshRenderer _beltRenderer;
        private Material _beltMaterial;
        private Material _railMaterial;
        private Material _authoredSharedMaterial;
        private bool _usingAuthoredStraight;
        private float _uvOffset;

        private void Awake()
        {
            _itemProperties = new MaterialPropertyBlock();
        }

        public void Initialize(ConveyorBelt belt)
        {
            if (_itemProperties == null) _itemProperties = new MaterialPropertyBlock();
            _belt = belt;
            CacheAuthoredVisuals();
            RebuildMesh();
        }

        private void Update()
        {
            if (_beltMaterial == null) return;

            _uvOffset += uvScrollSpeed * Time.deltaTime;
            if (_uvOffset > 100f) _uvOffset -= 100f;
            _beltMaterial.mainTextureOffset = new Vector2(0f, _uvOffset);
        }

        private void OnDestroy()
        {
            ReleaseGeneratedMesh();
            ReleaseRuntimeMaterials();
        }

        public void RebuildMesh()
        {
            if (_belt == null || !isActiveAndEnabled || !gameObject.activeInHierarchy) return;

            ReleaseGeneratedMesh();
            ReleaseRuntimeMaterials();

            if (_belt.shape == ConveyorShape.Straight && TryUseAuthoredStraightVisuals())
                return;

            SetAuthoredVisualsActive(false);
            PrepareDynamicMaterials();

            _meshRoot = new GameObject("MeshRoot");
            _meshRoot.transform.SetParent(transform, false);

            switch (_belt.shape)
            {
                case ConveyorShape.Corner:
                    BuildCornerMesh();
                    break;
                case ConveyorShape.RampUp:
                    BuildRampMesh(true);
                    break;
                case ConveyorShape.RampDown:
                    BuildRampMesh(false);
                    break;
                default:
                    BuildStraightMesh();
                    break;
            }
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

        private bool TryUseAuthoredStraightVisuals()
        {
            if (_authoredVisuals.Count == 0) CacheAuthoredVisuals();
            var beltTransform = transform.Find("Generated_BeltSurface");
            var authoredRenderer = beltTransform != null ? beltTransform.GetComponent<MeshRenderer>() : null;
            if (authoredRenderer == null) return false;

            SetAuthoredVisualsActive(true);
            _beltRenderer = authoredRenderer;
            _authoredSharedMaterial = authoredRenderer.sharedMaterial;
            _usingAuthoredStraight = true;
            _beltMaterial = _authoredSharedMaterial != null
                ? new Material(_authoredSharedMaterial)
                : new Material(GetRequiredShader());
            _beltMaterial.color = beltColor;
            if (_beltMaterial.HasProperty(BaseColorId)) _beltMaterial.SetColor(BaseColorId, beltColor);
            authoredRenderer.sharedMaterial = _beltMaterial;
            return true;
        }

        private void SetAuthoredVisualsActive(bool active)
        {
            for (int i = 0; i < _authoredVisuals.Count; i++)
            {
                var visual = _authoredVisuals[i];
                if (visual != null && visual.activeSelf != active) visual.SetActive(active);
            }
        }

        private void PrepareDynamicMaterials()
        {
            _beltMaterial = new Material(GetSharedBeltMaterial()) { color = beltColor };
            if (_beltMaterial.HasProperty(BaseColorId)) _beltMaterial.SetColor(BaseColorId, beltColor);

            _railMaterial = new Material(GetSharedRailMaterial()) { color = railColor };
            if (_railMaterial.HasProperty(BaseColorId)) _railMaterial.SetColor(BaseColorId, railColor);
        }

        private void BuildStraightMesh()
        {
            CreateHorizontalBeltSegment(Vector3.forward, Vector3.zero, 1f);
        }

        private void BuildCornerMesh()
        {
            Vector3 entry = HorizontalCardinal(_belt.entryDirection, Vector3.back);
            Vector3 exit = HorizontalCardinal(_belt.exitDirection, Vector3.right);
            if (Mathf.Abs(Vector3.Dot(entry, exit)) > 0.1f) exit = Vector3.Cross(Vector3.up, entry).normalized;

            CreateHorizontalBeltSegment(-entry, entry * 0.25f, 0.5f);
            CreateHorizontalBeltSegment(exit, exit * 0.25f, 0.5f);
        }

        private void CreateHorizontalBeltSegment(Vector3 direction, Vector3 center, float length)
        {
            direction = HorizontalCardinal(direction, Vector3.forward);
            bool alongX = Mathf.Abs(direction.x) > Mathf.Abs(direction.z);

            var beltGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltGo.name = "BeltSurface";
            beltGo.transform.SetParent(_meshRoot.transform, false);
            beltGo.transform.localPosition = center + Vector3.up * 0.48f;
            beltGo.transform.localScale = alongX
                ? new Vector3(length, 0.04f, 0.85f)
                : new Vector3(0.85f, 0.04f, length);
            Destroy(beltGo.GetComponent<Collider>());

            var renderer = beltGo.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _beltMaterial;
            if (_beltRenderer == null) _beltRenderer = renderer;

            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 railScale = alongX
                ? new Vector3(length, 0.08f, 0.06f)
                : new Vector3(0.06f, 0.08f, length);
            CreateRail(center + side * 0.45f + Vector3.up * 0.50f, railScale);
            CreateRail(center - side * 0.45f + Vector3.up * 0.50f, railScale);
        }

        private void BuildRampMesh(bool up)
        {
            var beltGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltGo.name = "BeltSurface";
            beltGo.transform.SetParent(_meshRoot.transform, false);
            float angle = up ? -26.5f : 26.5f;
            beltGo.transform.localPosition = new Vector3(0f, 0.73f, 0f);
            beltGo.transform.localScale = new Vector3(0.85f, 0.04f, 1.12f);
            beltGo.transform.localRotation = Quaternion.Euler(angle, 0f, 0f);
            Destroy(beltGo.GetComponent<Collider>());

            _beltRenderer = beltGo.GetComponent<MeshRenderer>();
            _beltRenderer.sharedMaterial = _beltMaterial;

            Quaternion rotation = Quaternion.Euler(angle, 0f, 0f);
            CreateRail(new Vector3(0.45f, 0.75f, 0f), new Vector3(0.06f, 0.08f, 1.12f), rotation);
            CreateRail(new Vector3(-0.45f, 0.75f, 0f), new Vector3(0.06f, 0.08f, 1.12f), rotation);
        }

        private void CreateRail(Vector3 localPosition, Vector3 scale, Quaternion rotation = default)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "Rail";
            rail.transform.SetParent(_meshRoot.transform, false);
            rail.transform.localPosition = localPosition;
            rail.transform.localScale = scale;
            rail.transform.localRotation = rotation;
            Destroy(rail.GetComponent<Collider>());
            rail.GetComponent<MeshRenderer>().sharedMaterial = _railMaterial;
        }

        public void UpdateVisuals(IReadOnlyList<ConveyorItem> items)
        {
            while (_itemVisuals.Count < items.Count)
            {
                _itemVisuals.Add(CreateItemVisual());
                _visualActive.Add(false);
            }

            for (int i = 0; i < _itemVisuals.Count; i++)
            {
                if (i < items.Count)
                {
                    var item = items[i];
                    var visual = _itemVisuals[i];
                    visual.position = _belt.GetWorldPosition(item.progress, item.lateralOffset);
                    visual.gameObject.SetActive(true);
                    _visualActive[i] = true;
                    UpdateItemVisualColor(visual, item.item);
                }
                else if (_visualActive[i])
                {
                    _itemVisuals[i].gameObject.SetActive(false);
                    _visualActive[i] = false;
                }
            }
        }

        private Transform CreateItemVisual()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ConveyorItemVisual";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.22f;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = GetSharedItemMaterial();
            go.SetActive(false);
            return go.transform;
        }

        private void UpdateItemVisualColor(Transform visual, ItemDefinition item)
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

        private void ReleaseGeneratedMesh()
        {
            if (_meshRoot == null) return;
            Destroy(_meshRoot);
            _meshRoot = null;
        }

        private void ReleaseRuntimeMaterials()
        {
            if (_usingAuthoredStraight && _beltRenderer != null)
                _beltRenderer.sharedMaterial = _authoredSharedMaterial;

            if (_beltMaterial != null) Destroy(_beltMaterial);
            if (_railMaterial != null) Destroy(_railMaterial);

            _beltRenderer = null;
            _beltMaterial = null;
            _railMaterial = null;
            _authoredSharedMaterial = null;
            _usingAuthoredStraight = false;
        }

        private static Vector3 HorizontalCardinal(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = fallback;
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.z))
                return new Vector3(Mathf.Sign(Mathf.Approximately(direction.x, 0f) ? 1f : direction.x), 0f, 0f);
            return new Vector3(0f, 0f, Mathf.Sign(Mathf.Approximately(direction.z, 0f) ? 1f : direction.z));
        }

        private static Shader GetRequiredShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Hidden/InternalErrorShader");
        }

        private static Material GetSharedBeltMaterial()
        {
            if (_sharedBeltMaterial != null) return _sharedBeltMaterial;
            _sharedBeltMaterial = new Material(GetRequiredShader()) { color = new Color(0.15f, 0.16f, 0.18f) };
            _sharedBeltMaterial.SetFloat("_Metallic", 0.3f);
            _sharedBeltMaterial.SetFloat("_Smoothness", 0.2f);
            return _sharedBeltMaterial;
        }

        private static Material GetSharedRailMaterial()
        {
            if (_sharedRailMaterial != null) return _sharedRailMaterial;
            _sharedRailMaterial = new Material(GetRequiredShader()) { color = new Color(0.35f, 0.38f, 0.42f) };
            _sharedRailMaterial.SetFloat("_Metallic", 0.7f);
            _sharedRailMaterial.SetFloat("_Smoothness", 0.5f);
            return _sharedRailMaterial;
        }

        private static Material GetSharedItemMaterial()
        {
            if (_sharedItemMaterial != null) return _sharedItemMaterial;
            _sharedItemMaterial = new Material(GetRequiredShader()) { color = Color.white };
            _sharedItemMaterial.SetFloat("_Metallic", 0.15f);
            _sharedItemMaterial.SetFloat("_Smoothness", 0.3f);
            return _sharedItemMaterial;
        }
    }
}
