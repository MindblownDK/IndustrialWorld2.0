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
        private readonly List<BuildSocket> _socketScratch = new(8);
        private Quaternion _ghostRot = Quaternion.identity;
        private float _railingRise;

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

            ComputeGhostTransform(hit, def);
            _ghost.transform.SetPositionAndRotation(_ghostPos, _ghostRot);
            if (_ghost.TryGetComponent<TieredRailing>(out var ghostRailing))
                ghostRailing.Configure(_railingRise);
            ApplyGhostMaterialIfChanged(_ghostValid ? _matValid : _matInvalid);

            // Place on the standard build action (RMB by default).
            if (_ghostValid && GameSettings.WasPressed(InputAction.Build))
            {
                if (CanAfford(def.placeCost))
                {
                    PayCost(def.placeCost);
                    Place(def, _ghostPos, _ghostRot, _railingRise);
                    // The feedback HUD receives the primary cost directly; building a
                    // second formatted summary here was unused work on every placement.
                    VoxelEngine.UI.BuildFeedbackHud.ShowBlockPlaced(
                        def.displayName, def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].item : null,
                        def.placeCost?.items?.Length > 0 ? def.placeCost.items[0].count : 0);
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
        private void ComputeGhostTransform(RaycastHit hit, TieredBlockDefinition def)
        {
            _railingRise = 0f;
            // 1) Try socket snap: look for the nearest BuildSocket within socketSnapRadius
            //    around the hit point that accepts this family.
            BuildSocket bestSocket = null;
            float bestSqr = socketSnapRadius * socketSnapRadius;

            var directHost = hit.collider != null ? hit.collider.GetComponentInParent<PlacedTieredBlock>() : null;
            if (def.family == BuildFamily.Stairs &&
                directHost != null && directHost.definition != null && directHost.definition.family == BuildFamily.Stairs &&
                TryComputeStairChainTransform(hit, directHost, out _ghostPos, out _ghostRot))
            {
                _ghostValid = ValidateOverlap(_ghostPos, def.family, directHost);
                return;
            }

            // Structural deck placement is resolved from the piece that was actually
            // aimed at. A wide search can see sockets through a wall or on the deck
            // behind it, making an unrelated centre socket win by a few centimetres.
            // These three common joins have an unambiguous answer in the host frame.
            if (directHost != null && directHost.definition != null &&
                TryComputeStructuralDeckTransform(hit, directHost, def.family, out _ghostPos, out _ghostRot))
            {
                _ghostValid = ValidateOverlap(_ghostPos, def.family, directHost);
                return;
            }

            _socketHosts.Clear();
            int socketCandidateCount = Physics.OverlapSphereNonAlloc(hit.point, socketSnapRadius,
                s_socketOverlapProbe, ~0, QueryTriggerInteraction.UseGlobal);
            ConsiderSocketCandidates(s_socketOverlapProbe, socketCandidateCount, def.family, hit.point,
                ref bestSocket, ref bestSqr);
            if (socketCandidateCount >= s_socketOverlapProbe.Length)
            {
                // Retain the former exhaustive result if the reusable local probe fills.
                var overflow = Physics.OverlapSphere(hit.point, socketSnapRadius, ~0, QueryTriggerInteraction.UseGlobal);
                ConsiderSocketCandidates(overflow, overflow.Length, def.family, hit.point,
                    ref bestSocket, ref bestSqr);
            }

            if (bestSocket != null)
            {
                var socketHost = bestSocket.GetComponentInParent<PlacedTieredBlock>();

                if (def.family == BuildFamily.Stairs &&
                    TryComputeStairTransform(hit, bestSocket, out _ghostPos, out _ghostRot))
                {
                    _ghostValid = ValidateOverlap(_ghostPos, def.family, socketHost);
                    return;
                }

                _ghostPos = bestSocket.transform.position;
                // Preserve the host/socket basis exactly. Reconstructing from world
                // Euler yaw introduced small rotational drift on spherical surfaces.
                Vector3 socketUp = bestSocket.transform.up;
                _ghostRot = Quaternion.AngleAxis(_ghostYaw, socketUp) * bestSocket.transform.rotation;
                _ghostValid = ValidateOverlap(_ghostPos, def.family, socketHost);
                return;
            }

            // 2) Fall back to grid snap or free placement on the hit surface.
            // Construction roots represent the bottom/hinge plane, not the center
            // of a module, so snap to grid intersections instead of cell centers.
            float surfaceOffset = def.family == BuildFamily.Foundation ? 0.50f : 0.02f;
            Vector3 raw = hit.point + hit.normal * surfaceOffset;

            if (gridSnap)
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
                    float snappedAlt = Mathf.Round(altitude / gridSize) * gridSize;

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
                        Mathf.Round(raw.y / gridSize) * gridSize,
                        Mathf.Round(raw.z / gridSize) * gridSize);
                }
            }
            else
            {
                _ghostPos = raw;
            }

            _ghostRot = GravityProvider.GetSurfaceRotation(_ghostPos, _ghostYaw);
            _ghostValid = ValidateOverlap(_ghostPos, def.family);
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
            bool incomingDeck = incoming == BuildFamily.Floor || incoming == BuildFamily.FloorHatch;
            bool wallHost = hostFamily == BuildFamily.Wall
                || hostFamily == BuildFamily.Doorway
                || hostFamily == BuildFamily.Window
                || hostFamily == BuildFamily.HalfWall
                || hostFamily == BuildFamily.WallFrame;

            Vector3 localHit = host.transform.InverseTransformPoint(hit.point);
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

            if ((incomingDeck || incoming == BuildFamily.Roof) && wallHost)
            {
                float height = hostFamily == BuildFamily.HalfWall ? HalfWallHeight : ConstructionStorey;
                float side = ResolveFaceSide(localHit.z, Vector3.Dot(hit.normal, host.transform.forward));

                float roofLift = incoming == BuildFamily.Roof ? 0.18f : 0f;
                position = host.transform.position
                    + host.transform.up * (height + roofLift)
                    + host.transform.forward * (side * ConstructionModule * 0.5f);
                rotation = Quaternion.AngleAxis(_ghostYaw, host.transform.up) * host.transform.rotation;
                return true;
            }

            bool incomingWall = incoming == BuildFamily.Wall || incoming == BuildFamily.HalfWall
                || incoming == BuildFamily.Doorway || incoming == BuildFamily.Window
                || incoming == BuildFamily.WallFrame;
            if (incomingWall &&
                (hostFamily == BuildFamily.Foundation || hostFamily == BuildFamily.Floor || hostFamily == BuildFamily.FloorHatch))
            {
                float surface = hostFamily == BuildFamily.Foundation ? 1.125f : 0.42f;
                bool edgeX = Mathf.Abs(localHit.x) > Mathf.Abs(localHit.z);
                float side = edgeX
                    ? (Mathf.Approximately(localHit.x, 0f) ? 1f : Mathf.Sign(localHit.x))
                    : (Mathf.Approximately(localHit.z, 0f) ? 1f : Mathf.Sign(localHit.z));
                position = host.transform.position + host.transform.up * surface
                    + (edgeX ? host.transform.right : host.transform.forward) * (side * ConstructionModule * 0.5f);
                float edgeYaw = edgeX ? side * 90f : (side < 0f ? 180f : 0f);
                rotation = Quaternion.AngleAxis(_ghostYaw + edgeYaw, host.transform.up) * host.transform.rotation;
                return true;
            }

            if (incoming == BuildFamily.Pillar &&
                (hostFamily == BuildFamily.Foundation || hostFamily == BuildFamily.Floor || hostFamily == BuildFamily.FloorHatch))
            {
                float surface = hostFamily == BuildFamily.Foundation ? 1.125f : 0.42f;
                Vector3 edge = NearestCentreOrEdge(localHit, ConstructionModule * 0.5f);
                position = host.transform.position + host.transform.up * surface
                    + host.transform.right * edge.x + host.transform.forward * edge.z;
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

        private static float ResolveFaceSide(float localAxis, float normalDot)
        {
            if (Mathf.Abs(localAxis) > 0.08f) return Mathf.Sign(localAxis);
            if (Mathf.Abs(normalDot) > 0.01f) return Mathf.Sign(normalDot);
            return 1f;
        }

        private static Vector3 NearestCentreOrEdge(Vector3 localHit, float halfModule)
        {
            // The middle owns the central pillar socket. Outside the middle third,
            // the nearest cardinal edge wins; corners never create diagonal pillars.
            float ax = Mathf.Abs(localHit.x);
            float az = Mathf.Abs(localHit.z);
            if (Mathf.Max(ax, az) < halfModule * 0.34f) return Vector3.zero;
            if (ax > az) return new Vector3(Mathf.Sign(localHit.x) * halfModule, 0f, 0f);
            return new Vector3(0f, 0f, Mathf.Sign(localHit.z) * halfModule);
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
            // Don't overlap the player.
            if (Vector3.Distance(pos, transform.position) < 0.6f) return false;

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

        private static bool IsOverlapColliderAllowed(Collider collider, PlacedTieredBlock socketHost, BuildFamily incoming, Vector3 placementPosition)
        {
            if (collider == null) return true;
            if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                return false;

            // Block placement inside existing tiered buildings UNLESS we're
            // socket-snapping to that exact host (adjacent stacking is fine).
            var host = collider.GetComponentInParent<PlacedTieredBlock>();
            if (host == null || host == socketHost) return true;
            if (socketHost != null && Vector3.Distance(host.transform.position, placementPosition) > 0.25f)
                return true;

            // A fitting occupies an opening whose root can touch the supporting
            // foundation/deck and the frame at once. Those authored tiered pieces
            // are expected neighbours; dynamic bodies remain rejected above.
            bool fitting = incoming == BuildFamily.Door || incoming == BuildFamily.GarageDoor
                || incoming == BuildFamily.WindowPane || incoming == BuildFamily.HatchLid
                || incoming == BuildFamily.Railing;
            return socketHost != null && fitting;
        }

        // ---------- Resource handling ----------
        private bool CanAfford(TierCost cost)
        {
            if (cost == null || cost.items == null) return true;
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (inventory.container.CountOf(ing.item) < ing.count) return false;
            }
            return true;
        }

        private void PayCost(TierCost cost)
        {
            if (cost == null || cost.items == null) return;
            foreach (var ing in cost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                inventory.container.Remove(ing.item, ing.count);
            }
        }

        // ---------- Place ----------
        private void Place(TieredBlockDefinition def, Vector3 pos, Quaternion rot, float railingRise)
        {
            var go = Instantiate(def.GetPrefab(BuildTier.Wood), pos, rot);
            if (go.TryGetComponent<TieredRailing>(out var railing)) railing.Configure(railingRise);
            go.name = $"{def.displayName} (Wood)";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, BuildTier.Wood);
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
            if (!CanAfford(cost)) return false;
            PayCost(cost);

            // Replace the prefab in place: spawn the new tier at the same transform, copy state.
            Vector3 pos = target.transform.position;
            Quaternion rot = target.transform.rotation;
            var def = target.definition;
            Destroy(target.gameObject);

            var go = Instantiate(def.GetPrefab(next), pos, rot);
            go.name = $"{def.displayName} ({next})";
            var pb = go.GetComponent<PlacedTieredBlock>();
            if (pb == null) pb = go.AddComponent<PlacedTieredBlock>();
            pb.Initialize(def, next);
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
