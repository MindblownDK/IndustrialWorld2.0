// Assets/Scripts/VoxelEngine/Networks/WrenchTool.cs
//
// Player tool for connecting/disconnecting network anchors and cables.
// LMB on an anchor = select first, then LMB on second = connect them.
// RMB on an anchor = disconnect from ALL connections.
// Shift+LMB = disconnect from SPECIFIC connection (the one you click on).
// Shift+RMB = toggle face port configuration (for machines with PortConfig).
// Uses raycasting from the player camera.

using System.Collections.Generic;
using UnityEngine;
// Support BOTH the standard Unity define (ENABLE_INPUT_SYSTEM) AND the project's
// own version-define from VoxelEngine.asmdef (VE_HAS_INPUT_SYSTEM). When either
// is on, we route shift-detection through the new Input System; otherwise we
// fall back to legacy. This makes the wrench work in every project configuration.
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using VoxelEngine.Items;
using VoxelEngine.Transport;

namespace VoxelEngine.Networks
{
    [CreateAssetMenu(menuName = "VoxelEngine/Items/Wrench", fileName = "Tool_Wrench")]
    public class WrenchTool : ToolItem
    {
        public WrenchTool() { toolType = ToolType.Other; maxDurability = 1000; maxStack = 1; }
    }

    /// <summary>
    /// Runtime wrench interaction handler. Auto-attached to PlayerInteractionTool.
    /// Supports connecting/disconnecting anchors and configuring port faces.
    /// </summary>
    public class WrenchInteraction
    {
        private ConnectionAnchor _selectedAnchor;
        private float _selectedTime;
        private const float SELECTION_TIMEOUT = 8f;

        // Visual indicator for selection
        private GameObject _selectionIndicator;

        /// <summary>
        /// Returns true if either Left or Right Shift is currently held.
        /// Guarded with try/catch so a misconfigured input backend can never
        /// crash the wrench — worst case, shift is simply treated as not held
        /// and the user falls back to the non-shift command.
        /// </summary>
        private static bool ShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            try
            {
                var kb = Keyboard.current;
                if (kb == null) return false;
                return (kb.leftShiftKey  != null && kb.leftShiftKey.isPressed)
                    || (kb.rightShiftKey != null && kb.rightShiftKey.isPressed);
            }
            catch { return false; }
#else
            try
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            catch { return false; } // Old Input Manager disabled — treat as not held.
#endif
        }

        // ── Cable / pipe selection state ──────────────────────────
        // The wrench works in two modes depending on what the player clicks:
        //   1) ConnectionAnchor  (data cables / machines that opted in)
        //   2) Plain conduit     (PowerCable / GasPipe / ItemPipe / WaterPipe /
        //                         DataCable) which use distance-auto-discovery
        // For mode 2 we remember the GameObject of the first click so the
        // second click can disconnect ONE specific pair via WrenchBlacklist.
        private GameObject _selectedConduit;

        /// <summary>
        /// Returns true if <paramref name="go"/> is any of the auto-discovery
        /// conduits the wrench can disconnect via the blacklist.
        /// </summary>
        private static bool IsWrenchableConduit(GameObject go)
        {
            if (go == null) return false;
            return go.GetComponentInParent<VoxelEngine.Power.PowerCable>()       != null
                || go.GetComponentInParent<VoxelEngine.Networks.DataCable>()     != null
                || go.GetComponentInParent<VoxelEngine.Gas.GasPipe>()            != null
                || go.GetComponentInParent<VoxelEngine.Transport.ItemPipe>()     != null
                || go.GetComponentInParent<VoxelEngine.Fluids.WaterPipe>()       != null;
        }

