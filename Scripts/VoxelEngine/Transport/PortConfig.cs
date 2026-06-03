// Assets/Scripts/VoxelEngine/Transport/PortConfig.cs
//
// Configurable I/O face system for machines, cables, and pipes.
// Every machine can expose up to 6 faces (±X, ±Y, ±Z). Each face is marked
// as None, Input, or Output. Cables/pipes snap to enabled faces.
// Now also supports network type filtering to restrict which cables connect.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Networks;

namespace VoxelEngine.Transport
{
    public enum PortDirection { None, Input, Output }

    /// <summary>
    /// The six cube faces, matching Unity's axis convention.
    /// </summary>
    public enum CubeFace { PosX = 0, NegX = 1, PosY = 2, NegY = 3, PosZ = 4, NegZ = 5 }

    /// <summary>
    /// Network type for port filtering - determines what can connect to this port.
    /// </summary>
    public enum PortNetworkType
    {
        Any,        // Can connect to any network type (backward compatible)
        Power,      // Only power cables
        Data,       // Only data cables
        Fluid,      // Only fluid pipes
        Gas         // Only gas pipes
    }

    /// <summary>
    /// Per-face port configuration. Attach to any machine, generator, consumer,
    /// cable, or pipe to control which faces accept connections and in which direction.
    /// </summary>
    public class PortConfig : MonoBehaviour
    {
        [Serializable]
        public struct FacePort
        {
            public CubeFace face;
            public PortDirection direction;
            public PortNetworkType networkType; // What network type can connect here
            public bool enabled; // Can be toggled on/off (for UI control)
        }

        [Tooltip("Configure which faces are active and their direction.")]
        public FacePort[] ports = new FacePort[]
        {
            new() { face = CubeFace.PosX, direction = PortDirection.Output, networkType = PortNetworkType.Any, enabled = true },
            new() { face = CubeFace.NegX, direction = PortDirection.None, networkType = PortNetworkType.Any, enabled = true },
            new() { face = CubeFace.PosY, direction = PortDirection.None, networkType = PortNetworkType.Any, enabled = true },
            new() { face = CubeFace.NegY, direction = PortDirection.None, networkType = PortNetworkType.Any, enabled = true },
            new() { face = CubeFace.PosZ, direction = PortDirection.None, networkType = PortNetworkType.Any, enabled = true },
            new() { face = CubeFace.NegZ, direction = PortDirection.None, networkType = PortNetworkType.Any, enabled = true },
        };

        [Header("Visual Settings")]
        [Tooltip("Size of the port indicator squares on each face.")]
        public float portIndicatorSize = 0.4f;
        [Tooltip("Should port indicators be visible?")]
        public bool showPortIndicators = true;

        // Visual indicator references
        private Dictionary<CubeFace, GameObject> _portIndicators = new();

        /// <summary>Get the world-space offset for a given face (unit vector).</summary>
        public Vector3 FaceNormal(CubeFace face)
        {
            return face switch
            {
                CubeFace.PosX => transform.right,
                CubeFace.NegX => -transform.right,
                CubeFace.PosY => transform.up,
                CubeFace.NegY => -transform.up,
                CubeFace.PosZ => transform.forward,
                CubeFace.NegZ => -transform.forward,
                _ => Vector3.zero
            };
        }

        /// <summary>Get the world-space snap point for a face (surface center).</summary>
        public Vector3 FaceWorldPoint(CubeFace face)
        {
            return transform.position + FaceNormal(face) * 0.5f;
        }

        /// <summary>Get the port direction for a given face.</summary>
        public PortDirection GetDirection(CubeFace face)
        {
            if (ports == null) return PortDirection.None;
            foreach (var p in ports)
                if (p.face == face) return p.direction;
            return PortDirection.None;
        }

        /// <summary>Get the network type filter for a given face.</summary>
        public PortNetworkType GetNetworkType(CubeFace face)
        {
            if (ports == null) return PortNetworkType.Any;
            foreach (var p in ports)
                if (p.face == face) return p.networkType;
            return PortNetworkType.Any;
        }

        /// <summary>Check if a face is enabled.</summary>
        public bool IsFaceEnabled(CubeFace face)
        {
            if (ports == null) return true;
            foreach (var p in ports)
                if (p.face == face) return p.enabled;
            return true;
        }

