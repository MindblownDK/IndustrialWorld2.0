// Assets/Scripts/VoxelEngine/GridSystem/GridRailTruck.cs
//
// THE RAIL TRUCK — the grid block that turns a construct into a train.
//
// This is the whole player-facing story of Train System v2: build any grid you like,
// put a Rail Truck on it, drive it onto track. There is no "locomotive entity" to
// acquire and no special vehicle type - a train is a grid, so a wagon can carry
// containers, tanks, refineries or turrets simply because those are grid blocks and it
// is a grid.
//
// WHY A BLOCK RATHER THAN A FLAG ON THE GRID
// The Orbital Programme declares a satellite through `GridIdentity`, a property of the
// whole construct. A train is different: being railed is a piece of HARDWARE, and the
// player should have to build and pay for it, be able to remove it, and have it occupy
// space on the hull. A flag would make every grid a potential train for free.
//
// It also means the rail capability lives where the player expects - on the block they
// placed - and a grid with no truck simply cannot be railed, with no extra rule needed.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public class GridRailTruck : GridBlock
    {
        [Header("Rail Truck")]
        [Tooltip("Top speed this truck can pull, m/s. The slowest truck on a grid wins, so " +
                 "a heavy wagon genuinely slows a consist down.")]
        public float maxSpeed = 14f;

        [Tooltip("Acceleration and braking contributed by this truck, m/s^2.")]
        public float acceleration = 2.5f;

        [Tooltip("Power drawn while the train is actually moving.")]
        public float powerDraw = 220f;

        [Tooltip("Auto-snap: while this is on, an unrailled train re-latches onto rail " +
                 "under it on its own - after building, after reloading, after the line " +
                 "is extended into the yard. Turn it off in the bogie console to keep a " +
                 "parked wagon parked.")]
        public bool autoSnap = true;

        /// <summary>The bogie this truck installed on its parent grid.</summary>
        private GridRailBogie _bogie;

        public override float PowerDraw
        {
            get
            {
                if (!Enabled || _bogie == null) return 0f;
                // Only bill power while genuinely moving: a parked train should not drain a
                // base's grid overnight for doing nothing.
                return _bogie.Speed > 0.05f ? powerDraw : powerDraw * 0.05f;
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();

            // A truck must sit on RAIL, never on another truck.
            //
            // Stacked bogies are nonsense physically and break the load model: each would
            // claim its own rated capacity while carrying the same mass, so a tower of
            // trucks would make an overloaded train arbitrarily fast. Refusing the
            // placement is also clearer than silently ignoring the extra ones.
            if (HasTruckBelow())
            {
                VoxelEngine.UI.BuildFeedbackHud.Show("Cannot stack bogies",
                    "A rail truck goes on the rails or on the hull - never on top of " +
                    "another truck. Place it alongside instead.",
                    null, new Color(0.92f, 0.60f, 0.12f));

                // Remove itself rather than sitting there doing nothing, so the player gets
                // the block back and the grid is left exactly as it was.
                if (Grid != null) Grid.RemoveBlock(GridPos);
                return;
            }

            InstallBogie();
        }

        /// <summary>
        /// True when another rail truck occupies the cell directly below this one.
        ///
        /// Checked downward only: two trucks side by side on a wide chassis is a real
        /// four-wheel bogie arrangement and must stay legal.
        /// </summary>
        private bool HasTruckBelow()
        {
            if (Grid == null) return false;

            var below = GridPos + Vector3Int.down;
            if (!Grid.Blocks.TryGetValue(below, out var block) || block == null) return false;

            return block is GridRailTruck;
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            // Removing the last truck takes the grid off the rails and hands it back to
            // ordinary physics, rather than leaving it frozen on a track it can no longer
            // drive along.
            RefreshBogie(removingSelf: true);
        }

        private void OnEnable() => InstallBogie();

        /// <summary>
        /// Ensures the parent grid has a bogie, and tunes it from every truck aboard.
        ///
        /// Tuning is the SLOWEST truck and the WEAKEST acceleration across the consist,
        /// because a train is limited by its worst component. Taking the best would mean
        /// bolting one fast truck to a heavy wagon made the whole thing fast, which is
        /// backwards.
        /// </summary>
        private void InstallBogie()
        {
            if (Grid == null) return;

            _bogie = Grid.GetComponent<GridRailBogie>();
            if (_bogie == null) _bogie = Grid.gameObject.AddComponent<GridRailBogie>();

            RefreshBogie(removingSelf: false);

            // Snap to rail the moment the truck is fitted, if there is rail under it.
            //
            // Previously the only way onto the rails was a button buried in the block
            // console, so a player who built a train beside a line had no indication it
            // needed one more step - it simply sat there. Auto-snapping makes the obvious
            // action work, and the console button remains for re-attaching after a detach.
            if (!_bogie.IsOnRails) _bogie.TrySnapToTrack();
        }

        private void RefreshBogie(bool removingSelf)
        {
            if (Grid == null) return;

            var bogie = Grid.GetComponent<GridRailBogie>();
            if (bogie == null) return;

            float slowest = float.MaxValue;
            float weakest = float.MaxValue;
            int truckCount = 0;

            var trucks = Grid.GetComponentsInChildren<GridRailTruck>(includeInactive: false);
            for (int i = 0; i < trucks.Length; i++)
            {
                var truck = trucks[i];
                if (truck == null) continue;
                if (removingSelf && truck == this) continue;
                if (!truck.Enabled) continue;

                truckCount++;
                slowest = Mathf.Min(slowest, truck.maxSpeed);
                weakest = Mathf.Min(weakest, truck.acceleration);
            }

            if (truckCount == 0)
            {
                // No trucks left: detach and remove the bogie so the grid behaves exactly
                // like any other construct again.
                bogie.Detach();
                Destroy(bogie);
                return;
            }

            bogie.maxSpeed = slowest;
            bogie.acceleration = weakest;
        }
    }
}