        /// <summary>Resolve a hit to the "owning" conduit GameObject for blacklist purposes.</summary>
        private static GameObject ResolveConduitRoot(Collider c)
        {
            if (c == null) return null;
            var pc = c.GetComponentInParent<VoxelEngine.Power.PowerCable>();
            if (pc != null) return pc.gameObject;
            var dc = c.GetComponentInParent<VoxelEngine.Networks.DataCable>();
            if (dc != null) return dc.gameObject;
            var gp = c.GetComponentInParent<VoxelEngine.Gas.GasPipe>();
            if (gp != null) return gp.gameObject;
            var ip = c.GetComponentInParent<VoxelEngine.Transport.ItemPipe>();
            if (ip != null) return ip.gameObject;
            var wp = c.GetComponentInParent<VoxelEngine.Fluids.WaterPipe>();
            if (wp != null) return wp.gameObject;
            return null;
        }

        /// <summary>Resolve gas/liquid/item endpoints that a selected conduit may connect to.</summary>
        private static GameObject ResolveConduitEndpointRoot(Collider c)
        {
            if (c == null) return null;

            // Grid endpoints.
            var gb = c.GetComponentInParent<VoxelEngine.GridSystem.GridBlock>();
            if (gb != null)
            {
                if (gb.GetComponentInChildren<VoxelEngine.Gas.GasPipe>() != null
                    || gb.GetComponentInChildren<VoxelEngine.Transport.ItemPipe>() != null
                    || gb.GetComponentInChildren<VoxelEngine.Fluids.WaterPipe>() != null)
                    return null; // it is a conduit, already handled
                return gb.gameObject;
            }

            // Static endpoints.
            var gasTank = c.GetComponentInParent<VoxelEngine.Gas.GasTank>();
            if (gasTank != null) return gasTank.gameObject;
            var hydroEngine = c.GetComponentInParent<VoxelEngine.Gas.HydrogenEngine>();
            if (hydroEngine != null) return hydroEngine.gameObject;
            var electrolyser = c.GetComponentInParent<VoxelEngine.Gas.Electrolyser>();
            if (electrolyser != null) return electrolyser.gameObject;
            var waterTank = c.GetComponentInParent<VoxelEngine.Fluids.WaterTank>();
            if (waterTank != null) return waterTank.gameObject;
            var fluidNode = c.GetComponentInParent<VoxelEngine.Fluids.FluidNode>();
            if (fluidNode != null && fluidNode.Kind != VoxelEngine.Fluids.FluidNodeKind.Pipe) return fluidNode.gameObject;
            var routing = c.GetComponentInParent<ItemPortRouting>();
            if (routing != null) return routing.gameObject;
            var itemContainer = c.GetComponentInParent<IItemContainer>();
            if (itemContainer is Component comp) return comp.gameObject;
            return null;
        }

        /// <summary>
        /// Force every network manager to re-evaluate topology so wrench-induced
        /// changes are reflected immediately (without waiting for the next dirty tick).
        /// </summary>
        private static void NudgeAllNetworks()
        {
            try { VoxelEngine.Power.PowerNetworkManager.Instance?.SetDirty(); } catch { }
            try { VoxelEngine.Gas.GasNetwork.Instance?.SetDirty(); }            catch { }
            try { VoxelEngine.Transport.ItemPipeNetwork.Instance?.SetDirty(); } catch { }
            try { VoxelEngine.Fluids.FluidNetworkManager.Instance?.SetDirty(); } catch { }
        }

