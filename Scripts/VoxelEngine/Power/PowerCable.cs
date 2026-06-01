// Assets/Scripts/VoxelEngine/Power/PowerCable.cs
namespace VoxelEngine.Power
{
    /// <summary>
    /// A cable. Doesn't generate or consume; carries power between nodes. Its WireDefinition
    /// determines the segment's capacity. The network's bottleneck is the MINIMUM capacity
    /// along its cables.
    /// </summary>
    public class PowerCable : PowerNode
    {
        public override PowerNodeKind Kind => PowerNodeKind.Cable;

        public WireDefinition wire;

        protected override void OnEnable()
        {
            // Use wire's radius but enforce minimum 1.6m so cables placed 1 grid-unit
            // apart in any direction (including vertically) reliably auto-connect.
            if (wire != null) connectRadius = UnityEngine.Mathf.Max(1.6f, wire.connectRadius);
            else connectRadius = 1.6f;
            base.OnEnable();
        }
    }
}
