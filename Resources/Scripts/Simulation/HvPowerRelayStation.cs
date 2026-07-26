namespace VoxelEngine.Simulation
{
    public sealed class HvPowerRelayStation : CompactVoltageStation
    {
        protected override void Awake()
        {
            maxConnections = 8;
            wireReach = 150f;
            isHighVoltage = true;
            base.Awake();
        }
    }
}
