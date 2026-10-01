// ─────────────────────────────────────────────────────────────────────────────
//  GridActionSync - replicating the things a player DOES to a block on a ship.
//
//  Milestone 9 replicated where a ship is and what its pilot is asking of it.
//  It did not replicate the levers. Landing gear, docking clamps, couplers,
//  pistons and doors were all still purely local: a guest pressed P, their own
//  copy of the ship put its gear down, and no other machine ever heard about it.
//  On the host - the only machine whose physics are real - the gear never locked
//  at all, so the ship stayed free to drift while the guest watched it clamped.
//
//  These are one problem, not five, so this is one channel rather than five
//  bespoke messages. A block is addressed the way milestone 9 addresses
//  everything on a hull: (grid net id, integer cell). Exact, stable while the
//  ship moves, and the same address on every machine.
//
//  The shape is request -> authority -> state:
//
//    1. A client never performs the action. It asks the host.
//    2. The host performs it for real, against its own physics.
//    3. The host tells everyone the resulting state, including the client that
//       asked - so what a player sees is always what actually happened, never
//       an optimistic guess that might not have.
//
//  No prediction, deliberately. Gear that visibly clamps and then lets go again
//  because the host disagreed is worse than gear that takes a moment to clamp,
//  and docking is a hard physical join that must not be guessed at.
//
//  What a client is allowed to do locally is the part that has no physics in it:
//  a door opens, a piston extends, because those are animations whose end state
//  the host has already approved. What a client must never do is create a joint
//  or hand a kinematic hull back to its own physics engine - that is the host's
//  simulation and a client touching it is exactly the divergence this whole
//  milestone exists to prevent.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Networking
{
    /// <summary>Which lever was pulled. Serialised as a byte, so values are explicit
    /// and must never be renumbered - an old client and a new host would disagree
    /// about what a 3 means and dock a ship when asked to open a door.</summary>
    public enum GridAction : byte
    {
        LandingGear = 0,
        DockingPort = 1,
        Piston      = 2,
        RailCoupler = 3,
        SlidingDoor = 4
    }

    public static class GridActionSync
    {
        /// <summary>True while a block is being driven by the host's answer rather
        /// than by a player. Stops an applied state from being announced straight
        /// back out again, which would be an endless round trip.</summary>
        public static bool IsApplyingRemote { get; private set; }

        private static bool CanSync =>
            NetworkBootstrap.Instance != null && NetworkSession.Mode != SessionMode.Offline;

        // ── address ──────────────────────────────────────────────────────────

        private static bool TryAddress(GridBlock block, out string netId, out Vector3Int cell)
        {
            netId = null;
            cell = Vector3Int.zero;
            if (block == null || block.Grid == null) return false;
            var tag = block.Grid.GetComponent<GridNetTag>();
            if (tag == null || string.IsNullOrEmpty(tag.Id)) return false;
            netId = tag.Id;
            cell = block.GridPos;
            return true;
        }

        private static GridBlock FindBlock(string netId, Vector3Int cell)
        {
            var grid = GridSync.Find(netId);
            if (grid == null) return null;
            foreach (var block in grid.AllBlocks)
                if (block != null && block.GridPos == cell) return block;
            return null;
        }

        // ── 1. the request ───────────────────────────────────────────────────

        /// <summary>Called at the top of a block's action method. Returns TRUE when the
        /// caller must stop immediately because the request has been handed to the host
        /// instead - which is the case on a client, and only on a client.</summary>
        public static bool Deferred(GridBlock block, GridAction action, bool desired)
        {
            // Offline, or already acting on the host's answer: do it here and now.
            if (IsApplyingRemote || !CanSync) return false;

            if (NetworkSession.Mode != SessionMode.Client)
                return false;   // the host performs its own actions directly

            if (!TryAddress(block, out string netId, out var cell)) return false;

            NetworkBootstrap.Instance.SendGridAction(netId, cell, action, desired);
            return true;
        }

        /// <summary>Called by the host after it has performed an action, so every other
        /// machine ends up holding the same answer. Silent off-host and offline.</summary>
        public static void Announce(GridBlock block, GridAction action, bool state)
        {
            if (IsApplyingRemote || !CanSync) return;
            if (NetworkSession.Mode != SessionMode.Host) return;
            if (!TryAddress(block, out string netId, out var cell)) return;

            NetworkBootstrap.Instance.SendGridActionState(netId, cell, action, state);
        }

        // ── 2. the host performs it ──────────────────────────────────────────

        /// <summary>Host side: carry out what a client asked for. The real methods are
        /// called, so every rule they enforce - gear needing a surface, a dock needing
        /// a free port - still applies. A refused action simply produces a state
        /// announcement saying it did not happen.</summary>
        public static void PerformRequest(string netId, Vector3Int cell, GridAction action, bool desired)
        {
            var block = FindBlock(netId, cell);
            if (block == null) return;

            switch (action)
            {
                case GridAction.LandingGear:
                    if (block is GridLandingGear gear)
                    {
                        if (desired) gear.TryLock(); else gear.Unlock();
                        Announce(gear, action, gear.IsLocked);
                    }
                    break;

                case GridAction.DockingPort:
                    if (block is GridDockingPort dock)
                    {
                        if (desired) dock.TryDock(); else dock.Disconnect();
                        Announce(dock, action, dock.IsDocked);
                    }
                    break;

                case GridAction.RailCoupler:
                    if (block is GridRailCoupler coupler)
                    {
                        coupler.Toggle();
                        Announce(coupler, action, coupler.IsCoupled);
                    }
                    break;

                case GridAction.Piston:
                    if (block is GridPiston piston)
                    {
                        piston.SetExtended(desired);
                        Announce(piston, action, desired);
                    }
                    break;

                case GridAction.SlidingDoor:
                    if (block is GridSlidingDoor door)
                    {
                        door.SetOpen(desired);
                        Announce(door, action, desired);
                    }
                    break;
            }
        }

        // ── 3. everyone mirrors the result ───────────────────────────────────

        /// <summary>Client side: adopt the host's answer. Animations are allowed to run
        /// here because their end state has already been approved; joints and rigidbody
        /// ownership are not touched, because those belong to the host's simulation.</summary>
        public static void ApplyState(string netId, Vector3Int cell, GridAction action, bool state)
        {
            var block = FindBlock(netId, cell);
            if (block == null) return;

            IsApplyingRemote = true;
            try
            {
                switch (action)
                {
                    case GridAction.LandingGear:
                        (block as GridLandingGear)?.ApplyNetworkLock(state);
                        break;
                    case GridAction.DockingPort:
                        (block as GridDockingPort)?.ApplyNetworkDock(state);
                        break;
                    case GridAction.RailCoupler:
                        (block as GridRailCoupler)?.ApplyNetworkCoupled(state);
                        break;
                    case GridAction.Piston:
                        (block as GridPiston)?.SetExtended(state);
                        break;
                    case GridAction.SlidingDoor:
                        (block as GridSlidingDoor)?.SetOpen(state);
                        break;
                }
            }
            finally { IsApplyingRemote = false; }
        }

        /// <summary>Host side: hand a joining client the current state of every lever on
        /// every hull, so a ship that was already sitting on its gear when they arrived
        /// does not appear to them to be floating.</summary>
        public static void SendSnapshot(FishNet.Connection.NetworkConnection target)
        {
            if (!CanSync || NetworkSession.Mode != SessionMode.Host) return;

            var tags = new System.Collections.Generic.List<GridNetTag>();
            GridSync.CollectTags(tags);

            foreach (var tag in tags)
            {
                if (tag == null || tag.Grid == null || string.IsNullOrEmpty(tag.Id)) continue;
                foreach (var block in tag.Grid.AllBlocks)
                {
                    if (block == null) continue;
                    switch (block)
                    {
                        case GridLandingGear gear when gear.IsLocked:
                            NetworkBootstrap.Instance.SendGridActionState(
                                tag.Id, gear.GridPos, GridAction.LandingGear, true, target);
                            break;
                        case GridDockingPort dock when dock.IsDocked:
                            NetworkBootstrap.Instance.SendGridActionState(
                                tag.Id, dock.GridPos, GridAction.DockingPort, true, target);
                            break;
                        case GridRailCoupler coupler when coupler.IsCoupled:
                            NetworkBootstrap.Instance.SendGridActionState(
                                tag.Id, coupler.GridPos, GridAction.RailCoupler, true, target);
                            break;
                        case GridPiston piston when piston.IsExtended:
                            NetworkBootstrap.Instance.SendGridActionState(
                                tag.Id, piston.GridPos, GridAction.Piston, true, target);
                            break;
                        case GridSlidingDoor door when door.IsOpen:
                            NetworkBootstrap.Instance.SendGridActionState(
                                tag.Id, door.GridPos, GridAction.SlidingDoor, true, target);
                            break;
                    }
                }
            }
        }
    }
}
