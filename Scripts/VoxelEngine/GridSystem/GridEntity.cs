// Assets/Scripts/VoxelEngine/GridSystem/GridEntity.cs
//
// The root component of a ship/vehicle/car. Manages the 3D block grid, physics,
// power distribution (global pool - no cables), thrust, and player control.
//
// Supports Small (0.5m) and Large (2.5m) grids like Space Engineers.
// Optimized for very large ships (hundreds to low-thousands of blocks) without lag/crash.
//
// Architecture:
//   GridEntity (Rigidbody, root GO)
//   └── GridBlock children at integer grid positions
//
// Power: grid-wide pool (all generators/consumers share).
// Gas/Fluids: shared storage + optional itempipes (via Networks) for conveyors.
// Liquid fuels: supported via extended fuel system (Phase 2+).
//
// Events: BlockPlaced, BlockRemoved, PowerChanged for decoupled systems.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class GridEntity : MonoBehaviour
    {
        [Header("Grid Configuration")]
        public GridSize gridSize = GridSize.Large;

        // ── Block Storage ──────────────────────────────────────────
        private readonly Dictionary<Vector3Int, GridBlock> _blocks = new();
        public IReadOnlyDictionary<Vector3Int, GridBlock> Blocks => _blocks;
        public int BlockCount => _blocks.Count;

        // ── Events (decoupled, production quality) ─────────────────
        public event Action<Vector3Int, GridBlock> OnBlockPlaced;
        public event Action<Vector3Int, GridBlock> OnBlockRemoved;
        public event Action<float, float> OnPowerChanged; // generated, consumed

        // ── Physics ────────────────────────────────────────────────
        private Rigidbody _rb;
        public Rigidbody Body => _rb;

        // ── Power (grid-wide pool - no cables needed) ──────────────
        public float PowerGenerated { get; private set; }
        public float PowerConsumed { get; private set; }
        public float PowerBalance => PowerGenerated - PowerConsumed;
        public bool HasPower => PowerBalance >= -0.1f;

        // ── Shared Resources (gas + future liquids) ────────────────
        public float HydrogenStored { get; set; }
        public float HydrogenCapacity { get; private set; }
        public float OxygenStored { get; set; }
        public float LiquidFuelStored { get; set; } // Phase 2 stub
        public float LiquidFuelCapacity { get; private set; }

        // ── Control Input ──────────────────────────────────────────
        public Vector3 ThrustInput { get; set; } // normalized (-1..1) per axis from cockpit
        public float RotationYaw { get; set; }
        public float RotationPitch { get; set; }
        public float RotationRoll { get; set; }
        public bool DampenersOn { get; set; } = true;

        // ── Cockpit ────────────────────────────────────────────────
        public GridCockpit ActiveCockpit { get; set; }
        public bool IsControlled => ActiveCockpit != null && ActiveCockpit.Pilot != null;

        // ── Performance Safeguards (large ships) ───────────────────
        private const int MAX_BLOCKS_WARNING = 2500;
        private bool _massDirty = true;
        private float _lastMassRecalcTime;

        // ── Lifecycle ──────────────────────────────────────────────
        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private void FixedUpdate()
        {
            UpdatePower();
            UpdateThrust();
            UpdateDampeners();
            UpdateWheels();
        }

        // ── Block Management (Hardened) ────────────────────────────
        public bool CanPlace(Vector3Int gridPos) => !_blocks.ContainsKey(gridPos);

        public void AddBlock(Vector3Int gridPos, GridBlock block)
        {
            if (_blocks.ContainsKey(gridPos) || block == null) return;

            _blocks[gridPos] = block;
            block.GridPos = gridPos;
            block.Grid = this;

            block.transform.SetParent(transform, true);
            float cs = gridSize.CellSize();
            block.transform.localPosition = new Vector3(gridPos.x, gridPos.y, gridPos.z) * cs;
            block.transform.localRotation = Quaternion.identity;

            if (block.GetComponent<Collider>() == null)
            {
                var box = block.gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one * cs;
            }

            _massDirty = true;
            block.OnPlaced();

            OnBlockPlaced?.Invoke(gridPos, block);

            if (BlockCount > MAX_BLOCKS_WARNING)
            {
                Debug.LogWarning($"[GridEntity] Large grid detected ({BlockCount} blocks). Consider optimization for performance.");
            }
        }

        public void RemoveBlock(Vector3Int gridPos)
        {
            if (!_blocks.TryGetValue(gridPos, out var block)) return;

            _blocks.Remove(gridPos);
            block.OnRemoved();
            Destroy(block.gameObject);

            _massDirty = true;
            OnBlockRemoved?.Invoke(gridPos, block);

            if (_blocks.Count == 0)
                Destroy(gameObject);
        }

        public GridBlock GetBlock(Vector3Int gridPos)
        {
            _blocks.TryGetValue(gridPos, out var b);
            return b;
        }

        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            float cs = gridSize.CellSize();
            return new Vector3Int(
                Mathf.RoundToInt(local.x / cs),
                Mathf.RoundToInt(local.y / cs),
                Mathf.RoundToInt(local.z / cs));
        }

        public Vector3 GridToWorld(Vector3Int gridPos)
        {
            float cs = gridSize.CellSize();
            return transform.TransformPoint(new Vector3(gridPos.x, gridPos.y, gridPos.z) * cs);
        }

        private void RecalculateMass()
        {
            if (!_massDirty || Time.time - _lastMassRecalcTime < 0.1f) return;

            float mass = 0f;
            foreach (var kv in _blocks)
                mass += kv.Value.BlockMass;

            _rb.mass = Mathf.Max(1f, mass);
            _massDirty = false;
            _lastMassRecalcTime = Time.time;
        }

        // ── Grid-Wide Power (Pool) ─────────────────────────────────
        private void UpdatePower()
        {
            float gen = 0f, con = 0f, h2Cap = 0f;

            foreach (var kv in _blocks)
            {
                var b = kv.Value;
                gen += b.PowerOutput;
                con += b.PowerDraw;

                if (b is GridGasTank gt && gt.gasType == Gas.GasType.Hydrogen)
                    h2Cap += gt.capacity;
                // Liquid fuel capacity stub (Phase 2) - add when LiquidFuelTank exists
            }

            PowerGenerated = gen;
            PowerConsumed = con;
            HydrogenCapacity = h2Cap;

            if (Mathf.Abs(PowerBalance - (gen - con)) > 0.01f)
                OnPowerChanged?.Invoke(gen, con);
        }

        // ── Thrust Application (Improved with Liquid Stub) ─────────
        private void UpdateThrust()
        {
            if (!IsControlled) return;

            Vector3 totalForce = Vector3.zero;
            Vector3 totalTorque = Vector3.zero;

            foreach (var kv in _blocks)
            {
                if (kv.Value is GridThruster thruster && thruster.IsOperational)
                {
                    Vector3 thrustDir = thruster.transform.forward;
                    float power = thruster.GetCurrentThrust(ThrustInput, this);
                    totalForce += thrustDir * power;

                    Vector3 offset = thruster.transform.position - _rb.worldCenterOfMass;
                    totalTorque += Vector3.Cross(offset, thrustDir * power);
                }
            }

            _rb.AddForce(totalForce, ForceMode.Force);

            Vector3 rotInput = new Vector3(RotationPitch, RotationYaw, RotationRoll);
            float rotPower = 5000f * _rb.mass;
            _rb.AddTorque(transform.TransformDirection(rotInput) * rotPower * Time.fixedDeltaTime, ForceMode.Force);
        }

        // ── Inertia Dampeners ──────────────────────────────────────
        private void UpdateDampeners()
        {
            if (!DampenersOn || !IsControlled || ThrustInput.sqrMagnitude > 0.01f) return;

            Vector3 vel = _rb.linearVelocity;
            if (vel.sqrMagnitude > 0.1f)
            {
                Vector3 brake = -vel.normalized * Mathf.Min(vel.magnitude, 5f * Time.fixedDeltaTime);
                _rb.AddForce(brake * _rb.mass, ForceMode.Force);
            }

            Vector3 angVel = _rb.angularVelocity;
            if (angVel.sqrMagnitude > 0.01f)
            {
                _rb.angularVelocity = Vector3.Lerp(angVel, Vector3.zero, 3f * Time.fixedDeltaTime);
            }
        }

        // ── Wheels ─────────────────────────────────────────────────
        private void UpdateWheels()
        {
            foreach (var kv in _blocks)
            {
                if (kv.Value is GridWheel wheel)
                    wheel.UpdateWheel(this);
            }
        }

        /// <summary>Create a new empty grid entity.</summary>
        public static GridEntity Create(Vector3 position, GridSize size)
        {
            var go = new GameObject($"Grid_{size}_{Time.frameCount}");
            go.transform.position = position;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            var entity = go.AddComponent<GridEntity>();
            entity.gridSize = size;
            return entity;
        }

        private void LateUpdate()
        {
            if (_massDirty)
                RecalculateMass();
        }
    }
}