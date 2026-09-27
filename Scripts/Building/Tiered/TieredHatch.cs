// Assets/Scripts/VoxelEngine/Building/Tiered/TieredHatch.cs

using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// A floor hatch lid with a ladder that folds out of it.
    ///
    /// Closed, the lid sits flush in the floor frame and the ladder is furled
    /// inside it — you can walk over the hatch and never know it is there.
    /// Interacting swings the lid up on its hinge and unrolls the ladder down
    /// through the opening; interacting again folds both away. The ladder is
    /// only climbable while it is actually deployed, so a shut hatch is a floor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TieredHatch : MonoBehaviour
    {
        public Transform lidPivot;
        public Transform ladder;

        [Range(60f, 110f)] public float openAngle = 84f;
        [Min(1f)] public float turnSpeed = 7f;
        [Min(0.5f)] public float ladderDrop = 5.2f;

        private Quaternion _closedRotation;
        private Vector3 _furledScale;
        private Vector3 _furledPosition;
        private ClimbableLadder _climb;
        private bool _open;

        /// <summary>True once the lid is far enough open for the ladder to be usable.</summary>
        public bool IsDeployed => _open && Deployment > 0.55f;

        private float Deployment
        {
            get
            {
                if (lidPivot == null) return _open ? 1f : 0f;
                float angle = Quaternion.Angle(_closedRotation, lidPivot.localRotation);
                return Mathf.Clamp01(angle / Mathf.Max(1f, openAngle));
            }
        }

        private void Awake()
        {
            if (lidPivot == null) lidPivot = transform.Find("Generated_HatchPivot");
            if (ladder == null) ladder = transform.Find("Generated_Ladder");
            if (lidPivot != null) _closedRotation = lidPivot.localRotation;

            if (ladder != null)
            {
                _furledPosition = ladder.localPosition;
                _furledScale = ladder.localScale;
                // Start furled: zero length, tucked at the hinge.
                ladder.localScale = new Vector3(_furledScale.x, 0.02f, _furledScale.z);
                _climb = ladder.GetComponentInChildren<ClimbableLadder>(true);
            }
            if (_climb != null) _climb.deployed = false;
        }

        private void Update()
        {
            float t = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);

            if (lidPivot != null)
            {
                Quaternion target = _closedRotation * Quaternion.Euler(-(_open ? openAngle : 0f), 0f, 0f);
                lidPivot.localRotation = Quaternion.Slerp(lidPivot.localRotation, target, t);
            }

            if (ladder != null)
            {
                // The ladder unrolls: it grows downward from the hinge rather than
                // sliding as a rigid stick, which is what a furled rope ladder does.
                float want = _open ? 1f : 0.02f;
                var scale = ladder.localScale;
                scale.y = Mathf.Lerp(scale.y, _furledScale.y * want, t);
                ladder.localScale = scale;

                var pos = ladder.localPosition;
                pos.y = Mathf.Lerp(pos.y, _furledPosition.y - (_open ? ladderDrop * 0.5f : 0f), t);
                ladder.localPosition = pos;
            }

            if (_climb != null) _climb.deployed = IsDeployed;
        }

        /// <summary>Toggles the hatch. The opener's position is accepted for parity with doors.</summary>
        public void Toggle(Vector3 openerPosition)
        {
            _open = !_open;
            if (!_open && _climb != null) _climb.Release();
        }

        public void SetOpen(bool open)
        {
            _open = open;
            if (!open && _climb != null) _climb.Release();
        }
    }
}