        /// <summary>Called when the player LMB with the wrench.</summary>
        public void OnUse(RaycastHit hit, Player.PlayerInteractionTool tool)
        {
            var anchor = hit.collider.GetComponentInParent<ConnectionAnchor>();
            var portConfig = hit.collider.GetComponentInParent<PortConfig>();

            // Shift+Click: Cycle port direction on machines
            if (ShiftHeld())
            {
                if (portConfig != null)
                {
                    CyclePortDirection(portConfig, hit);
                    return;
                }
            }

            // No anchor? Try the cable/pipe conduit selection workflow.
            if (anchor == null)
            {
                var conduit = ResolveConduitRoot(hit.collider);
                if (conduit != null)
                {
                    HandleConduitClick(conduit);
                    return;
                }

                // If a pipe/cable is already selected, a second click on a valid
                // machine/tank endpoint toggles that specific pipe ↔ endpoint link.
                if (_selectedConduit != null)
                {
                    var endpoint = ResolveConduitEndpointRoot(hit.collider);
                    if (endpoint != null)
                    {
                        ToggleConduitEndpoint(_selectedConduit, endpoint);
                        return;
                    }
                }

                ClearSelection();
                return;
            }

            if (_selectedAnchor == null)
            {
                // First click: select this anchor.
                _selectedAnchor = anchor;
                _selectedTime = Time.time;
                CreateSelectionIndicator(anchor.transform.position);
                UI.BuildFeedbackHud.Show("Wrench: Selected",
                    $"{anchor.networkType} anchor (click another to connect)",
                    null, UI.UITheme.AccentCyan);
            }
            else
            {
                // Second click: try to connect.
                if (_selectedAnchor == anchor)
                {
                    ClearSelection();
                    return;
                }

                // Check network type compatibility
                if (_selectedAnchor.networkType != anchor.networkType)
                {
                    UI.BuildFeedbackHud.Show("Cannot connect",
                        $"Different network types: {_selectedAnchor.networkType} vs {anchor.networkType}",
                        null, UI.UITheme.AccentRed);
                    ClearSelection();
                    return;
                }

                // Check PortConfig compatibility
                if (!CanConnect(_selectedAnchor, anchor))
                {
                    UI.BuildFeedbackHud.Show("Cannot connect",
                        "Port not accepting connections",
                        null, UI.UITheme.AccentRed);
                    ClearSelection();
                    return;
                }

                if (_selectedAnchor.TryConnect(anchor))
                {
                    UI.BuildFeedbackHud.Show("Connected!",
                        $"{anchor.networkType} network",
                        null, UI.UITheme.AccentGreen);
                    
                    // Trigger visual rebuild on both
                    NotifyVisualUpdate(_selectedAnchor);
                    NotifyVisualUpdate(anchor);
                }
                else
                {
                    UI.BuildFeedbackHud.Show("Cannot connect",
                        "Already connected or incompatible",
                        null, UI.UITheme.AccentRed);
                }
                ClearSelection();
            }
        }

        /// <summary>
        /// LMB workflow for plain conduits (PowerCable / GasPipe / ItemPipe /
        /// WaterPipe / DataCable). First click selects, second click on a
        /// DIFFERENT conduit blacklists the pair so they stop being auto-linked.
        /// Re-clicking the same pair (or shift+LMB) un-blacklists them.
        /// </summary>
        private void HandleConduitClick(GameObject conduit)
        {
            if (conduit == null) return;

            if (_selectedConduit == null)
            {
                _selectedConduit = conduit;
                _selectedTime    = Time.time;
                CreateSelectionIndicator(conduit.transform.position);
                UI.BuildFeedbackHud.Show("Wrench: Selected",
                    $"{conduit.name} — click an adjacent conduit to break/reconnect",
                    null, UI.UITheme.AccentCyan);
                return;
            }

            if (_selectedConduit == conduit)
            {
                ClearSelection();
                return;
            }

            // Second click — toggle blacklist for this pair.
            if (WrenchBlacklist.IsBlocked(_selectedConduit, conduit))
            {
                WrenchBlacklist.Unblock(_selectedConduit, conduit);
                UI.BuildFeedbackHud.Show("Reconnected",
                    $"{_selectedConduit.name} ↔ {conduit.name}", null, UI.UITheme.AccentGreen);
            }
            else
            {
                WrenchBlacklist.Block(_selectedConduit, conduit);
                UI.BuildFeedbackHud.Show("Disconnected",
                    $"{_selectedConduit.name} ↔ {conduit.name}", null, UI.UITheme.AccentOrange);
            }
            NudgeAllNetworks();
            ClearSelection();
        }

