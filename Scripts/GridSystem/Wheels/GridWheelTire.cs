// Assets/Scripts/VoxelEngine/GridSystem/Wheels/GridWheelTire.cs
//
// THE TIRE — half of a wheel, and the half the player bolts on last.
//
// A tire is an attachment, not a lattice block: it hangs off a hub's mount socket
// exactly where the ghost snapped it, so it can sit outside the grid's cell volume
// like a real wheel does. It therefore never occupies a cell, never blocks a build
// and never needs the lattice to make room for a 5x5 carcass.
//
// The tire owns rubber, not suspension. It contributes radius, width, mass and the
// three friction numbers; the hub owns the spring, the steering and the torque.
// That split is the whole point of the system: a worn tire can be swapped without
// rebuilding the suspension, and a big tire can be fitted to a small hub.

using UnityEngine;

namespace VoxelEngine.GridSystem
{
    [DisallowMultipleComponent]
    public class GridWheelTire : GridBlock
    {
        [Header("Tire")]
        public WheelSizeClass sizeClass = WheelSizeClass.Size_3x3;
        [Tooltip("Fine-tune the authored radius without leaving the size class.")]
        [Range(0.75f, 1.25f)] public float radiusScale = 1f;
        [Tooltip("Fine-tune the authored width without leaving the size class.")]
        [Range(0.6f, 1.6f)] public float widthScale = 1f;

        [Header("Rubber")]
        [Tooltip("Peak grip before the contact patch breaks away.")]
        [Range(0.2f, 2f)] public float staticFriction = 1.1f;
        [Tooltip("Grip once it is sliding. Always below static friction.")]
        [Range(0.2f, 2f)] public float dynamicFriction = 0.92f;
        [Tooltip("Sideways hold multiplier. High values resist drifting.")]
        [Range(0.2f, 2.5f)] public float lateralGrip = 1.12f;

        [Header("Wear")]
        [Tooltip("Grip left at zero tread. Tread is consumed by slip, not by distance.")]
        [Range(0.2f, 1f)] public float wornGripFloor = 0.55f;
        [Tooltip("Tread consumed per second of full wheelspin.")]
        [Range(0f, 0.02f)] public float wearPerSlipSecond = 0.0012f;
        [Range(0f, 1f)] public float tread01 = 1f;

        /// <summary>The hub this tire is bolted to, or null while it is loose.</summary>
        public GridWheel Hub { get; private set; }
        public bool IsMounted => Hub != null;

        /// <summary>Slip reported by the hub last step (0..1). FX and audio read this.</summary>
        public float Slip01 { get; internal set; }

        private Transform _spin;
        private Collider[] _colliders;
        private float _cachedCellSize = WheelTuning.ReferenceCellSize;

        public WheelPreset Preset => WheelTuning.For(sizeClass, _cachedCellSize);
        public float Radius => Preset.TireRadius * Mathf.Clamp(radiusScale, 0.5f, 2f);
        public float Width  => Preset.TireWidth  * Mathf.Clamp(widthScale, 0.4f, 2f);
        public float TireMass => Preset.TireMass;

        /// <summary>Grip scales down as tread is scrubbed away, never below the floor.</summary>
        public float WearFactor => Mathf.Lerp(Mathf.Clamp01(wornGripFloor), 1f, Mathf.Clamp01(tread01));
        public float EffectiveStatic  => staticFriction  * WearFactor;
        public float EffectiveDynamic => dynamicFriction * WearFactor;
        public float EffectiveLateral => lateralGrip     * WearFactor;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = $"Wheel {sizeClass.Label()}";
            CacheParts();
            ApplyPresetDefaults();
        }

        private void Awake() => CacheParts();

        private void CacheParts()
        {
            if (_spin == null)
            {
                _spin = transform.Find("TireSpin");
                if (_spin == null)
                {
                    var go = new GameObject("TireSpin");
                    go.transform.SetParent(transform, false);
                    _spin = go.transform;
                    // Re-parent authored visuals under the spin pivot so a prefab built
                    // without one still rotates instead of standing eerily still.
                    for (int i = transform.childCount - 1; i >= 0; i--)
                    {
                        var child = transform.GetChild(i);
                        if (child == _spin) continue;
                        child.SetParent(_spin, true);
                    }
                }
            }
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>Pulls authored friction defaults from the size table when unset.</summary>
        private void ApplyPresetDefaults()
        {
            var preset = Preset;
            if (staticFriction  <= 0.2f) staticFriction  = preset.StaticFriction;
            if (dynamicFriction <= 0.2f) dynamicFriction = preset.DynamicFriction;
            if (lateralGrip     <= 0.2f) lateralGrip     = preset.LateralGrip;
            if (BlockMass < preset.TireMass * 0.25f) BlockMass = preset.TireMass;
        }

        /// <summary>Bolts this tire onto a hub socket. Idempotent and null-safe.</summary>
        public void AttachTo(GridWheel hub, Transform socket, float cellSize)
        {
            if (hub == null || socket == null) return;
            CacheParts();
            Hub = hub;
            _cachedCellSize = cellSize > 0.1f ? cellSize : WheelTuning.ReferenceCellSize;
            // Deliberately NOT bound to the grid: a tire is an attachment, and a GridBlock
            // that knows a grid would ask that grid to remove the cell at its default
            // coordinate the moment it lost its last hit point.
            Grid = null;
            transform.SetParent(socket, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            SetCollidersEnabled(false);
            ApplyPresetDefaults();
        }

        /// <summary>Unbolts without destroying — the hub decides what happens next.</summary>
        public void DetachFromHub()
        {
            Hub = null;
            Slip01 = 0f;
            transform.SetParent(null, true);
            SetCollidersEnabled(true);
        }

        /// <summary>
        /// Tire colliders are dead weight while mounted: the hub's spherecast already
        /// resolves contact, and a real collider on the carcass would fight the spring
        /// and let a parked rig vibrate itself apart.
        /// </summary>
        private void SetCollidersEnabled(bool enabled)
        {
            if (_colliders == null) return;
            foreach (var collider in _colliders)
                if (collider != null) collider.enabled = enabled;
        }

        /// <summary>Called by the hub each physics step.</summary>
        public void ApplySpin(float degrees)
        {
            if (_spin == null) CacheParts();
            if (_spin != null) _spin.localRotation = Quaternion.AngleAxis(degrees, Vector3.right);
        }

        /// <summary>Scrubbing rubber off under slip. Purely local, no allocations.</summary>
        public void ReportSlip(float slip01, float deltaTime)
        {
            Slip01 = Mathf.Clamp01(slip01);
            if (wearPerSlipSecond <= 0f || Slip01 <= 0.05f) return;
            tread01 = Mathf.Clamp01(tread01 - Slip01 * wearPerSlipSecond * deltaTime);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Hub != null) Hub.NotifyTireLost(this);
            Hub = null;
        }
    }
}
