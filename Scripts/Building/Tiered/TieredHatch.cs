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

        private Quaternion _closedRotation;
        private Vector3 _deployedScale;
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
                // The ladder's authored scale IS its deployed length, and its origin
                // sits in the hatch plane. Only the length animates: sliding the whole
                // ladder down as well left it hanging in mid-air below the opening,
                // with a gap you could not climb up into.
                _deployedScale = ladder.localScale;
                ladder.localScale = new Vector3(_deployedScale.x, 0.02f, _deployedScale.z);
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
                // Unrolls downward from a fixed origin, which is what a furled
                // ladder does and what keeps its top rung at the hatch lip.
                var scale = ladder.localScale;
                scale.y = Mathf.Lerp(scale.y, _deployedScale.y * (_open ? 1f : 0.02f), t);
                ladder.localScale = scale;
            }

            if (_climb != null) _climb.deployed = IsDeployed;
        }

        public bool IsOpen => _open;

        /// <summary>Toggles the hatch. The opener's position is accepted for parity with doors.</summary>
        public void Toggle(Vector3 openerPosition)
        {
            _open = !_open;
            if (!_open && _climb != null) _climb.Release();
            VoxelEngine.Networking.BuildingSync.AnnounceDoorState(this, _open, 1f);
        }

        public void SetOpen(bool open)
        {
            _open = open;
            if (!open && _climb != null) _climb.Release();
        }
    }
}
