// Assets/Scripts/VoxelEngine/Networking/GridControlSync.cs
//
// 14.65.0 - the Grid Control toolbar and the terminal groups over the wire,
// riding the exact BagSync pattern: whoever edits announces the WHOLE state
// (both payloads are tiny JSON and edits are rare - whole-state beats deltas
// at 2-8 players), the host relays, and every machine applies what it did not
// author itself.
//
// Join-in-progress needs no work here at all: the toolbar and the groups ride
// the grid's SavedGrid record, and that record IS the join snapshot - a
// joining client rebuilds the ship and its toolbar from the same bytes the
// host would have saved.

using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    public static class GridControlSync
    {
        /// <summary>Raised while a remote state is being applied locally, so the
        /// UI hooks never announce an echo back into the network.</summary>
        public static bool IsApplyingRemote { get; private set; }

        private static bool ShouldAnnounce =>
            !IsApplyingRemote
            && NetworkSession.Mode != SessionMode.Offline
            && NetworkBootstrap.Instance != null
            && !NetworkBootstrap.Instance.WorldMismatch;

        /// <summary>Ship this grid's toolbar + groups to the other machines.
        /// Called from every local edit site; a no-op offline.</summary>
        public static void Announce(GridEntity grid)
        {
            if (grid == null || !ShouldAnnounce) return;
            string netId = GridSync.IdOf(grid);
            if (string.IsNullOrEmpty(netId)) return;
            NetworkBootstrap.Instance.SendGridControlBar(netId,
                GridSystem.UI.GridControlHud.ExportBar(grid),
                GridSystem.UI.GridMasterTerminal.ExportGroups(grid));
        }

        /// <summary>A remote machine edited this grid's toolbar/groups: apply as
        /// whole-state. Groups first - the bar may reference them.</summary>
        public static void Apply(string netId, string bar, string groups)
        {
            var grid = GridSync.Find(netId);
            if (grid == null) return;
            IsApplyingRemote = true;
            try
            {
                GridSystem.UI.GridMasterTerminal.ImportGroups(grid, groups);
                GridSystem.UI.GridControlHud.ImportBar(grid, bar);
            }
            finally { IsApplyingRemote = false; }
        }
    }
}
