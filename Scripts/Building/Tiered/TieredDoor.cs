// Assets/Scripts/VoxelEngine/Building/Tiered/TieredDoor.cs

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>Animated side-hinged door or segmented overhead garage shutter.</summary>
    public sealed class TieredDoor : MonoBehaviour
    {
        private const float GarageHeight = 5.05f;
        private const float GarageRollRadius = 0.34f;

        public Transform doorPivot;
        /// <summary>Second hinge of a double gate; both leaves swing apart together.</summary>
        public Transform doorPivotB;
        public bool opensUp;
        [Range(70f, 130f)] public float openAngle = 100f;
        [Min(1f)] public float turnSpeed = 8f;
        /// <summary>When above zero the leaves turn at this constant rate instead of
        /// the eased door swing - heavy gates move slowly to signify their mass.</summary>
        [Min(0f)] public float degreesPerSecond = 0f;

        private Quaternion _closedRotation;
        private Quaternion _closedRotationB;
        private float _signedOpenAngle;
        private bool _open;
        private float _roll;
        private readonly List<MeshFilter> _shutterFilters = new();
        private readonly List<Vector3[]> _closedVertices = new();
        private Collider[] _doorColliders;
        private Collider[] _rootColliders = System.Array.Empty<Collider>();

        private void Awake()
        {
            if (doorPivot == null) doorPivot = transform.Find("Generated_DoorHinge");
            if (doorPivot != null) _closedRotation = doorPivot.localRotation;
            if (doorPivotB == null) doorPivotB = transform.Find("Generated_DoorHingeB");
            if (doorPivotB != null) _closedRotationB = doorPivotB.localRotation;
            _signedOpenAngle = Mathf.Abs(openAngle);
            _doorColliders = GetComponentsInChildren<Collider>(true);
            // Colliders directly on the ROOT of a side-hinged door are the old
            // closed-pose boxes from prefabs built before 13.17.0 - they cannot
            // swing, so they walled open doorways shut. They stay solid while
            // the leaf is closed and release as it opens (rebuilt prefabs carry
            // their colliders on the hinges and leave this list empty).
            if (!opensUp) _rootColliders = GetComponents<Collider>();
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
            doorPivot.localRotation = Turn(doorPivot.localRotation, targetRotation);

            if (doorPivotB != null)
            {
                // The mirror hinge carries a 180 degree turn, so the opposite
                // sign swings its leaf to the same world side as the first.
                // Both leaves carry their colliders on the hinges, so the
                // passage clears physically as they part.
                Quaternion targetB = _closedRotationB * Quaternion.Euler(0f, -angle, 0f);
                doorPivotB.localRotation = Turn(doorPivotB.localRotation, targetB);
            }

            if (_rootColliders.Length > 0)
            {
                // Legacy closed-pose boxes: solid while shut, released once the
                // leaf has swung a quarter of the way open.
                float travelled = Quaternion.Angle(doorPivot.localRotation, _closedRotation);
                bool solid = travelled < Mathf.Abs(_signedOpenAngle) * 0.25f;
                for (int i = 0; i < _rootColliders.Length; i++)
                {
                    Collider rootCollider = _rootColliders[i];
                    if (rootCollider != null && !rootCollider.isTrigger)
                        rootCollider.enabled = solid;
                }
            }
        }

        /// <summary>Constant-rate turn for heavy gates, eased swing for doors.</summary>
        private Quaternion Turn(Quaternion current, Quaternion target)
        {
            return degreesPerSecond > 0f
                ? Quaternion.RotateTowards(current, target, degreesPerSecond * Time.deltaTime)
                : Quaternion.Slerp(current, target, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
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

        public bool IsOpen => _open;

        /// <summary>Side the leaf currently swings to, +1 or -1 (replicated with the open state).</summary>
        public float OpenSideSign => _signedOpenAngle < 0f ? -1f : 1f;

        public void Toggle(Vector3 openerPosition)
        {
            if (_open) { _open = false; AnnounceState(); return; }
            if (doorPivot == null || opensUp) { _open = true; AnnounceState(); return; }

            Transform pivotParent = doorPivot.parent;
            Vector3 closedNormal = pivotParent != null
                ? pivotParent.TransformDirection(_closedRotation * Vector3.forward)
                : _closedRotation * Vector3.forward;
            float openerSide = Vector3.Dot(openerPosition - doorPivot.position, closedNormal);
            float magnitude = Mathf.Abs(openAngle);
            _signedOpenAngle = openerSide >= 0f ? magnitude : -magnitude;
            _open = true;
            AnnounceState();
        }

        public void Toggle()
        {
            _open = !_open;
            if (!opensUp && _open) _signedOpenAngle = Mathf.Abs(openAngle);
            AnnounceState();
        }

        /// <summary>Remote/snapshot state application - silent, never announces back.</summary>
        public void SetOpenState(bool open, float sideSign)
        {
            if (!opensUp && sideSign != 0f) _signedOpenAngle = Mathf.Abs(openAngle) * Mathf.Sign(sideSign);
            _open = open;
        }

        private void AnnounceState()
            => VoxelEngine.Networking.BuildingSync.AnnounceDoorState(this, _open, OpenSideSign);
    }
}
