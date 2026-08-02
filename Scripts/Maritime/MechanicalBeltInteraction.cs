// Assets/Scripts/VoxelEngine/Maritime/MechanicalBeltInteraction.cs
//
// Player-facing two-click workflow for Mechanical Belt items:
// RMB shaft → RMB parallel shaft = consume one belt and create a live belt bus.
// Shift+RMB a shaft removes every attached belt and returns those belt items.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    /// <summary>Lightweight runtime interaction state, owned by PlayerInteractionTool.</summary>
    public sealed class MechanicalBeltInteraction
    {
        private const float SelectionTimeoutSeconds = 12f;

        private GridBlock _firstShaft;
        private float _selectedAt;
        private GameObject _selectionIndicator;
        private LineRenderer _previewLine;

        private static Material s_previewMaterial;
        private static Material s_selectionMaterial;

        public bool HasSelection => _firstShaft != null;

        public void OnUse(RaycastHit hit, Inventory inventory, MechanicalBeltItem beltItem, bool shiftHeld)
        {
            if (inventory == null || beltItem == null) return;

            var target = ResolveEligibleShaft(hit);
            if (shiftHeld)
            {
                CancelSelection(false);
                RemoveAttachedBelts(target, inventory, beltItem);
                return;
            }

            if (target == null)
            {
                bool wasSelecting = _firstShaft != null;
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    wasSelecting ? "Mechanical belt cancelled" : "Mechanical belt",
                    "Aim at a drive shaft or watertight shaft housing.",
                    beltItem.icon,
                    wasSelecting ? Color.gray : new Color(1f, 0.65f, 0.22f));
                return;
            }

            if (_firstShaft == null)
            {
                _firstShaft = target;
                _selectedAt = Time.time;
                CreateSelectionIndicator(target.transform.position);
                EnsurePreviewLine();
                VoxelEngine.UI.BuildFeedbackHud.Show(
                    "Belt pulley selected",
                    "Right-click a parallel shaft on this grid to route the mechanical belt.",
                    beltItem.icon,
                    new Color(0.95f, 0.67f, 0.16f));
                return;
            }

            if (_firstShaft == target)
            {
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt cancelled", "Choose a different shaft pulley.", beltItem.icon, Color.gray);
                return;
            }

            var first = _firstShaft;
            var grid = first != null ? first.Grid : null;
            if (grid == null || target.Grid != grid)
            {
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show("Belt link rejected", "A mechanical belt can only link shafts on the same grid.", beltItem.icon, Color.red);
                return;
            }

            var network = MechanicalBeltNetwork.GetOrAdd(grid);
            string reason = null;
            if (network == null || !network.CanCreateLink(first, target, beltItem, out reason))
            {
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show("Belt link rejected", reason ?? "Unable to create that belt.", beltItem.icon, Color.red);
                return;
            }

            // Spend only after the complete physical / topology validation passes.
            if (inventory.container.Remove(beltItem, 1) != 1)
            {
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt", "No belt item was available to install.", beltItem.icon, Color.red);
                return;
            }

            if (!network.TryCreateLink(first, target, beltItem, out reason))
            {
                ReturnBelts(inventory, beltItem, 1);
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show("Belt link rejected", reason ?? "Unable to create that belt.", beltItem.icon, Color.red);
                return;
            }

            CancelSelection(false);
            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Mechanical belt installed",
                "The belt is live. Place parallel shafts through its run for additional take-off outputs.",
                beltItem.icon,
                new Color(0.30f, 0.92f, 0.63f));
        }

        /// <summary>Allows RMB in open air to cleanly abandon the first selected pulley.</summary>
        public void OnMiss(MechanicalBeltItem beltItem)
        {
            if (_firstShaft == null) return;
            CancelSelection(false);
            VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt cancelled", "No second shaft targeted.",
                beltItem != null ? beltItem.icon : null, Color.gray);
        }

        /// <summary>Keeps the selection preview responsive and clears stale selections safely.</summary>
        public void Tick(Inventory inventory, Camera camera)
        {
            if (_firstShaft == null)
            {
                DestroyPreviewObjects();
                return;
            }

            var active = inventory != null ? inventory.ActiveStack : null;
            if (active == null || active.IsEmpty || active.item is not MechanicalBeltItem)
            {
                CancelSelection(false);
                return;
            }
            if (Time.time - _selectedAt > SelectionTimeoutSeconds)
            {
                CancelSelection(false);
                VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt cancelled", "Pulley selection timed out.", active.item.icon, Color.gray);
                return;
            }

            if (_selectionIndicator != null)
                _selectionIndicator.transform.position = _firstShaft.transform.position;

            EnsurePreviewLine();
            if (_previewLine == null) return;
            _previewLine.SetPosition(0, _firstShaft.transform.position);

            Vector3 end = _firstShaft.transform.position;
            if (camera != null)
            {
                Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                end = Physics.Raycast(ray, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : ray.GetPoint(8f);
            }
            _previewLine.SetPosition(1, end);
        }

        public void CancelSelection(bool showFeedback, MechanicalBeltItem beltItem = null)
        {
            bool hadSelection = _firstShaft != null;
            _firstShaft = null;
            _selectedAt = 0f;
            DestroyPreviewObjects();
            if (showFeedback && hadSelection)
                VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt cancelled", "Pulley selection cleared.",
                    beltItem != null ? beltItem.icon : null, Color.gray);
        }

        private static GridBlock ResolveEligibleShaft(RaycastHit hit)
        {
            var block = hit.collider != null ? hit.collider.GetComponentInParent<GridBlock>() : null;
            return MechanicalBeltNetwork.IsBeltEligibleShaft(block) ? block : null;
        }

        private void RemoveAttachedBelts(GridBlock shaft, Inventory inventory, MechanicalBeltItem beltItem)
        {
            if (shaft == null || shaft.Grid == null)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt", "Shift-right-click a shaft to remove its attached belts.", beltItem.icon, Color.gray);
                return;
            }

            var network = shaft.Grid.GetComponent<MechanicalBeltNetwork>();
            int removed = network != null ? network.RemoveLinksAttachedTo(shaft) : 0;
            if (removed <= 0)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Mechanical belt", "That shaft has no attached belts.", beltItem.icon, Color.gray);
                return;
            }

            ReturnBelts(inventory, beltItem, removed);
            VoxelEngine.UI.BuildFeedbackHud.Show(
                "Mechanical belt removed",
                $"Returned {removed} belt{(removed == 1 ? string.Empty : "s")} to your inventory.",
                beltItem.icon,
                new Color(0.95f, 0.67f, 0.16f));
        }

        private static void ReturnBelts(Inventory inventory, MechanicalBeltItem beltItem, int count)
        {
            if (inventory == null || beltItem == null || count <= 0) return;
            var leftover = inventory.container.Insert(new ItemStack(beltItem, count));
            if (leftover != null && !leftover.IsEmpty)
                DroppedItem.Spawn(leftover, inventory.transform.position + Vector3.up * 0.6f, Vector3.up);
        }

        private void EnsurePreviewLine()
        {
            if (_previewLine != null) return;
            var go = new GameObject("MechanicalBeltPreview")
            {
                hideFlags = HideFlags.DontSave
            };
            _previewLine = go.AddComponent<LineRenderer>();
            _previewLine.positionCount = 2;
            _previewLine.useWorldSpace = true;
            _previewLine.startWidth = 0.045f;
            _previewLine.endWidth = 0.025f;
            _previewLine.startColor = new Color(1f, 0.72f, 0.16f, 0.95f);
            _previewLine.endColor = new Color(0.25f, 0.86f, 1f, 0.78f);
            _previewLine.numCapVertices = 4;
            _previewLine.material = PreviewMaterial;
        }

        private void CreateSelectionIndicator(Vector3 position)
        {
            if (_selectionIndicator != null) Object.Destroy(_selectionIndicator);
            _selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _selectionIndicator.name = "MechanicalBeltPulleySelection";
            _selectionIndicator.hideFlags = HideFlags.DontSave;
            _selectionIndicator.transform.position = position;
            _selectionIndicator.transform.localScale = Vector3.one * 0.28f;
            var collider = _selectionIndicator.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = _selectionIndicator.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = SelectionMaterial;
        }

        private void DestroyPreviewObjects()
        {
            if (_previewLine != null)
            {
                Object.Destroy(_previewLine.gameObject);
                _previewLine = null;
            }
            if (_selectionIndicator != null)
            {
                Object.Destroy(_selectionIndicator);
                _selectionIndicator = null;
            }
        }

        private static Material PreviewMaterial => s_previewMaterial ??= CreateRuntimeMaterial(
            "MechanicalBeltPreview", new Color(1f, 0.70f, 0.16f), new Color(0.42f, 0.16f, 0.01f));

        private static Material SelectionMaterial => s_selectionMaterial ??= CreateRuntimeMaterial(
            "MechanicalBeltSelection", new Color(0.98f, 0.68f, 0.16f), new Color(0.55f, 0.24f, 0.02f));

        private static Material CreateRuntimeMaterial(string name, Color color, Color emission)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;
            var material = new Material(shader)
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                color = color
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission);
            }
            return material;
        }
    }
}