        private void ToggleConduitEndpoint(GameObject conduit, GameObject endpoint)
        {
            if (conduit == null || endpoint == null) return;

            if (WrenchBlacklist.IsBlocked(conduit, endpoint))
            {
                WrenchBlacklist.Unblock(conduit, endpoint);
                UI.BuildFeedbackHud.Show("Endpoint Reconnected",
                    $"{conduit.name} ↔ {endpoint.name}", null, UI.UITheme.AccentGreen);
            }
            else
            {
                WrenchBlacklist.Block(conduit, endpoint);
                UI.BuildFeedbackHud.Show("Endpoint Disconnected",
                    $"{conduit.name} ↔ {endpoint.name}", null, UI.UITheme.AccentOrange);
            }

            var visual = conduit.GetComponentInChildren<PipeVisualBuilder>();
            if (visual != null) visual.ForceRebuild();
            NudgeAllNetworks();
            ClearSelection();
        }

        /// <summary>Called when the player RMB with the wrench — disconnect.</summary>
        public void OnAltUse(RaycastHit hit)
        {
            var anchor = hit.collider.GetComponentInParent<ConnectionAnchor>();
            if (anchor == null)
            {
                // RMB on a plain conduit — break it from EVERY current neighbour.
                var conduit = ResolveConduitRoot(hit.collider);
                if (conduit != null)
                {
                    DisconnectAllConduitNeighbours(conduit);
                    return;
                }
                return;
            }

            // Shift+Click: Disconnect SPECIFIC connection (the one you clicked on)
            if (ShiftHeld())
            {
                if (_selectedAnchor != null && _selectedAnchor != anchor)
                {
                    // Disconnect selected from this specific anchor
                    if (_selectedAnchor.connections.Contains(anchor))
                    {
                        _selectedAnchor.Disconnect(anchor);
                        UI.BuildFeedbackHud.Show("Disconnected",
                            $"From selected {anchor.networkType}",
                            null, UI.UITheme.AccentOrange);
                        
                        NotifyVisualUpdate(_selectedAnchor);
                        NotifyVisualUpdate(anchor);
                        ClearSelection();
                        return;
                    }
                }
            }

            // Regular RMB: Disconnect ALL connections
            if (anchor.connections.Count > 0)
            {
                // Store connections before disconnecting
                var connections = new List<ConnectionAnchor>(anchor.connections);
                anchor.DisconnectAll();
                
                UI.BuildFeedbackHud.Show("Disconnected",
                    $"All {anchor.networkType} connections removed",
                    null, UI.UITheme.AccentOrange);
                
                // Notify all disconnected anchors to rebuild visuals
                foreach (var c in connections)
                    NotifyVisualUpdate(c);
                NotifyVisualUpdate(anchor);
            }
            else
            {
                UI.BuildFeedbackHud.Show("No connections",
                    $"This {anchor.networkType} anchor has no connections",
                    null, UI.UITheme.AccentDim);
            }
        }

        /// <summary>Cycle the port direction on the clicked face of a machine.</summary>
        private void CyclePortDirection(PortConfig config, RaycastHit hit)
        {
            // Find which face was hit
            CubeFace face = GetHitFace(hit, config.transform);
            if (face == CubeFace.PosX && !config.IsFaceEnabled(face)) return;

            var currentDir = config.GetDirection(face);
            PortDirection nextDir;

            if (!config.IsFaceEnabled(face))
            {
                // Enable the face and set as Input
                config.SetFaceEnabled(face, true);
                nextDir = PortDirection.Input;
            }
            else
            {
                switch (currentDir)
                {
                    case PortDirection.None:
                        nextDir = PortDirection.Input;
                        break;
                    case PortDirection.Input:
                        nextDir = PortDirection.Output;
                        break;
                    case PortDirection.Output:
                        nextDir = PortDirection.None;
                        config.SetFaceEnabled(face, false);
                        break;
                    default:
                        nextDir = PortDirection.None;
                        break;
                }
            }

            config.SetDirection(face, nextDir);
            config.RefreshIndicators();

            string dirName = nextDir.ToString();
            UI.BuildFeedbackHud.Show("Port Config",
                $"{GetFaceLabel(face)}: {dirName}",
                null, nextDir == PortDirection.Input ? UI.UITheme.AccentCyan : 
                      nextDir == PortDirection.Output ? UI.UITheme.AccentOrange : UI.UITheme.AccentDim);
        }

