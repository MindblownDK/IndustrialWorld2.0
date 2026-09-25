// Assets/Scripts/VoxelEngine/Building/PortalFrameBlock.cs
//
// Portal Frame — one structural piece of a player-built portal. NOT a grid block:
// a static placed block for permanent structures (space stations, gate arrays).
//
// The player closes a loop of frames — a square or a ring "as round as squares can
// be", up to 64x64 — and a Portal Controller placed in front reads the loop as one
// portal. The frame itself is dumb: it only registers so the controller's scan can
// find frames by proximity. Every portal property (size, power cost, transits)
// lives on the controller.
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    public class PortalFrameBlock : MonoBehaviour
    {
        private static readonly List<PortalFrameBlock> s_all = new();

        /// <summary>Live frames, for the controller's scans.</summary>
        public static IReadOnlyList<PortalFrameBlock> All => s_all;

        private Bounds _bounds;
        private bool _boundsCached;

        public Bounds Bounds
        {
            get
            {
                if (!_boundsCached)
                {
                    var col = GetComponentInChildren<Collider>();
                    _bounds = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
                    _boundsCached = true;
                }
                return _bounds;
            }
        }

        /// <summary>Largest face dimension — the cell size this frame contributes
        /// to the portal grid (frames should be uniform for a clean surface).</summary>
        public float CellSize
        {
            get
            {
                Vector3 s = Bounds.size;
                return Mathf.Max(Mathf.Max(s.x, s.y), Mathf.Max(s.z, 0.05f));
            }
        }

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }
    }
}
