// Assets/Scripts/VoxelEngine/Menu/WorldBootGate.cs
//
// 14.23.0-dev - milestone 8, step 1: join a game, not a save file.
//
// A client joining from the main menu has no world yet. It cannot: the seed,
// the per-planet seed table and the chosen solar system all belong to the
// HOST, and until the handshake delivers them there is nothing to generate.
//
// So world generation is held. This gate is the one switch that holds it, and
// it is held ONLY on a remote join - in single player it is never raised and
// every system that consults it behaves exactly as it did before. That is the
// whole safety argument for this change: offline boot runs the same code in
// the same order, because the gate is down.
//
// Raised by WorldSession when the main menu starts a join; lowered by
// NetworkBootstrap the moment the host's world card has been adopted.

using System;
using UnityEngine;

namespace VoxelEngine.Menu
{
    public static class WorldBootGate
    {
        /// <summary>True while world generation must not start yet.</summary>
        public static bool IsHeld { get; private set; }

        /// <summary>Plain-words reason, shown on the joining screen.</summary>
        public static string Status { get; private set; } = "";

        /// <summary>Raised when the gate opens. Subscribers boot their world.</summary>
        public static event Action Opened;

        /// <summary>Set while the gate is held and the join has gone wrong, so
        /// the joining screen can offer a way out instead of spinning forever.</summary>
        public static string Failure { get; private set; } = "";

        /// <summary>True once the join has gone wrong. The gate stays HELD on
        /// failure - there is still no world to show - so callers that mean
        /// "is the join still in flight" must test this too.</summary>
        public static bool HasFailed => !string.IsNullOrEmpty(Failure);

        /// <summary>True while a join is still working. The one condition a
        /// watchdog or a disconnect handler should act on.</summary>
        public static bool IsPending => IsHeld && !HasFailed;

        public static void Hold(string status)
        {
            IsHeld = true;
            Failure = "";
            Status = status ?? "";
        }

        public static void Report(string status)
        {
            if (IsHeld) Status = status ?? "";
        }

        /// <summary>Records a failure. Deliberately NOT conditional on the
        /// gate still being held: the world build runs inside Open(), so a
        /// build that throws reports after the gate has already lowered, and
        /// that report is the most important one there is.</summary>
        public static void Fail(string reason)
        {
            if (HasFailed) return;   // keep the FIRST reason: it is the cause
            Failure = string.IsNullOrEmpty(reason) ? "The join failed." : reason;
            Status = Failure;
            Debug.LogWarning("[WorldBootGate] " + Failure);
        }

        /// <summary>Let the world build. Safe to call twice - the second call
        /// does nothing, which matters because the handshake can legitimately
        /// be re-sent (a rename re-announces identity).</summary>
        public static void Open()
        {
            if (!IsHeld) return;
            IsHeld = false;
            Failure = "";
            Status = "";
            var handler = Opened;
            Opened = null;              // one-shot: a world is built once
            handler?.Invoke();
        }

        /// <summary>Scene teardown / returning to the menu. Drops everything so
        /// a stale subscription from a previous attempt can never fire.</summary>
        public static void Reset()
        {
            IsHeld = false;
            Status = "";
            Failure = "";
            Opened = null;
        }
    }
}
