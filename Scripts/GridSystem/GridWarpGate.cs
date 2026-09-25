// Assets/Scripts/VoxelEngine/GridSystem/GridWarpGate.cs
//
// THE Warp Gate (prototype) — a buildable fixed structure for ship-scale transit
// without a drive aboard. Two gates pair by matching code; a gate that is powered,
// charged and in vacuum opens its aperture for a short window, and the first ship
// whose hull enters the sphere is handed to the paired gate's rendezvous point —
// the same floating-origin hop the drive uses (TeleportSubjectToCosmic +
// SettleGridAfterHop) and the same transit FX (WarpFx). One transit consumes the
// window; the coils then cool before the gate can charge again.
//
// Prototype rules, stated honestly:
//   • Gates only pair within the charted system — this is the interplanetary
//     prototype, not the endgame interstellar gate.
//   • Code 0 means unpaired and never opens; 1-99 pair with the oldest other
//     powered, charged gate sharing the code.
//   • The paired arrival volume is collision-checked (ArrivalBlocked); a buried
//     gate refuses transits instead of burying ships.
//   • The gate never moves a ship that is docked to IT — its own grid is excluded.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.UI;

namespace VoxelEngine.GridSystem
{
    public class GridWarpGate : GridBlock
    {
        [Header("Warp Gate (prototype)")]
        [Tooltip("Seconds of charging under full draw before the gate can open.")]
        public float chargeSeconds = 90f;

        [Tooltip("Power draw (W) while charging.")]
        public float powerDrawWatts = 120000f;

        [Tooltip("Seconds the aperture stays open. One transit consumes the window.")]
        public float openSeconds = 25f;

        [Tooltip("Cooldown after a window closes (expired or used) before charging again.")]
        public float cooldownSeconds = 120f;

        [Tooltip("Aperture radius (m). A ship whose hull enters the sphere while the gate is open transits.")]
        public float apertureRadius = 220f;

        [Tooltip("Rendezvous distance (km) kept off the paired gate on arrival.")]
        public double arrivalStandoffKm = 2d;

        [Tooltip("Gates pair by matching code. 0 = unpaired (never opens). 1-99 pair with the oldest other powered, charged gate sharing the code.")]
        [Range(0, 99)] public int pairingCode = 0;

        // ── Runtime state ─────────────────────────────────────────
        public float Charge01 { get; private set; }
        public float Cooldown01 { get; private set; }
        public bool IsOpen { get; private set; }
        public float OpenRemain { get; private set; }
        public float CurrentChargeWatts { get; private set; }

        public override float PowerDraw =>
            (Enabled && Grid != null && !IsOpen && Cooldown01 <= 0f && pairingCode != 0 && Charge01 < 1f)
                ? powerDrawWatts : 0f;

        private static readonly List<GridWarpGate> s_all = new();
        /// <summary>Live gates, for pairing and the panel.</summary>
        public static IReadOnlyList<GridWarpGate> All => s_all;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Warp Gate";
            BlockMass = 8000f;
            maxHP = 4000f;
            currentHP = maxHP;
            Register();
        }

        private void OnEnable() => Register();
        private void OnDisable() => Unregister();
        private void Register() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void Unregister() { s_all.Remove(this); }

        public override void OnRemoved()
        {
            Unregister();
            base.OnRemoved();
        }

        private void Update()
        {
            if (Grid == null) return;

            if (Cooldown01 > 0f)
                Cooldown01 = Mathf.Max(0f, Cooldown01 - Time.deltaTime / Mathf.Max(1f, cooldownSeconds));

            CurrentChargeWatts = 0f;
            bool canCharge = Enabled && Grid.HasPower && pairingCode != 0
                && !IsOpen && Cooldown01 <= 0f && Charge01 < 1f;
            if (canCharge)
            {
                Charge01 = Mathf.MoveTowards(Charge01, 1f, Time.deltaTime / Mathf.Max(1f, chargeSeconds));
                CurrentChargeWatts = powerDrawWatts * Mathf.Clamp01(Grid.PowerAvailability01);
            }

            if (IsOpen)
            {
                OpenRemain -= Time.deltaTime;
                if (OpenRemain <= 0f) CloseGate("expired");
                return;
            }

            if (Charge01 >= 1f && Enabled && Grid.HasPower && pairingCode != 0 && Cooldown01 <= 0f
                && AtmosphereManager.IsInSpace(transform.position))
                OpenGate();
        }

