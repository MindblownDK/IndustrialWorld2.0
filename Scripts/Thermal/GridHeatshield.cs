// Assets/Scripts/VoxelEngine/Thermal/GridHeatshield.cs
//
// Ablative heat shield. Absorbs atmospheric entry heat on behalf of itself and the
// block directly behind it, burning away a finite ablator layer as it does so. Once
// the ablator is gone the shield still exists as structure but no longer protects,
// so a re-entry vehicle is a consumable that must be re-serviced between drops.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Thermal
{
    public class GridHeatshield : GridBlock, IHeatshieldBlock, IGridDataProvider
    {
        [Header("Heat Shield")]
        [Tooltip("Ablator charge in units. Entry heat burns this away before it touches HP.")]
        public float ablatorCapacity = 1000f;

        [Tooltip("Current ablator charge. Refill at a Ventilation/service bay or by rebuilding.")]
        public float ablatorRemaining = -1f;

        [Tooltip("Fraction of incident entry heat that passes through an intact shield.")]
        [Range(0.01f, 1f)] public float heatTransmission = 0.12f;

        /// <summary>Ablator units consumed per HP of thermal load absorbed.</summary>
        public const float AblatorPerDamage = 2.4f;

        public float HeatTransmission => Mathf.Clamp01(heatTransmission);
        public bool ShieldIntact => ablatorRemaining > 0.01f;

        /// <summary>0..1 ablator remaining, for gauges.</summary>
        public float Ablator01 => ablatorCapacity > 0f
            ? Mathf.Clamp01(ablatorRemaining / ablatorCapacity) : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block")
                blockName = "Heat Shield";
            // A negative value marks "never initialised", so an authored partial charge
            // on an existing prefab or a restored save is never silently topped up.
            if (ablatorRemaining < 0f) ablatorRemaining = ablatorCapacity;
            GridThermalSystem.For(Grid);
        }

        /// <summary>
        /// Burns ablator instead of hull. Any load beyond the remaining ablator spills
        /// over into real structural damage, so an exhausted shield fails gracefully.
        /// </summary>
        public void Ablate(float thermalLoad)
        {
            if (thermalLoad <= 0f) return;

            float cost = thermalLoad * AblatorPerDamage;
            if (cost <= ablatorRemaining)
            {
                ablatorRemaining -= cost;
                return;
            }

            float covered = ablatorRemaining / AblatorPerDamage;
            ablatorRemaining = 0f;
            float overflow = thermalLoad - covered;
            if (overflow > 0f) Damage(overflow);
        }

        /// <summary>Restores ablator charge. Returns the units actually accepted.</summary>
        public float Refill(float units)
        {
            if (units <= 0f) return 0f;
            float space = Mathf.Max(0f, ablatorCapacity - ablatorRemaining);
            float taken = Mathf.Min(space, units);
            ablatorRemaining += taken;
            return taken;
        }

        // ── Screen telemetry ───────────────────────────────────────────────────
        public string SourceName => string.IsNullOrWhiteSpace(blockName) ? "Heat Shield" : blockName;
        public string DataCategory => "Thermal";

        public string GetDisplayData()
        {
            float temp = Grid != null
                ? GridThermalSystem.For(Grid).TemperatureOf(this)
                : ThermalRules.FallbackAmbientC;

            return "HEAT SHIELD\n"
                 + (ShieldIntact ? ThermalRules.BandLabel(ThermalRules.Band(this, temp)) : "ABLATED")
                 + "\nTemp " + temp.ToString("0") + " °C"
                 + "\nAblator " + (Ablator01 * 100f).ToString("0") + "%";
        }
    }
}
