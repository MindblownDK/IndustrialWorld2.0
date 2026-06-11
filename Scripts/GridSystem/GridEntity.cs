// Assets/Scripts/VoxelEngine/GridSystem/GridEntity.cs
//
// The root component of a ship/vehicle. Manages the 3D block grid, physics,
// power distribution, thrust, and player control.
//
// Architecture:
//   GridEntity (Rigidbody, root GO)
//   └── GridBlock children at integer grid positions
//
// Power is grid-wide (no cables needed on ships) — all generators/consumers
// share a single power pool. Gas (hydrogen) still needs gas pipes.
//
// Built on 0.14.1-dev audio/UI base.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class GridEntity : MonoBehaviour
    {
        [Header("Grid")]
        public GridSize gridSize = GridSize.Large;

        // ── Block storage ──────────────────────────────────────────
        private readonly Dictionary<Vector3Int, GridBlock> _blocks = new();
        public IReadOnlyDictionary<Vector3Int, GridBlock> Blocks => _blocks;
        public int BlockCount => _blocks.Count;

        // ── Physics ────────────────────────────────────────────────
        private Rigidbody _rb;
        public Rigidbody Body => _rb;

        // ── Power (grid-wide, no cables) ───────────────────────────
        public float PowerGenerated { get; private set; }
        public float PowerConsumed  { get; private set; }
        public float PowerBalance   => PowerGenerated - PowerConsumed;
        public bool  HasPower       => PowerBalance >= -0.1f;

        // ── Gas storage (shared across grid) ───────────────────────
        public float HydrogenStored { get; set; }
        public float HydrogenCapacity { get; private set; }
        public float OxygenStored   { get; set; }

        // ── Thrust ─────────────────────────────────────────────────
        public Vector3 ThrustInput { get; set; }
        public float   RotationYaw { get; set; }
        public float   RotationPitch { get; set; }
        public float   RotationRoll { get; set; }
        public bool    DampenersOn { get; set; } = true;

        /// <summary>Total max thrust (N) available along each of the 6 local directions —
        /// Fwd, Back, Right, Left, Up, Down — for the cockpit HUD readout.</summary>
        public (float fwd, float back, float right, float left, float up, float down) GetThrustByDirection()
        {
            float fwd=0,back=0,right=0,left=0,up=0,down=0;
            foreach (var kv in _blocks)
            {
                if (!(kv.Value is GridThruster t)) continue;
                // Thruster pushes the ship along its local forward.
                Vector3 d = transform.InverseTransformDirection(t.transform.forward);
                if (d.z >  0.5f) fwd   += t.maxThrustN;
                if (d.z < -0.5f) back  += t.maxThrustN;
                if (d.x >  0.5f) right += t.maxThrustN;
                if (d.x < -0.5f) left  += t.maxThrustN;
                if (d.y >  0.5f) up    += t.maxThrustN;
                if (d.y < -0.5f) down  += t.maxThrustN;
            }
            return (fwd,back,right,left,up,down);
        }

        /// <summary>Index of the currently selected fire-tool (drill/weapon). Cycled in
        /// the cockpit with the scroll wheel; only the selected tool activates on click.</summary>
        public int SelectedToolIndex { get; set; }

        /// <summary>All drill/weapon blocks on the grid, in a stable order, for tool cycling.</summary>
        public System.Collections.Generic.List<GridBlock> GetFireTools()
        {
            var list = new System.Collections.Generic.List<GridBlock>();
            foreach (var kv in _blocks)
                if (kv.Value is GridDrill || kv.Value is GridWeapon) list.Add(kv.Value);
            return list;
        }

        /// <summary>Is the given block the player's currently-selected fire tool?</summary>
        public bool IsSelectedTool(GridBlock b)
        {
            var tools = GetFireTools();
            if (tools.Count == 0) return false;
            int idx = ((SelectedToolIndex % tools.Count) + tools.Count) % tools.Count;
            return tools[idx] == b;
        }

        /// <summary>Set by the piloting cockpit each frame to drive thrusters + gyros.</summary>
        public void SetFlightInput(Vector3 thrust, float yaw, float pitch, float roll)
        {
            ThrustInput   = thrust;
            RotationYaw   = yaw;
            RotationPitch = pitch;
            RotationRoll  = roll;
        }

        // ── Cockpit ────────────────────────────────────────────────
        public GridCockpit ActiveCockpit { get; set; }
        public bool IsControlled => ActiveCockpit != null && ActiveCockpit.Pilot != null;

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
            ApplyGravity();
        }

        private void Update()
        {
            RefreshContentMass();
        }

        private void ApplyGravity()
        {
            if (_rb == null) return;

            float gravityMultiplier = AtmosphereManager.GetGravityMultiplier(transform.position);
            Vector3 gravityForce = Physics.gravity * gravityMultiplier * _rb.mass;
            _rb.AddForce(gravityForce, ForceMode.Force);
        }

        // ── Block Management ───────────────────────────────────────
        public bool CanPlace(Vector3Int gridPos) => !_blocks.ContainsKey(gridPos);

        /// <summary>True if any of the 6 face-neighbours of <paramref name="gridPos"/> is occupied
        /// (so a new block there actually connects to the existing structure).</summary>
        public bool HasNeighbor(Vector3Int gridPos)
        {
            if (_blocks.Count == 0) return true; // first block always "connects"
            return _blocks.ContainsKey(gridPos + Vector3Int.right)
                || _blocks.ContainsKey(gridPos + Vector3Int.left)
                || _blocks.ContainsKey(gridPos + Vector3Int.up)
                || _blocks.ContainsKey(gridPos + Vector3Int.down)
                || _blocks.ContainsKey(gridPos + new Vector3Int(0, 0, 1))
                || _blocks.ContainsKey(gridPos + new Vector3Int(0, 0, -1));
        }

        public void AddBlock(Vector3Int gridPos, GridBlock block)
        {
            if (_blocks.ContainsKey(gridPos)) return;
            _blocks[gridPos] = block;
            block.GridPos = gridPos;
            block.Grid = this;

            block.transform.SetParent(transform, true);
            float cs = gridSize.CellSize();
            block.transform.localPosition = new Vector3(gridPos.x, gridPos.y, gridPos.z) * cs;
            // Removed rotation reset - handled by the caller (GridBuilder)
            // block.transform.localRotation = Quaternion.identity;

            if (block.GetComponent<Collider>() == null)
            {
                var box = block.gameObject.AddComponent<BoxCollider>();
                box.size = Vector3.one * cs;
            }

            RecalculateMass();
            block.OnPlaced();
        }

        public void RemoveBlock(Vector3Int gridPos)
        {
            if (!_blocks.TryGetValue(gridPos, out var block)) return;
            _blocks.Remove(gridPos);
            block.OnRemoved();
            Destroy(block.gameObject);
            RecalculateMass();

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

        /// <summary>Sum of every block's structural mass + its current content mass
        /// (cargo items, stored fluids, ammo). Drives the rigidbody mass so a loaded
        /// ship genuinely flies heavier.</summary>
        public float TotalMass { get; private set; }

        public void RecalculateMass()
        {
            if (_rb == null) return;
            float mass = 0f;
            foreach (var kv in _blocks)
            {
                if (kv.Value == null) continue;
                mass += kv.Value.TotalMass; // BlockMass + ContentMass
            }
            TotalMass = mass;
            _rb.mass = Mathf.Max(1f, mass);
        }

        // Content mass changes constantly (items dragged in/out, fluids filling).
        // Refresh at a light cadence so the ship's handling reflects its load.
        private float _massTimer;
        private void RefreshContentMass()
        {
            _massTimer += Time.deltaTime;
            if (_massTimer < 0.5f) return;
            _massTimer = 0f;
            RecalculateMass();
        }

        // ── Grid-Wide Power ────────────────────────────────────────
        private void UpdatePower()
        {
            float gen = 0, con = 0, h2Cap = 0;
            foreach (var kv in _blocks)
            {
                var b = kv.Value;
                gen += b.PowerOutput;
                con += b.PowerDraw;
                if (b is GridGasTank gt && gt.gasType == Gas.GasType.Hydrogen)
                    h2Cap += gt.capacity;
            }
            PowerGenerated = gen;
            PowerConsumed = con;
            HydrogenCapacity = h2Cap;
        }

        // ── Thrust Application ─────────────────────────────────────
        private void UpdateThrust()
        {
            if (!IsControlled) return;

            // Sum the available thrust (N) from all operational thrusters and push the
            // ship in the PILOT'S desired direction. This makes WASD intuitive no matter
            // how thrusters are mounted (each still consumes its own fuel/power).
            float totalThrustN = 0f;
            foreach (var kv in _blocks)
            {
                if (kv.Value is GridThruster thruster && thruster.IsOperational)
                    totalThrustN += thruster.AvailableThrust(ThrustInput, this);
            }

            if (ThrustInput.sqrMagnitude > 0.0001f && totalThrustN > 0f)
            {
                Vector3 worldDir = transform.TransformDirection(ThrustInput.normalized);
                // Acceleration-based with a strong gain so realistic-mass ships fly well.
                _rb.AddForce(worldDir * (totalThrustN / Mathf.Max(1f, _rb.mass)) * 4f, ForceMode.Acceleration);
            }

            Vector3 rotInput = new Vector3(RotationPitch, RotationYaw, RotationRoll);
            // Rotational authority comes from installed (enabled) gyroscopes.
            float gyroTorque = 0f;
            foreach (var kv in _blocks)
                if (kv.Value is GridGyroscope gy && gy.Enabled) gyroTorque += gy.torquePower;
            if (gyroTorque > 0f && rotInput.sqrMagnitude > 0.0001f)
            {
                // Acceleration mode = mass-independent, so a small ship turns crisply and
                // a big one needs more gyros. Scale keeps it responsive to mouse input.
                Vector3 worldTorque = transform.TransformDirection(rotInput) * (gyroTorque * 0.0005f);
                _rb.AddTorque(worldTorque, ForceMode.Acceleration);
            }
            // Angular damping so the ship stops spinning when you let go (SE-style).
            _rb.angularDamping = 3f;
        }

        // ── Inertia Dampeners ──────────────────────────────────────
        private void UpdateDampeners()
        {
            if (!DampenersOn || !IsControlled) return;
            if (ThrustInput.sqrMagnitude > 0.01f) return;

            Vector3 vel = _rb.linearVelocity;
            if (vel.sqrMagnitude > 0.1f)
            {
                // Strong braking toward zero velocity (acceleration-based so mass-independent).
                _rb.AddForce(-vel * 2.5f, ForceMode.Acceleration);
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
    }
}