        private void FixedUpdate()
        {
            if (!IsOpen) return;
            var pair = FindPairedGate();
            if (pair == null) { CloseGate("pair gone"); return; }

            // One hull inside the sphere: the first ship found transits and the
            // window is spent. The gate's own grid never transits through itself.
            Vector3 centre = Grid != null ? Grid.GetGridCenter() : transform.position;
            foreach (var col in Physics.OverlapSphere(centre, Mathf.Max(10f, apertureRadius)))
            {
                if (col == null) continue;
                var ship = col.GetComponentInParent<GridEntity>();
                if (ship == null || ship == Grid) continue;
                Transit(ship, pair);
                return;
            }
        }

        /// <summary>Changing the pairing code invalidates the charge in the coils —
        /// the panel calls this so a re-pointed gate starts its spin honestly.</summary>
        public void ResetCharge()
        {
            Charge01 = 0f;
        }

        /// <summary>The oldest other live gate sharing this code — the paired end.</summary>
        public GridWarpGate FindPairedGate()
        {
            if (pairingCode == 0) return null;
            for (int i = 0; i < s_all.Count; i++)
            {
                var other = s_all[i];
                if (other == null || other == this || other.pairingCode != pairingCode) continue;
                if (!other.Enabled || other.Grid == null) continue;
                return other;
            }
            return null;
        }

        public string PairedName()
        {
            var pair = FindPairedGate();
            if (pair == null) return pairingCode == 0 ? "unpaired" : "no partner";
            return !string.IsNullOrEmpty(pair.Grid?.name) ? pair.Grid.name : pair.name;
        }

        private void OpenGate()
        {
            IsOpen = true;
            OpenRemain = Mathf.Max(1f, openSeconds);
            BuildFeedbackHud.Show("Warp Gate", $"APERTURE OPEN — paired with {PairedName()}",
                null, new Color(0.55f, 0.85f, 1f));
        }

        private void CloseGate(string why)
        {
            IsOpen = false;
            OpenRemain = 0f;
            Charge01 = 0f;
            Cooldown01 = 1f;
        }

        /// <summary>Hand a ship to the paired gate: rendezvous standoff on the line the
        /// ship approaches from, collision-vetoed, then the same hop the drive uses.
        /// The window is spent whether or not the FX callback has fired — the
        /// destination is captured as a value, so the pair can burn down mid-flash.</summary>
        private void Transit(GridEntity ship, GridWarpGate pair)
        {
            var origin = SpaceOrigin.Instance;
            var registry = CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady) return;

            double3 shipKm = origin.GetCosmicKm(ship.transform.position);
            double3 pairKm = origin.GetCosmicKm(pair.transform.position);
            double3 radial = math.normalizesafe(shipKm - pairKm, new double3(0d, 1d, 0d));
            double3 dest = pairKm + radial * System.Math.Max(0.5d, arrivalStandoffKm);
            double distKm = math.length(dest - shipKm);
            if (GridWarpDrive.ArrivalBlocked(registry, dest))
            {
                BuildFeedbackHud.Show("Warp Gate", $"{PairedName()}'s arrival volume obstructed — transit refused",
                    null, new Color(1f, 0.7f, 0.25f));
                return;
            }

            string fromName = name;
            string toName = pair.PairedName();
            var subject = ship.transform;
            var subjectGrid = ship;
            VoxelEngine.FX.WarpFx.PlayJump(subjectGrid, () =>
            {
                if (subjectGrid == null || subject == null) return;
                origin.RegisterRoot(subject);
                origin.TeleportSubjectToCosmic(subject, dest);
                // A gate delivers at rest: unattended arrivals must not drift.
                GridWarpDrive.SettleGridAfterHop(subjectGrid, Vector3.zero);
                VoxelEngine.FX.WarpFx.ReportArrival(subjectGrid,
                    $"GATE TRANSIT - {fromName} TO {toName} - {distKm:0} km");
                Debug.Log($"[GridWarpGate] {fromName} -> {toName}: hull transited {distKm:0} km.");
            });

            CloseGate("transit");
            BuildFeedbackHud.Show("Warp Gate", $"Transit engaged — {toName} rendezvous",
                null, new Color(0.55f, 0.85f, 1f));
        }

        /// <summary>Save-load restore: charge, cooldown and the pairing code.
        /// An open window never persists — a loaded gate recharges and reopens.</summary>
        public void RestorePersistentState(float charge01, float cooldown01, int code)
        {
            Charge01 = Mathf.Clamp01(charge01);
            Cooldown01 = Mathf.Clamp01(cooldown01);
            pairingCode = Mathf.Clamp(code, 0, 99);
            IsOpen = false;
            OpenRemain = 0f;
        }
    }
}
