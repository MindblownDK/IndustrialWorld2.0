// Assets/Scripts/VoxelEngine/Building/Tiered/TieredDoor.cs

using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Animated hinged panel for a separately placed tiered Door.</summary>
    public sealed class TieredDoor : MonoBehaviour
    {
        public Transform doorPivot;
        public bool opensUp;
        [Range(70f, 130f)] public float openAngle = 100f;
        [Min(1f)] public float turnSpeed = 8f;

        private Quaternion _closedRotation;
        private float _signedOpenAngle;
        private bool _open;

        private void Awake()
        {
            if (doorPivot == null) doorPivot = transform.Find("Generated_DoorHinge");
            if (doorPivot != null) _closedRotation = doorPivot.localRotation;
            _signedOpenAngle = Mathf.Abs(openAngle);
        }

        private void Update()
        {
            if (doorPivot == null) return;
            float angle = _open ? _signedOpenAngle : 0f;
            Quaternion target = _closedRotation * (opensUp
                ? Quaternion.Euler(-angle, 0f, 0f)
                : Quaternion.Euler(0f, angle, 0f));
            doorPivot.localRotation = Quaternion.Slerp(
                doorPivot.localRotation,
                target,
                1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        /// <summary>
        /// Opens away from the interacting player, or closes when already open.
        /// The side is evaluated for every opening so approaching from the opposite
        /// side reverses the swing direction automatically.
        /// </summary>
        public void Toggle(Vector3 openerPosition)
        {
            if (_open)
            {
                _open = false;
                return;
            }

            if (doorPivot == null)
            {
                _open = true;
                return;
            }

            if (opensUp)
            {
                _signedOpenAngle = Mathf.Abs(openAngle) * 0.9f;
                _open = true;
                return;
            }

            Transform pivotParent = doorPivot.parent;
            Vector3 closedNormal = pivotParent != null
                ? pivotParent.TransformDirection(_closedRotation * Vector3.forward)
                : _closedRotation * Vector3.forward;
            float openerSide = Vector3.Dot(openerPosition - doorPivot.position, closedNormal);

            // Positive local yaw moves the free edge toward local back. A player on
            // local front therefore gets a positive swing; a player on local back
            // gets the mirrored negative swing.
            float magnitude = Mathf.Abs(openAngle);
            _signedOpenAngle = openerSide >= 0f ? magnitude : -magnitude;
            _open = true;
        }

        /// <summary>Compatibility overload for non-player callers.</summary>
        public void Toggle()
        {
            if (_open)
            {
                _open = false;
                return;
            }

            _signedOpenAngle = Mathf.Abs(openAngle);
            _open = true;
        }
    }
}
