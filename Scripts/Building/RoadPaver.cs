// Assets/Scripts/VoxelEngine/Building/RoadPaver.cs
//
// THE PAVE GESTURE — drag-to-lay, and the placement maths both the gesture and the ordinary
// block-by-block path share.
//
// Two jobs, one file, because they must never disagree:
//
//   1. STATIC PLACEMENT. Where a road cell goes, whether it may go there, and what it costs. The
//      ghost preview, the paver drag and a hand-placed road block all resolve through
//      `TryComputePose` + `EvaluateCell`, so a cell the preview shows as valid is a cell the
//      commit accepts — the same "cannot drift apart" discipline the route book and the autopilot
//      evaluator use.
//   2. THE DRAG. A held LMB lays a continuous strip. The aim moves faster than one cell per frame
//      when the player sweeps the mouse, so the paver interpolates the skipped cells instead of
//      laying dots. The interpolation is bounded (`maxCellsPerStep`) so a flick across the horizon
//      cannot lay a kilometre of road in one frame or spend the whole inventory doing it.
//
// LATTICE. A run must be continuous, and on a spherical world rounding world axes drifts between
// neighbours (the same drift `BuildSystem` warns about for machines). So a new cell anchors to an
// existing road when one is in reach: it inherits that road's frame and quantises its offset in
// THAT frame, which is what makes a dragged strip line up cell for cell around a planet. With no
// neighbour to anchor to, the first cell quantises on the local tangent frame.
//
// HEIGHT is never inherited — it is re-probed from the ground under the resolved lateral position,
// ignoring road colliders. That is what lets a strip climb a terrace as a ramp while staying
// perfectly aligned, and what stops a cell laid on top of an existing road from stacking 8 cm high
// every time.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    public sealed class RoadPaver
    {
        // ════════════════════════════════════════════════════════════════
        //  STATIC PLACEMENT — shared by the preview, the drag and the hand path
        // ════════════════════════════════════════════════════════════════

        /// <summary>True when a block item lays asphalt. Used by `BuildSystem` to route a held road
        /// block through the road snap instead of the generic machine snap.</summary>
        public static bool IsRoadBlock(BlockItem block)
        {
            if (block == null || block.placedPrefab == null) return false;
            return block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true) != null;
        }

        private static readonly List<AsphaltRoad> _anchorScratch = new List<AsphaltRoad>(8);

        /// <summary>
        /// Resolves where a road cell goes for a given aim. Returns false when there is nothing to
        /// lay on (sky, water with no bed, out of reach).
        /// </summary>
        public static bool TryComputePose(RaycastHit hit, BlockItem block, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (block == null || block.placedPrefab == null) return false;

            var template = block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true);
            float cell = template != null ? Mathf.Max(0.25f, template.cellSize) : 1f;

            // ── Anchor: an existing road cell in reach, or the collider we actually hit ──
            AsphaltRoad anchor = hit.collider != null ? hit.collider.GetComponentInParent<AsphaltRoad>() : null;
            if (anchor == null)
            {
                RoadSurfaceUtility.QueryAt(hit.point, _anchorScratch);
                float bestSqr = cell * cell * 3.2f;
                for (int i = 0; i < _anchorScratch.Count; i++)
                {
                    var candidate = _anchorScratch[i];
                    if (candidate == null) continue;
                    float sqr = (candidate.transform.position - hit.point).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; anchor = candidate; }
                }
            }

            if (anchor != null)
            {
                Transform a = anchor.transform;
                Vector3 delta = hit.point - a.position;
                float lx = Mathf.Round(Vector3.Dot(delta, a.right) / cell) * cell;
                float lz = Mathf.Round(Vector3.Dot(delta, a.forward) / cell) * cell;
                Vector3 lateral = a.position + a.right * lx + a.forward * lz;
                rotation = a.rotation;
                if (!AsphaltRoad.ProbeGround(lateral, a.up, out float groundOffset)) return false;
                position = lateral + a.up * groundOffset;
                return true;
            }

            // ── No anchor: quantise on the local tangent frame ──
            rotation = GravityProvider.GetSurfaceRotation(hit.point);
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float cx = Mathf.Round(Vector3.Dot(hit.point, right) / cell) * cell;
            float cz = Mathf.Round(Vector3.Dot(hit.point, forward) / cell) * cell;
            Vector3 flat = right * cx + forward * cz + up * Vector3.Dot(hit.point, up);
            if (!AsphaltRoad.ProbeGround(flat, up, out float offset)) return false;
            position = flat + up * offset;
            return true;
        }

        /// <summary>Grade and volume verdict for a resolved cell, plus what it costs.</summary>
        public static AsphaltRoad.GradeBand EvaluateCell(BlockItem block, Vector3 position, Quaternion rotation,
                                                         out int materialCost)
        {
            materialCost = 1;
            var template = block != null && block.placedPrefab != null
                ? block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true)
                : null;

            float cell        = template != null ? template.cellSize            : 1f;
            float maxSmooth   = template != null ? template.maxGradeSmooth      : 0.22f;
            float maxRough    = template != null ? template.maxGradeRoughness   : 0.50f;

            var band = AsphaltRoad.EvaluateSite(position, rotation, cell, maxSmooth, maxRough, out _);
            if (band == AsphaltRoad.GradeBand.Rough) materialCost = 2;
            else if (band != AsphaltRoad.GradeBand.Smooth) materialCost = 0;
            return band;
        }

        /// <summary>True when a road cell already occupies this slot, so paving again would be a
        /// no-op the player should be told about rather than charged for.</summary>
        public static bool IsCellOccupied(Vector3 position, float cellSize)
        {
            RoadSurfaceUtility.QueryAt(position, _anchorScratch);
            float limit = cellSize * 0.45f;
            for (int i = 0; i < _anchorScratch.Count; i++)
            {
                var road = _anchorScratch[i];
                if (road == null) continue;
                if ((road.transform.position - position).sqrMagnitude <= limit * limit) return true;
            }
            return false;
        }

        /// <summary>
        /// Commits one road cell. Instantiates the authored prefab, wires the `PlacedBlock` exactly
        /// as `BuildSystem.TryPlace` does, and tells the strip to reshape itself around the new cell.
        /// </summary>
        public static AsphaltRoad PlaceCell(BlockItem block, Vector3 position, Quaternion rotation)
        {
            if (block == null || block.placedPrefab == null) return null;

            var go = Object.Instantiate(block.placedPrefab, position, rotation);
            go.name = block.displayName;

            if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();

            var placed = go.GetComponent<PlacedBlock>();
            if (placed == null) placed = go.AddComponent<PlacedBlock>();
            placed.Item   = block;
            placed.Hp     = block.blockHealth;
            placed.onGrid = false;

            if (block.placedMaterial != null || block.texture != null)
            {
                var texturizer = go.AddComponent<BlockTexturizer>();
                texturizer.overrideMaterial = block.placedMaterial;
                texturizer.overrideTexture  = block.texture;
            }

            var road = go.GetComponentInChildren<AsphaltRoad>(true);
            road?.RefreshAfterPlacement();
            return road;
        }

        // ════════════════════════════════════════════════════════════════
        //  THE DRAG — instance state owned by PlayerInteractionTool
        // ════════════════════════════════════════════════════════════════

        private bool _dragging;
        private readonly List<Vector3> _targetScratch = new List<Vector3>(16);
        private readonly List<Quaternion> _rotationScratch = new List<Quaternion>(16);
        private Vector3 _lastCellPosition;
        private Quaternion _lastCellRotation;
        private bool _hasLastCell;
        private GameObject _preview;
        private Material _previewMaterial;
        private int _laidThisDrag;

        public bool IsDragging => _dragging;
        public int LaidThisDrag => _laidThisDrag;

        /// <summary>Begins a drag. Resets the interpolation anchor so the first cell of a new
        /// gesture is never interpolated from where the last gesture ended.</summary>
        public void BeginDrag()
        {
            _dragging = true;
            _hasLastCell = false;
            _laidThisDrag = 0;
        }

        public void EndDrag()
        {
            _dragging = false;
            _hasLastCell = false;
            HidePreview();
        }

        /// <summary>
        /// One frame of the drag. Lays every cell between the previous cell and the current aim,
        /// spending material as it goes and stopping the moment the player cannot afford the next
        /// cell — a drag that runs out of asphalt ends cleanly rather than half-laying a cell.
        /// </summary>
        public void TickDrag(RaycastHit hit, bool hasHit, BlockItem block, RoadPaverTool tool,
                             Inventory inventory, out int laid, out string feedback)
        {
            laid = 0;
            feedback = null;
            if (!_dragging || !hasHit || block == null || tool == null || inventory == null) { HidePreview(); return; }

            if (!TryComputePose(hit, block, out Vector3 position, out Quaternion rotation))
            {
                HidePreview();
                feedback = "Nothing to lay on";
                return;
            }

            var template = block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true);
            float cell = template != null ? template.cellSize : 1f;

            // Walk from the last laid cell to this one so a fast sweep stays a continuous strip.
            // Both lists are reused: a drag runs every frame for as long as LMB is held.
            var targets = _targetScratch;
            var rotations = _rotationScratch;
            targets.Clear();
            rotations.Clear();
            if (_hasLastCell)
            {
                InterpolateCells(_lastCellPosition, _lastCellRotation, position, rotation, cell,
                                 tool.maxCellsPerStep, targets, rotations);
            }
            else
            {
                targets.Add(position);
                rotations.Add(rotation);
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (!TryLayOne(targets[i], rotations[i], block, tool, inventory, cell, out string reason))
                {
                    feedback = reason;
                    break;
                }
                laid++;
                _lastCellPosition = targets[i];
                _lastCellRotation = rotations[i];
                _hasLastCell = true;
            }

            var previewBand = EvaluateCell(block, position, rotation, out _);
            ShowPreview(position, rotation, cell,
                        previewBand == AsphaltRoad.GradeBand.Smooth || previewBand == AsphaltRoad.GradeBand.Rough);
            if (laid > 0) feedback = null;
        }

        private bool TryLayOne(Vector3 position, Quaternion rotation, BlockItem block, RoadPaverTool tool,
                               Inventory inventory, float cell, out string reason)
        {
            reason = null;
            if (IsCellOccupied(position, cell))
            {
                // Dragging along an existing strip is not an error: it is the normal case when the
                // player widens or repairs. Say nothing and let the repair path handle it.
                TryRepair(position, cell, tool, inventory, out reason);
                return string.IsNullOrEmpty(reason);
            }

            var band = EvaluateCell(block, position, rotation, out int cost);
            if (band != AsphaltRoad.GradeBand.Smooth && band != AsphaltRoad.GradeBand.Rough)
            {
                reason = AsphaltRoad.DescribeSite(band);
                return false;
            }

            var material = tool.pavingMaterial;
            if (material == null)
            {
                reason = "Paver has no material configured";
                return false;
            }

            // The authored per-cell price times the grade surcharge: rough ground costs double,
            // which is the grading rule the design asks for and the reason a player levels a strip.
            int price = Mathf.Max(1, tool.materialPerCell) * Mathf.Max(1, cost);
            if (inventory.CountOf(material) < price)
            {
                reason = $"Out of {material.displayName}";
                return false;
            }

            inventory.container.Remove(material, price);
            PlaceCell(block, position, rotation);
            _laidThisDrag++;
            return true;
        }

        /// <summary>
        /// Tops a worn cell's run back up. Priced by how worn the run is, so a lightly used strip is
        /// cheap to keep and a broken one costs a proper repair — the upkeep economy the design asks
        /// for, with no separate repair item to invent.
        /// </summary>
        private bool TryRepair(Vector3 position, float cell, RoadPaverTool tool,
                               Inventory inventory, out string reason)
        {
            reason = null;
            RoadSurfaceUtility.QueryAt(position, _anchorScratch);
            AsphaltRoad target = null;
            float bestSqr = cell * cell * 0.5f;
            for (int i = 0; i < _anchorScratch.Count; i++)
            {
                var road = _anchorScratch[i];
                if (road == null) continue;
                float sqr = (road.transform.position - position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; target = road; }
            }
            if (target == null || target.Run == null) return true;
            if (target.Run.Wear01 <= 0.001f) return true;   // nothing to do, and nothing to charge

            var material = tool.pavingMaterial;
            if (material == null) return true;

            // Priced by the run's PAVED AREA, not its cell count, so repairing a carriageway of
            // wide slabs and a footpath of patch cells both cost about a third of laying them.
            float area = target.Run.PavedArea;
            int cost = Mathf.Max(1, Mathf.CeilToInt(
                target.Run.Wear01 * area * Mathf.Max(0f, tool.repairMaterialPerSquareMetre)));
            if (inventory.CountOf(material) < cost)
            {
                reason = $"Needs {cost} {material.displayName} to repair this run";
                return false;
            }

            inventory.container.Remove(material, cost);
            target.Run.Repair();
            VoxelEngine.UI.BuildFeedbackHud.Show("Road repaired",
                $"{area:0} m² · −{cost} {material.displayName}", null, new Color(0.42f, 0.85f, 0.55f));
            return true;
        }

        /// <summary>Walks the tangent-plane line between two cells, emitting the intermediate cells
        /// a fast drag skipped. Bounded so one frame can never lay more than `maxCells`.</summary>
        private static void InterpolateCells(Vector3 from, Quaternion fromRot, Vector3 to, Quaternion toRot,
                                             float cell, int maxCells, List<Vector3> positions,
                                             List<Quaternion> rotations)
        {
            positions.Clear();
            rotations.Clear();

            Vector3 delta = to - from;
            int steps = Mathf.RoundToInt(delta.magnitude / Mathf.Max(0.05f, cell));
            steps = Mathf.Clamp(steps, 1, Mathf.Max(1, maxCells));
            if (steps <= 1)
            {
                positions.Add(to);
                rotations.Add(toRot);
                return;
            }

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 flat = Vector3.Lerp(from, to, t);
                Quaternion rot = Quaternion.Slerp(fromRot, toRot, t);
                // Re-drop each interpolated cell onto the ground so a bridged gully does not
                // produce a strip of floating slabs between two supported ends.
                if (AsphaltRoad.ProbeGround(flat, rot * Vector3.up, out float groundOffset))
                    flat += rot * Vector3.up * groundOffset;
                positions.Add(flat);
                rotations.Add(rot);
            }
        }

        /// <summary>Lifts one cell back. Refunds material only while the run is still in good
        /// condition; worn-out pavement is rubble, not stock.</summary>
        public bool TryScrape(RaycastHit hit, RoadPaverTool tool, Inventory inventory)
        {
            if (hit.collider == null || tool == null || inventory == null) return false;
            var road = hit.collider.GetComponentInParent<AsphaltRoad>();
            if (road == null)
            {
                // The registry returns everything sharing a hash cell, which is wider than a road
                // cell, so aim at the nearest one to the hit rather than the first in the list.
                RoadSurfaceUtility.QueryAt(hit.point, _anchorScratch);
                float bestSqr = float.MaxValue;
                for (int i = 0; i < _anchorScratch.Count; i++)
                {
                    var candidate = _anchorScratch[i];
                    if (candidate == null) continue;
                    float sqr = (candidate.transform.position - hit.point).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; road = candidate; }
                }
                if (road != null && bestSqr > 1.5f * 1.5f) road = null;
            }
            if (road == null) return false;

            float wear = road.Run?.Wear01 ?? 0f;
            bool refunded = wear <= tool.refundWearLimit && tool.pavingMaterial != null && tool.refundPerCell > 0;

            // DetachFromRun leaves the registry, releases the cell and re-splits whatever runs the
            // lifted cell was holding together, so the destroy below cannot strand a neighbour.
            road.DetachFromRun();
            Object.Destroy(road.gameObject);

            if (refunded) inventory.Add(tool.pavingMaterial, tool.refundPerCell);

            VoxelEngine.UI.BuildFeedbackHud.Show("Road lifted",
                refunded ? $"+{tool.refundPerCell} {tool.pavingMaterial.displayName}"
                         : "Worn out - no material recovered",
                null, refunded ? new Color(0.55f, 0.80f, 0.95f) : new Color(0.85f, 0.70f, 0.45f));
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        //  PREVIEW
        // ════════════════════════════════════════════════════════════════

        private static readonly Color PreviewValid   = new Color(0.30f, 0.85f, 0.55f, 0.42f);
        private static readonly Color PreviewInvalid = new Color(0.95f, 0.35f, 0.30f, 0.42f);

        private void ShowPreview(Vector3 position, Quaternion rotation, float cell, bool valid)
        {
            EnsurePreview();
            if (_preview == null) return;
            _preview.SetActive(true);
            _preview.transform.SetPositionAndRotation(position + rotation * Vector3.up * 0.03f, rotation);
            _preview.transform.localScale = new Vector3(cell, 1f, cell);
            Color color = valid ? PreviewValid : PreviewInvalid;
            _previewMaterial.color = color;
            if (_previewMaterial.HasProperty(BaseColorId)) _previewMaterial.SetColor(BaseColorId, color);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void EnsurePreview()
        {
            if (_preview != null) return;
            _previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"))
            {
                hideFlags = HideFlags.DontSave
            };
            if (_previewMaterial.HasProperty("_Surface")) _previewMaterial.SetFloat("_Surface", 1f);
            if (_previewMaterial.HasProperty("_Blend"))   _previewMaterial.SetFloat("_Blend", 0f);
            _previewMaterial.SetOverrideTag("RenderType", "Transparent");
            _previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _previewMaterial.SetInt("_ZWrite", 0);
            _previewMaterial.renderQueue = 3000;

            // Holder + child quad: the holder takes the cell's world rotation from `ShowPreview`,
            // while the child carries the fixed 90-degree tilt that turns a Quad (which faces -Z)
            // into a flat patch. One transform cannot do both.
            _preview = new GameObject("RoadPaverPreview") { hideFlags = HideFlags.DontSave };
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Patch";
            quad.hideFlags = HideFlags.DontSave;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_preview.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var previewRenderer = quad.GetComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = _previewMaterial;
            previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;
            _preview.SetActive(false);
        }

        private void HidePreview()
        {
            if (_preview != null) _preview.SetActive(false);
        }
    }
}
