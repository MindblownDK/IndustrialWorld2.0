using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    public sealed class LvWireConnectorStation : CompactVoltageStation
    {
        protected override void Awake()
        {
            maxConnections = 1;
            wireReach = 15f;
            isHighVoltage = false;
            base.Awake();
        }
    }
}
