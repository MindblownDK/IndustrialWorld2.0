namespace VoxelEngine.Simulation
{
    public sealed class PowerRelayStation : CompactVoltageStation
    {
        protected override void Awake()
        {
            maxConnections = 8;
            wireReach = 25f;
            isHighVoltage = false;
            base.Awake();
        }
    }
}
