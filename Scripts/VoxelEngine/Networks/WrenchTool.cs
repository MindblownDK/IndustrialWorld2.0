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

        /// <summary>Called when the player LMB with the wrench.</summary>
        public void OnUse(RaycastHit hit, Player.PlayerInteractionTool tool)
        {
            var anchor = hit.collider.GetComponentInParent<ConnectionAnchor>();
            var portConfig = hit.collider.GetComponentInParent<PortConfig>();

            // Shift+Click: Cycle port direction on machines
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                if (portConfig != null)
                {
                    CyclePortDirection(portConfig, hit);
                    return;
                }
            }

            if (anchor == null)
            {
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

        /// <summary>Called when the player RMB with the wrench — disconnect.</summary>
        public void OnAltUse(RaycastHit hit)
        {
            var anchor = hit.collider.GetComponentInParent<ConnectionAnchor>();
            if (anchor == null) return;

            // Shift+Click: Disconnect SPECIFIC connection (the one you clicked on)
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
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
            _selectedAnchor = null;
            DestroySelectionIndicator();
        }

        /// <summary>Auto-clear selection after timeout and update indicator position.</summary>
        public void Tick()
        {
            if (_selectedAnchor != null)
            {
                if (Time.time - _selectedTime > SELECTION_TIMEOUT)
                {
                    ClearSelection();
                    return;
                }

                // Update indicator position to follow the selected anchor
                if (_selectionIndicator != null && _selectedAnchor != null)
                {
                    _selectionIndicator.transform.position = _selectedAnchor.transform.position;
                }
            }
        }
    }
}