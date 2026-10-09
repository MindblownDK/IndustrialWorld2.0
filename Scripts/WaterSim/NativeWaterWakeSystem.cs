// Assets/Scripts/VoxelEngine/WaterSim/NativeWaterWakeSystem.cs
//
// Native, spherical-aware water wake registry. Maritime propulsion submits a
// lightweight moving hull stamp; the in-house VoxelWaterURP shader builds a
// tapered V, animated crest shading, and shallow-water response from a fixed
// small array. No external ocean package or flat-world coordinate assumption.

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
            public float trailLength;
            public float speed;
            public float strength;
            public float lastSubmitted;
            public int ownerId;
            public bool active;
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
        /// over real simulated water, then projected to its local surface (radially on a planet).
        /// </summary>
        public static void RegisterWake(Vector3 worldPosition, Vector3 velocity, float hullSize)
            => RegisterWake(worldPosition, velocity, hullSize, 0);

        internal static void RegisterWake(Vector3 worldPosition, Vector3 velocity, float hullSize, int ownerId)
        {
            EnsureInstance();
            Instance?.Submit(worldPosition, velocity, hullSize, ownerId);
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

        private void Submit(Vector3 worldPosition, Vector3 velocity, float hullSize, int ownerId)
        {
            var world = ActiveWorld.Current;
            if (world == null) return;

            Vector3 up = PlanetWaterUtility.WorldUp(worldPosition);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            Vector3 tangentVelocity = Vector3.ProjectOnPlane(velocity, up);
            float speed = tangentVelocity.magnitude;
            if (speed < 0.55f) return;

            if (!TryFindWaterSurface(world, worldPosition, up, hullSize, out Vector3 surface)) return;

            Vector3 direction = tangentVelocity / speed;
            float width = Mathf.Clamp(0.7f + Mathf.Sqrt(Mathf.Max(1f, hullSize)) * 0.18f, 0.8f, 8f);
            float length = Mathf.Clamp(width * 5.5f + speed * 1.35f, 6f, 72f);
            float speedStrength = Mathf.Clamp01((speed - 0.45f) / 5.5f);
            float hullStrength = Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(hullSize / 50f));
            float strength = speedStrength * hullStrength;
            float now = Time.unscaledTime;
            int slotIndex = FindReusableSlot(surface, direction, now, ownerId);
            ref WakeSlot previous = ref _slots[slotIndex];
            float trailLength = 0f;
            if (previous.active && now - previous.lastSubmitted < WakeLifetime
                && Vector3.Dot(previous.direction, direction) > 0.75f)
            {
                Vector3 displacement = Vector3.ProjectOnPlane(surface - previous.position, up);
                float forwardTravel = Mathf.Max(0f, Vector3.Dot(displacement, direction));
                trailLength = Mathf.Min(length, previous.trailLength + forwardTravel);
            }

            _slots[slotIndex] = new WakeSlot
            {
                position = surface,
                direction = direction,
                width = width,
                trailLength = trailLength,
                speed = speed,
                strength = strength,
                lastSubmitted = now,
                ownerId = ownerId,
                active = true
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
                    Vector3 localHullPosition = PlanetWaterUtility.WorldToBodyLocal(worldPosition);
                    Vector3 localUp = localHullPosition.sqrMagnitude > 0.0001f
                        ? localHullPosition.normalized
                        : localCenter.sqrMagnitude > 0.0001f ? localCenter.normalized : Vector3.up;
                    float surfaceRadius = localCenter.magnitude
                        + (voxel.waterLevel / 255f - 0.5f) * VoxelConstants.VOXEL_SIZE;
                    surface = sphere.body.transform.TransformPoint(localUp * surfaceRadius);
                }
                else
                {
                    surface = new Vector3(worldPosition.x,
                        (voxelPos.y + voxel.waterLevel / 255f) * VoxelConstants.VOXEL_SIZE,
                        worldPosition.z);
                }
                return true;
            }
            return false;
        }

        private int FindReusableSlot(Vector3 position, Vector3 direction, float now, int ownerId)
        {
            int available = -1;
            int oldest = 0;
            float oldestTime = float.MaxValue;
            for (int i = 0; i < MaxWakes; i++)
            {
                ref WakeSlot slot = ref _slots[i];
                if (!slot.active || now - slot.lastSubmitted >= WakeLifetime)
                {
                    slot.active = false;
                    if (available < 0) available = i;
                    continue;
                }

                if (slot.ownerId == ownerId && (slot.position - position).sqrMagnitude < 20f * 20f
                    && Vector3.Dot(slot.direction, direction) > 0.75f)
                    return i;

                if (slot.lastSubmitted < oldestTime)
                {
                    oldestTime = slot.lastSubmitted;
                    oldest = i;
                }
            }
            return available >= 0 ? available : oldest;
        }

        private void PublishShaderState()
        {
            int count = 0;
            float now = Time.unscaledTime;
            for (int i = 0; i < MaxWakes; i++)
            {
                ref WakeSlot slot = ref _slots[i];
                float age = now - slot.lastSubmitted;
                if (!slot.active || age >= WakeLifetime)
                {
                    slot.active = false;
                    _positions[i] = Vector4.zero;
                    _directions[i] = Vector4.zero;
                    _data[i] = Vector4.zero;
                    continue;
                }

                float life = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / WakeLifetime));
                _positions[count] = new Vector4(slot.position.x, slot.position.y, slot.position.z, life);
                _directions[count] = new Vector4(slot.direction.x, slot.direction.y, slot.direction.z, slot.width);
                _data[count] = new Vector4(slot.trailLength, slot.strength, life, slot.speed);
                count++;
            }

            Shader.SetGlobalInt("_VoxelWakeCount", count);
            Shader.SetGlobalVectorArray("_VoxelWakePositions", _positions);
            Shader.SetGlobalVectorArray("_VoxelWakeDirections", _directions);
            Shader.SetGlobalVectorArray("_VoxelWakeData", _data);
        }
    }
}
