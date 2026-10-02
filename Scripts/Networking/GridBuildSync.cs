// ─────────────────────────────────────────────────────────────────────────────
//  GridBuildSync - a client's building on a hull reaching the machine that owns it.
//
//  THE GAP THIS CLOSES (raised 14.25.0, observed live in 14.28.x)
//
//  The host polls its own hulls for shape changes and resends the record when one
//  changes. A CLIENT's hulls are kinematic copies the host never looks at, so a
//  guest welding a block onto a ship was building on that machine only: the host
//  never learned about the block, no other player ever saw it, and the next
//  structure resend - triggered by anything at all - deleted it. "The guest's
//  building exists only on that machine" was the last single-player assumption
//  left in milestone 9.
//
//  THE SHAPE OF THE FIX
//
//  A client keeps placing locally - the ghost, the cost, the placement rules all
//  run exactly as they do offline, so building FEELS the same - and what it just
//  did travels to the host as a per-block edit through the save seam:
//
//    - PLACE: the new block is captured with the same CaptureGridBlockJson the
//      state poller already uses (the full save record: item id, cell, rotation,
//      precision seat, shape variant), and the host rebuilds it through the same
//      restore path a save file uses. No second builder to drift from the first.
//    - REMOVE: (grid net id, cell) travels and the host removes through the same
//      RemoveBlock single-player calls, so every removal rule still applies.
//    - A BRAND-NEW HULL: there is nothing on the host to address a cell against,
//      so the whole grid goes up once as the same record the join catch-up uses.
//      The host accepts a client record ONLY for an id it has never heard of - a
//      client can start a ship, it can never overwrite one.
//
//  After every accepted (or refused) edit the host answers with the full structure
//  record, which is the existing convergence mechanism doing its job: the builder's
//  own copy is rebuilt from the host's answer, so what a player ends up looking at
//  is always what actually happened. A refused placement - two players reaching for
//  the same cell in the same instant - is corrected by that same echo rather than
//  needing its own message.
//
//  Placements are captured at END OF FRAME, not inside AddBlock: the builder keeps
//  configuring a block after it is attached (ports, paint, variants), and a record
//  captured mid-setup would describe a block that never quite existed.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    public static class GridBuildSync
    {
        // Hulls this client created that the host has not yet echoed back. Their
        // first block travels as a whole record; the mark is cleared the moment a
        // host record for that id arrives, after which per-block edits resume.
        private static readonly HashSet<string> _clientBorn = new();

        // Blocks the local player attached this frame, flushed in LateUpdate.
        private static readonly List<(GridEntity Grid, GridBlock Block)> _placed = new();

        private static bool CanSend =>
            NetworkSession.Mode == SessionMode.Client
            && NetworkBootstrap.Instance != null
            && !NetworkBootstrap.Instance.WorldMismatch;

        // ── client side: the hooks ───────────────────────────────────────────

        /// <summary>Called by GridEntity/GridPrecisionAttachmentLayer whenever a block
        /// is attached. Silent everywhere except on a client performing a LOCAL edit -
        /// a record being applied, a save being restored and the host's own building
        /// all pass through here without a sound.</summary>
        public static void NotifyLocalPlaced(GridEntity grid, GridBlock block)
        {
            if (grid == null || block == null) return;
            if (!CanSend || GridSync.IsApplyingRemote) return;
            _placed.Add((grid, block));
        }

        /// <summary>Called whenever a block comes off a hull. Sent immediately - unlike
        /// a placement there is nothing left to configure - and only for hulls the
        /// network has an id for; a hull it never heard of has nothing to remove.</summary>
        public static void NotifyLocalRemoved(GridEntity grid, Vector3Int cell,
            bool precision, Vector3Int precisionCell)
        {
            if (grid == null) return;
            if (!CanSend || GridSync.IsApplyingRemote) return;
            var tag = grid.GetComponent<GridNetTag>();
            if (tag == null || string.IsNullOrEmpty(tag.Id)) return;
            NetworkBootstrap.Instance.RequestGridBuild(tag.Id, cell, precision, precisionCell, false, "");
        }

        /// <summary>The host's record for this id arrived, so the hull is no longer
        /// this client's private invention. Per-block edits address it from now on.</summary>
        internal static void MarkHostBorn(string netId)
        {
            if (!string.IsNullOrEmpty(netId)) _clientBorn.Remove(netId);
        }

        /// <summary>End-of-frame drain of this frame's placements, called by
        /// GridBuildSyncManager. One whole-record upload for a hull born this frame
        /// (the record already contains every block placed into it, so the per-block
        /// entries for it are skipped); one per-block message for everything else.</summary>
        internal static void FlushPlacements()
        {
            if (_placed.Count == 0) return;
            if (!CanSend || GridSync.IsApplyingRemote) { _placed.Clear(); return; }

            var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
            if (persistence == null) { _placed.Clear(); return; }

            HashSet<string> recordSent = null;
            foreach (var (grid, block) in _placed)
            {
                // Placed and gone again inside one frame, or the hull died with it.
                if (grid == null || block == null || block.Grid != grid) continue;

                var tag = grid.GetComponent<GridNetTag>();
                bool fresh = tag == null || string.IsNullOrEmpty(tag.Id);
                string id = GridSync.IdOf(grid);   // mints the id for a fresh hull
                if (string.IsNullOrEmpty(id)) continue;

                if (fresh)
                {
                    _clientBorn.Add(id);
                    if (recordSent != null && recordSent.Contains(id)) continue;
                    var record = GridSync.CaptureOne(grid);
                    if (!record.HasValue) continue;
                    NetworkBootstrap.Instance.RequestGridRecord(record.Value);
                    (recordSent ??= new HashSet<string>()).Add(id);
                    continue;
                }

                // Already inside the record that just went up this flush.
                if (recordSent != null && recordSent.Contains(id)) continue;

                string json = persistence.CaptureGridBlockJson(block);
                if (string.IsNullOrEmpty(json)) continue;
                NetworkBootstrap.Instance.RequestGridBuild(id, block.GridPos,
                    block.IsPrecisionAttachment, block.PrecisionGridPos, true, json);
            }
            _placed.Clear();
        }

        // ── host side: perform and echo ──────────────────────────────────────

        /// <summary>Carry out one edit a client asked for, against the only hull that
        /// counts. The real placement and removal paths run - the restore seam for a
        /// place, the single-player RemoveBlock for a remove - so an occupied cell, an
        /// unknown item or an already-empty cell is refused by the same code that
        /// refuses it everywhere else. Accepted or refused, the hull is echoed back as
        /// it actually is now, which corrects the asker instead of leaving their local
        /// guess standing.</summary>
        public static void HostPerform(string netId, Vector3Int cell, bool precision,
            Vector3Int precisionCell, bool place, string json)
        {
            if (NetworkSession.Mode != SessionMode.Host) return;
            var grid = GridSync.Find(netId);
            if (grid == null) return;

            if (place)
            {
                var persistence = VoxelEngine.Persistence.WorldStatePersistence.Instance;
                persistence?.ApplyGridBlockAddJson(grid, json);
            }
            else if (precision)
            {
                var layer = grid.PrecisionAttachments;
                if (layer != null && layer.Blocks.ContainsKey(precisionCell))
                    layer.RemoveBlock(precisionCell);
            }
            else if (grid.GetBlock(cell) != null)
            {
                grid.RemoveBlock(cell);
            }

            // Echo the shape as it stands. A hull at zero blocks is dying - its
            // GridNetTag announces the removal on destroy, so nothing to say here.
            if (grid.BlockCount > 0)
            {
                GridSyncManager.Instance?.RecordShape(grid);
                GridSync.AnnounceStructure(grid);
            }
        }

        /// <summary>Forget everything. Called on session teardown so the next session
        /// never treats a hull as client-born because of a mark from the last one.</summary>
        public static void Clear()
        {
            _clientBorn.Clear();
            _placed.Clear();
        }
    }

    /// <summary>Drains the placement queue once per frame, after the frame's building
    /// is fully configured. Idles completely outside client sessions.</summary>
    public sealed class GridBuildSyncManager : MonoBehaviour
    {
        public static GridBuildSyncManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate() => GridBuildSync.FlushPlacements();
    }
}
