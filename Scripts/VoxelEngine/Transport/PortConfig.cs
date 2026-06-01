// Assets/Scripts/VoxelEngine/Transport/PortConfig.cs
//
// Configurable I/O face system for machines, cables, and pipes.
// Every machine can expose up to 6 faces (±X, ±Y, ±Z). Each face is marked
// as None, Input, or Output. Cables/pipes snap to enabled faces.

using System;
using UnityEngine;

namespace VoxelEngine.Transport
{
    public enum PortDirection { None, Input, Output }

    /// <summary>
    /// The six cube faces, matching Unity's axis convention.
    /// </summary>
    public enum CubeFace { PosX = 0, NegX = 1, PosY = 2, NegY = 3, PosZ = 4, NegZ = 5 }

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
        }

        [Tooltip("Configure which faces are active and their direction.")]
        public FacePort[] ports = new FacePort[]
        {
            new() { face = CubeFace.PosX, direction = PortDirection.Output },
            new() { face = CubeFace.NegX, direction = PortDirection.None },
            new() { face = CubeFace.PosY, direction = PortDirection.None },
            new() { face = CubeFace.NegY, direction = PortDirection.None },
            new() { face = CubeFace.PosZ, direction = PortDirection.None },
            new() { face = CubeFace.NegZ, direction = PortDirection.None },
        };

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

        /// <summary>Set the port direction for a given face.</summary>
        public void SetDirection(CubeFace face, PortDirection dir)
        {
            if (ports == null) return;
            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i].face == face)
                {
                    ports[i].direction = dir;
                    return;
                }
            }
        }

        /// <summary>Does this config have at least one output face?</summary>
        public bool HasAnyOutput()
        {
            if (ports == null) return false;
            foreach (var p in ports)
                if (p.direction == PortDirection.Output) return true;
            return false;
        }

        /// <summary>Does this config have at least one input face?</summary>
        public bool HasAnyInput()
        {
            if (ports == null) return false;
            foreach (var p in ports)
                if (p.direction == PortDirection.Input) return true;
            return false;
        }

        /// <summary>
        /// Check if a neighbouring position (in world space) aligns with any
        /// enabled face of the given direction. Used by cables/pipes to snap.
        /// </summary>
        public bool IsAlignedWith(Vector3 neighbourPos, PortDirection requiredDir, float tolerance = 0.8f)
        {
            if (ports == null) return false;
            Vector3 toNeighbour = (neighbourPos - transform.position).normalized;
            foreach (var p in ports)
            {
                if (p.direction != requiredDir) continue;
                float dot = Vector3.Dot(toNeighbour, FaceNormal(p.face));
                if (dot > tolerance) return true;
            }
            return false;
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
                foreach (var o in old)
                    if (o.face == f) { ports[i].direction = o.direction; break; }
            }
        }
    }
}
