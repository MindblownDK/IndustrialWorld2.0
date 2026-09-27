// Assets/Scripts/VoxelEngine/Building/Tiered/FoundationSupportLegs.cs
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Extends the four authored foundation legs to the first solid surface below.</summary>
    public sealed class FoundationSupportLegs : MonoBehaviour
    {
        private const float MaxDrop = 40f;
        public Transform[] legs;

        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private float _nextRefresh;

        private void OnEnable()
        {
            _lastPosition = new Vector3(float.PositiveInfinity, 0f, 0f);
            Refresh();
        }

        private void LateUpdate()
        {
            if (Time.time < _nextRefresh && Vector3.SqrMagnitude(transform.position - _lastPosition) < 0.0001f
                && Quaternion.Angle(transform.rotation, _lastRotation) < 0.05f) return;
            Refresh();
        }

        public void Refresh()
        {
            _nextRefresh = Time.time + 0.2f;
            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
            if (legs == null) return;

            Vector3 up = transform.up.normalized;
            for (int i = 0; i < legs.Length; i++)
            {
                Transform leg = legs[i];
                if (leg == null) continue;
                Vector3 origin = transform.TransformPoint(new Vector3(leg.localPosition.x, 0.08f, leg.localPosition.z));
                float length = FindGroundDistance(origin, -up);
                bool visible = length > 0.06f;
                leg.gameObject.SetActive(visible);
                if (!visible) continue;
                leg.localPosition = new Vector3(leg.localPosition.x, -length * 0.5f, leg.localPosition.z);
                leg.localScale = new Vector3(0.34f, length, 0.34f);
            }
        }

        private float FindGroundDistance(Vector3 origin, Vector3 direction)
        {
            var hits = Physics.RaycastAll(origin, direction, MaxDrop, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider candidate = hits[i].collider;
                if (candidate == null || candidate.transform.IsChildOf(transform)) continue;
                if (candidate.GetComponentInParent<PlacedTieredBlock>() != null) continue;
                return Mathf.Max(0f, hits[i].distance - 0.08f);
            }
            return 0f;
        }
    }
}
