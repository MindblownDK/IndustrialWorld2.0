// Assets/Scripts/VoxelEngine/Items/OrbitalMapItem.cs
//
// Personal equipment that grants the orbital map.
//
// The map is not a free UI screen — it is a device the player has to research,
// pay for, and carry in a life-support equipment slot. That makes the first one
// a real milestone rather than a menu that was always there.
//
// The item also defines HOW GOOD the map is. A basic unit shows the bodies and
// your own vessel; better units add tracking range and the telemetry readouts.
// That gives the tier ladder somewhere to go without new UI.

using UnityEngine;

namespace VoxelEngine.Items
{
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Orbital Map Item", fileName = "OrbitalMap_New")]
    public class OrbitalMapItem : ItemDefinition
    {
        [Header("Tracking")]
        [Tooltip("How far from the player a construct can be and still appear on the map, in km. " +
                 "Constructs beyond this are listed as OUT OF RANGE rather than hidden, so the " +
                 "player can see that a better unit would reach them.")]
        public double trackingRangeKm = 250000d;

        [Tooltip("Show full orbital telemetry (apoapsis, periapsis, period, inclination). " +
                 "A basic unit shows only position and a state word.")]
        public bool showFullTelemetry = true;

        [Tooltip("Show the predicted ground track and orbit ellipse for tracked constructs.")]
        public bool showOrbitPaths = true;

        [Tooltip("Allow the map to retarget the camera onto any tracked construct.")]
        public bool allowFocusSwitching = true;

        [Header("Power")]
        [Tooltip("Whether this unit needs charge to operate. Early units are passive optics " +
                 "and are always available; advanced units trade convenience for upkeep.")]
        public bool requiresPower = false;

        /// <summary>The map device is a single carried instrument, never a stack.</summary>
        public override bool IsStackable => false;

        /// <summary>Short tier word for the equipment panel.</summary>
        public string CapabilityLabel
        {
            get
            {
                if (!showOrbitPaths) return "BASIC";
                return showFullTelemetry ? "FULL TELEMETRY" : "STANDARD";
            }
        }
    }
}