        private CubeFace GetHitFace(RaycastHit hit, Transform target)
        {
            Vector3 localHit = target.InverseTransformPoint(hit.point);
            float absX = Mathf.Abs(localHit.x);
            float absY = Mathf.Abs(localHit.y);
            float absZ = Mathf.Abs(localHit.z);

            if (absX >= absY && absX >= absZ)
                return localHit.x > 0 ? CubeFace.PosX : CubeFace.NegX;
            if (absY >= absX && absY >= absZ)
                return localHit.y > 0 ? CubeFace.PosY : CubeFace.NegY;
            return localHit.z > 0 ? CubeFace.PosZ : CubeFace.NegZ;
        }

        private string GetFaceLabel(CubeFace face) => face switch
        {
            CubeFace.PosX => "+X",
            CubeFace.NegX => "-X",
            CubeFace.PosY => "+Y",
            CubeFace.NegY => "-Y",
            CubeFace.PosZ => "+Z",
            CubeFace.NegZ => "-Z",
            _ => "?"
        };

        /// <summary>Check if two anchors can be connected based on PortConfig.</summary>
        private bool CanConnect(ConnectionAnchor a, ConnectionAnchor b)
        {
            // Check PortConfig on both ends
            var configA = a.GetComponent<PortConfig>();
            var configB = b.GetComponent<PortConfig>();

            // If either has PortConfig, verify compatibility
            if (configA != null)
            {
                var matchA = configA.GetMatchingFace(b.transform.position, PortDirection.Input);
                if (!matchA.HasValue) matchA = configA.GetMatchingFace(b.transform.position, PortDirection.Output);
                if (!matchA.HasValue) return false;
                if (!configA.AcceptsNetworkType(matchA.Value.face, a.networkType)) return false;
            }

            if (configB != null)
            {
                var matchB = configB.GetMatchingFace(a.transform.position, PortDirection.Input);
                if (!matchB.HasValue) matchB = configB.GetMatchingFace(a.transform.position, PortDirection.Output);
                if (!matchB.HasValue) return false;
                if (!configB.AcceptsNetworkType(matchB.Value.face, b.networkType)) return false;
            }

            return true;
        }

        private void NotifyVisualUpdate(ConnectionAnchor anchor)
        {
            if (anchor == null) return;

            // Trigger visual rebuild on DataCable
            var dataCable = anchor.GetComponent<DataCable>();
            if (dataCable != null) dataCable.RebuildVisuals();

            // Trigger rebuild on PowerNode
            var powerNode = anchor.GetComponent<Power.PowerNode>();
            if (powerNode != null)
            {
                // Request topology rebuild
                var manager = Power.PowerNetworkManager.Instance;
                // The manager will handle visual updates
            }
        }

        private void CreateSelectionIndicator(Vector3 position)
        {
            DestroySelectionIndicator();
            _selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _selectionIndicator.name = "WrenchSelection";
            _selectionIndicator.transform.position = position;
            _selectionIndicator.transform.localScale = Vector3.one * 0.3f;
            
            var mr = _selectionIndicator.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mr.material.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            mr.material.EnableKeyword("_EMISSION");
            mr.material.SetColor("_EmissionColor", new Color(0.3f, 0.9f, 1f) * 0.5f);
            
            Object.Destroy(_selectionIndicator.GetComponent<Collider>());
        }

