// Assets/Scripts/VoxelEngine/Maritime/GridGearbox.cs
//
// Gearbox — transmits rotational power in ALL directions while trading
// torque for RPM. The higher the gear ratio, the faster the output spins
// but the less torque it delivers (power ≈ conserved). Speed is hard-clamped
// to prevent runaway gearing.
//
//   output_rpm    = input_rpm × GearRatio   (clamped to MaxOutputSpeed)
//   output_torque = input_torque ÷ GearRatio
//
// The actual math happens per-node in MechanicalPropagationJob using the BFS
// parent map built at graph rebuild — power always flows engine → load, so
// EITHER face can be the physical input: the far side automatically becomes
// the output. No orientation wiring needed.
//
// v6.11.0-dev —
//   • Free-form ratio: the player types or slides any value between
//     0.25× and 20.0× (was 20 fixed steps up to 6.00×).
//   • Ratio changes still apply LIVE every tick, no graph rebuild needed.
//   • Legacy blocks keep working: their stored gearRatio is re-clamped into
//     the new 0.25–20 range on first tick.
//
// v6.10.0-dev —
//   • 20 selectable gears, applied LIVE from the UI; Input RPM readout fixed;
//     legacy 2000 RPM cap auto-migrated to 10000 RPM.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridGearbox : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Gearbox;

        /// <summary>Minimum selectable ratio — 0.25× speed, 4× torque.</summary>
        public const float MinGearRatio = 0.25f;
        /// <summary>Maximum selectable ratio — 20× speed, 1/20th torque.</summary>
        public const float MaxGearRatio = 20f;

        [Header("Gearbox")]
        [Tooltip("Speed multiplier. >1 = faster but less torque. <1 = more torque but slower.\nAdjustable 0.25× … 20× from the gearbox panel.")]
        [Range(MinGearRatio, MaxGearRatio)]
        public float gearRatio = 1f;

        [Tooltip("Absolute RPM clamp on the output. The more speed, the less torque.")]
        public float maxOutputSpeed = 10000f;

        /// <summary>Legacy field from the fixed 20-gear era — kept so old prefabs and
        /// saves deserialize cleanly; no longer drives the ratio.</summary>
        [HideInInspector] public int selectedGear = 10;

        /// <summary>Current RPM passing through (written back by ApplyResults).</summary>
        public float CurrentRPM { get; private set; }
        /// <summary>Input RPM (before gear ratio).</summary>
        public float InputRPM { get; private set; }
        /// <summary>Output RPM (after gear ratio, clamped).</summary>
        public float OutputRPM => CurrentRPM;
        /// <summary>Actual ratio after output RPM governor clamping.</summary>
        public float AppliedRatio { get; private set; } = 1f;
        /// <summary>True when output torque demand exceeds safe limits.</summary>
        public bool IsOverstressed { get; private set; }
        /// <summary>0..1 stress level for UI.</summary>
        public float Stress01 { get; private set; }
        /// <summary>0..1 actual downstream torque demand relative to this gearbox's output capacity.</summary>
        public float MechanicalLoad01 { get; private set; }
        /// <summary>Unclamped downstream torque demand / available output torque. Above one means overload.</summary>
        public float MechanicalLoadRatio { get; private set; }
        /// <summary>Resolved input torque after the chain's finite-power service pass.</summary>
        public float InputTorque { get; private set; }
        /// <summary>Resolved output torque after this gearbox ratio.</summary>
        public float OutputTorque { get; private set; }

        /// <summary>Effective ratio, always inside MinGearRatio..MaxGearRatio.</summary>
        public float EffectiveRatio => Mathf.Clamp(gearRatio, MinGearRatio, MaxGearRatio);

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Gearbox";
            // Legacy migration: pre-6.10 blocks were capped at 2000 RPM, which
            // silently killed gears above ~1.3× on stock engines.
            if (maxOutputSpeed <= 2000f) maxOutputSpeed = 10000f;
            gearRatio = EffectiveRatio; // fold legacy ratios into the new range
            AppliedRatio = EffectiveRatio;
        }

        /// <summary>Set a free-form ratio (typed field / slider in the UI) — applied
        /// immediately, no graph rebuild needed.</summary>
        public void SetRatio(float ratio)
        {
            gearRatio = Mathf.Clamp(ratio, MinGearRatio, MaxGearRatio);
            // Until the next mechanical tick determines any governor clamp, show the
            // player's selected ratio rather than a stale previous result.
            AppliedRatio = gearRatio;
        }

        /// <summary>
        /// Restores the player's selected gearbox setting after <see cref="OnPlaced"/>
        /// has initialized prefab defaults during a grid load. The hidden legacy gear
        /// slot is retained alongside the exact free-form ratio so older saves and UI
        /// states remain meaningful.
        /// </summary>
        public void RestorePersistentSettings(float ratio, int persistedSelectedGear)
        {
            if (!float.IsNaN(ratio) && !float.IsInfinity(ratio)) SetRatio(ratio);
            selectedGear = Mathf.Clamp(persistedSelectedGear, 0, 20);
        }

        public override void PopulateMaritimeNode(ref MechanicalNode node)
        {
            node.GearRatio = EffectiveRatio;
            node.MaxGearSpeed = maxOutputSpeed;
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            if (!Enabled)
                node.SetFlag(MechanicalFlags.Broken);
            else
                node.ClearFlag(MechanicalFlags.Broken);

            // Stay in sync with the UI every tick so ratio changes apply LIVE.
            node.GearRatio = EffectiveRatio;
            node.MaxGearSpeed = maxOutputSpeed;
        }

        public override void ApplyResults(in MechanicalNode node)
        {
            CurrentRPM = node.CurrentRPM;
            AppliedRatio = Mathf.Max(0.01f, node.AppliedGearRatio);
            // Input speed/torque use the actual governor-clamped ratio, not an
            // impossible selected ratio above the RPM limit.
            InputRPM = CurrentRPM / AppliedRatio;
            OutputTorque = Mathf.Max(0f, node.ShaftTorque);
            InputTorque = OutputTorque * AppliedRatio;
            MechanicalLoadRatio = Mathf.Max(0f, node.MechanicalLoadRatio);
            MechanicalLoad01 = Mathf.Clamp01(MechanicalLoadRatio);

            // Both speed and actual downstream torque load matter. A gearbox at a
            // safe RPM but feeding a large generator bank now shows the corresponding
            // mechanical stress instead of reporting nearly idle forever.
            float speedRatio = maxOutputSpeed > 0f ? CurrentRPM / maxOutputSpeed : 0f;
            float overload = Mathf.Max(0f, MechanicalLoadRatio - 1f);
            Stress01 = Mathf.Clamp01(
                speedRatio * 0.25f
                + MechanicalLoad01 * 0.72f
                + overload * 0.55f);
            IsOverstressed = Stress01 > 0.92f;
        }
    }
}
