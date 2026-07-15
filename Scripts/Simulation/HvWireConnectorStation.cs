namespace VoxelEngine.Simulation
{
    public sealed class HvWireConnectorStation : CompactVoltageStation
    {
        protected override void Awake()
        {
            maxConnections = 1;
            wireReach = 150f;
            isHighVoltage = true;
            base.Awake();
        }
    }
}