        private void DestroySelectionIndicator()
        {
            if (_selectionIndicator != null)
            {
                Object.Destroy(_selectionIndicator);
                _selectionIndicator = null;
            }
        }

        private void ClearSelection()
        {
            _selectedAnchor  = null;
            _selectedConduit = null;
            DestroySelectionIndicator();
        }

        /// <summary>
        /// RMB-on-cable behaviour: blacklist this conduit's link with EVERY
        /// current neighbour (cable, pipe, machine) so the next topology rebuild
        /// leaves it standing alone. Re-wrenching individual pairs (LMB select →
        /// LMB the same target) re-bonds them one at a time.
        /// </summary>
        private void DisconnectAllConduitNeighbours(GameObject conduit)
        {
            if (conduit == null) return;
            int blocked = 0;

            // Power cable neighbours.
            var pc = conduit.GetComponent<VoxelEngine.Power.PowerCable>();
            if (pc != null && pc.neighbours != null)
            {
                foreach (var nb in pc.neighbours)
                {
                    if (nb == null) continue;
                    WrenchBlacklist.Block(conduit, nb.gameObject);
                    blocked++;
                }
            }

            // Gas pipe neighbours.
            var gp = conduit.GetComponent<VoxelEngine.Gas.GasPipe>();
            if (gp != null && gp.neighbours != null)
            {
                foreach (var nb in gp.neighbours)
                {
                    if (nb == null) continue;
                    WrenchBlacklist.Block(conduit, nb.gameObject);
                    blocked++;
                }
            }

            // Item pipe neighbours.
            var ip = conduit.GetComponent<VoxelEngine.Transport.ItemPipe>();
            if (ip != null && ip.neighbours != null)
            {
                foreach (var nb in ip.neighbours)
                {
                    if (nb == null) continue;
                    WrenchBlacklist.Block(conduit, nb.gameObject);
                    blocked++;
                }
            }

            // Water pipe (FluidNode) neighbours.
            var wp = conduit.GetComponent<VoxelEngine.Fluids.WaterPipe>();
            if (wp != null && wp.neighbours != null)
            {
                foreach (var nb in wp.neighbours)
                {
                    if (nb == null) continue;
                    WrenchBlacklist.Block(conduit, nb.gameObject);
                    blocked++;
                }
            }

            // Data cable neighbours (via ConnectionAnchor).
            var dc = conduit.GetComponent<VoxelEngine.Networks.DataCable>();
            if (dc != null && dc.anchor != null)
            {
                var copy = new List<ConnectionAnchor>(dc.anchor.connections);
                foreach (var other in copy)
                {
                    if (other == null) continue;
                    WrenchBlacklist.Block(conduit, other.gameObject);
                    dc.anchor.Disconnect(other);
                    blocked++;
                }
            }

            NudgeAllNetworks();

            UI.BuildFeedbackHud.Show(
                blocked > 0 ? "Disconnected" : "No connections",
                blocked > 0 ? $"{blocked} link(s) broken on {conduit.name}"
                            : $"{conduit.name} has no active links",
                null,
                blocked > 0 ? UI.UITheme.AccentOrange : UI.UITheme.AccentDim);
        }

        /// <summary>Auto-clear selection after timeout and update indicator position.</summary>
        public void Tick()
        {
            if (_selectedAnchor != null || _selectedConduit != null)
            {
                if (Time.time - _selectedTime > SELECTION_TIMEOUT)
                {
                    ClearSelection();
                    return;
                }

                // Update indicator position to follow the selected anchor / conduit.
                if (_selectionIndicator != null)
                {
                    if (_selectedAnchor != null)
                        _selectionIndicator.transform.position = _selectedAnchor.transform.position;
                    else if (_selectedConduit != null)
                        _selectionIndicator.transform.position = _selectedConduit.transform.position;
                }
            }
        }
    }
}