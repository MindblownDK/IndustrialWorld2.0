// Assets/Scripts/VoxelEngine/GridSystem/GridContainmentVault.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║           CONTAINMENT VAULT — powered exotic storage (Phase 5)        ║
// ║                                                                       ║
// ║  Antimatter and dark matter are containment-class items: plain grid   ║
// ║  cargo REFUSES them. This vault is the only grid storage built to     ║
// ║  hold them — and containment is a POWERED process:                    ║
// ║                                                                       ║
// ║   • The containment field draws grid power that scales with the       ║
// ║     amount of exotic matter held (bigger load = stronger field).      ║
// ║   • Field PRESSURE rises toward the nominal target while powered and  ║
// ║     decays when power fails. A stable band marks healthy operation.   ║
// ║   • Below the stable band the vault warns (HUD + cockpit banner).     ║
// ║   • At zero pressure the exotic matter ANNIHILATES slowly — power      ║
// ║     loss on a loaded vault is a real emergency.                       ║
// ║                                                                       ║
// ║  Runtime visuals: the contained black hole spins its accretion disc,  ║
// ║  the containment rings counter-rotate and a status light bar reports  ║
// ║  the field state (green = stable, amber = low, red = critical).       ║
// ║  The dedicated machine panel (GridBlockUI) shows the full picture.    ║
// ╚══════════════════════════════════════════════════════════════════════╝
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public class GridContainmentVault : GridCargoContainer
    {
        [Header("Containment Power")]
        [Tooltip("Base power draw (W) to hold the containment field.")]
        public float basePowerDrawWatts = 12000f;

        [Tooltip("Additional power (W) per stored exotic unit — the field scales with its load.")]
        public float wattsPerStoredUnit = 200f;

        [Header("Containment Pressure")]
        [Tooltip("Nominal field pressure the vault maintains while powered.")]
        public float targetPressure = 70f;

        [Tooltip("Lower edge of the healthy pressure band.")]
        public float stablePressureMin = 55f;

        [Tooltip("Upper edge of the healthy pressure band.")]
        public float stablePressureMax = 85f;

        [Tooltip("Below this pressure containment is FAILING.")]
        public float criticalPressure = 32f;

        [Tooltip("Pressure gained per second while powered.")]
        public float pressureResponsePerSec = 14f;

        [Tooltip("Pressure lost per second while unpowered.")]
        public float pressureDecayPerSec = 10f;

        [Tooltip("Exotic units annihilated per second while pressure sits at zero.")]
        public float annihilatePerSecAtZero = 0.25f;

        [Header("Runtime Visuals")]
        [Tooltip("Spin speed of the contained black hole's accretion disc (deg/s).")]
        public float discSpinDegPerSecond = 14f;

        [Tooltip("Counter-spin speed of the containment rings (deg/s).")]
        public float ringSpinDegPerSecond = 9f;

        // ── Live state ────────────────────────────────────────────
        public float Pressure { get; private set; }
        public int ExoticUnits { get; private set; }
        public bool IsAnnihilating { get; private set; }

        /// <summary>STABLE / LOW PRESSURE / CRITICAL / NO POWER / ANNIHILATION.</summary>
        public string FieldStatus { get; private set; } = "CHARGING";

        /// <summary>Normalized pressure 0..1 for gauges.</summary>
        public float Pressure01 => targetPressure > 0f ? Mathf.Clamp01(Pressure / targetPressure) : 0f;

        public override float PowerDraw =>
            Enabled && Grid != null ? basePowerDrawWatts + wattsPerStoredUnit * ExoticUnits : 0f;

        private Transform _coreDisc, _ringA, _ringB;
        private Renderer _statusLight;
        private Renderer[] _pylonTips;
        private float _warnTimer;
        private float _annihilateAccumulator;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (blockName == "Armor Block" || blockName == "Cargo Container")
                blockName = "Containment Vault";
            // Containment-grade storage: this container DOES accept containment items.
            if (container != null) container.allowContainment = true;
            // Field starts charged on placement (fresh vaults power up on first grid tick).
            if (Pressure <= 0.01f) Pressure = targetPressure;
        }

        private void Update()
        {
            // ── Runtime visual drive (animation only while the field has power) ──
            if (_coreDisc == null) _coreDisc = transform.Find("CoreDisc");
            if (_ringA == null) _ringA = transform.Find("ContainmentRingA");
            if (_ringB == null) _ringB = transform.Find("ContainmentRingB");
            if (_statusLight == null)
            {
                var sl = transform.Find("StatusLight");
                if (sl != null) _statusLight = sl.GetComponent<Renderer>();
            }
            if (_pylonTips == null || _pylonTips.Length == 0)
            {
                var tips = new System.Collections.Generic.List<Renderer>();
                foreach (Transform child in transform)
                    if (child.name == "PylonTip")
                    {
                        var r = child.GetComponent<Renderer>();
                        if (r != null) tips.Add(r);
                    }
                _pylonTips = tips.ToArray();
            }

            bool powered = Enabled && Grid != null && Grid.HasPower;
            float dt = Time.deltaTime;
            float spinScale = powered ? 1f : 0.06f;   // field dies with the power

            if (_coreDisc != null) _coreDisc.Rotate(0f, discSpinDegPerSecond * dt * spinScale, 0f, Space.Self);
            if (_ringA != null) _ringA.Rotate(0f, -ringSpinDegPerSecond * dt * spinScale, 0f, Space.Self);
            if (_ringB != null) _ringB.Rotate(0f, ringSpinDegPerSecond * dt * spinScale, 0f, Space.Self);
            if (_statusLight != null && _statusLight.material != null)
                _statusLight.material.color = FieldLightColor();

            // Pylon tips breathe violet while powered, dim when the field is down.
            float pulse = powered ? 1.35f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.1f) : 0.30f;
            Color pylonC = new Color(0.55f, 0.30f, 0.95f) * pulse;
            for (int i = 0; i < _pylonTips.Length; i++)
            {
                if (_pylonTips[i] == null || _pylonTips[i].material == null) continue;
                _pylonTips[i].material.color = pylonC;
            }
        }

        private void FixedUpdate()
        {
            RecomputeExoticUnits();

            bool powered = Enabled && Grid != null && Grid.HasPower;
            float dt = Time.fixedDeltaTime;

            // ── Pressure dynamics ──
            if (powered)
                Pressure = Mathf.MoveTowards(Pressure, targetPressure, pressureResponsePerSec * dt);
            else
                Pressure = Mathf.Max(0f, Pressure - pressureDecayPerSec * dt);

            // ── Field state ──
            if (!Enabled)                 FieldStatus = "DISABLED";
            else if (Grid == null)        FieldStatus = "NO GRID";
            else if (!powered)            FieldStatus = "NO POWER";
            else if (Pressure <= 0.01f)   FieldStatus = "ANNIHILATION";
            else if (Pressure < criticalPressure) FieldStatus = "CRITICAL";
            else if (Pressure < stablePressureMin) FieldStatus = "LOW PRESSURE";
            else                          FieldStatus = "STABLE";

            bool danger = Pressure < stablePressureMin;

            // ── Annihilation: zero pressure + exotic content = loss ──
            IsAnnihilating = Pressure <= 0.01f && ExoticUnits > 0;
            if (IsAnnihilating)
            {
                _annihilateAccumulator += annihilatePerSecAtZero * dt;
                if (_annihilateAccumulator >= 1f)
                {
                    int lose = Mathf.FloorToInt(_annihilateAccumulator);
                    _annihilateAccumulator -= lose;
                    AnnihilateExotic(lose);
                }
            }
            else
            {
                _annihilateAccumulator = 0f;
            }

            // ── Warnings: HUD toast + cockpit banner ──
            if (danger || IsAnnihilating)
            {
                _warnTimer -= dt;
                if (_warnTimer <= 0f)
                {
                    _warnTimer = IsAnnihilating ? 2f : 4f;
                    bool critical = Pressure < criticalPressure || IsAnnihilating;
                    string title, detail;
                    if (IsAnnihilating)
                    {
                        title = "CONTAINMENT FIELD COLLAPSED";
                        detail = $"Vault — antimatter/dark matter annihilating ({ExoticUnits} units at risk)";
                    }
                    else if (critical)
                    {
                        title = "CONTAINMENT PRESSURE CRITICAL";
                        detail = $"Vault — {Pressure:0} pressure · field failing · stable range {stablePressureMin:0}–{stablePressureMax:0}";
                    }
                    else
                    {
                        title = "CONTAINMENT PRESSURE LOW";
                        detail = $"Vault — {Pressure:0} pressure · stable range {stablePressureMin:0}–{stablePressureMax:0}";
                    }
                    VoxelEngine.UI.BuildFeedbackHud.Show(title, detail, null,
                        critical ? new Color(1f, 0.2f, 0.1f) : new Color(1f, 0.7f, 0.25f));
                    VoxelEngine.UI.CockpitAlertHud.Report(title, detail, critical);
                }
            }
        }

        private void RecomputeExoticUnits()
        {
            ExoticUnits = 0;
            if (container == null) return;
            for (int i = 0; i < container.Size; i++)
            {
                var s = container.GetSlot(i);
                if (s != null && !s.IsEmpty && s.item != null && s.item.requiresContainment)
                    ExoticUnits += s.count;
            }
        }

        /// <summary>Destroy exotic units one at a time (only containment-class items).</summary>
        private void AnnihilateExotic(int count)
        {
            if (container == null) return;
            for (int i = 0; i < container.Size && count > 0; i++)
            {
                var s = container.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null || !s.item.requiresContainment) continue;
                int take = Mathf.Min(s.count, count);
                container.Remove(s.item, take);
                count -= take;
            }
            RecomputeExoticUnits();
        }

        /// <summary>Field-state colour for the prefab's status light bar.</summary>
        private Color FieldLightColor()
        {
            return FieldStatus switch
            {
                "ANNIHILATION" => new Color(1f, 0.08f, 0.06f),
                "CRITICAL"     => new Color(1f, 0.2f, 0.1f),
                "LOW PRESSURE" => new Color(1f, 0.65f, 0.15f),
                "NO POWER"     => new Color(0.9f, 0.3f, 0.15f),
                "DISABLED"     => new Color(0.35f, 0.36f, 0.4f),
                "CHARGING"     => new Color(0.4f, 0.7f, 1f),
                _              => new Color(0.25f, 0.9f, 0.45f),   // STABLE
            };
        }

        /// <summary>Only containment-class items (antimatter, dark matter).</summary>
        protected override bool MatchesFilter(ItemDefinition item)
        {
            if (item == null) return false;
            return item.requiresContainment;
        }

        // ── LCD data provider display ─────────────────────────────
        public override string DataCategory => "Containment";
        public override string GetDisplayData()
        {
            int antimatter = 0, darkMatter = 0;
            if (container != null)
            {
                for (int i = 0; i < container.Size; i++)
                {
                    var s = container.GetSlot(i);
                    if (s == null || s.IsEmpty || s.item == null) continue;
                    if (s.item.itemId != null && s.item.itemId.IndexOf("antimatter", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        antimatter += s.count;
                    else if (s.item.itemId != null && s.item.itemId.IndexOf("dark", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        darkMatter += s.count;
                }
            }
            return $"CONTAINMENT VAULT\n{FieldStatus}\nPressure {Pressure:0.0} / {targetPressure:0} (stable {stablePressureMin:0}–{stablePressureMax:0})\nPower {PowerDraw / 1000f:0.0} kW\nAM {antimatter} · DM {darkMatter}";
        }
    }
}
