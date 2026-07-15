// Assets/Scripts/VoxelEngine/Building/Tiered/TieredDoor.cs

using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Animated door panel generated inside tiered Doorway prefabs.</summary>
    public sealed class TieredDoor : MonoBehaviour
    {
        public Transform doorPivot;
        [Range(70f, 130f)] public float openAngle = 100f;
        [Min(1f)] public float turnSpeed = 8f;

        private Quaternion _closedRotation;
        private bool _open;

        private void Awake()
        {
            if (doorPivot == null) doorPivot = transform.Find("Generated_DoorHinge");
            if (doorPivot != null) _closedRotation = doorPivot.localRotation;
        }

        private void Update()
        {
            if (doorPivot == null) return;
            Quaternion target = _closedRotation * Quaternion.Euler(0f, _open ? openAngle : 0f, 0f);
            doorPivot.localRotation = Quaternion.Slerp(
                doorPivot.localRotation,
                target,
                1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        public void Toggle()
        {
            _open = !_open;
        }
    }
}
