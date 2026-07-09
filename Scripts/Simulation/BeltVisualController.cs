// Assets/Scripts/VoxelEngine/Simulation/BeltVisualController.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — BELT VISUAL CONTROLLER                      ║
// ║  Generates and animates the conveyor belt mesh + item sprites.  ║
// ║  Belt surface scrolls via UV offset; items are positioned on    ║
//  ║  top using the parent belt's GetWorldPosition().               ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Handles the visual representation of a conveyor belt:
    ///   1. Procedural belt mesh (flat quad with scrolling UV)
    ///   2. Item sprites/models positioned on top of the belt
    ///   3. Side rails (thin metal strips)
    /// </summary>
    [RequireComponent(typeof(ConveyorBelt))]
    public class BeltVisualController : MonoBehaviour
    {
        private ConveyorBelt _belt;
        private Material _beltMaterial;
        private MeshRenderer _beltRenderer;
        private float _uvOffset;

        // Item visual pool — reuse GameObjects for performance.
        private readonly List<Transform> _itemVisuals = new(16);
        private readonly List<bool> _visualActive = new(16);

        [Header("Visual Settings")]
        [Tooltip("Speed of the scrolling belt texture UV offset.")]
        public float uvScrollSpeed = 2f;

        [Tooltip("Colour of the belt surface.")]
        public Color beltColor = new(0.15f, 0.16f, 0.18f, 1f);

        [Tooltip("Colour of the metal side rails.")]
        public Color railColor = new(0.35f, 0.38f, 0.42f, 1f);

        // Cached material to avoid creating new ones every frame.
        private static Material _sharedBeltMat;
        private static Material _sharedRailMat;

        public void Initialize(ConveyorBelt belt)
        {
            _belt = belt;
            BuildMesh();
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

        private void BuildMesh()
        {
            // Belt surface — flat quad slightly below item riding height.
            var beltGo = new GameObject("BeltSurface");
            beltGo.transform.SetParent(transform, false);
            beltGo.transform.localPosition = Vector3.up * 0.48f;
            beltGo.transform.localScale = new Vector3(0.85f, 0.04f, 0.95f);

            var beltCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beltCube.transform.SetParent(beltGo.transform, false);
            beltCube.transform.localPosition = Vector3.zero;
            beltCube.transform.localScale = Vector3.one;

            // Remove the default collider from the visual cube.
            var col = beltCube.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _beltRenderer = beltCube.GetComponent<MeshRenderer>();
            _beltMaterial = new Material(GetSharedBeltMaterial());
            _beltMaterial.color = beltColor;
            if (_beltMaterial.HasProperty("_BaseColor"))
                _beltMaterial.SetColor("_BaseColor", beltColor);
            _beltRenderer.material = _beltMaterial;

            // Side rails — thin metal strips on each side.
            CreateRail(Vector3.right * 0.45f);
            CreateRail(Vector3.left * 0.45f);
        }

        private void CreateRail(Vector3 localOffset)
        {
            var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "Rail";
            rail.transform.SetParent(transform, false);
            rail.transform.localPosition = localOffset + Vector3.up * 0.50f;
            rail.transform.localScale = new Vector3(0.06f, 0.08f, 0.95f);

            var col = rail.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = rail.GetComponent<MeshRenderer>();
            mr.material = GetSharedRailMaterial();
        }

        /// <summary>
        /// Called each frame by ConveyorBelt.Update() to reposition item visuals.
        /// </summary>
        public void UpdateVisuals(IReadOnlyList<ConveyorItem> items)
        {
            // Ensure we have enough visual objects.
            while (_itemVisuals.Count < items.Count)
            {
                var vis = CreateItemVisual();
                _itemVisuals.Add(vis);
                _visualActive.Add(false);
            }

            // Position active items, hide extras.
            for (int i = 0; i < _itemVisuals.Count; i++)
            {
                if (i < items.Count)
                {
                    var ci = items[i];
                    Vector3 worldPos = _belt.GetWorldPosition(ci.progress, ci.lateralOffset);
                    _itemVisuals[i].position = worldPos;
                    _itemVisuals[i].gameObject.SetActive(true);
                    _visualActive[i] = true;

                    // Tint the visual based on item colour.
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
            go.name = "ItemVisual";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.22f;

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = Color.white;
            mr.material = mat;

            go.SetActive(false);
            return go.transform;
        }

        private void UpdateItemVisualColor(Transform visual, ItemDefinition item)
        {
            if (item == null) return;
            var mr = visual.GetComponent<MeshRenderer>();
            if (mr == null || mr.material == null) return;
            mr.material.color = item.iconTint;
            if (mr.material.HasProperty("_BaseColor"))
                mr.material.SetColor("_BaseColor", item.iconTint);
        }

        // ── Shared Materials ──────────────────────────────────────────

        private static Material GetSharedBeltMaterial()
        {
            if (_sharedBeltMat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _sharedBeltMat = new Material(shader);
                _sharedBeltMat.color = new Color(0.15f, 0.16f, 0.18f);
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
                _sharedRailMat = new Material(shader);
                _sharedRailMat.color = new Color(0.35f, 0.38f, 0.42f);
                _sharedRailMat.SetFloat("_Metallic", 0.7f);
                _sharedRailMat.SetFloat("_Smoothness", 0.5f);
            }
            return _sharedRailMat;
        }
    }
}
