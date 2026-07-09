// Assets/Scripts/VoxelEngine/Simulation/BeltVisualController.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    [RequireComponent(typeof(ConveyorBelt))]
    public class BeltVisualController : MonoBehaviour
    {
        private ConveyorBelt _belt;
        private Material _beltMaterial;
        private MeshRenderer _beltRenderer;
        private float _uvOffset;

        private readonly List<Transform> _itemVisuals = new(16);
        private readonly List<bool> _visualActive = new(16);

        [Header("Visual Settings")]
        public float uvScrollSpeed = 2f;
        public Color beltColor = new(0.15f, 0.16f, 0.18f, 1f);
        public Color railColor = new(0.35f, 0.38f, 0.42f, 1f);

        private static Material _sharedBeltMat;
        private static Material _sharedRailMat;

        private GameObject _meshRoot;

        public void Initialize(ConveyorBelt belt)
        {
            _belt = belt;
            RebuildMesh();
        }

        private void Update()
        {
            if (_beltMaterial != null)
            {
                _uvOffset += uvScrollSpeed * Time.deltaTime;
                if (_uvOffset > 100f) _uvOffset -= 100f;
                _beltMaterial.mainTextureOffset = new Vector2(0f, _uvOffset);
            }
        }

        public void RebuildMesh()
        {
            if (_meshRoot != null) Destroy(_meshRoot);
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

        private void BuildStraightMesh()
        {
            var beltGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltGo.name = "BeltSurface";
            beltGo.transform.SetParent(_meshRoot.transform, false);
            beltGo.transform.localPosition = Vector3.up * 0.48f;
            beltGo.transform.localScale = new Vector3(0.85f, 0.04f, 1.0f);
            Destroy(beltGo.GetComponent<Collider>());

            _beltRenderer = beltGo.GetComponent<MeshRenderer>();
            _beltMaterial = new Material(GetSharedBeltMaterial());
            _beltMaterial.color = beltColor;
            _beltRenderer.material = _beltMaterial;

            CreateRail(new Vector3(0.45f, 0.50f, 0), new Vector3(0.06f, 0.08f, 1.0f));
            CreateRail(new Vector3(-0.45f, 0.50f, 0), new Vector3(0.06f, 0.08f, 1.0f));
        }

        private void BuildCornerMesh()
        {
            // Simple corner mesh using two boxes for now
            bool isRight = _belt.entryDirection == Vector3.left;
            
            var beltA = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltA.transform.SetParent(_meshRoot.transform, false);
            beltA.transform.localPosition = new Vector3(0, 0.48f, -0.25f);
            beltA.transform.localScale = new Vector3(0.85f, 0.04f, 0.5f);
            Destroy(beltA.GetComponent<Collider>());

            var beltB = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltB.transform.SetParent(_meshRoot.transform, false);
            float xPos = isRight ? 0.25f : -0.25f;
            beltB.transform.localPosition = new Vector3(xPos, 0.48f, 0);
            beltB.transform.localScale = new Vector3(0.5f, 0.04f, 0.85f);
            Destroy(beltB.GetComponent<Collider>());

            _beltRenderer = beltA.GetComponent<MeshRenderer>();
            _beltMaterial = new Material(GetSharedBeltMaterial());
            _beltMaterial.color = beltColor;
            beltA.GetComponent<MeshRenderer>().material = _beltMaterial;
            beltB.GetComponent<MeshRenderer>().material = _beltMaterial;

            // Rails
            CreateRail(new Vector3(isRight ? -0.45f : 0.45f, 0.5f, 0), new Vector3(0.06f, 0.08f, 1f));
            CreateRail(new Vector3(0, 0.5f, isRight ? 0.45f : -0.45f), new Vector3(1f, 0.08f, 0.06f));
        }

        private void BuildRampMesh(bool up)
        {
            var beltGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltGo.transform.SetParent(_meshRoot.transform, false);
            float angle = up ? -26.5f : 26.5f;
            beltGo.transform.localPosition = new Vector3(0, 0.73f, 0);
            beltGo.transform.localScale = new Vector3(0.85f, 0.04f, 1.12f);
            beltGo.transform.localRotation = Quaternion.Euler(angle, 0, 0);
            Destroy(beltGo.GetComponent<Collider>());

            _beltRenderer = beltGo.GetComponent<MeshRenderer>();
            _beltMaterial = new Material(GetSharedBeltMaterial());
            _beltMaterial.color = beltColor;
            _beltRenderer.material = _beltMaterial;

            CreateRail(new Vector3(0.45f, 0.75f, 0), new Vector3(0.06f, 0.08f, 1.12f), Quaternion.Euler(angle, 0, 0));
            CreateRail(new Vector3(-0.45f, 0.75f, 0), new Vector3(0.06f, 0.08f, 1.12f), Quaternion.Euler(angle, 0, 0));
        }

        private void CreateRail(Vector3 localPos, Vector3 scale, Quaternion rotation = default)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.transform.SetParent(_meshRoot.transform, false);
            rail.transform.localPosition = localPos;
            rail.transform.localScale = scale;
            rail.transform.localRotation = rotation;
            Destroy(rail.GetComponent<Collider>());
            rail.GetComponent<MeshRenderer>().material = GetSharedRailMaterial();
        }

        public void UpdateVisuals(IReadOnlyList<ConveyorItem> items)
        {
            while (_itemVisuals.Count < items.Count)
            {
                var vis = CreateItemVisual();
                _itemVisuals.Add(vis);
                _visualActive.Add(false);
            }

            for (int i = 0; i < _itemVisuals.Count; i++)
            {
                if (i < items.Count)
                {
                    var ci = items[i];
                    Vector3 worldPos = _belt.GetWorldPosition(ci.progress, ci.lateralOffset);
                    _itemVisuals[i].position = worldPos;
                    _itemVisuals[i].gameObject.SetActive(true);
                    _visualActive[i] = true;
                    UpdateItemVisualColor(_itemVisuals[i], ci.item);
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
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.22f;
            Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            go.SetActive(false);
            return go.transform;
        }

        private void UpdateItemVisualColor(Transform visual, ItemDefinition item)
        {
            if (item == null) return;
            var mr = visual.GetComponent<MeshRenderer>();
            if (mr == null || mr.material == null) return;
            mr.material.color = item.iconTint;
        }

        private static Material GetSharedBeltMaterial()
        {
            if (_sharedBeltMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _sharedBeltMat = new Material(shader) { color = new Color(0.15f, 0.16f, 0.18f) };
                _sharedBeltMat.SetFloat("_Metallic", 0.3f);
                _sharedBeltMat.SetFloat("_Smoothness", 0.2f);
            }
            return _sharedBeltMat;
        }

        private static Material GetSharedRailMaterial()
        {
            if (_sharedRailMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _sharedRailMat = new Material(shader) { color = new Color(0.35f, 0.38f, 0.42f) };
                _sharedRailMat.SetFloat("_Metallic", 0.7f);
                _sharedRailMat.SetFloat("_Smoothness", 0.5f);
            }
            return _sharedRailMat;
        }
    }
}
