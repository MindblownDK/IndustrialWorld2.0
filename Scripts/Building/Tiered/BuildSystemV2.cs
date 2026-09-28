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

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _matValid   = MakeGhostMaterial(new Color(0.40f, 0.90f, 0.45f, ghostAlpha));
            _matInvalid = MakeGhostMaterial(new Color(0.95f, 0.30f, 0.25f, ghostAlpha));
        }

        private void Update()
        {
            bool buildWheelHeld = GameSettings.IsHeld(InputAction.BuildWheel);
            if (VoxelEngine.UI.UIState.IsBlocking && !buildWheelHeld) { HideGhost(); return; }

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
            if (ctrl && Mathf.Abs(wheel) > 0.01f) _ghostYaw += Mathf.Sign(wheel) * yawStep;
            if (GameSettings.WasPressed(InputAction.BuildRotate)) _ghostYaw += yawStep;

            UpdateGhost();
        }

        // ---------- Ghost / placement ----------
        private void UpdateGhost()
        {
            if (inventory == null || registry == null) { HideGhost(); return; }
            // Build mode now requires holding the Hammer AND having picked a family in the wheel.
            var stack = inventory.ActiveStack;
            bool holdingHammer = !stack.IsEmpty && stack.item is VoxelEngine.Items.ToolItem t && t.toolType == VoxelEngine.Items.ToolType.Other && stack.item.GetType().Name == "Hammer";

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
                _ghost.name = "BuildGhost";
                _appliedGhostMaterial = null;
                StripGhost(_ghost);
            }

            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!TryRaycastIgnoringSelf(ray, out var hit, reach))
            {
                _ghost.SetActive(false);
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

            // Place on the standard build action (RMB by default).
            if (_ghostValid && GameSettings.WasPressed(InputAction.Build))
            {
                int pillarCostMultiplier = activeFam.Value == BuildFamily.Pillar
                    ? Mathf.Max(1, Mathf.CeilToInt(_pillarHeight / ConstructionStorey))
                    : 1;
                if (CanAfford(def.placeCost, pillarCostMultiplier))
                {
                    PayCost(def.placeCost, pillarCostMultiplier);
                    Place(def, _ghostPos, _ghostRot, _railingRise);
                    // The feedback HUD receives the primary cost directly; building a
                    // second formatted summary here was unused work on every placement.
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(
                        def.displayName, def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].item : null,
                        def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].count * pillarCostMultiplier : 0);
                }
            }
        }

        private void HideGhost()
        {
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
            const float surfaceOffset = 0.02f;
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
            // Eave box first, then the two rake edges (for panels joining sideways).
            Vector3[] centres =
            {
                position + fwd * (-ConstructionModule * 0.5f + 0.2f) - up * 0.1f,
                position + right * (ConstructionModule * 0.5f) + up * 0.5f + fwd * 0f,
                position - right * (ConstructionModule * 0.5f) + up * 0.5f + fwd * 0f,
            };
            Vector3[] halves =
            {
                new(ConstructionModule * 0.5f, 0.75f, 0.65f),
                new(0.5f, 1.2f, ConstructionModule * 0.45f),
                new(0.5f, 1.2f, ConstructionModule * 0.45f),
            };
            for (int probe = 0; probe < centres.Length; probe++)
            {
                var overlaps = Physics.OverlapBox(centres[probe], halves[probe], rotation,
                    ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < overlaps.Length; i++)
                {
                    var block = overlaps[i] != null ? overlaps[i].GetComponentInParent<PlacedTieredBlock>() : null;
                    if (block != null && block.definition != null && block != ignore) return true;
                }
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
        private bool CanAfford(TierCost cost, int multiplier = 1)
        {
            multiplier = Mathf.Max(1, multiplier);
            if (cost == null || cost.items == null) return true;
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (inventory.container.CountOf(ing.item) < ing.count * multiplier) return false;
            }
            return true;
        }

        private void PayCost(TierCost cost, int multiplier = 1)
        {
            multiplier = Mathf.Max(1, multiplier);
            if (cost == null || cost.items == null) return;
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                inventory.container.Remove(ing.item, ing.count * multiplier);
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
        }

        // ============================================================
        //                   PUBLIC API for Hammer
        // ============================================================
        public bool TryUpgrade(PlacedTieredBlock target)
        {
            if (target == null || target.definition == null) return false;
            if (target.tier == BuildTier.Steel) return false;

            BuildTier next = TieredBlockDefinition.NextTier(target.tier);
            var cost = target.definition.GetUpgradeCost(target.tier);
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

            var go = Instantiate(def.GetPrefab(next), pos, rot);
            go.name = $"{def.displayName} ({next})";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, next);
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
