// Assets/Scripts/VoxelEngine/Building/Tiered/TieredDoor.cs

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Animated side-hinged door or segmented overhead garage shutter.</summary>
    public sealed class TieredDoor : MonoBehaviour
    {
        private const float GarageHeight = 4.30f;
        private const float GarageRollRadius = 0.34f;

        public Transform doorPivot;
        public bool opensUp;
        [Range(70f, 130f)] public float openAngle = 100f;
        [Min(1f)] public float turnSpeed = 8f;

        private Quaternion _closedRotation;
        private float _signedOpenAngle;
        private bool _open;
        private float _roll;
        private readonly List<MeshFilter> _shutterFilters = new();
        private readonly List<Vector3[]> _closedVertices = new();
        private Collider[] _doorColliders;

        private void Awake()
        {
            if (doorPivot == null) doorPivot = transform.Find("Generated_DoorHinge");
            if (doorPivot != null) _closedRotation = doorPivot.localRotation;
            _signedOpenAngle = Mathf.Abs(openAngle);
            _doorColliders = GetComponentsInChildren<Collider>(true);
            if (opensUp) CacheShutterMeshes();
        }

        private void Update()
        {
            if (doorPivot == null) return;
            if (opensUp)
            {
                float target = _open ? 1f : 0f;
                float next = Mathf.MoveTowards(_roll, target, turnSpeed * 0.22f * Time.deltaTime);
                if (!Mathf.Approximately(next, _roll))
                {
                    _roll = next;
                    ApplyGarageRoll();
                }
                bool blocksOpening = _roll < 0.72f;
                for (int i = 0; i < _doorColliders.Length; i++)
                {
                    Collider doorCollider = _doorColliders[i];
                    if (doorCollider != null && !doorCollider.isTrigger)
                        doorCollider.enabled = blocksOpening;
                }
                return;
            }

            float angle = _open ? _signedOpenAngle : 0f;
            Quaternion targetRotation = _closedRotation * Quaternion.Euler(0f, angle, 0f);
            doorPivot.localRotation = Quaternion.Slerp(
                doorPivot.localRotation, targetRotation,
                1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        private void CacheShutterMeshes()
        {
            var filters = doorPivot.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null || !filter.name.StartsWith("Mesh_Skin")) continue;
                Mesh instance = Instantiate(filter.sharedMesh);
                instance.name = filter.sharedMesh.name + "_RollingInstance";
                filter.sharedMesh = instance;
                _shutterFilters.Add(filter);
                _closedVertices.Add(instance.vertices);
            }
        }

        private void ApplyGarageRoll()
        {
            for (int i = 0; i < _shutterFilters.Count; i++)
            {
                Mesh mesh = _shutterFilters[i].sharedMesh;
                Vector3[] source = _closedVertices[i];
                var vertices = new Vector3[source.Length];
                for (int v = 0; v < source.Length; v++)
                {
                    Vector3 closed = source[v];
                    float along = Mathf.Clamp01(closed.y / GarageHeight);
                    float turns = along * Mathf.PI * 5f;
                    Vector3 rolled = closed;
                    rolled.y = GarageHeight + Mathf.Sin(turns) * GarageRollRadius;
                    rolled.z += (1f - Mathf.Cos(turns)) * GarageRollRadius;
                    vertices[v] = Vector3.LerpUnclamped(closed, rolled, _roll);
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
            }
        }

        public void Toggle(Vector3 openerPosition)
        {
            if (_open) { _open = false; return; }
            if (doorPivot == null) { _open = true; return; }
            if (opensUp) { _open = true; return; }

            Transform pivotParent = doorPivot.parent;
            Vector3 closedNormal = pivotParent != null
                ? pivotParent.TransformDirection(_closedRotation * Vector3.forward)
                : _closedRotation * Vector3.forward;
            float openerSide = Vector3.Dot(openerPosition - doorPivot.position, closedNormal);
            float magnitude = Mathf.Abs(openAngle);
            _signedOpenAngle = openerSide >= 0f ? magnitude : -magnitude;
            _open = true;
        }

        public void Toggle()
        {
            _open = !_open;
            if (!opensUp && _open) _signedOpenAngle = Mathf.Abs(openAngle);
        }
    }
}
