// Assets/Scripts/VoxelEngine/Player/PickaxeTool.cs
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;
using VoxelEngine.Modification;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Player
{
    public class PickaxeTool : MonoBehaviour
    {
        [Header("References")]
        public VoxelWorld world;
        public MaterialRegistry registry;
        public Camera shootCamera;

        [Header("Tool")]
        public float reach = 6f;
        public float brushRadius = 1.6f;
        public float strength = 60f;
        public MaterialId buildMaterial = MaterialId.Stone;
        public float fireRate = 6f;

        [Header("Effects (optional)")]
        public LineRenderer beam;
        public AudioSource  hitAudio;

        private float _nextHit;

        private void Awake()
        {
            if (world == null) world = VoxelWorld.Instance;
            if (shootCamera == null) shootCamera = Camera.main;
            // Route tool SFX through the SFX mixer bus (no-op without a mixer asset).
            if (hitAudio != null) VoxelEngine.FX.AudioManager.Route(hitAudio, music: false);
        }

        private void Update()
        {
            if (world == null) world = VoxelWorld.Instance;
            if (world == null || shootCamera == null) return;

            bool mine  = GameSettings.IsHeld(InputAction.Mine);
            bool build = GameSettings.IsHeld(InputAction.Build);
            if (!mine && !build)
            {
                if (beam) beam.enabled = false;
                return;
            }

            if (Time.time < _nextHit) return;
            _nextHit = Time.time + 1f / Mathf.Max(0.1f, fireRate);

            var ray = shootCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (!Physics.Raycast(ray, out var hit, reach))
            {
                if (beam) beam.enabled = false;
                return;
            }

            Vector3 point = mine
                ? hit.point - ray.direction.normalized * 0.2f
                : hit.point + hit.normal * 0.2f;

            if (mine)
                VoxelEditor.Subtract(world, registry, point, brushRadius, strength);
            else
                VoxelEditor.Add(world, registry, point, brushRadius, strength, buildMaterial);

            if (beam)
            {
                beam.enabled = true;
                beam.SetPosition(0, transform.position);
                beam.SetPosition(1, hit.point);
            }
            if (hitAudio) hitAudio.Play();
        }
    }
}
