// Assets/Scripts/VoxelEngine/Thermal/PlacedBlockHeat.cs
//
// Thermal state for a STATIC block (PlacedBlock or PlacedTieredBlock) that is being
// heated by an external source — today a thruster plume, tomorrow a reactor breach.
// Grid blocks do not use this; their temperature lives in GridThermalSystem.
//
//   • Slews toward the injected heat with the same rules as hull plate, so a landing
//     pad glows for minutes after the ship has lifted off.
//   • Above the block damage threshold it bleeds HP through the block's own Damage()
//     path, so drops, inventories and destruction all behave exactly as if a tool
//     had broken it. Integer HP is handled through an accumulator.
//   • Feeds BlockDamageVisual for glow, soot and cracks.
//   • Removes itself once the block has cooled back to ambient.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.Thermal
{
    [DisallowMultipleComponent]
    public class PlacedBlockHeat : MonoBehaviour
    {
        private const float TickInterval = 0.25f;

        private PlacedBlock _placed;
        private PlacedTieredBlock _tiered;
        private float _timer;
        private float _pendingHeatC;
        private float _damageAccumulator;
        private float _idleSeconds;

        /// <summary>Current temperature in °C.</summary>
        public float TemperatureC { get; private set; }

        public static PlacedBlockHeat For(Component blockRoot)
        {
            if (blockRoot == null) return null;
            var heat = blockRoot.GetComponent<PlacedBlockHeat>();
            if (heat == null)
            {
                heat = blockRoot.gameObject.AddComponent<PlacedBlockHeat>();
                heat.TemperatureC = ThermalRules.AmbientTemperatureC(blockRoot.transform.position);
            }
            return heat;
        }

        /// <summary>Inject a heat target (°C above ambient) for the current tick.</summary>
        public void AddHeat(float temperatureAboveAmbientC)
        {
            if (temperatureAboveAmbientC > _pendingHeatC) _pendingHeatC = temperatureAboveAmbientC;
            enabled = true;
        }

        private void Awake()
        {
            _placed = GetComponent<PlacedBlock>();
            _tiered = GetComponent<PlacedTieredBlock>();
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            float dt = TickInterval - _timer;
            _timer = TickInterval;

            float ambient = ThermalRules.AmbientTemperatureC(transform.position);
            float target = ambient + _pendingHeatC;
            _pendingHeatC = 0f;

            float rate = ThermalRules.SlewRate(TemperatureC, target, ambient);
            TemperatureC = Mathf.Lerp(TemperatureC, target, 1f - Mathf.Exp(-rate * dt));

            BlockDamageVisual.ReportTemperature(this, TemperatureC);

            float burn = ThermalRules.BlockDamagePerSecond(TemperatureC);
            if (burn > 0f)
            {
                _damageAccumulator += burn * dt;
                int whole = Mathf.FloorToInt(_damageAccumulator);
                if (whole > 0)
                {
                    _damageAccumulator -= whole;
                    if (ApplyDamage(whole)) return;   // block destroyed; component dies with it
                }
            }

            // Publish the structural fraction so cracks match the HP the heat has cost.
            ReportStructure();

            if (Mathf.Abs(TemperatureC - ambient) < 5f && target <= ambient + 1f)
            {
                _idleSeconds += dt;
                if (_idleSeconds > 4f) Destroy(this);
            }
            else _idleSeconds = 0f;
        }

        private bool ApplyDamage(int amount)
        {
            if (_placed != null)
            {
                _placed.Damage(amount, null);
                return _placed == null || _placed.Hp <= 0;
            }
            if (_tiered != null && _tiered.definition != null)
            {
                // Heat ignores tool tiers: pass the maximum so the check never blocks it.
                return _tiered.Damage(amount, 99, null);
            }
            return false;
        }

        private void ReportStructure()
        {
            if (_placed != null)
            {
                int max = _placed.Item != null ? Mathf.Max(1, _placed.Item.blockHealth) : Mathf.Max(1, _placed.Hp);
                BlockDamageVisual.ReportDamage(this, 1f - Mathf.Clamp01(_placed.Hp / (float)max));
            }
            else if (_tiered != null && _tiered.definition != null)
            {
                int max = Mathf.Max(1, _tiered.definition.GetStats(_tiered.tier).hp);
                BlockDamageVisual.ReportDamage(this, 1f - Mathf.Clamp01(_tiered.hp / (float)max));
            }
        }
    }
}
