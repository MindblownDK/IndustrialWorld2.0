// Assets/Scripts/VoxelEngine/WaterSim/NativeWaterWakeSystem.cs
//
// Native, spherical-aware water wake registry. Maritime propulsion submits a
// lightweight wake stamp; the in-house VoxelWaterURP shader consumes the fixed
// small array for foam and a subtle radial surface displacement. No external
// ocean package, plane, or flat-world coordinate assumption is involved.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.WaterSim
{
    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    public sealed class NativeWaterWakeSystem : MonoBehaviour
    {
        private const int MaxWakes = 16;
        private const float WakeLifetime = 2.4f;

        private struct WakeSlot
        {
            public Vector3 position;
            public Vector3 direction;
            public float width;
            public float length;
            public float strength;
            public float lastSubmitted;
        }

        public static NativeWaterWakeSystem Instance { get; private set; }

        private readonly WakeSlot[] _slots = new WakeSlot[MaxWakes];
        private readonly Vector4[] _positions = new Vector4[MaxWakes];
        private readonly Vector4[] _directions = new Vector4[MaxWakes];
        private readonly Vector4[] _data = new Vector4[MaxWakes];

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var existing = Object.FindAnyObjectByType<NativeWaterWakeSystem>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var go = new GameObject("NativeWaterWakeSystem");
            go.AddComponent<NativeWaterWakeSystem>();
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Called by the native maritime system. Stamps are accepted only while the hull is
        /// actually over real simulated water, then projected onto the body's radial sea shell.
        /// </summary>
        public static void RegisterWake(Vector3 worldPosition, Vector3 velocity, float hullSize)
        {
            EnsureInstance();
            Instance?.Submit(worldPosition, velocity, hullSize);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void LateUpdate()
        {
            PublishShaderState();
        }

        private void OnDisable()
        {
            Shader.SetGlobalInt("_VoxelWakeCount", 0);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Shader.SetGlobalInt("_VoxelWakeCount", 0);
                Instance = null;
            }
        }

        private void Submit(Vector3 worldPosition, Vector3 velocity, float hullSize)
        {
            var world = ActiveWorld.Current;
            if (world == null) return;

            Vector3 up = PlanetWaterUtility.WorldUp(worldPosition);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 tangentVelocity = Vector3.ProjectOnPlane(velocity, up);
            float speed = tangentVelocity.magnitude;
            if (speed < 0.45f) return;

            if (!TryFindWaterSurface(world, worldPosition, up, hullSize, out Vector3 surface)) return;

            Vector3 direction = tangentVelocity / speed;
            float width = Mathf.Clamp(0.7f + Mathf.Sqrt(Mathf.Max(1f, hullSize)) * 0.18f, 0.8f, 8f);
            float length = Mathf.Clamp(width * 5.5f + speed * 1.35f, 6f, 72f);
            float strength = Mathf.Clamp01((speed - 0.4f) / 9f) * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(hullSize / 50f));
            int slot = FindReusableSlot(surface, direction);

            _slots[slot] = new WakeSlot
            {
                position = surface,
                direction = direction,
                width = width,
                length = length,
                strength = strength,
                lastSubmitted = Time.unscaledTime
            };
        }

        private static bool TryFindWaterSurface(IVoxelWorld world, Vector3 worldPosition, Vector3 up,
            float hullSize, out Vector3 surface)
        {
            surface = default;
            int depth = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1f, hullSize)) * 0.35f) + 3, 3, 10);
            for (int step = -1; step <= depth; step++)
            {
                Vector3 probeWorld = worldPosition - up * step;
                Vector3Int voxelPos = world.WorldToVoxel(probeWorld);
                Voxel voxel = world.GetVoxelWorld(voxelPos);
                if (!FluidMaterialUtility.IsFluid(voxel)
                    || FluidMaterialUtility.LiquidFromVoxel(voxel) != VoxelEngine.Items.LiquidType.Water)
                    continue;

                if (world is SphereWorld sphere && sphere.body != null)
                {
                    Vector3 localCenter = PlanetWaterUtility.VoxelCenterToLocalPosition(voxelPos);
                    Vector3 localUp = localCenter.sqrMagnitude > 0.0001f ? localCenter.normalized : Vector3.up;
                    float surfaceRadius = localCenter.magnitude
                        + (voxel.waterLevel / 255f - 0.5f) * VoxelConstants.VOXEL_SIZE;
                    surface = sphere.body.transform.TransformPoint(localUp * surfaceRadius);
                }
                else
                {
                    surface = new Vector3(
                        (voxelPos.x + 0.5f) * VoxelConstants.VOXEL_SIZE,
                        (voxelPos.y + voxel.waterLevel / 255f) * VoxelConstants.VOXEL_SIZE,
                        (voxelPos.z + 0.5f) * VoxelConstants.VOXEL_SIZE);
                }
                return true;
            }
            return false;
        }

        private int FindReusableSlot(Vector3 position, Vector3 direction)
        {
            int oldest = 0;
            float oldestTime = float.MaxValue;
            for (int i = 0; i < MaxWakes; i++)
            {
                ref WakeSlot slot = ref _slots[i];
                if (slot.lastSubmitted <= 0f || Time.unscaledTime - slot.lastSubmitted > WakeLifetime)
                    return i;

                if ((slot.position - position).sqrMagnitude < 20f * 20f
                    && Vector3.Dot(slot.direction, direction) > 0.75f)
                    return i;

                if (slot.lastSubmitted < oldestTime)
                {
                    oldestTime = slot.lastSubmitted;
                    oldest = i;
                }
            }
            return oldest;
        }

        private void PublishShaderState()
        {
            int count = 0;
            float now = Time.unscaledTime;
            for (int i = 0; i < MaxWakes; i++)
            {
                WakeSlot slot = _slots[i];
                float fade = slot.lastSubmitted <= 0f ? 0f : Mathf.Clamp01(1f - (now - slot.lastSubmitted) / WakeLifetime);
                if (fade <= 0f)
                {
                    _positions[i] = Vector4.zero;
                    _directions[i] = Vector4.zero;
                    _data[i] = Vector4.zero;
                    continue;
                }

                _positions[count] = new Vector4(slot.position.x, slot.position.y, slot.position.z, fade);
                _directions[count] = new Vector4(slot.direction.x, slot.direction.y, slot.direction.z, slot.width);
                _data[count] = new Vector4(slot.length, slot.strength, fade, 0f);
                count++;
            }

            Shader.SetGlobalInt("_VoxelWakeCount", count);
            Shader.SetGlobalVectorArray("_VoxelWakePositions", _positions);
            Shader.SetGlobalVectorArray("_VoxelWakeDirections", _directions);
            Shader.SetGlobalVectorArray("_VoxelWakeData", _data);
        }
    }
}
