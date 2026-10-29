// Assets/Scripts/VoxelEngine/Building/Tiered/BuildSystemV2.cs
//
// Tiered construction build system:
//   * Holding a "BuildToken" item (one per family) shows a Wood-tier ghost preview.
//   * Ghost snaps to the nearest BuildSocket within range; falls back to grid snap.
//   * RMB places at Wood tier, consuming definition.placeCost from the player inventory.
//   * Toggle grid-vs-free with the BuildToggleGrid keybind (default G).
//   * The Hammer tool (separate) handles upgrade / rotate / destroy.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Building.Tiered
{
    public class BuildSystemV2 : MonoBehaviour
    {
        // Authored Size-V6 geometry dimensions. Structural joins must use these,
        // not the scene's configurable fallback grid, because an older serialized
        // grid value otherwise moves every join to exactly half its required span.
        private const float ConstructionModule = 7.5f;
        private const float ConstructionStorey = 5.625f;
        private const float HalfWallHeight = 2.8f;

        public static BuildSystemV2 Instance { get; private set; }
        public bool HammerPatternActive { get; private set; }

        /// <summary>What the local tiered-block ghost shows right now - for the network mirror.</summary>
        public bool TryGetGhostState(out string itemId, out Vector3 position, out Quaternion rotation)
        {
            itemId = null; position = default; rotation = default;
            if (_ghost == null || _ghostDef == null || !_ghost.activeSelf) return false;
            itemId = "tiered:" + _ghostDef.family;
            var t = _ghost.transform;
            position = t.position;
            rotation = t.rotation;
            return true;
        }
        public int HammerPatternPreviewCount { get; private set; }
        public int HammerPatternValidCount { get; private set; }
        public int HammerPatternAffordableCount { get; private set; }
        public static int HammerPatternCostMultiplier { get; private set; } = 1;
        public Vector2Int HammerPatternPlanSize { get; private set; }
        public string HammerPatternPlaneLabel { get; private set; } = string.Empty;
        public string HammerPatternWarning { get; private set; } = string.Empty;

        [Header("Refs")]
        public Camera shootCamera;
        public Inventory inventory;
        public TieredBlockRegistry registry;

        [Header("Tuning")]
        // Size-V6: one construction module is 7.5 m, so the free-placement grid,
        // the socket search radius and the builder's reach all scale with it.
        public float reach = 12f;
        public float socketSnapRadius = 8f;       // spans one full 7.5 m module from the aim point
        public bool  gridSnap = true;
        public float gridSize = 7.5f;
        public float ghostAlpha = 0.55f;
        public float yawStep = 90f;

        // Runtime
        /// <summary>True while a piece hologram is in the world and following the crosshair.</summary>
        public bool GhostShown => _ghost != null && _ghost.activeSelf;

        private GameObject _ghost;
        private TieredBlockDefinition _ghostDef;
        private float _ghostYaw;
        private Material _matValid, _matInvalid;
        private Material _appliedGhostMaterial;
        private bool _ghostValid;

        private Vector3 _ghostPos;
        // Fixed buffers cover normal building scenes. Saturation intentionally uses
        // the former allocating exhaustive APIs so no candidate socket or collider
        // is skipped in unusually dense factories.
        private static readonly RaycastHit[] s_buildRaycastProbe = new RaycastHit[64];
        private static readonly Collider[] s_socketOverlapProbe = new Collider[32];
        private static readonly Collider[] s_placementOverlapProbe = new Collider[64];
        private readonly HashSet<PlacedTieredBlock> _socketHosts = new(16);
        /// <summary>Host the current ghost validated against; consumed by Place() for fittings.</summary>
        private PlacedTieredBlock _ghostHost;
        private readonly List<BuildSocket> _socketScratch = new(8);
        private Quaternion _ghostRot = Quaternion.identity;
        private float _railingRise;
        private int _structuralSpan;
        private Vector3 _structuralAnchor;
        private float _pillarHeight = ConstructionStorey;
        private const float MaximumPillarHeight = ConstructionStorey * 1.5f;

        private struct HammerPlanCell
        {
            public PlacementCell placement;
            public BuildFamily family;
            public PlacedTieredBlock host;
            public bool valid;
            public int structuralSpan;
            public Vector3 structuralAnchor;
            public float pillarHeight;
            public float railingRise;
        }

        private bool _patternActive;
        private bool _wallRunIsDepth;
        private bool _buildWasHeld;
        private BuildFamily _patternFamily;
        private PlacementPattern _patternMode = PlacementPattern.Single;
        private TieredBlockDefinition _patternDefinition;
        private Vector3 _patternAnchorPosition;
        private Quaternion _patternAnchorRotation;
        private Vector3 _patternUp = Vector3.up;
        private PlacedTieredBlock _patternAnchorHost;
        private int _patternAnchorSpan;
        private bool _patternAnchorValid;
        private Vector3 _patternAnchorSupport;
        private float _patternAnchorPillarHeight = ConstructionStorey;
        private float _patternAnchorRailingRise;
        private Vector3 _lastPatternTarget;
        private int _patternSignature = -1;
        private float _nextPatternValidationTime;
        private TieredBlockDefinition _patternPreviewDefinition;
        private readonly List<PlacementCell> _hammerPlacements = new(PlacementOptions.MaxCells);
        private readonly List<HammerPlanCell> _hammerPlan = new(PlacementOptions.MaxCells);
        private readonly List<GameObject> _hammerGhosts = new(PlacementOptions.MaxCells - 1);
        private readonly List<Material> _hammerGhostMaterials = new(PlacementOptions.MaxCells - 1);
        private readonly List<BuildSocket> _patternSocketScratch = new(16);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _matValid   = MakeGhostMaterial(new Color(0.40f, 0.90f, 0.45f, ghostAlpha));
            _matInvalid = MakeGhostMaterial(new Color(0.95f, 0.30f, 0.25f, ghostAlpha));

            // 17.46.1-dev: with the hammer out and no piece armed, the crosshair marks what
            // it is looking at instead of promising a block. Created here because this is the
            // one tiered system guaranteed to exist in a scene that can build anything.
            if (GetComponent<AimedPieceHighlight>() == null)
                gameObject.AddComponent<AimedPieceHighlight>();
        }

        private void Update()
        {
            bool buildWheelHeld = GameSettings.IsHeld(InputAction.BuildWheel)
                || GameSettings.IsHeld(InputAction.PlacementWheel)
                || VoxelEngine.UI.RadialWheelController.AnyOpen;
            if (VoxelEngine.UI.UIState.IsBlocking && !buildWheelHeld)
            {
                HideGhost();
                _buildWasHeld = GameSettings.IsHeld(InputAction.Build);
                return;
            }

            // Toggle grid mode.
            if (GameSettings.WasPressed(InputAction.BuildToggleGrid))
                gridSnap = !gridSnap;

            // Rotate ghost only with Ctrl+wheel (matches GameUIController).
            bool ctrl = false;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            ctrl = UnityEngine.InputSystem.Keyboard.current != null
                   && UnityEngine.InputSystem.Keyboard.current.leftCtrlKey.isPressed;
            float wheel = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y : 0f;
#else
            ctrl = Input.GetKey(KeyCode.LeftControl);
            float wheel = Input.mouseScrollDelta.y;
#endif
            bool hammerWheelOpen = buildWheelHeld || (HammerBuildWheel.Instance != null
                && HammerBuildWheel.Instance.IsOpen);
            if (!hammerWheelOpen)
            {
                if (ctrl && Mathf.Abs(wheel) > 0.01f) _ghostYaw += Mathf.Sign(wheel) * yawStep;
                if (GameSettings.WasPressed(InputAction.BuildRotate)) _ghostYaw += yawStep;
            }

            UpdateGhost();
            _buildWasHeld = GameSettings.IsHeld(InputAction.Build);
        }

        private void OnDisable()
        {
            if (Instance == null || Instance == this) HideGhost();
            _buildWasHeld = false;
        }

        private void OnDestroy()
        {
            if (Instance == null || Instance == this) CancelHammerPattern();
            for (int i = 0; i < _hammerGhosts.Count; i++)
                if (_hammerGhosts[i] != null) Destroy(_hammerGhosts[i]);
            _hammerGhosts.Clear();
            _hammerGhostMaterials.Clear();
            if (_ghost != null) Destroy(_ghost);
            if (Instance == this) Instance = null;
        }

        // ---------- Ghost / placement ----------
        private void UpdateGhost()
        {
            if (inventory == null || registry == null) { HideGhost(); return; }
            // Build mode now requires holding the Hammer AND having picked a family in the wheel.
            var stack = inventory.ActiveStack;
            bool holdingHammer = !stack.IsEmpty && stack.item is VoxelEngine.Items.ToolItem t && t.toolType == VoxelEngine.Items.ToolType.Other && stack.item.GetType().Name == "Hammer";

            // ── 17.46.1-dev: A GHOST IS A PROMISE THAT A CLICK BUILDS A PIECE. ──────
            // The upgrade and repair faces of the hammer dial are not build mode, but the
            // family picked before them survived in ActiveFamily, so the last piece's ghost
            // stayed in the world and a click laid another piece instead of upgrading the
            // one being aimed at. Nothing but the family face may raise a ghost now, and the
            // aimed piece is outlined instead (AimedPieceHighlight).
            if (HammerBuildWheel.Instance != null
                && (HammerBuildWheel.Instance.IsUpgradeMode || HammerBuildWheel.Instance.IsRepairMode))
            {
                HideGhost();
                return;
            }

            BuildFamily? wheelFam = HammerBuildWheel.Instance != null ? HammerBuildWheel.Instance.ActiveFamily : null;
            // Legacy BuildToken support: if a token is held, it overrides the wheel selection.
            BuildFamily? activeFam = null;
            if (!stack.IsEmpty && stack.item is BuildToken tok) activeFam = tok.family;
            else if (holdingHammer && wheelFam.HasValue) activeFam = wheelFam.Value;

            if (activeFam == null)
            {
                HideGhost();
                return;
            }
            var def = registry.Get(activeFam.Value);
            if (def == null || def.GetPrefab(BuildTier.Wood) == null) { HideGhost(); return; }

            if (_ghost == null || _ghostDef != def)
            {
                if (_ghost != null) Destroy(_ghost);
                _ghostDef = def;
                _ghost = Instantiate(def.GetPrefab(BuildTier.Wood));
                if (_ghost == null) { HideGhost(); return; }
                _ghost.name = "BuildGhost";
                _appliedGhostMaterial = null;
                StripGhost(_ghost);
            }

            if (shootCamera == null) shootCamera = Camera.main;
            if (shootCamera == null) { HideGhost(); return; }
            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            bool hammerWheelOpen = holdingHammer && HammerBuildWheel.Instance != null
                && HammerBuildWheel.Instance.IsOpen;
            if (!TryRaycastIgnoringSelf(ray, out var hit, reach))
            {
                if (_patternActive && holdingHammer && !hammerWheelOpen
                    && _patternFamily == activeFam.Value && _patternDefinition == def)
                {
                    // Keep the last measured cells when the crosshair leaves every collider;
                    // releasing still commits the visible plan rather than stranding the press.
                    _ghost.SetActive(true);
                    if (!HandleHammerPattern(def, activeFam.Value, hasCurrentAim: false))
                        CancelHammerPattern();
                }
                else
                {
                    if (_patternActive) CancelHammerPattern();
                    PlacementPattern selectedPattern = PlacementOptions.Pattern;
                    bool canPattern = holdingHammer && !hammerWheelOpen
                        && selectedPattern != PlacementPattern.Single
                        && HammerPlacementPlanner.Supports(activeFam.Value, selectedPattern);
                    bool patternPress = GameSettings.WasPressed(InputAction.Build)
                        || (GameSettings.IsHeld(InputAction.Build) && !_buildWasHeld);
                    if (canPattern && patternPress)
                        VoxelEngine.UI.BuildFeedbackHud.Show("Run needs a clear anchor",
                            "Aim at a valid socket or build surface before dragging.",
                            null, VoxelEngine.UI.UITheme.AccentAmber);
                    _ghost.SetActive(false);
                }
                return;
            }
            _ghost.SetActive(true);

            ComputeGhostTransform(hit, def, activeFam.Value);
            // Final family-level guard lives outside every snap branch. Even an
            // older definition asset with a stale serialized family cannot bypass
            // the two-roof rule selected by the wheel.
            bool suspendedPanel = activeFam.Value == BuildFamily.Roof
                || activeFam.Value == BuildFamily.Floor
                || activeFam.Value == BuildFamily.FloorHatch
                || activeFam.Value == BuildFamily.Stairs
                || BuildFamilyInfo.IsRoofPanel(activeFam.Value);
            if (suspendedPanel)
            {
                // A Floor continuing from a Foundation starts one complete module
                // from that anchor, so two unsupported panels require 15 m. Roofs
                // still stop at span two before this wider geometric cap matters.
                const float maximumUnsupportedReach = ConstructionModule * 2f + 1f;
                bool beyondAnchor = _structuralAnchor == Vector3.zero
                    || Vector3.Distance(_ghostPos, _structuralAnchor) > maximumUnsupportedReach;
                if (_structuralSpan < 1 || _structuralSpan > 2 || beyondAnchor)
                    _ghostValid = false;
                // A sloped panel must PHYSICALLY touch something at its eave -
                // a wall head, a gable, a deck edge or the panel it chains from.
                // Span bookkeeping alone let panels ride stale numbers into open
                // air, standing on nothing.
                if (_ghostValid && BuildFamilyInfo.IsRoofPanel(activeFam.Value)
                    && !RoofPanelHasEaveContact(_ghostPos, _ghostRot))
                    _ghostValid = false;
            }
            _ghost.transform.SetPositionAndRotation(_ghostPos, _ghostRot);
            if (_ghost.TryGetComponent<TieredRailing>(out var ghostRailing))
                ghostRailing.Configure(_railingRise);
            if (_ghost.TryGetComponent<AdjustablePillar>(out var ghostPillar))
                ghostPillar.Configure(_pillarHeight);
            ApplyGhostMaterialIfChanged(_ghostValid ? _matValid : _matInvalid);

            PlacementPattern pattern = PlacementOptions.Pattern;
            bool patternSupported = HammerPlacementPlanner.Supports(activeFam.Value, pattern);
            HammerPatternWarning = !holdingHammer || pattern == PlacementPattern.Single || patternSupported
                ? string.Empty
                : HammerPlacementPlanner.UnsupportedReason(activeFam.Value, pattern);

            bool placementInputAllowed = !VoxelEngine.UI.UIState.IsBlocking
                && (holdingHammer ? !hammerWheelOpen
                    : !stack.IsEmpty && stack.item is BuildToken);
            if (_patternActive && (!holdingHammer || !placementInputAllowed || !patternSupported
                || _patternFamily != activeFam.Value || _patternDefinition != def))
                CancelHammerPattern();

            if (holdingHammer && placementInputAllowed && pattern != PlacementPattern.Single && patternSupported
                && HandleHammerPattern(def, activeFam.Value, hasCurrentAim: true))
                return;

            // An unsupported pattern never guesses a new plane or family: it is a
            // clear single-click fallback, named on the cost card and once on click.
            bool buildPressed = GameSettings.WasPressed(InputAction.Build);
            if (holdingHammer && placementInputAllowed && buildPressed
                && pattern != PlacementPattern.Single && !patternSupported)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Single placement",
                    HammerPatternWarning + " The selected pattern remains unchanged.",
                    null, VoxelEngine.UI.UITheme.AccentAmber);
            }

            // Place on the standard build action (RMB by default).
            if (placementInputAllowed && _ghostValid && buildPressed)
            {
                int pillarCostMultiplier = activeFam.Value == BuildFamily.Pillar
                    ? Mathf.Max(1, Mathf.CeilToInt(_pillarHeight / ConstructionStorey))
                    : 1;
                if (CanAfford(def.placeCost, pillarCostMultiplier))
                {
                    PayCost(def.placeCost, pillarCostMultiplier);
                    Place(def, _ghostPos, _ghostRot, _railingRise);
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(
                        def.displayName, def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].item : null,
                        def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].count * pillarCostMultiplier : 0);
                }
            }
        }

        /// <summary>Placement-pattern contract exposed to the hammer wheel and its cost card.</summary>
        public static bool SupportsHammerPattern(BuildFamily family, PlacementPattern pattern)
            => HammerPlacementPlanner.Supports(family, pattern);

        public static string HammerPatternFallbackReason(BuildFamily family, PlacementPattern pattern)
            => HammerPlacementPlanner.UnsupportedReason(family, pattern);

        private bool HandleHammerPattern(TieredBlockDefinition def, BuildFamily family, bool hasCurrentAim)
        {
            PlacementPattern pattern = PlacementOptions.Pattern;
            if (def == null || pattern == PlacementPattern.Single
                || !HammerPlacementPlanner.Supports(family, pattern)) return false;

            bool held = GameSettings.IsHeld(InputAction.Build);
            bool pressed = GameSettings.WasPressed(InputAction.Build)
                || (held && !_buildWasHeld);

            if (!_patternActive)
            {
                if (!pressed) return false;

                float pillarHeight = family == BuildFamily.Pillar
                    ? Mathf.Max(0.5f, _pillarHeight) : ConstructionStorey;
                int costMultiplier = PlacementCostMultiplier(family, pillarHeight);
                int affordable = GetAffordablePieceCount(def, costMultiplier);
                HammerPatternAffordableCount = affordable;
                HammerPatternCostMultiplier = costMultiplier;
                if (affordable <= 0)
                {
                    VoxelEngine.UI.BuildFeedbackHud.Show("Not enough materials",
                        "A run needs the cost of at least one " + def.displayName + ".",
                        null, VoxelEngine.UI.UITheme.AccentAmber);
                    return true;
                }
                if (!_ghostValid)
                {
                    VoxelEngine.UI.BuildFeedbackHud.Show("Run needs a clear anchor",
                        "Aim at a valid socket or build surface before dragging.",
                        null, VoxelEngine.UI.UITheme.AccentAmber);
                    return true;
                }

                _patternActive = true;
                HammerPatternActive = true;
                _patternFamily = family;
                _patternMode = pattern;
                _patternDefinition = def;
                _patternAnchorPosition = _ghostPos;
                _patternAnchorRotation = _ghostRot;
                _patternUp = GravityProvider.GetUp(_patternAnchorPosition);
                if (_patternUp.sqrMagnitude < 0.0001f) _patternUp = Vector3.up;
                _patternAnchorHost = _ghostHost;
                _patternAnchorSpan = _structuralSpan;
                _patternAnchorValid = _ghostValid;
                _patternAnchorSupport = _structuralAnchor;
                _patternAnchorPillarHeight = pillarHeight;
                _patternAnchorRailingRise = _railingRise;
                _lastPatternTarget = _patternAnchorPosition;
                _wallRunIsDepth = pattern == PlacementPattern.Wall
                    && HammerPlacementPlanner.InitialWallRunIsDepth(_patternAnchorRotation,
                        _patternUp, CurrentCameraForward());
                _patternSignature = -1;
                _nextPatternValidationTime = 0f;
                HammerPatternWarning = string.Empty;
                PlacementFeedback.Snap(_patternAnchorPosition, 1);
            }
            else if (_patternFamily != family || _patternDefinition != def || _patternMode != pattern)
            {
                CancelHammerPattern();
                return false;
            }

            if (hasCurrentAim) _lastPatternTarget = _ghostPos;
            if (_patternMode == PlacementPattern.Wall)
            {
                bool nextDepth = HammerPlacementPlanner.UpdateWallRunIsDepth(
                    _wallRunIsDepth, _patternAnchorRotation, _patternUp, CurrentCameraForward());
                if (nextDepth != _wallRunIsDepth)
                {
                    _wallRunIsDepth = nextDepth;
                    _patternSignature = -1;
                    PlacementFeedback.Snap(_patternAnchorPosition, 1);
                }
            }
            if (!hasCurrentAim) UpdateHammerPatternAimFromView();

            UpdateHammerPatternPlan(def, family, forceValidation: !held);
            if (!held)
            {
                int placed = CommitHammerPattern(def, family);
                if (placed > 0) PlacementFeedback.Placed(_patternAnchorPosition, placed);
                else PlacementFeedback.Refused(_patternAnchorPosition);
                CancelHammerPattern();
            }
            return true;
        }

        private Vector3 CurrentCameraForward()
        {
            if (shootCamera == null) shootCamera = Camera.main;
            return shootCamera != null ? shootCamera.transform.forward : Vector3.forward;
        }

        private void UpdateHammerPatternAimFromView()
        {
            if (shootCamera == null) shootCamera = Camera.main;
            if (shootCamera == null) return;

            Ray ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            float aimLimit = Mathf.Max(12f, reach) * 3f;
            Vector3 rayPoint = ray.GetPoint(aimLimit);
            Vector3 axisA;
            Vector3 axisB;
            Vector3 up = _patternUp.sqrMagnitude > 0.0001f ? _patternUp.normalized : Vector3.up;
            bool slopeFamily = _patternFamily == BuildFamily.Stairs
                || _patternFamily == BuildFamily.SlantedRoof
                || _patternFamily == BuildFamily.SlantedTriangularRoof;
            HammerPlacementPlanner.ResolveHorizontalAxes(_patternAnchorRotation, up,
                out Vector3 right, out Vector3 forward);
            Vector3 slopeStep = forward * ConstructionModule + up * ConstructionStorey;
            Vector3 slopeAxis = slopeStep.sqrMagnitude > 0.0001f ? slopeStep.normalized : forward;

            if (_patternMode == PlacementPattern.Pillar)
            {
                Vector3 chainAxis = slopeFamily ? slopeAxis : up;
                _lastPatternTarget = _patternAnchorPosition
                    + chainAxis * Vector3.Dot(rayPoint - _patternAnchorPosition, chainAxis);
                return;
            }

            if (_patternMode == PlacementPattern.Deck)
            {
                axisA = right;
                axisB = slopeFamily ? slopeAxis : forward;
            }
            else if (_patternMode == PlacementPattern.Wall)
            {
                axisA = _wallRunIsDepth ? forward : right;
                axisB = up;
            }
            else
            {
                // SPAN uses the same view ray as a hit aim; its planner chooses the
                // family's strongest authored axis from that three-dimensional delta.
                _lastPatternTarget = rayPoint;
                return;
            }

            Vector3 planeNormal = Vector3.Cross(axisA, axisB);
            if (planeNormal.sqrMagnitude < 0.0001f) planeNormal = up;
            planeNormal.Normalize();
            Plane plane = new Plane(planeNormal, _patternAnchorPosition);
            if (plane.Raycast(ray, out float enter) && enter > 0f)
                rayPoint = ray.GetPoint(Mathf.Min(enter, aimLimit));
            _lastPatternTarget = _patternAnchorPosition
                + Vector3.ProjectOnPlane(rayPoint - _patternAnchorPosition, planeNormal);
        }

        private int GetAffordablePieceCount(TieredBlockDefinition def, int costMultiplier)
        {
            costMultiplier = Mathf.Max(1, costMultiplier);
            if (def == null || def.placeCost == null || def.placeCost.items == null
                || def.placeCost.items.Length == 0) return PlacementOptions.MaxCells;
            if (inventory == null || inventory.container == null) return 0;

            var rack = BuildNetworkRack();
            int affordable = PlacementOptions.MaxCells;
            var costs = def.placeCost.items;
            for (int i = 0; i < costs.Length; i++)
            {
                ItemDefinition item = costs[i].item;
                if (item == null || costs[i].count <= 0) continue;
                bool alreadyCounted = false;
                for (int prior = 0; prior < i; prior++)
                {
                    if (costs[prior].item != item) continue;
                    alreadyCounted = true;
                    break;
                }
                if (alreadyCounted) continue;

                long perPiece = 0L;
                for (int duplicate = i; duplicate < costs.Length; duplicate++)
                    if (costs[duplicate].item == item && costs[duplicate].count > 0)
                        perPiece += (long)costs[duplicate].count * costMultiplier;
                if (perPiece <= 0L) continue;

                long available = inventory.container.CountOf(item);
                if (rack != null) available += rack.NetworkCount(item.itemId);
                long availablePieces = available / perPiece;
                int clampedAvailablePieces = availablePieces > int.MaxValue
                    ? int.MaxValue : (int)availablePieces;
                affordable = Mathf.Min(affordable, clampedAvailablePieces);
                if (affordable <= 0) return 0;
            }
            return affordable;
        }

        private static int PlacementCostMultiplier(BuildFamily family, float pillarHeight)
            => family == BuildFamily.Pillar
                ? Mathf.Max(1, Mathf.CeilToInt(pillarHeight / ConstructionStorey))
                : 1;

        private void UpdateHammerPatternPlan(TieredBlockDefinition def, BuildFamily family,
            bool forceValidation)
        {
            int costMultiplier = PlacementCostMultiplier(family, _patternAnchorPillarHeight);
            int affordable = GetAffordablePieceCount(def, costMultiplier);
            HammerPatternAffordableCount = affordable;
            HammerPatternCostMultiplier = costMultiplier;

            if (affordable <= 0)
            {
                _hammerPlacements.Clear();
                _hammerPlan.Clear();
                HammerPatternPreviewCount = 0;
                HammerPatternValidCount = 0;
                HammerPatternPlanSize = Vector2Int.zero;
                HammerPatternPlaneLabel = "NO AFFORDABLE CELLS";
                _patternSignature = -1;
                HideHammerGhosts();
                _ghostValid = false;
                ApplyGhostMaterialIfChanged(_matInvalid);
                return;
            }

            HammerPlacementPlanner.Build(family, _patternMode,
                _patternAnchorPosition, _patternAnchorRotation, _lastPatternTarget, _patternUp,
                _wallRunIsDepth, _patternAnchorPillarHeight, affordable, _hammerPlacements,
                out Vector2Int size, out string planeLabel);

            int signature = HammerPlanSignature(family, _patternMode, _wallRunIsDepth,
                _hammerPlacements);
            if (!forceValidation && signature == _patternSignature
                && Time.unscaledTime < _nextPatternValidationTime)
            {
                PlaceHammerGhostAtTip();
                return;
            }

            _patternSignature = signature;
            _nextPatternValidationTime = Time.unscaledTime + 0.18f;
            _hammerPlan.Clear();
            HammerPatternValidCount = 0;

            for (int i = 0; i < _hammerPlacements.Count; i++)
            {
                PlacementCell placement = _hammerPlacements[i];
                PlacedTieredBlock host = i == 0
                    ? _patternAnchorHost
                    : FindCompatibleSocketHostAt(placement.position, family);
                int span = i == 0 ? _patternAnchorSpan : 0;
                Vector3 support = i == 0 ? _patternAnchorSupport : Vector3.zero;

                if (RequiresPatternSupport(family))
                {
                    if (host != null)
                    {
                        _structuralAnchor = Vector3.zero;
                        int resolved = ResolveStructuralSpan(host, family);
                        if (resolved > 0 && (span <= 0 || resolved < span))
                        {
                            span = resolved;
                            support = _structuralAnchor;
                        }
                    }

                    if ((span < 1 || span > 2)
                        && TryFindPlannedPatternSupport(family, placement.position, i,
                            out int inheritedSpan, out Vector3 inheritedAnchor))
                    {
                        span = inheritedSpan;
                        support = inheritedAnchor;
                    }
                }

                _structuralSpan = span;
                _structuralAnchor = support;
                bool connected = i == 0 || host != null
                    || HasCompatiblePlannedSocket(def, family, placement, i)
                    || (RequiresPatternBaseSupport(family)
                        && HasPlannedVerticalSupport(placement, family, i));
                bool valid = ValidateOverlap(placement.position, family, host);
                if (i == 0) valid &= _patternAnchorValid;
                else valid &= connected;
                if (RequiresPatternSupport(family))
                    valid &= span >= 1 && span <= 2;
                if (RequiresPatternBaseSupport(family))
                    valid &= HasPatternBaseSupport(placement, family, host, i);
                if (BuildFamilyInfo.IsRoofPanel(family)
                    && !RoofPanelHasEaveContact(placement.position, placement.rotation)
                    && !HasPlannedRoofContact(placement.position, i))
                    valid = false;

                var planned = new HammerPlanCell
                {
                    placement = placement,
                    family = family,
                    host = host,
                    valid = valid,
                    structuralSpan = span,
                    structuralAnchor = support,
                    pillarHeight = _patternAnchorPillarHeight,
                    railingRise = _patternAnchorRailingRise,
                };
                _hammerPlan.Add(planned);
                if (valid) HammerPatternValidCount++;
            }

            HammerPatternPreviewCount = _hammerPlan.Count;
            HammerPatternPlanSize = size;
            HammerPatternPlaneLabel = planeLabel;
            ShowHammerPatternPreview(def);
        }

        private static bool RequiresPatternSupport(BuildFamily family)
            => family == BuildFamily.Floor || family == BuildFamily.FloorHatch
                || family == BuildFamily.Stairs || family == BuildFamily.Roof
                || BuildFamilyInfo.IsRoofPanel(family);

        private static bool RequiresPatternBaseSupport(BuildFamily family)
            => family == BuildFamily.Foundation
                || StructuralLoadState.IsVerticalSupport(family)
                || family == BuildFamily.TriangularWall
                || family == BuildFamily.TriangularWallInverted
                || family == BuildFamily.CompoundWall;

        private bool HasPatternBaseSupport(PlacementCell placement, BuildFamily family,
            PlacedTieredBlock host, int beforeIndex)
        {
            if (HasPlannedVerticalSupport(placement, family, beforeIndex)) return true;
            if (HasExistingStructuralSupport(host)) return true;
            return HasWorldVerticalBaseSupport(placement.position, placement.rotation)
                || HasWorldVerticalOverheadSupport(placement.position, placement.rotation, family);
        }

        private bool HasPlannedVerticalSupport(PlacementCell placement, BuildFamily family,
            int beforeIndex)
        {
            for (int i = 0; i < beforeIndex; i++)
            {
                HammerPlanCell previous = _hammerPlan[i];
                if (!previous.valid) continue;

                Vector3 previousUp = (previous.placement.rotation * Vector3.up).normalized;
                Vector3 currentUp = (placement.rotation * Vector3.up).normalized;
                Vector3 toCurrent = placement.position - previous.placement.position;
                Vector3 toPrevious = -toCurrent;
                float previousHeight = previous.family == BuildFamily.HalfWall ? HalfWallHeight
                    : previous.family == BuildFamily.Pillar ? previous.pillarHeight : ConstructionStorey;
                float currentHeight = family == BuildFamily.HalfWall ? HalfWallHeight
                    : family == BuildFamily.Pillar ? _patternAnchorPillarHeight : ConstructionStorey;

                Vector3 previousPlanar = Vector3.ProjectOnPlane(toCurrent, previousUp);
                float previousRise = Vector3.Dot(toCurrent, previousUp);
                bool restsOnPrevious = previousPlanar.sqrMagnitude <= 0.1225f
                    && Mathf.Abs(previousRise - previousHeight) <= 0.35f
                    && BuildSocketCompat.AreCompatible(previous.family, SocketSide.Top, family);
                if (restsOnPrevious) return true;

                Vector3 currentPlanar = Vector3.ProjectOnPlane(toPrevious, currentUp);
                float currentRise = Vector3.Dot(toPrevious, currentUp);
                bool hangsFromPrevious = currentPlanar.sqrMagnitude <= 0.1225f
                    && Mathf.Abs(currentRise - currentHeight) <= 0.35f
                    && BuildSocketCompat.AreCompatible(family, SocketSide.Top, previous.family);
                if (hangsFromPrevious) return true;
            }
            return false;
        }

        private static bool HasExistingStructuralSupport(PlacedTieredBlock host)
        {
            if (host == null || host.definition == null) return false;
            BuildFamily hostFamily = host.definition.family;
            if (hostFamily == BuildFamily.Foundation) return true;
            if (host.TryGetComponent<AdjustablePillar>(out var pillar))
                return pillar.IsSupportGrounded();
            if (BuildFamilyInfo.IsDeck(hostFamily))
                return StructuralLoadState.TryResolveSupportBase(host, out var carryingDeck)
                    || carryingDeck != null;
            if (StructuralLoadState.TryResolveSupportBase(host, out var supportDeck)
                || supportDeck != null) return true;
            var load = host.GetComponent<StructuralLoadState>();
            return load != null && load.armed;
        }

        private bool HasWorldVerticalBaseSupport(Vector3 position, Quaternion rotation)
        {
            Vector3 up = rotation * Vector3.up;
            Vector3 centre = position - up * 0.15f;
            Vector3 halfExtents = new(0.6f, 0.25f, 0.6f);
            int count = Physics.OverlapBoxNonAlloc(centre, halfExtents,
                s_placementOverlapProbe, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (HasWorldVerticalBaseSupport(s_placementOverlapProbe, count)) return true;
            if (count >= s_placementOverlapProbe.Length)
            {
                Collider[] overflow = Physics.OverlapBox(centre, halfExtents,
                    rotation, ~0, QueryTriggerInteraction.Ignore);
                return HasWorldVerticalBaseSupport(overflow, overflow.Length);
            }
            return false;
        }

        private bool HasWorldVerticalBaseSupport(Collider[] hits, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i];
                if (collider == null || IsDynamicBody(collider)) continue;
                if (transform.root != null && collider.transform.IsChildOf(transform.root)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null || HasExistingStructuralSupport(block)) return true;
            }
            return false;
        }

        private bool HasWorldVerticalOverheadSupport(Vector3 position,
            Quaternion rotation, BuildFamily family)
        {
            if (family == BuildFamily.Foundation) return false;
            Vector3 up = rotation * Vector3.up;
            float height = family == BuildFamily.HalfWall ? HalfWallHeight
                : family == BuildFamily.Pillar ? _patternAnchorPillarHeight : ConstructionStorey;
            Vector3 centre = position + up * (height + 0.1f);
            Vector3 halfExtents = new(0.6f, 0.25f, 0.6f);
            int count = Physics.OverlapBoxNonAlloc(centre, halfExtents,
                s_placementOverlapProbe, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (HasWorldVerticalOverheadSupport(s_placementOverlapProbe, count, family)) return true;
            if (count >= s_placementOverlapProbe.Length)
            {
                Collider[] overflow = Physics.OverlapBox(centre, halfExtents,
                    rotation, ~0, QueryTriggerInteraction.Ignore);
                return HasWorldVerticalOverheadSupport(overflow, overflow.Length, family);
            }
            return false;
        }

        private static bool HasWorldVerticalOverheadSupport(Collider[] hits,
            int count, BuildFamily family)
        {
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i];
                if (collider == null || IsDynamicBody(collider)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null || block.definition == null) continue;
                BuildFamily hostFamily = block.definition.family;
                if (family == BuildFamily.Pillar || BuildFamilyInfo.IsDeck(hostFamily)
                    || StructuralLoadState.IsVerticalSupport(hostFamily)
                    || hostFamily == BuildFamily.TriangularWall
                    || hostFamily == BuildFamily.TriangularWallInverted
                    || hostFamily == BuildFamily.CompoundWall) return true;
            }
            return false;
        }

        private bool HasCompatiblePlannedSocket(TieredBlockDefinition def, BuildFamily family,
            PlacementCell placement, int beforeIndex)
        {
            if (HasSpecialPlannedJoint(family, placement, beforeIndex)) return true;
            GameObject prefab = def != null ? def.GetPrefab(BuildTier.Wood) : null;
            if (prefab == null) return false;

            _patternSocketScratch.Clear();
            prefab.GetComponentsInChildren<BuildSocket>(true, _patternSocketScratch);
            if (_patternSocketScratch.Count == 0) return false;

            for (int i = 0; i < beforeIndex; i++)
            {
                HammerPlanCell previous = _hammerPlan[i];
                if (!previous.valid) continue;
                for (int socketIndex = 0; socketIndex < _patternSocketScratch.Count; socketIndex++)
                {
                    BuildSocket socket = _patternSocketScratch[socketIndex];
                    if (socket == null || !BuildSocketCompat.AreCompatible(
                            previous.family, socket.side, family)) continue;
                    Vector3 localSocket = prefab.transform.InverseTransformPoint(socket.transform.position);
                    localSocket = Vector3.Scale(localSocket, prefab.transform.localScale);
                    Vector3 expected = previous.placement.position
                        + previous.placement.rotation * localSocket;
                    if ((expected - placement.position).sqrMagnitude <= 0.1225f) return true;
                }
            }
            return false;
        }

        private bool HasSpecialPlannedJoint(BuildFamily family, PlacementCell placement,
            int beforeIndex)
        {
            for (int i = 0; i < beforeIndex; i++)
            {
                HammerPlanCell previous = _hammerPlan[i];
                if (!previous.valid || previous.family != family) continue;
                Vector3 delta = placement.position - previous.placement.position;
                Vector3 up = (previous.placement.rotation * Vector3.up).normalized;
                Vector3 forward = (previous.placement.rotation * Vector3.forward).normalized;

                if (family == BuildFamily.Pillar)
                {
                    Vector3 expected = up * previous.pillarHeight;
                    Vector3 lowerExpected = (placement.rotation * Vector3.up).normalized
                        * _patternAnchorPillarHeight;
                    if ((delta - expected).sqrMagnitude <= 0.1225f
                        || (delta + expected).sqrMagnitude <= 0.1225f
                        || (delta - lowerExpected).sqrMagnitude <= 0.1225f
                        || (delta + lowerExpected).sqrMagnitude <= 0.1225f) return true;
                }
                else if (family == BuildFamily.Stairs
                    || family == BuildFamily.SlantedRoof
                    || family == BuildFamily.SlantedTriangularRoof)
                {
                    Vector3 slopeStep = forward * ConstructionModule + up * ConstructionStorey;
                    bool slopeJoint = (delta - slopeStep).sqrMagnitude <= 0.1225f
                        || (delta + slopeStep).sqrMagnitude <= 0.1225f;
                    bool widthJoint = family == BuildFamily.Stairs
                        && ((delta - previous.placement.rotation * Vector3.right * ConstructionModule).sqrMagnitude <= 0.1225f
                            || (delta + previous.placement.rotation * Vector3.right * ConstructionModule).sqrMagnitude <= 0.1225f);
                    if (slopeJoint || widthJoint) return true;
                }
            }
            return false;
        }

        private bool TryFindPlannedPatternSupport(BuildFamily family, Vector3 position, int beforeIndex,
            out int span, out Vector3 anchor)
        {
            span = int.MaxValue;
            anchor = Vector3.zero;
            float slopeStep = Mathf.Sqrt(ConstructionModule * ConstructionModule
                                       + ConstructionStorey * ConstructionStorey);
            for (int i = 0; i < beforeIndex; i++)
            {
                var previous = _hammerPlan[i];
                if (!previous.valid || previous.structuralSpan < 1
                    || !CanRelayPatternSupport(previous, family)) continue;

                float distance = Vector3.Distance(position, previous.placement.position);
                bool adjacent = Mathf.Abs(distance - ConstructionModule) <= 0.35f
                    || ((family == BuildFamily.Stairs || family == BuildFamily.SlantedRoof
                         || family == BuildFamily.SlantedTriangularRoof)
                        && Mathf.Abs(distance - slopeStep) <= 0.35f);
                if (!adjacent) continue;

                int candidateSpan = previous.structuralSpan + 1;
                if (candidateSpan >= span) continue;
                span = candidateSpan;
                anchor = previous.structuralAnchor;
            }
            return span != int.MaxValue;
        }

        private static bool CanRelayPatternSupport(HammerPlanCell previous, BuildFamily family)
        {
            if (BuildFamilyInfo.IsRoofPanel(family))
                return BuildFamilyInfo.IsRoofPanel(previous.family);
            if (family == BuildFamily.Stairs)
                return previous.family == BuildFamily.Stairs;
            return BuildFamilyInfo.IsDeck(family) && BuildFamilyInfo.IsDeck(previous.family);
        }

        private bool HasPlannedRoofContact(Vector3 position, int beforeIndex)
        {
            float slopeStep = Mathf.Sqrt(ConstructionModule * ConstructionModule
                                       + ConstructionStorey * ConstructionStorey);
            for (int i = 0; i < beforeIndex; i++)
            {
                var previous = _hammerPlan[i];
                if (!previous.valid) continue;
                if (!BuildFamilyInfo.IsRoofPanel(previous.family)) continue;
                float distance = Vector3.Distance(position, previous.placement.position);
                if (Mathf.Abs(distance - ConstructionModule) <= 0.35f
                    || Mathf.Abs(distance - slopeStep) <= 0.35f) return true;
            }
            return false;
        }

        private PlacedTieredBlock FindCompatibleSocketHostAt(Vector3 position, BuildFamily family)
        {
            _socketHosts.Clear();
            BuildSocket bestSocket = null;
            float searchRadius = Mathf.Max(socketSnapRadius, ConstructionModule + 0.5f);
            float bestSqr = searchRadius * searchRadius;
            int count = Physics.OverlapSphereNonAlloc(position, searchRadius,
                s_socketOverlapProbe, ~0, QueryTriggerInteraction.UseGlobal);
            ConsiderSocketCandidates(s_socketOverlapProbe, count, family, position,
                ref bestSocket, ref bestSqr);
            if (count >= s_socketOverlapProbe.Length)
            {
                var overflow = Physics.OverlapSphere(position, searchRadius,
                    ~0, QueryTriggerInteraction.UseGlobal);
                ConsiderSocketCandidates(overflow, overflow.Length, family, position,
                    ref bestSocket, ref bestSqr);
            }

            if (bestSocket == null
                || (bestSocket.transform.position - position).sqrMagnitude > 0.0625f) return null;
            return bestSocket.GetComponentInParent<PlacedTieredBlock>();
        }

        private void ShowHammerPatternPreview(TieredBlockDefinition def)
        {
            if (def == null || _hammerPlan.Count == 0)
            {
                HideHammerGhosts();
                return;
            }

            if (_patternPreviewDefinition != def)
            {
                for (int i = 0; i < _hammerGhosts.Count; i++)
                    if (_hammerGhosts[i] != null) Destroy(_hammerGhosts[i]);
                _hammerGhosts.Clear();
                _hammerGhostMaterials.Clear();
                _patternPreviewDefinition = def;
            }

            int wanted = Mathf.Clamp(_hammerPlan.Count - 1, 0, PlacementOptions.MaxCells - 1);
            while (_hammerGhosts.Count < wanted)
            {
                GameObject preview = Instantiate(def.GetPrefab(BuildTier.Wood));
                if (preview == null) break;
                preview.name = "HammerRunGhost";
                StripGhost(preview);
                _hammerGhosts.Add(preview);
                _hammerGhostMaterials.Add(null);
            }

            for (int i = 0; i < _hammerGhosts.Count; i++)
            {
                GameObject preview = _hammerGhosts[i];
                if (preview == null) continue;
                bool used = i < wanted;
                if (preview.activeSelf != used) preview.SetActive(used);
                if (!used) continue;

                HammerPlanCell cell = _hammerPlan[i];
                preview.transform.SetPositionAndRotation(cell.placement.position, cell.placement.rotation);
                if (preview.TryGetComponent<AdjustablePillar>(out var pillar))
                    pillar.Configure(cell.pillarHeight);
                Material material = cell.valid ? _matValid : _matInvalid;
                if (i >= _hammerGhostMaterials.Count) _hammerGhostMaterials.Add(null);
                if (_hammerGhostMaterials[i] != material)
                {
                    ApplyGhostMaterial(preview, material);
                    _hammerGhostMaterials[i] = material;
                }
            }

            PlaceHammerGhostAtTip();
        }

        private void PlaceHammerGhostAtTip()
        {
            if (_ghost == null || _hammerPlan.Count == 0) return;
            HammerPlanCell tip = _hammerPlan[_hammerPlan.Count - 1];
            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(tip.placement.position, tip.placement.rotation);
            if (_ghost.TryGetComponent<AdjustablePillar>(out var pillar))
                pillar.Configure(tip.pillarHeight);
            _ghostValid = tip.valid;
            ApplyGhostMaterialIfChanged(tip.valid ? _matValid : _matInvalid);
        }

        private void HideHammerGhosts()
        {
            for (int i = 0; i < _hammerGhosts.Count; i++)
                if (_hammerGhosts[i] != null && _hammerGhosts[i].activeSelf)
                    _hammerGhosts[i].SetActive(false);
        }

        private int CommitHammerPattern(TieredBlockDefinition def, BuildFamily family)
        {
            if (def == null) return 0;
            if (_hammerPlan.Count == 0)
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("No cells placed",
                    "The run no longer has an affordable cell.",
                    null, VoxelEngine.UI.UITheme.AccentAmber);
                return 0;
            }
            int placed = 0;
            int costMultiplier = PlacementCostMultiplier(family, _patternAnchorPillarHeight);
            int commitBudget = GetAffordablePieceCount(def, costMultiplier);
            for (int i = 0; i < _hammerPlan.Count && placed < commitBudget; i++)
            {
                HammerPlanCell cell = _hammerPlan[i];
                if (!cell.valid) continue;

                PayCost(def.placeCost, costMultiplier);
                _structuralSpan = cell.structuralSpan;
                _structuralAnchor = cell.structuralAnchor;
                _pillarHeight = cell.pillarHeight;
                _railingRise = cell.railingRise;
                _ghostHost = cell.host;
                Place(def, cell.placement.position, cell.placement.rotation, cell.railingRise);
                placed++;
            }

            if (placed > 0)
            {
                string spent = DescribePatternCost(def.placeCost, placed, costMultiplier);
                string detail = placed + " of " + _hammerPlan.Count + " planned cells built";
                if (placed < _hammerPlan.Count) detail += " · blocked cells skipped";
                if (!string.IsNullOrEmpty(spent)) detail += " · spent " + spent;
                VoxelEngine.UI.BuildFeedbackHud.Show(placed + " " + def.displayName + " placed",
                    detail, null, VoxelEngine.UI.UITheme.AccentGreen);
            }
            else
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("No cells placed",
                    HammerPatternValidCount == 0
                        ? "Every planned cell is blocked or lacks structural support."
                        : "The required materials are no longer available.",
                    null, VoxelEngine.UI.UITheme.AccentAmber);
            }
            return placed;
        }

        private static string DescribePatternCost(TierCost cost, int pieces, int multiplier)
        {
            if (cost?.items == null) return string.Empty;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < cost.items.Length; i++)
            {
                ItemDefinition item = cost.items[i].item;
                if (item == null || cost.items[i].count <= 0) continue;
                bool alreadyCounted = false;
                for (int prior = 0; prior < i; prior++)
                {
                    if (cost.items[prior].item != item) continue;
                    alreadyCounted = true;
                    break;
                }
                if (alreadyCounted) continue;

                long basePerPiece = 0L;
                for (int duplicate = i; duplicate < cost.items.Length; duplicate++)
                    if (cost.items[duplicate].item == item && cost.items[duplicate].count > 0)
                        basePerPiece += cost.items[duplicate].count;
                long total = basePerPiece * pieces * multiplier;
                if (builder.Length > 0) builder.Append(", ");
                builder.Append(total).Append(' ').Append(item.displayName);
            }
            return builder.ToString();
        }

        private static int HammerPlanSignature(BuildFamily family, PlacementPattern pattern,
            bool wallRunIsDepth, List<PlacementCell> placements)
        {
            unchecked
            {
                int signature = 17;
                signature = signature * 31 + (int)family;
                signature = signature * 31 + (int)pattern;
                signature = signature * 31 + (wallRunIsDepth ? 1 : 0);
                signature = signature * 31 + (placements != null ? placements.Count : 0);
                if (placements == null) return signature;
                for (int i = 0; i < placements.Count; i++)
                {
                    Vector3 p = placements[i].position;
                    Vector3 e = placements[i].rotation.eulerAngles;
                    signature = signature * 31 + Mathf.RoundToInt(p.x * 100f);
                    signature = signature * 31 + Mathf.RoundToInt(p.y * 100f);
                    signature = signature * 31 + Mathf.RoundToInt(p.z * 100f);
                    signature = signature * 31 + Mathf.RoundToInt(e.x);
                    signature = signature * 31 + Mathf.RoundToInt(e.y);
                    signature = signature * 31 + Mathf.RoundToInt(e.z);
                }
                return signature;
            }
        }

        private void CancelHammerPattern()
        {
            _patternActive = false;
            _patternFamily = default;
            _patternMode = PlacementPattern.Single;
            _patternDefinition = null;
            _patternAnchorHost = null;
            _patternAnchorSpan = 0;
            _patternAnchorValid = false;
            _patternAnchorSupport = Vector3.zero;
            _patternAnchorPillarHeight = ConstructionStorey;
            _patternSignature = -1;
            _hammerPlacements.Clear();
            _hammerPlan.Clear();
            HideHammerGhosts();
            HammerPatternActive = false;
            HammerPatternPreviewCount = 0;
            HammerPatternValidCount = 0;
            HammerPatternAffordableCount = 0;
            HammerPatternCostMultiplier = 1;
            HammerPatternPlanSize = Vector2Int.zero;
            HammerPatternPlaneLabel = string.Empty;
            HammerPatternWarning = string.Empty;
        }

        private void HideGhost()
        {
            CancelHammerPattern();
            if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostDef = null; }
            _appliedGhostMaterial = null;
        }

        private bool TryRaycastIgnoringSelf(Ray ray, out RaycastHit hit, float maxDistance)
        {
            int count = Physics.RaycastNonAlloc(ray, s_buildRaycastProbe, maxDistance,
                ~0, QueryTriggerInteraction.Ignore);
            if (count >= s_buildRaycastProbe.Length)
            {
                // NonAlloc has no complete-ordering guarantee on saturation.
                var overflow = Physics.RaycastAll(ray, maxDistance, ~0, QueryTriggerInteraction.Ignore);
                return TryGetNearestBuildRaycastHit(overflow, overflow.Length, out hit);
            }
            return TryGetNearestBuildRaycastHit(s_buildRaycastProbe, count, out hit);
        }

        private bool TryGetNearestBuildRaycastHit(RaycastHit[] hits, int count, out RaycastHit hit)
        {
            Transform selfRoot = transform.root;
            float closest = float.MaxValue;
            hit = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null || candidate.distance >= closest) continue;
                if (selfRoot != null && candidate.collider.transform.IsChildOf(selfRoot)) continue;
                if (VoxelEngine.Player.PlayerRaycastFilter.IsOwnPlayerCollider(candidate.collider, transform)) continue;
                closest = candidate.distance;
                hit = candidate;
                found = true;
            }
            return found;
        }

        // ---------- Snap / placement math ----------
        private void ComputeGhostTransform(RaycastHit hit, TieredBlockDefinition def, BuildFamily requestedFamily)
        {
            _railingRise = 0f;
            _structuralSpan = 0;
            _structuralAnchor = Vector3.zero;
            _pillarHeight = ConstructionStorey;
            // 1) Try socket snap: look for the nearest BuildSocket within socketSnapRadius
            //    around the hit point that accepts this family.
            BuildSocket bestSocket = null;
            float bestSqr = socketSnapRadius * socketSnapRadius;

            var directHost = hit.collider != null ? hit.collider.GetComponentInParent<PlacedTieredBlock>() : null;
            if (requestedFamily == BuildFamily.Roof || requestedFamily == BuildFamily.Floor
                || requestedFamily == BuildFamily.FloorHatch || requestedFamily == BuildFamily.Stairs
                || BuildFamilyInfo.IsRoofPanel(requestedFamily))
                _structuralSpan = ResolveStructuralSpan(directHost, requestedFamily);
            if (requestedFamily == BuildFamily.Stairs &&
                directHost != null && directHost.definition != null && directHost.definition.family == BuildFamily.Stairs &&
                TryComputeStairChainTransform(hit, directHost, out _ghostPos, out _ghostRot))
            {
                _ghostValid = ValidateOverlap(_ghostPos, requestedFamily, directHost);
                return;
            }
            if (BuildFamilyInfo.IsRoofPanel(requestedFamily) &&
                directHost != null && directHost.definition != null
                && BuildFamilyInfo.IsRoofPanel(directHost.definition.family) &&
                TryComputeRoofChainTransform(hit, directHost, requestedFamily, out _ghostPos, out _ghostRot))
            {
                _ghostValid = ValidateOverlap(_ghostPos, requestedFamily, directHost);
                return;
            }

            // Structural deck placement is resolved from the piece that was actually
            // aimed at. A wide search can see sockets through a wall or on the deck
            // behind it, making an unrelated centre socket win by a few centimetres.
            // These three common joins have an unambiguous answer in the host frame.
            if (directHost != null && directHost.definition != null &&
                TryComputeStructuralDeckTransform(hit, directHost, requestedFamily, out _ghostPos, out _ghostRot))
            {
                _ghostValid = ValidateOverlap(_ghostPos, requestedFamily, directHost);
                return;
            }

            // A pillar sits flush with the deck it carries, so the aim ray often
            // hits the pillar when the player means the deck join beside it.
            // Without this redirect the ghost fell into free placement, where
            // the overlap rule vetoes anything touching a structure - walls on
            // a pillar-supported edge were permanently red.
            if (directHost != null && (directHost.GetComponent<AdjustablePillar>() != null
                    || (directHost.definition != null && directHost.definition.family == BuildFamily.Pillar)))
            {
                var carriedDeck = FindDeckAtPillarTop(directHost);
                if (carriedDeck != null &&
                    TryComputeStructuralDeckTransform(hit, carriedDeck, requestedFamily, out _ghostPos, out _ghostRot))
                {
                    _ghostValid = ValidateOverlap(_ghostPos, requestedFamily, carriedDeck);
                    return;
                }
            }

            _socketHosts.Clear();
            int socketCandidateCount = Physics.OverlapSphereNonAlloc(hit.point, socketSnapRadius,
                s_socketOverlapProbe, ~0, QueryTriggerInteraction.UseGlobal);
            ConsiderSocketCandidates(s_socketOverlapProbe, socketCandidateCount, requestedFamily, hit.point,
                ref bestSocket, ref bestSqr);
            if (socketCandidateCount >= s_socketOverlapProbe.Length)
            {
                // Retain the former exhaustive result if the reusable local probe fills.
                var overflow = Physics.OverlapSphere(hit.point, socketSnapRadius, ~0, QueryTriggerInteraction.UseGlobal);
                ConsiderSocketCandidates(overflow, overflow.Length, requestedFamily, hit.point,
                    ref bestSocket, ref bestSqr);
            }

            if (bestSocket != null)
            {
                var socketHost = bestSocket.GetComponentInParent<PlacedTieredBlock>();

                if (requestedFamily == BuildFamily.Stairs &&
                    TryComputeStairTransform(hit, bestSocket, out _ghostPos, out _ghostRot))
                {
                    _ghostValid = ValidateOverlap(_ghostPos, requestedFamily, socketHost);
                    return;
                }

                _ghostPos = bestSocket.transform.position;
                // Preserve the host/socket basis exactly. Reconstructing from world
                // Euler yaw introduced small rotational drift on spherical surfaces.
                Vector3 socketUp = bestSocket.transform.up;
                _ghostRot = Quaternion.AngleAxis(_ghostYaw, socketUp) * bestSocket.transform.rotation;
                _ghostValid = ValidateOverlap(_ghostPos, requestedFamily, socketHost);
                return;
            }

            // 2) Fall back to grid snap or free placement on the hit surface.
            // Construction roots represent the bottom/hinge plane, not the center
            // of a module, so snap to grid intersections instead of cell centers.
            // Keep the free-placement contact margin at 2 mm: enough to avoid
            // z-fighting without leaving the visible 2 cm seam at foundation edges.
            const float surfaceOffset = 0.002f;
            Vector3 raw = hit.point + hit.normal * surfaceOffset;

            // Ground-standing pieces (walls, gates, compound walls) keep the aimed
            // surface HEIGHT and only snap horizontally. Rounding their root to the
            // nearest 7.5 m shell hung gates in mid-air on any terrain between
            // shells, where they promptly decayed for lack of base contact.
            bool groundStanding = requestedFamily == BuildFamily.Wall || requestedFamily == BuildFamily.HalfWall
                || requestedFamily == BuildFamily.Doorway || requestedFamily == BuildFamily.Window
                || requestedFamily == BuildFamily.WallFrame
                || requestedFamily == BuildFamily.TriangularWall || requestedFamily == BuildFamily.TriangularWallInverted
                || requestedFamily == BuildFamily.GateFrame || requestedFamily == BuildFamily.BigGateFrame
                || requestedFamily == BuildFamily.CompoundWall;

            // A Foundation establishes the construction grid. Snapping its radial
            // altitude to an arbitrary 7.5 m shell can bury it after loading a
            // world whose terrain surface is between shells. Use the aimed surface;
            // neighbouring foundations continue through authored sockets.
            if (gridSnap && requestedFamily != BuildFamily.Foundation)
            {
                if (GravityProvider.IsRadial && GravityProvider.ActiveBody != null)
                {
                    // Spherical planet: snap along the tangent plane so blocks
                    // follow the curvature and align at consistent heights.
                    Vector3 planetCenter = GravityProvider.ActiveBody.transform.position;
                    Vector3 toPoint = raw - planetCenter;
                    float altitude = toPoint.magnitude;
                    Vector3 up = toPoint.normalized;

                    // Round altitude to grid increments for consistent storey heights.
                    float snappedAlt = groundStanding ? altitude
                        : Mathf.Round(altitude / gridSize) * gridSize;

                    Vector3 fwd = Vector3.Cross(up, Vector3.right);
                    if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.Cross(up, Vector3.forward);
                    fwd = fwd.normalized;
                    Vector3 rgt = Vector3.Cross(fwd, up).normalized;

                    Vector3 tangentOffset = raw - planetCenter - up * Vector3.Dot(toPoint, up);
                    float localX = Vector3.Dot(tangentOffset, rgt);
                    float localZ = Vector3.Dot(tangentOffset, fwd);

                    localX = Mathf.Round(localX / gridSize) * gridSize;
                    localZ = Mathf.Round(localZ / gridSize) * gridSize;

                    _ghostPos = planetCenter + up * snappedAlt + rgt * localX + fwd * localZ;
                }
                else
                {
                    _ghostPos = new Vector3(
                        Mathf.Round(raw.x / gridSize) * gridSize,
                        groundStanding ? raw.y : Mathf.Round(raw.y / gridSize) * gridSize,
                        Mathf.Round(raw.z / gridSize) * gridSize);
                }
            }
            else
            {
                _ghostPos = raw;
            }

            _ghostRot = GravityProvider.GetSurfaceRotation(_ghostPos, _ghostYaw);
            _ghostValid = ValidateOverlap(_ghostPos, requestedFamily);
        }

        private bool TryComputeStructuralDeckTransform(
            RaycastHit hit,
            PlacedTieredBlock host,
            BuildFamily incoming,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            BuildFamily hostFamily = host.definition.family;
            bool incomingDeck = incoming == BuildFamily.Floor || incoming == BuildFamily.FloorHatch
                || incoming == BuildFamily.Roof;
            bool wallHost = hostFamily == BuildFamily.Wall
                || hostFamily == BuildFamily.Doorway
                || hostFamily == BuildFamily.Window
                || hostFamily == BuildFamily.HalfWall
                || hostFamily == BuildFamily.WallFrame;

            Vector3 localHit = host.transform.InverseTransformPoint(hit.point);
            bool hostIsPillar = hostFamily == BuildFamily.Pillar
                || host.GetComponent<AdjustablePillar>() != null;
            if (incomingDeck && hostIsPillar)
            {
                // A pillar carries a floor CORNER - the 90 degree point where up
                // to four modules meet - matching the corner anchors used when a
                // pillar is placed under an existing deck. The deck extends
                // diagonally toward the builder, so the piece lands on the side
                // they are standing on.
                Vector3 toBuilder = transform.position - host.transform.position;
                float sideX = Vector3.Dot(toBuilder, host.transform.right) >= 0f ? 1f : -1f;
                float sideZ = Vector3.Dot(toBuilder, host.transform.forward) >= 0f ? 1f : -1f;
                float height = host.TryGetComponent<AdjustablePillar>(out var sizedPillar)
                    ? sizedPillar.currentHeight : ConstructionStorey;
                position = host.transform.position + host.transform.up * height
                    + host.transform.right * (sideX * ConstructionModule * 0.5f)
                    + host.transform.forward * (sideZ * ConstructionModule * 0.5f);
                rotation = Quaternion.AngleAxis(_ghostYaw, host.transform.up) * host.transform.rotation;
                return true;
            }

            if (incoming == BuildFamily.Pillar && hostIsPillar &&
                TryComputePillarChainTransform(hit, host, out position, out rotation))
                return true;

            if (incoming == BuildFamily.Railing)
            {
                if (hostFamily == BuildFamily.Stairs)
                {
                    float side = Mathf.Approximately(localHit.x, 0f) ? 1f : Mathf.Sign(localHit.x);
                    position = host.transform.position + host.transform.right * (side * (ConstructionModule - 0.6f) * 0.5f);
                    rotation = host.transform.rotation * Quaternion.Euler(0f, -90f, 0f);
                    _railingRise = ConstructionStorey;
                    return true;
                }

                if (hostFamily == BuildFamily.Foundation || hostFamily == BuildFamily.Floor || hostFamily == BuildFamily.FloorHatch)
                {
                    float surface = hostFamily == BuildFamily.Foundation ? 1.125f : 0.42f;
                    bool edgeX = Mathf.Abs(localHit.x) > Mathf.Abs(localHit.z);
                    float side = edgeX
                        ? (Mathf.Approximately(localHit.x, 0f) ? 1f : Mathf.Sign(localHit.x))
                        : (Mathf.Approximately(localHit.z, 0f) ? 1f : Mathf.Sign(localHit.z));
                    position = host.transform.position + host.transform.up * surface
                        + (edgeX ? host.transform.right : host.transform.forward) * (side * ConstructionModule * 0.5f);
                    float edgeYaw = edgeX ? 90f : 0f;
                    rotation = Quaternion.AngleAxis(_ghostYaw + edgeYaw, host.transform.up) * host.transform.rotation;
                    return true;
                }
            }

            if ((incomingDeck || BuildFamilyInfo.IsRoofPanel(incoming)) && wallHost)
            {
                float height = hostFamily == BuildFamily.HalfWall ? HalfWallHeight : ConstructionStorey;
                float side = ResolveFaceSide(localHit.z, Vector3.Dot(hit.normal, host.transform.forward));

                position = host.transform.position
                    + host.transform.up * height
                    + host.transform.forward * (side * ConstructionModule * 0.5f);
                // A sloped panel seats its EAVE on the wall head and rises away
                // from the aimed side, so gutters land on walls and ridges point
                // into the building. R still spins it a quarter turn at a time.
                float baseYaw = BuildFamilyInfo.IsRoofPanel(incoming) ? (side > 0f ? 180f : 0f) : 0f;
                rotation = Quaternion.AngleAxis(_ghostYaw + baseYaw, host.transform.up) * host.transform.rotation;
                return true;
            }

            bool incomingWall = incoming == BuildFamily.Wall || incoming == BuildFamily.HalfWall
                || incoming == BuildFamily.Doorway || incoming == BuildFamily.Window
                || incoming == BuildFamily.WallFrame
                || incoming == BuildFamily.TriangularWall || incoming == BuildFamily.TriangularWallInverted
                || incoming == BuildFamily.GateFrame || incoming == BuildFamily.BigGateFrame
                || incoming == BuildFamily.CompoundWall;
            if (incomingWall &&
                (hostFamily == BuildFamily.Foundation || hostFamily == BuildFamily.Floor || hostFamily == BuildFamily.FloorHatch))
            {
                float surface = hostFamily == BuildFamily.Foundation ? 1.125f : 0.42f;
                bool edgeX = Mathf.Abs(localHit.x) > Mathf.Abs(localHit.z);
                float side = edgeX
                    ? (Mathf.Approximately(localHit.x, 0f) ? 1f : Mathf.Sign(localHit.x))
                    : (Mathf.Approximately(localHit.z, 0f) ? 1f : Mathf.Sign(localHit.z));
                // Aiming at the deck's UNDERSIDE hangs the piece below the edge:
                // its head sits flush against the slab bottom and the piece grows
                // downward, so vertical building continues under a floor line.
                bool underside = hostFamily != BuildFamily.Foundation
                    && incoming != BuildFamily.GateFrame && incoming != BuildFamily.BigGateFrame
                    && incoming != BuildFamily.CompoundWall
                    && Vector3.Dot(hit.normal, host.transform.up) < -0.35f;
                float drop = incoming == BuildFamily.HalfWall ? HalfWallHeight : ConstructionStorey;
                position = host.transform.position
                    + host.transform.up * (underside ? -drop : surface)
                    + (edgeX ? host.transform.right : host.transform.forward) * (side * ConstructionModule * 0.5f);
                float edgeYaw = edgeX ? side * 90f : (side < 0f ? 180f : 0f);
                rotation = Quaternion.AngleAxis(_ghostYaw + edgeYaw, host.transform.up) * host.transform.rotation;
                return true;
            }

            if (incoming == BuildFamily.Pillar &&
                (hostFamily == BuildFamily.Foundation || hostFamily == BuildFamily.Floor
                    || hostFamily == BuildFamily.FloorHatch || hostFamily == BuildFamily.Stairs))
            {
                Vector3 anchor;
                if (hostFamily == BuildFamily.Stairs)
                {
                    // Stairs rise continuously; the aimed underside point is the
                    // actual load point rather than a flat deck centre/corner.
                    anchor = hit.point;
                }
                else
                {
                    Vector3 edge = NearestCentreOrEdge(localHit, ConstructionModule * 0.5f);
                    anchor = host.transform.position
                        + host.transform.right * edge.x + host.transform.forward * edge.z;
                }

                bool aimedUnder = hostFamily != BuildFamily.Foundation
                    && Vector3.Dot(hit.normal.normalized, host.transform.up) < -0.45f;
                if (aimedUnder)
                {
                    // Ground within one pillar: meet it exactly. Farther away: a
                    // full-length stage hangs in the air - placeable, but it only
                    // becomes a valid support once the chain built below it
                    // reaches the surface.
                    _pillarHeight = TryFindSolidGround(anchor, -host.transform.up, host,
                        MaximumPillarHeight, out float drop) ? drop : MaximumPillarHeight;
                    position = anchor - host.transform.up * _pillarHeight;
                }
                else
                {
                    float surface = hostFamily == BuildFamily.Foundation ? 1.125f : 0.42f;
                    position = anchor + host.transform.up * surface;
                }
                rotation = Quaternion.AngleAxis(_ghostYaw, host.transform.up) * host.transform.rotation;
                return true;
            }

            bool sameDeck = (hostFamily == BuildFamily.Foundation
                    && (incoming == BuildFamily.Foundation || incomingDeck))
                || ((hostFamily == BuildFamily.Floor || hostFamily == BuildFamily.FloorHatch)
                    && incomingDeck);
            if (!sameDeck) return false;

            // Pick the edge nearest the aimed point. This remains deterministic at
            // the centre and never depends on whether an edge socket happened to
            // fall inside the broad physics query.
            float vertical = hostFamily == BuildFamily.Foundation && incomingDeck ? 1.125f : 0f;
            bool useX = Mathf.Abs(localHit.x) > Mathf.Abs(localHit.z);
            if (useX)
            {
                float side = Mathf.Approximately(localHit.x, 0f) ? 1f : Mathf.Sign(localHit.x);
                position = host.transform.position + host.transform.up * vertical
                    + host.transform.right * (side * ConstructionModule);
            }
            else
            {
                float side = Mathf.Approximately(localHit.z, 0f) ? 1f : Mathf.Sign(localHit.z);
                position = host.transform.position + host.transform.up * vertical
                    + host.transform.forward * (side * ConstructionModule);
            }
            rotation = Quaternion.AngleAxis(_ghostYaw, host.transform.up) * host.transform.rotation;
            return true;
        }

        private static bool TryFindSolidGround(Vector3 origin, Vector3 down, PlacedTieredBlock host,
            float maximumDistance, out float distance, bool stopAtPlaced = false)
        {
            var hits = Physics.RaycastAll(origin - down * 0.05f, down.normalized,
                maximumDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || (host != null && collider.transform.IsChildOf(host.transform))) continue;
                if (IsDynamicBody(collider)) continue;
                if (!stopAtPlaced && collider.GetComponentInParent<PlacedTieredBlock>() != null) continue;
                distance = Mathf.Max(0.5f, hits[i].distance - 0.05f);
                return true;
            }
            distance = 0f;
            return false;
        }

        /// <summary>
        /// Pillar aimed at another pillar. Aiming at the lower half or the underside
        /// extends the chain downward toward the ground - hanging in the air is
        /// allowed, the chain simply carries no load until it touches down. Aiming
        /// at the upper half stacks a new stage on top, sized to meet a deck
        /// underside exactly when one is within reach.
        /// </summary>
        private bool TryComputePillarChainTransform(RaycastHit hit, PlacedTieredBlock host,
            out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (host == null) return false;

            Vector3 up = host.transform.up.normalized;
            float hostHeight = host.TryGetComponent<AdjustablePillar>(out var hostPillar)
                ? hostPillar.currentHeight : ConstructionStorey;

            float normalDot = Vector3.Dot(hit.normal.normalized, up);
            float hitHeight = Vector3.Dot(hit.point - host.transform.position, up);
            bool extendDown = normalDot < -0.45f
                || (normalDot < 0.45f && hitHeight < hostHeight * 0.5f);

            rotation = Quaternion.AngleAxis(_ghostYaw, up) * host.transform.rotation;
            if (extendDown)
            {
                // Hang the new stage from the host root. Meet the ground (or any
                // placed piece) exactly when it is within one pillar, otherwise
                // place a full-length hanging stage the player keeps extending.
                Vector3 anchor = host.transform.position;
                _pillarHeight = TryFindSolidGround(anchor, -up, host, MaximumPillarHeight,
                    out float drop, stopAtPlaced: true) ? drop : MaximumPillarHeight;
                position = anchor - up * _pillarHeight;
                return true;
            }

            Vector3 top = host.transform.position + up * hostHeight;
            _pillarHeight = TryFindDeckAbove(top, up, host, MaximumPillarHeight, out float rise)
                ? rise : ConstructionStorey;
            position = top;
            return true;
        }

        /// <summary>The Floor or Floor Hatch resting directly on a pillar's top, if any.</summary>
        private static PlacedTieredBlock FindDeckAtPillarTop(PlacedTieredBlock pillarHost)
        {
            if (pillarHost == null) return null;
            float height = pillarHost.TryGetComponent<AdjustablePillar>(out var pillar)
                ? pillar.currentHeight : ConstructionStorey;
            Vector3 top = pillarHost.transform.position + pillarHost.transform.up * height;
            var hits = Physics.RaycastAll(top - pillarHost.transform.up * 0.1f,
                pillarHost.transform.up, 1f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(pillarHost.transform)) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null || block.definition == null) continue;
                BuildFamily family = block.definition.family;
                if (family == BuildFamily.Floor || family == BuildFamily.FloorHatch
                    || family == BuildFamily.Roof) return block;
            }
            return null;
        }

        /// <summary>Finds the first deck underside above a pillar top so a stacked stage can meet it exactly.</summary>
        private static bool TryFindDeckAbove(Vector3 origin, Vector3 up, PlacedTieredBlock host,
            float maximumDistance, out float rise)
        {
            var hits = Physics.RaycastAll(origin + up * 0.05f, up.normalized,
                maximumDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || (host != null && collider.transform.IsChildOf(host.transform))) continue;
                var block = collider.GetComponentInParent<PlacedTieredBlock>();
                if (block == null || block.definition == null) continue;
                BuildFamily family = block.definition.family;
                if (family != BuildFamily.Floor && family != BuildFamily.FloorHatch
                    && family != BuildFamily.Stairs) continue;
                rise = Mathf.Clamp(hits[i].distance + 0.05f, 0.5f, maximumDistance);
                return true;
            }
            rise = 0f;
            return false;
        }

        /// <summary>Players, fauna and loose physics objects never count as ground or decks.</summary>
        internal static bool IsDynamicBody(Collider collider)
            => collider is CharacterController
                || (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic);

        private static float ResolveFaceSide(float localAxis, float normalDot)
        {
            if (Mathf.Abs(localAxis) > 0.08f) return Mathf.Sign(localAxis);
            if (Mathf.Abs(normalDot) > 0.01f) return Mathf.Sign(normalDot);
            return 1f;
        }

        private static Vector3 NearestCentreOrEdge(Vector3 localHit, float halfModule)
        {
            // The middle owns the central pillar socket. Everywhere else resolves
            // to one of the four true module corners, where up to four foundations
            // meet at ninety degrees. Side midpoints are deliberately not sockets.
            float ax = Mathf.Abs(localHit.x);
            float az = Mathf.Abs(localHit.z);
            if (Mathf.Max(ax, az) < halfModule * 0.34f) return Vector3.zero;
            float x = Mathf.Approximately(localHit.x, 0f) ? 1f : Mathf.Sign(localHit.x);
            float z = Mathf.Approximately(localHit.z, 0f) ? 1f : Mathf.Sign(localHit.z);
            return new Vector3(x * halfModule, 0f, z * halfModule);
        }

        /// <summary>
        /// True when a placed block sits under the panel's eave line (local -Z
        /// edge at root level) or flush against either side edge. Terrain does
        /// not count: roofing rests on structure, not on dirt.
        /// </summary>
        public static bool RoofPanelHasEaveContact(Vector3 position, Quaternion rotation,
            PlacedTieredBlock ignore = null)
        {
            Vector3 up = rotation * Vector3.up;
            Vector3 fwd = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            for (int probe = 0; probe < 3; probe++)
            {
                Vector3 centre = probe switch
                {
                    0 => position + fwd * (-ConstructionModule * 0.5f + 0.2f) - up * 0.1f,
                    1 => position + right * (ConstructionModule * 0.5f) + up * 0.5f,
                    _ => position - right * (ConstructionModule * 0.5f) + up * 0.5f,
                };
                Vector3 halfExtents = probe == 0
                    ? new Vector3(ConstructionModule * 0.5f, 0.75f, 0.65f)
                    : new Vector3(0.5f, 1.2f, ConstructionModule * 0.45f);
                int count = Physics.OverlapBoxNonAlloc(centre, halfExtents,
                    s_placementOverlapProbe, rotation, ~0, QueryTriggerInteraction.Ignore);
                if (HasRoofSupportInOverlap(s_placementOverlapProbe, count, ignore)) return true;
                if (count >= s_placementOverlapProbe.Length)
                {
                    Collider[] overflow = Physics.OverlapBox(centre, halfExtents,
                        rotation, ~0, QueryTriggerInteraction.Ignore);
                    if (HasRoofSupportInOverlap(overflow, overflow.Length, ignore)) return true;
                }
            }
            return false;
        }

        private static bool HasRoofSupportInOverlap(Collider[] overlaps, int count,
            PlacedTieredBlock ignore)
        {
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlaps[i];
                PlacedTieredBlock block = collider != null
                    ? collider.GetComponentInParent<PlacedTieredBlock>() : null;
                if (block != null && block.definition != null && block != ignore) return true;
            }
            return false;
        }

        /// <summary>
        /// Roof panels chain exactly like stairs: aim up/down the slope of an
        /// existing panel and the next one continues the pitch a full module out
        /// and a full storey up or down; aim at its side and the next panel
        /// extends the ridge line at the same level. Flat triangular caps and
        /// pyramid/corner pieces chain level in every direction.
        /// </summary>
        private bool TryComputeRoofChainTransform(
            RaycastHit hit,
            PlacedTieredBlock host,
            BuildFamily incoming,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (host == null) return false;

            Vector3 up = host.transform.up.normalized;
            Vector3 localHit = host.transform.InverseTransformPoint(hit.point);
            rotation = Quaternion.AngleAxis(_ghostYaw, up) * host.transform.rotation;

            bool hostSloped = host.definition.family == BuildFamily.SlantedRoof
                || host.definition.family == BuildFamily.SlantedTriangularRoof;
            bool lateral = Mathf.Abs(localHit.x) > Mathf.Abs(localHit.z);
            if (lateral || !hostSloped)
            {
                // Extend along the ridge (or any edge of a level panel).
                Vector3 step = lateral
                    ? host.transform.right * (Mathf.Sign(localHit.x) * ConstructionModule)
                    : host.transform.forward * (Mathf.Sign(localHit.z == 0f ? 1f : localHit.z) * ConstructionModule);
                position = host.transform.position + step;
                return true;
            }

            // Continue the pitch: uphill is the host's +Z, matching the slanted
            // panel that rises one storey across one module.
            bool chainUpward = localHit.z >= 0f;
            Vector3 run = host.transform.forward * ConstructionModule;
            Vector3 rise = up * ConstructionStorey;
            position = host.transform.position + (chainUpward ? run + rise : -run - rise);
            return true;
        }

        private bool TryComputeStairChainTransform(
            RaycastHit hit,
            PlacedTieredBlock host,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (host == null) return false;

            Vector3 up = host.transform.up.normalized;
            Vector3 localHit = host.transform.InverseTransformPoint(hit.point);
            bool chainUpward = localHit.z >= 0f || Vector3.Dot(hit.normal.normalized, up) > 0.45f;

            rotation = Quaternion.AngleAxis(_ghostYaw, up) * host.transform.rotation;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 vertical = up * ConstructionStorey;
            position = host.transform.position + (chainUpward
                ? forward * ConstructionModule + vertical
                : -forward * ConstructionModule - vertical);
            return true;
        }

        private bool TryComputeStairTransform(
            RaycastHit hit,
            BuildSocket socket,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (socket == null) return false;

            bool perimeterSocket = socket.side == SocketSide.TopNorth
                || socket.side == SocketSide.TopSouth
                || socket.side == SocketSide.TopEast
                || socket.side == SocketSide.TopWest;
            var host = socket.GetComponentInParent<PlacedTieredBlock>();
            bool doorwayThreshold = host != null
                && host.definition != null
                && host.definition.family == BuildFamily.Doorway
                && socket.side == SocketSide.Bottom;
            if (!perimeterSocket && !doorwayThreshold) return false;

            Vector3 up = socket.transform.up.normalized;
            // Side-face aiming deliberately chooses a descending staircase whose
            // upper tread meets the selected edge. Aiming at the horizontal top
            // chooses the opposite, upward-growing orientation. Door thresholds
            // always place the useful exterior staircase down from the doorway.
            bool descending = doorwayThreshold
                || Mathf.Abs(Vector3.Dot(hit.normal.normalized, up)) < 0.55f;

            Quaternion baseRotation = descending
                ? Quaternion.AngleAxis(180f, up) * socket.transform.rotation
                : socket.transform.rotation;
            rotation = Quaternion.AngleAxis(_ghostYaw, up) * baseRotation;

            Vector3 stairForward = rotation * Vector3.forward;
            float halfRun = ConstructionModule * 0.5f;
            if (descending)
            {
                // The high edge is local +Z. Keep that edge on the socket while
                // moving the stair root one complete storey below the threshold.
                position = socket.transform.position
                    - stairForward * halfRun
                    - up * ConstructionStorey;
            }
            else
            {
                // The low edge is local -Z. Keep it on the selected top edge and
                // let the staircase rise outward to the next storey.
                position = socket.transform.position + stairForward * halfRun;
            }
            return true;
        }

        private void ConsiderSocketCandidates(Collider[] colliders, int count, BuildFamily placedFamily,
            Vector3 hitPoint, ref BuildSocket bestSocket, ref float bestSqr)
        {
            for (int i = 0; i < count; i++)
            {
                var collider = colliders[i];
                var host = collider != null ? collider.GetComponentInParent<PlacedTieredBlock>() : null;
                if (host == null || host.definition == null || !_socketHosts.Add(host)) continue;

                _socketScratch.Clear();
                host.GetComponentsInChildren<BuildSocket>(true, _socketScratch);
                for (int socketIndex = 0; socketIndex < _socketScratch.Count; socketIndex++)
                {
                    var socket = _socketScratch[socketIndex];
                    if (socket == null || !BuildSocketCompat.AreCompatible(host.definition.family, socket.side, placedFamily))
                        continue;
                    float distance = (socket.transform.position - hitPoint).sqrMagnitude;
                    if (distance < bestSqr) { bestSqr = distance; bestSocket = socket; }
                }
            }
        }

        private bool ValidateOverlap(Vector3 pos, BuildFamily family, PlacedTieredBlock socketHost = null)
        {
            // Every ghost path funnels through here, so this is the one spot that
            // knows which frame a fitting is being hung in. Place() reads it to
            // arm the fitting against that host.
            _ghostHost = socketHost;
            // Don't overlap the player.
            if (Vector3.Distance(pos, transform.position) < 0.6f) return false;
            if (BuildFamilyInfo.IsRoofPanel(family) && (_structuralSpan < 1 || _structuralSpan > 2)) return false;

            int count = Physics.OverlapBoxNonAlloc(pos, Vector3.one * 0.45f,
                s_placementOverlapProbe, Quaternion.identity, ~0, QueryTriggerInteraction.UseGlobal);
            for (int i = 0; i < count; i++)
                if (!IsOverlapColliderAllowed(s_placementOverlapProbe[i], socketHost, family, pos)) return false;
            if (count < s_placementOverlapProbe.Length) return true;

            // Preserve exact legacy behaviour if a very dense area fills the probe.
            foreach (var collider in Physics.OverlapBox(pos, Vector3.one * 0.45f, Quaternion.identity))
                if (!IsOverlapColliderAllowed(collider, socketHost, family, pos)) return false;
            return true;
        }

        private int ResolveStructuralSpan(PlacedTieredBlock host, BuildFamily incoming)
        {
            if (host == null || host.definition == null) return 0;
            BuildFamily hostFamily = host.definition.family;
            var load = host.GetComponent<StructuralLoadState>();

            // Component identity wins over stale serialized definition data.
            if (load != null && load.armed)
            {
                bool compatibleLoad = BuildFamilyInfo.IsRoofPanel(incoming)
                    ? BuildFamilyInfo.IsRoofPanel(load.loadFamily)
                    : BuildFamilyInfo.IsDeck(load.loadFamily);
                if (compatibleLoad)
                {
                    _structuralAnchor = load.supportAnchor;
                    return load.spanFromSupport + 1;
                }
            }

            bool adjustableSupport = host.TryGetComponent<AdjustablePillar>(out var adjustablePillar);
            if (adjustableSupport && !adjustablePillar.IsSupportGrounded())
                return 0; // a hanging pillar chain carries nothing yet
            if (adjustableSupport || StructuralLoadState.IsVerticalSupport(hostFamily))
            {
                if (!adjustableSupport
                    && !StructuralLoadState.TryResolveSupportBase(host, out var carryingDeck))
                {
                    // A wall standing on a suspended deck is a pass-through, not
                    // a fresh support: the piece above continues the deck's own
                    // span. This closes the floor-floor-wall-floor ladder that
                    // reset the cantilever forever.
                    if (carryingDeck == null || !carryingDeck.armed) return 0;
                    _structuralAnchor = carryingDeck.supportAnchor;
                    return carryingDeck.spanFromSupport + 1;
                }
                float height = hostFamily == BuildFamily.HalfWall ? HalfWallHeight : ConstructionStorey;
                if (adjustableSupport)
                    height = adjustablePillar.currentHeight;
                _structuralAnchor = host.transform.position + host.transform.up * height;
                return 1;
            }
            if (!BuildFamilyInfo.IsRoofPanel(incoming) && hostFamily == BuildFamily.Foundation)
            {
                _structuralAnchor = host.transform.position + host.transform.up * 1.125f;
                return 1;
            }

            return 0;
        }

        private static bool HasRoofSupport(Vector3 position)
        {
            // Count roof modules, not metres to any support in a broad sphere.
            // Candidate = depth 1; one neighbouring roof = depth 2. We never walk
            // through a second neighbour, so a long roof chain cannot relay support
            // forever merely because every panel touches another panel.
            const float searchRadius = ConstructionModule * 2f + ConstructionStorey;
            const float neighbourDistance = ConstructionModule + 0.45f;
            var colliders = Physics.OverlapSphere(position, searchRadius, ~0, QueryTriggerInteraction.Ignore);
            var blocks = new List<PlacedTieredBlock>();
            var unique = new HashSet<PlacedTieredBlock>();
            for (int i = 0; i < colliders.Length; i++)
            {
                var block = colliders[i] != null ? colliders[i].GetComponentInParent<PlacedTieredBlock>() : null;
                if (block != null && block.definition != null && unique.Add(block)) blocks.Add(block);
            }

            if (HasDirectRoofSupport(position, blocks)) return true;

            float neighbourSqr = neighbourDistance * neighbourDistance;
            for (int i = 0; i < blocks.Count; i++)
            {
                var roof = blocks[i];
                if (roof.definition.family != BuildFamily.Roof) continue;
                Vector3 delta = roof.transform.position - position;
                float vertical = Mathf.Abs(Vector3.Dot(delta, roof.transform.up));
                Vector3 planar = delta - roof.transform.up * Vector3.Dot(delta, roof.transform.up);
                if (vertical > 0.75f || planar.sqrMagnitude > neighbourSqr) continue;
                if (HasDirectRoofSupport(roof.transform.position, blocks)) return true;
            }
            return false;
        }

        private static bool HasDirectRoofSupport(Vector3 roofPosition, List<PlacedTieredBlock> blocks)
        {
            const float edgeReach = ConstructionModule * 0.707107f + 0.45f;
            float edgeReachSqr = edgeReach * edgeReach;
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                float height;
                switch (block.definition.family)
                {
                    case BuildFamily.Wall:
                    case BuildFamily.Doorway:
                    case BuildFamily.Window:
                    case BuildFamily.WallFrame:
                    case BuildFamily.Pillar:
                        height = ConstructionStorey;
                        break;
                    case BuildFamily.HalfWall:
                        height = HalfWallHeight;
                        break;
                    default:
                        continue;
                }

                Vector3 supportTop = block.transform.position + block.transform.up * height;
                Vector3 delta = roofPosition - supportTop;
                float vertical = Mathf.Abs(Vector3.Dot(delta, block.transform.up));
                Vector3 planar = delta - block.transform.up * Vector3.Dot(delta, block.transform.up);
                if (vertical <= 0.75f && planar.sqrMagnitude <= edgeReachSqr) return true;
            }
            return false;
        }

        private static bool IsOverlapColliderAllowed(Collider collider, PlacedTieredBlock socketHost, BuildFamily incoming, Vector3 placementPosition)
        {
            if (collider == null) return true;
            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                return false;

            // Block placement inside existing tiered buildings UNLESS we're
            // socket-snapping to that exact host (adjacent stacking is fine).
            var host = collider.GetComponentInParent<PlacedTieredBlock>();
            if (host == null || host == socketHost) return true;
            // A pillar is a flush joint piece: its top face deliberately meets
            // deck undersides, corners and wall lines, so a pillar neighbour
            // never vetoes structural placement.
            if (host.GetComponent<AdjustablePillar>() != null
                || (host.definition != null && host.definition.family == BuildFamily.Pillar))
                return true;
            if (socketHost != null && Vector3.Distance(host.transform.position, placementPosition) > 0.25f)
                return true;

            // A fitting occupies an opening whose root can touch the supporting
            // foundation/deck and the frame at once. Those authored tiered pieces
            // are expected neighbours; dynamic bodies remain rejected above.
            bool fitting = incoming == BuildFamily.Door || incoming == BuildFamily.GarageDoor
                || incoming == BuildFamily.WindowPane || incoming == BuildFamily.HatchLid
                || incoming == BuildFamily.Railing || incoming == BuildFamily.DoubleDoor
                || incoming == BuildFamily.Gate || incoming == BuildFamily.BigGate;
            return socketHost != null && fitting;
        }

        // ---------- Resource handling ----------
        // 14.40.0: building can draw materials from the mass-storage network
        // when the player carries a handheld Wireless Terminal inside an
        // authorized transmitter's range. Inventory is always spent FIRST;
        // only the shortfall is pulled from the network.
        private VoxelEngine.Storage.ServerRack BuildNetworkRack()
        {
            if (inventory == null) return null;
            return VoxelEngine.Storage.WirelessStorageAccess.TryGetRack(
                inventory,
                transform.position,
                VoxelEngine.Networking.NetworkSession.LocalPlayerId ?? "");
        }

        private static TierCost FullTierConstructionCost(TieredBlockDefinition definition, BuildTier tier)
        {
            var totals = new Dictionary<ItemDefinition, int>();
            AddTierCost(totals, definition != null ? definition.placeCost : null);
            if ((int)tier >= (int)BuildTier.Stone) AddTierCost(totals, definition.woodToStone);
            if ((int)tier >= (int)BuildTier.Iron) AddTierCost(totals, definition.stoneToIron);
            if ((int)tier >= (int)BuildTier.Steel) AddTierCost(totals, definition.ironToSteel);

            var ingredients = new List<Ingredient>(totals.Count);
            foreach (var pair in totals)
                if (pair.Key != null && pair.Value > 0)
                    ingredients.Add(new Ingredient { item = pair.Key, count = pair.Value });
            return new TierCost { items = ingredients.ToArray() };
        }

        private static void AddTierCost(Dictionary<ItemDefinition, int> totals, TierCost cost)
        {
            if (totals == null || cost?.items == null) return;
            foreach (var ingredient in cost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                totals.TryGetValue(ingredient.item, out int existing);
                totals[ingredient.item] = existing + ingredient.count;
            }
        }

        /// <summary>Charge the damaged fraction of original construction materials, rounded up to whole items.</summary>
        private static TierCost ProRateRepairCost(TierCost originalCost, float damagedFraction,
            int materialMultiplier = 1)
        {
            damagedFraction = Mathf.Clamp01(damagedFraction);
            materialMultiplier = Mathf.Max(1, materialMultiplier);
            var totals = new Dictionary<ItemDefinition, int>();
            if (originalCost?.items != null)
            {
                foreach (var ingredient in originalCost.items)
                {
                    if (ingredient.item == null || ingredient.count <= 0) continue;
                    int amount = Mathf.CeilToInt(ingredient.count * (float)materialMultiplier * damagedFraction);
                    if (amount <= 0) continue;
                    totals.TryGetValue(ingredient.item, out int existing);
                    totals[ingredient.item] = existing + amount;
                }
            }

            var ingredients = new List<Ingredient>(totals.Count);
            foreach (var pair in totals)
                if (pair.Key != null && pair.Value > 0)
                    ingredients.Add(new Ingredient { item = pair.Key, count = pair.Value });
            return new TierCost { items = ingredients.ToArray() };
        }

        private bool CanAfford(TierCost cost, int multiplier = 1)
        {
            multiplier = Mathf.Max(1, multiplier);
            if (cost == null || cost.items == null) return true;
            var rack = BuildNetworkRack();
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                int have = inventory.container.CountOf(ing.item);
                if (rack != null) have += rack.NetworkCount(ing.item.itemId);
                if (have < ing.count * multiplier) return false;
            }
            return true;
        }

        private void PayCost(TierCost cost, int multiplier = 1)
        {
            multiplier = Mathf.Max(1, multiplier);
            if (cost == null || cost.items == null) return;
            var rack = BuildNetworkRack();
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                int need = ing.count * multiplier;
                // Inventory first - the familiar cost path stays untouched.
                int fromInv = Mathf.Min(need, inventory.container.CountOf(ing.item));
                if (fromInv > 0)
                {
                    inventory.container.Remove(ing.item, fromInv);
                    need -= fromInv;
                }
                // Shortfall comes out of the storage network.
                if (need > 0 && rack != null)
                    rack.NetworkExtract(ing.item.itemId, need);
            }
        }

        /// <summary>
        /// Wall-type pieces and railings get the lightweight base audit so they
        /// collapse with the deck that carried them. Pillars are excluded: a
        /// hanging pillar chain is a deliberate build, governed by its own logic.
        /// </summary>
        private static bool RequiresBaseAudit(BuildFamily family)
            => family == BuildFamily.Railing
                || family == BuildFamily.TriangularWall || family == BuildFamily.TriangularWallInverted
                || family == BuildFamily.GateFrame || family == BuildFamily.BigGateFrame
                || family == BuildFamily.CompoundWall
                || (StructuralLoadState.IsVerticalSupport(family) && family != BuildFamily.Pillar);

        // ---------- Place ----------
        private void Place(TieredBlockDefinition def, Vector3 pos, Quaternion rot, float railingRise)
        {
            var go = Instantiate(def.GetPrefab(BuildTier.Wood), pos, rot);
            if (go.TryGetComponent<TieredRailing>(out var railing)) railing.Configure(railingRise);
            if (go.TryGetComponent<AdjustablePillar>(out var pillar)) pillar.Configure(_pillarHeight);
            go.name = $"{def.displayName} (Wood)";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, BuildTier.Wood);
            pb.ownerId = VoxelEngine.Networking.NetworkSession.LocalPlayerId ?? "";
            var load = go.GetComponent<StructuralLoadState>();
            if (load != null && _structuralSpan > 0) load.Arm(_structuralSpan, _structuralAnchor);
            else if (load == null && StructuralLoadState.IsFitting(def.family))
                go.AddComponent<StructuralLoadState>().ArmFitting(_ghostHost, def.family);
            else if (load == null && RequiresBaseAudit(def.family))
                go.AddComponent<StructuralLoadState>().ArmVertical(def.family);
            else if (load == null && (pillar != null || def.family == BuildFamily.Pillar))
                go.AddComponent<StructuralLoadState>().ArmPillar();
            TagStationPiece(go, def);
            // Satisfying placement thunk at the build location.
            VoxelEngine.FX.AudioManager.PlayAt(
                VoxelEngine.FX.SfxLibrary.Get(VoxelEngine.FX.Sfx.Place), pos,
                volume: 0.6f, pitch: UnityEngine.Random.Range(0.95f, 1.05f), maxDistance: 20f);
            // Multiplayer: the authority point for placement (14.4.0).
            VoxelEngine.Networking.BuildingSync.AnnouncePlaced(def, BuildTier.Wood, pos, rot,
                railingRise, _pillarHeight);
        }

        // ============================================================
        //                   PUBLIC API for Hammer
        // ============================================================
        public bool TryGetTieredRepairCost(PlacedTieredBlock target, out TierCost cost,
            out int repairAmount)
        {
            cost = null;
            repairAmount = 0;
            if (target == null || target.definition == null) return false;

            int maxHp = target.definition.GetStats(target.tier).hp;
            if (maxHp <= 0 || target.hp <= 0) return false;
            int currentHp = Mathf.Clamp(target.hp, 0, maxHp);
            repairAmount = maxHp - currentHp;
            if (repairAmount <= 0) return false;

            float damagedFraction = repairAmount / (float)maxHp;
            TierCost fullConstructionCost = FullTierConstructionCost(target.definition, target.tier);
            var sizedPillar = target.GetComponent<AdjustablePillar>();
            int materialMultiplier = sizedPillar != null
                ? Mathf.Max(1, Mathf.CeilToInt(sizedPillar.currentHeight / ConstructionStorey))
                : 1;
            cost = ProRateRepairCost(fullConstructionCost, damagedFraction, materialMultiplier);
            return true;
        }

        public bool TryGetBlockRepairCost(PlacedBlock target, out TierCost cost, out int repairAmount)
        {
            cost = null;
            repairAmount = 0;
            if (target == null || target.Item == null || target.Item.blockHealth <= 0) return false;
            // Ship/grid blocks keep their established Welder repair path.
            if (target.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>() != null
                || target.GetComponentInChildren<VoxelEngine.GridSystem.GridBlock>(true) != null)
                return false;

            int maxHp = target.Item.blockHealth;
            if (target.Hp <= 0) return false;
            int currentHp = Mathf.Clamp(target.Hp, 0, maxHp);
            repairAmount = maxHp - currentHp;
            if (repairAmount <= 0) return false;

            cost = ProRateRepairCost(new TierCost
            {
                // One BlockItem is consumed to place an ordinary static block.
                items = new[] { new Ingredient { item = target.Item, count = 1 } }
            }, repairAmount / (float)maxHp);
            return true;
        }

        public bool TryRepair(PlacedTieredBlock target)
        {
            if (target != null && !target.AllowsHammerAction())
            {
                VoxelEngine.Networking.SharedOwnership.ShowAccessDeniedToast("Repair Blocked", target.ownerId, VoxelEngine.Networking.AccessShareMode.Team);
                return false;
            }
            if (!TryGetTieredRepairCost(target, out var cost, out int amount)
                || inventory == null || inventory.container == null || !CanAfford(cost))
                return false;
            PayCost(cost);
            return target.Repair(amount) > 0;
        }

        public bool TryRepair(PlacedBlock target)
        {
            if (target != null && !VoxelEngine.Networking.SharedOwnership.AllowsOwnerOrTeam(target.ownerId))
            {
                VoxelEngine.Networking.SharedOwnership.ShowAccessDeniedToast("Repair Blocked", target.ownerId, VoxelEngine.Networking.AccessShareMode.Team);
                return false;
            }
            if (!TryGetBlockRepairCost(target, out var cost, out int amount)
                || inventory == null || inventory.container == null || !CanAfford(cost))
                return false;
            PayCost(cost);
            return target.Repair(amount) > 0;
        }

        /// <summary>Inventory plus currently authorized wireless-storage stock for repair prompts.</summary>
        public int GetAvailableBuildCount(ItemDefinition item)
        {
            if (item == null) return 0;
            int have = inventory != null && inventory.container != null
                ? inventory.container.CountOf(item) : 0;
            var rack = BuildNetworkRack();
            if (rack != null) have += rack.NetworkCount(item.itemId);
            return have;
        }

        /// <summary>Raises a piece by exactly one tier. Kept for every existing caller that
        /// means "one step"; the hammer itself now names a destination.</summary>
        public bool TryUpgrade(PlacedTieredBlock target)
        {
            if (target == null) return false;
            return TryUpgradeTo(target, TieredBlockDefinition.NextTier(target.tier));
        }

        /// <summary>
        /// 17.45.0-dev: takes a piece straight to the tier the player selected on the hammer's
        /// tier wheel, charging the SINGLE material step that makes that tier.
        ///
        /// It used to be one tier per click, so a Wood wall taken to Steel cost three clicks
        /// and three full payments - Stone, then Iron, then Steel - which is the cumulative
        /// price of wandering there. A player who names Steel has skipped the stopovers, and
        /// is charged the Steel step alone.
        ///
        /// Destination at or below the piece's own tier does nothing: the hammer upgrades,
        /// it never downgrades, and a piece is never charged for standing still.
        /// </summary>
        public bool TryUpgradeTo(PlacedTieredBlock target, BuildTier destination)
        {
            if (target == null || target.definition == null) return false;
            if (!target.AllowsHammerAction())
            {
                VoxelEngine.Networking.SharedOwnership.ShowAccessDeniedToast("Upgrade Blocked", target.ownerId, VoxelEngine.Networking.AccessShareMode.Team);
                return false;
            }
            if (destination <= target.tier) return false;
            if (destination > BuildTier.Steel) destination = BuildTier.Steel;
            if (target.definition.GetPrefab(destination) == null) return false;

            var cost = HammerTierSelection.CostToReach(target.definition, destination);
            var sizedPillar = target.GetComponent<AdjustablePillar>();
            int costMultiplier = sizedPillar != null
                ? Mathf.Max(1, Mathf.CeilToInt(sizedPillar.currentHeight / ConstructionStorey))
                : 1;
            if (!CanAfford(cost, costMultiplier)) return false;
            PayCost(cost, costMultiplier);

            // Replace the prefab in place: spawn the new tier at the same transform, copy state.
            Vector3 pos = target.transform.position;
            Quaternion rot = target.transform.rotation;
            var def = target.definition;
            var oldLoad = target.GetComponent<StructuralLoadState>();
            int oldSpan = oldLoad != null && oldLoad.armed ? oldLoad.spanFromSupport : 0;
            Vector3 oldAnchor = oldLoad != null ? oldLoad.supportAnchor : Vector3.zero;
            bool oldVertical = oldLoad != null && oldLoad.armed && oldLoad.verticalPiece;
            bool oldFitting = oldLoad != null && oldLoad.armed && oldLoad.fittingPiece;
            bool oldPillarAudit = oldLoad != null && oldLoad.armed && oldLoad.pillarPiece;
            PlacedTieredBlock oldHost = oldFitting ? oldLoad.hostPiece : null;
            string oldOwnerId = target.ownerId ?? "";
            int oldAccessMode = target.accessMode;

            // Upgrading a frame rebuilds its GameObject; any fitting armed against
            // the old object must be re-pointed at the replacement or it would
            // read its host as destroyed and wrongly collapse.
            var dependentFittings = new List<StructuralLoadState>();
            foreach (var near in Physics.OverlapSphere(pos, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                var nearLoad = near != null ? near.GetComponentInParent<StructuralLoadState>() : null;
                if (nearLoad != null && nearLoad.armed && nearLoad.fittingPiece
                    && nearLoad.hostPiece == target && !dependentFittings.Contains(nearLoad))
                    dependentFittings.Add(nearLoad);
            }
            var oldPillar = target.GetComponent<AdjustablePillar>();
            float oldPillarHeight = oldPillar != null ? oldPillar.currentHeight : ConstructionStorey;
            Destroy(target.gameObject);

            var go = Instantiate(def.GetPrefab(destination), pos, rot);
            go.name = $"{def.displayName} ({destination})";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, destination);
            pb.ownerId = oldOwnerId;
            pb.accessMode = oldAccessMode;
            var newLoad = go.GetComponent<StructuralLoadState>();
            if (newLoad != null && oldSpan > 0) newLoad.Arm(oldSpan, oldAnchor);
            else if (oldVertical)
            {
                if (newLoad == null) newLoad = go.AddComponent<StructuralLoadState>();
                newLoad.ArmVertical(def.family);
            }
            else if (oldFitting)
            {
                if (newLoad == null) newLoad = go.AddComponent<StructuralLoadState>();
                newLoad.ArmFitting(oldHost, def.family);
            }
            else if (oldPillarAudit)
            {
                if (newLoad == null) newLoad = go.AddComponent<StructuralLoadState>();
                newLoad.ArmPillar();
            }
            for (int i = 0; i < dependentFittings.Count; i++)
                if (dependentFittings[i] != null) dependentFittings[i].hostPiece = pb;
            var newPillar = go.GetComponent<AdjustablePillar>();
            if (newPillar != null) newPillar.Configure(oldPillarHeight);
            // Re-tag on upgrade: the upgrade path destroys and rebuilds the object, so a
            // station hull would silently stop being a station piece the first time it was
            // upgraded from wood to steel.
            TagStationPiece(go, def);
            // Multiplayer: the authority point for upgrades (14.4.0).
            VoxelEngine.Networking.BuildingSync.AnnounceUpgraded(def.family, pos, destination);
            return true;
        }

        /// <summary>
        /// Attaches (or refreshes) the station marker when the placed definition belongs to
        /// the orbital station group. Ordinary structural pieces get nothing, so the
        /// everyday building path is completely untouched.
        /// </summary>
        private static void TagStationPiece(GameObject go, TieredBlockDefinition def)
        {
            if (go == null || def == null) return;
            if (BuildFamilyInfo.GroupOf(def.family) != BuildFamilyGroup.OrbitalStation) return;

            var piece = go.GetComponent<StationPiece>();
            if (piece == null) piece = go.AddComponent<StationPiece>();
            piece.Configure(def.family);
        }

        public void Rotate(PlacedTieredBlock target, float delta)
        {
            if (target == null) return;
            Vector3 planetUp = GravityProvider.GetUp(target.transform.position);
            target.transform.rotation = Quaternion.AngleAxis(delta, planetUp) * target.transform.rotation;
        }

        // ============================================================
        //                      Ghost material
        // ============================================================
        private static Material MakeGhostMaterial(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Surface"))   m.SetFloat("_Surface", 1f); // transparent
            if (m.HasProperty("_Blend"))     m.SetFloat("_Blend",   0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }
        private static void StripGhost(GameObject root)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var rb  in root.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            // Hide socket gizmos in the ghost.
            foreach (var sock in root.GetComponentsInChildren<BuildSocket>(true)) sock.enabled = false;
        }
        private void ApplyGhostMaterialIfChanged(Material mat)
        {
            if (_ghost == null || mat == null || _appliedGhostMaterial == mat) return;
            ApplyGhostMaterial(_ghost, mat);
            _appliedGhostMaterial = mat;
        }

        private static void ApplyGhostMaterial(GameObject root, Material mat)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
            }
        }
    }
}