        /// <summary>Set the port direction for a given face.</summary>
        public void SetDirection(CubeFace face, PortDirection dir)
        {
            if (ports == null) return;
            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i].face == face)
                {
                    ports[i].direction = dir;
                    UpdatePortIndicator(face);
                    NotifyNetworksChanged();
                    return;
                }
            }
        }

        /// <summary>Set the network type for a given face.</summary>
        public void SetNetworkType(CubeFace face, PortNetworkType type)
        {
            if (ports == null) return;
            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i].face == face)
                {
                    ports[i].networkType = type;
                    UpdatePortIndicator(face);
                    NotifyNetworksChanged();
                    return;
                }
            }
        }

        /// <summary>Toggle a face enabled/disabled.</summary>
        public void SetFaceEnabled(CubeFace face, bool enabled)
        {
            if (ports == null) return;
            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i].face == face)
                {
                    ports[i].enabled = enabled;
                    UpdatePortIndicator(face);
                    NotifyNetworksChanged();
                    return;
                }
            }
        }

        /// <summary>
        /// Called after any port edit. Two responsibilities:
        ///   1) Clear THIS machine's entries from <see cref="WrenchBlacklist"/>
        ///      so re-enabling a face intuitively restores its connections —
        ///      the player obviously WANTS the cable to relink now.
        ///   2) Dirty every network manager so the next Update tick rebuilds
        ///      topology immediately (otherwise the cable visuals lag behind
        ///      the face change until something else triggers a register churn).
        /// </summary>
        private void NotifyNetworksChanged()
        {
            // Drop any blacklist entries that involve this machine — the user
            // is clearly re-configuring it, so previous wrench-disconnects
            // shouldn't permanently veto the freshly-enabled face.
            try { VoxelEngine.Networks.WrenchBlacklist.ClearForGameObject(gameObject); } catch { }

            // Nudge every topology rebuilder so visuals refresh next frame.
            try { VoxelEngine.Power.PowerNetworkManager.Instance?.SetDirty(); } catch { }
            try { VoxelEngine.Gas.GasNetwork.Instance?.SetDirty(); }           catch { }
            try { VoxelEngine.Transport.ItemPipeNetwork.Instance?.SetDirty(); } catch { }
            try { VoxelEngine.Fluids.FluidNetworkManager.Instance?.SetDirty(); } catch { }
        }

        /// <summary>Check if this port accepts connections from a specific network type.</summary>
        public bool AcceptsNetworkType(CubeFace face, NetworkType networkType)
        {
            var filter = GetNetworkType(face);
            if (filter == PortNetworkType.Any) return true;

            return filter switch
            {
                PortNetworkType.Power => networkType == NetworkType.Power,
                PortNetworkType.Data => networkType == NetworkType.Data,
                PortNetworkType.Fluid => networkType == NetworkType.Fluid,
                PortNetworkType.Gas => networkType == NetworkType.Gas,
                _ => true
            };
        }

        /// <summary>Check if this port accepts connections from a power cable.</summary>
        public bool AcceptsPower(CubeFace face)
        {
            return AcceptsNetworkType(face, NetworkType.Power);
        }

        /// <summary>Check if this port accepts connections from a data cable.</summary>
        public bool AcceptsData(CubeFace face)
        {
            return AcceptsNetworkType(face, NetworkType.Data);
        }

        /// <summary>Does this config have at least one output face?</summary>
        public bool HasAnyOutput()
        {
            if (ports == null) return false;
            foreach (var p in ports)
                if (p.direction == PortDirection.Output && p.enabled) return true;
            return false;
        }

        /// <summary>Does this config have at least one input face?</summary>
        public bool HasAnyInput()
        {
            if (ports == null) return false;
            foreach (var p in ports)
                if (p.direction == PortDirection.Input && p.enabled) return true;
            return false;
        }

        /// <summary>
        /// Find the best matching face for a neighbouring position.
        /// Returns the face if aligned with an enabled port of the required direction.
        /// </summary>
        public (CubeFace face, PortDirection dir)? GetMatchingFace(Vector3 neighbourPos, PortDirection requiredDir)
        {
            if (ports == null) return null;
            Vector3 toNeighbour = (neighbourPos - transform.position);
            float dist = toNeighbour.magnitude;
            if (dist < 0.001f) return null;
            Vector3 dir = toNeighbour / dist;

            float bestDot = -1f;
            CubeFace bestFace = CubeFace.PosX;

            foreach (var p in ports)
            {
                if (p.direction != requiredDir || !p.enabled) continue;
                float dot = Vector3.Dot(dir, FaceNormal(p.face));
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestFace = p.face;
                }
            }

            if (bestDot > 0.8f)
                return (bestFace, requiredDir);

            return null;
        }

        /// <summary>
        /// Check if a neighbouring position (in world space) aligns with any
        /// enabled face of the given direction. Used by cables/pipes to snap.
        /// </summary>
        public bool IsAlignedWith(Vector3 neighbourPos, PortDirection requiredDir, float tolerance = 0.8f)
        {
            return GetMatchingFace(neighbourPos, requiredDir).HasValue;
        }

        /// <summary>Ensure all 6 faces exist in the array (repair).</summary>
        public void EnsureAllFaces()
        {
            if (ports != null && ports.Length == 6) return;
            var old = ports ?? Array.Empty<FacePort>();
            ports = new FacePort[6];
            for (int i = 0; i < 6; i++)
            {
                var f = (CubeFace)i;
                ports[i].face = f;
                ports[i].direction = PortDirection.None;
                ports[i].networkType = PortNetworkType.Any;
                ports[i].enabled = true;
                foreach (var o in old)
                    if (o.face == f) { ports[i].direction = o.direction; ports[i].networkType = o.networkType; ports[i].enabled = o.enabled; break; }
            }
        }

        // ========================================================
        //                  VISUAL INDICATORS
        // ========================================================

        private void Awake()
        {
            EnsureAllFaces();
        }

        private void OnEnable()
        {
            if (showPortIndicators) CreateAllPortIndicators();
        }

        private void OnDisable()
        {
            DestroyAllPortIndicators();
        }

        /// <summary>Create visual indicator squares on each face.</summary>
        public void CreateAllPortIndicators()
        {
            DestroyAllPortIndicators();
            foreach (var p in ports)
            {
                if (p.direction == PortDirection.None || !p.enabled) continue;
                CreatePortIndicator(p.face, p.direction, p.networkType);
            }
        }

        private void CreatePortIndicator(CubeFace face, PortDirection dir, PortNetworkType netType)
        {
            var go = new GameObject($"PortIndicator_{face}");
            go.transform.SetParent(transform);
            go.transform.localPosition = FaceNormal(face) * 0.51f;
            go.transform.localRotation = Quaternion.LookRotation(-FaceNormal(face));
            go.layer = gameObject.layer;

            // Create the square indicator mesh
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();

            // Create a flat quad/plane mesh for the square
            meshFilter.mesh = CreateQuadMesh(portIndicatorSize);

            // Set material based on direction and network type
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (dir == PortDirection.Input)
            {
                mat.color = netType switch
                {
                    PortNetworkType.Power => new Color(0.15f, 0.48f, 0.82f, 0.9f), // Blue for power input
                    PortNetworkType.Data => new Color(0.30f, 0.85f, 0.40f, 0.9f), // Green for data
                    PortNetworkType.Fluid => new Color(0.20f, 0.60f, 0.90f, 0.9f), // Cyan for fluid
                    PortNetworkType.Gas => new Color(0.60f, 0.40f, 0.90f, 0.9f), // Purple for gas
                    _ => new Color(0.5f, 0.5f, 0.5f, 0.9f)
                };
            }
            else // Output
            {
                mat.color = netType switch
                {
                    PortNetworkType.Power => new Color(0.82f, 0.50f, 0.12f, 0.9f), // Orange for power output
                    PortNetworkType.Data => new Color(0.40f, 0.30f, 0.90f, 0.9f), // Purple for data
                    PortNetworkType.Fluid => new Color(0.12f, 0.82f, 0.60f, 0.9f), // Teal for fluid
                    PortNetworkType.Gas => new Color(0.90f, 0.60f, 0.30f, 0.9f), // Amber for gas
                    _ => new Color(0.7f, 0.5f, 0.2f, 0.9f)
                };
            }

            // Add emission for visibility
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", mat.color * 0.5f);

            meshRenderer.material = mat;

            _portIndicators[face] = go;
        }

        private Mesh CreateQuadMesh(float size)
        {
            var mesh = new Mesh();
            float half = size / 2f;

            Vector3[] vertices = new Vector3[]
            {
                new(-half, -half, 0),
                new( half, -half, 0),
                new( half,  half, 0),
                new(-half,  half, 0)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,
                0, 3, 2
            };

            Vector2[] uv = new Vector2[]
            {
                new(0, 0),
                new(1, 0),
                new(1, 1),
                new(0, 1)
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();

            return mesh;
        }

        /// <summary>Update indicator for a specific face when config changes.</summary>
        public void UpdatePortIndicator(CubeFace face)
        {
            if (_portIndicators.TryGetValue(face, out var existing))
            {
                Destroy(existing);
                _portIndicators.Remove(face);
            }

            if (!showPortIndicators) return;

            var dir = GetDirection(face);
            var netType = GetNetworkType(face);
            if (dir != PortDirection.None && IsFaceEnabled(face))
            {
                CreatePortIndicator(face, dir, netType);
            }
        }

        private void DestroyAllPortIndicators()
        {
            foreach (var kvp in _portIndicators)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _portIndicators.Clear();
        }

        /// <summary>Refresh all visual indicators based on current config.</summary>
        public void RefreshIndicators()
        {
            CreateAllPortIndicators();
        }
    }
}
