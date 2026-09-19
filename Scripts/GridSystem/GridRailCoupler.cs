// Assets/Scripts/VoxelEngine/GridSystem/GridRailCoupler.cs
//
// THE WAGON COUPLER — the block that joins one car to the next.
//
// WHY A BLOCK RATHER THAN A BUTTON
// Coupling already worked from the Rail Truck console, but that made it an abstract
// command rather than a thing on the train. A coupler you bolt to the end of a wagon
// says where the connection physically is, has to be built and paid for, and can be
// removed - which is the same argument that made the Rail Truck a block rather than a
// flag on the grid.
//
// It also gives the player somewhere to aim. Walking up to the gap between two wagons
// and pressing a coupler is a far clearer gesture than opening a menu on one of them and
// picking the other from a list.
//
// WHAT IT DOES NOT DO
// It holds no physics joint. Consists follow the leader's recorded path (see
// GridRailBogie), because a joint between kinematic bodies does nothing and a rope-style
// follower cuts corners. The coupler is the player-facing control for that system, not a
// second implementation of it.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridRailCoupler : GridBlock
    {
        [Header("Coupler")]
        [Tooltip("How far this coupler reaches to find another car, in metres.")]
        public float reachMetres = 12f;

        [Tooltip("Power drawn while coupled. Small but not free: a coupler is a powered " +
                 "latch, and a long consist should cost something to hold together.")]
        public float powerDraw = 15f;

        public override float PowerDraw => Enabled && IsCoupled ? powerDraw : 0f;

        /// <summary>The bogie on this coupler's own grid.</summary>
        public GridRailBogie OwnBogie =>
            Grid != null ? Grid.GetComponent<GridRailBogie>() : null;

        /// <summary>True when this car is towed by something.</summary>
        public bool IsCoupled => OwnBogie != null && OwnBogie.LeadBogie != null;

        /// <summary>
        /// Couples this car behind the nearest eligible one.
        ///
        /// Returns false with a reason rather than silently doing nothing, because a
        /// coupler that appears to do nothing is indistinguishable from a broken one.
        /// </summary>
        public bool TryCouple(out string reason)
        {
            var bogie = OwnBogie;
            if (bogie == null)
            {
                reason = "This construct has no rail truck, so it cannot be part of a train.";
                return false;
            }

            if (bogie.LeadBogie != null)
            {
                reason = "Already coupled.";
                return false;
            }

            var leader = bogie.FindCouplingCandidate();
            if (leader == null)
            {
                reason = $"No car within {GridRailBogie.MaxCouplingRange:0} m that can take a wagon.";
                return false;
            }

            return bogie.TryCoupleTo(leader, out reason);
        }

        /// <summary>Releases this car from the one ahead.</summary>
        public void Uncouple()
        {
            var bogie = OwnBogie;
            if (bogie == null) return;
            bogie.Uncouple();
        }

        /// <summary>One press toggles, which is what a coupler lever does.</summary>
        public void Toggle()
        {
            if (IsCoupled) Uncouple();
            else TryCouple(out _);
        }

        public string StatusLabel
        {
            get
            {
                var bogie = OwnBogie;
                if (bogie == null) return "No rail truck on this construct";
                if (bogie.LeadBogie != null) return $"Coupled to {bogie.LeadBogie.name}";
                if (!bogie.IsOnRails) return "Not on rails";

                var candidate = bogie.FindCouplingCandidate();
                return candidate != null
                    ? $"Ready to couple to {candidate.name}"
                    : "Nothing in range to couple to";
            }
        }
    }
}
