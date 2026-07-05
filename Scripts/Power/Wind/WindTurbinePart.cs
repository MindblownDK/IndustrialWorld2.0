// Assets/Scripts/VoxelEngine/Power/Wind/WindTurbinePart.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  WIND TURBINE PART — one placeable module of a modular turbine. ║
// ║  Tower / Nacelle / Gearbox / Generator / Hub / Blade (HAWT)     ║
// ║  VerticalRotor / VerticalBlade (VAWT).                          ║
// ║  Parts self-attach to the nearest compatible controller and     ║
// ║  snap into their exact socket pose. Each part carries its own   ║
// ║  condition (100 → degrades slowly under load).                  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public enum WindTurbinePartKind
    {
        Tower,
        Nacelle,
        Gearbox,
        Generator,
        Hub,
        Blade,
        VerticalRotor,
        VerticalBlade
    }

    public class WindTurbinePart : MonoBehaviour
    {
        [Header("Identity")]
        public WindTurbinePartKind kind = WindTurbinePartKind.Tower;
        [Tooltip("Tier id — must match the controller's tierId to attach (t90 / t150 / t236 / vsmall / vlarge).")]
        public string tierId = "t90";

        [Header("Condition")]
        [Tooltip("100 = factory-new. Degrades slowly while the turbine runs under load.")]
        [Range(0f, 100f)] public float condition = 100f;

        /// <summary>Controller this part is attached to (null while orphaned).</summary>
        public WindTurbineController Controller { get; internal set; }

        /// <summary>Blade slot index (0..n-1) — -1 for non-blade parts.</summary>
        public int SlotIndex { get; internal set; } = -1;

        private float _retryTimer;
        private bool  _isRoot;

        private void Awake()
        {
            // Tower / VerticalRotor prefabs carry the controller on the same GameObject.
            _isRoot = GetComponent<WindTurbineController>() != null;
        }

        private bool _live;

        private void Start()
        {
            // Build ghosts are prefab clones WITHOUT a PlacedBlock — never let a
            // ghost part claim a real turbine socket.
            _live = GetComponent<VoxelEngine.Building.PlacedBlock>() != null;
            if (_live && !_isRoot) TryAttach();
        }

        private void Update()
        {
            if (!_live || _isRoot || Controller != null) return;

            // Orphaned part (e.g. restored from save before its tower spawned) —
            // keep looking for a compatible controller at a relaxed cadence.
            _retryTimer += Time.deltaTime;
            if (_retryTimer >= 0.5f)
            {
                _retryTimer = 0f;
                TryAttach();
            }
        }

        private void TryAttach()
        {
            var c = WindTurbineController.FindBestFor(this, transform.position);
            if (c != null) c.Attach(this);
        }

        private void OnDestroy()
        {
            Controller?.Detach(this);
        }
    }
}
