// Assets/Scripts/VoxelEngine/Farming/Sprinkler.cs
//
// 18.3.0-dev — the sprinkler became a piece of equipment instead of a blue box.
//
// The old block was a cube that answered one yes/no question ("is there water
// within two metres?"). It had no water of its own, so it could not be built
// away from a pipe, and the answer it gave was the same for every bed in range
// whether it had power or not. It is now a mast with a real tank:
//
//   * the tank holds water. It fills from a connected water source (a pipe or
//     tank within reach) and it can be filled BY HAND with a filled Liquid
//     Canister, so a player who has not built plumbing yet can still automate a
//     bed for a while;
//   * it draws power while it sprays, so automation is earned rather than free;
//   * it sprays from its tank at a trickle - a little water goes a long way,
//     which is what makes a sprinkler an upgrade rather than a tax.
//
// Beds read `IsActive` exactly as before, so nothing downstream changed: a bed
// that sees an active sprinkler inside its radius is irrigated, full stop.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Power;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Powered sprinkler mast with an internal water tank. Irrigates every farm bed
    /// within <see cref="radius"/> while it has power and water.
    /// </summary>
    [RequireComponent(typeof(PlacedBlock))]
    public class Sprinkler : MonoBehaviour
    {
        [Header("Coverage")]
        [Tooltip("Radius in metres this sprinkler irrigates.")]
        public float radius = 8f;

        [Header("Water")]
        [Tooltip("Litres held in the mast's internal tank.")]
        public float waterCapacity = 60f;
        [Tooltip("Litres in the tank right now.")]
        public float waterStored = 60f;
        [Tooltip("Litres sprayed per second while running. Small on purpose: a full tank waters a bed for hours.")]
        public float litresPerSecond = 0.4f;
        [Tooltip("Litres pulled per second from a connected pipe or tank, so a plumbed sprinkler never runs dry.")]
        public float refillLitresPerSecond = 2f;
        [Tooltip("Litres one Liquid Canister click pours in. Matches the canister's own per-click volume.")]
        public float litresPerCanisterClick = LiquidCanister.LitresPerClick;
        [Tooltip("True when a water source must be connected for the tank to refill. Hand filling always works.")]
        public bool requiresWaterConnection = true;
        [Tooltip("How far it reaches for a fluid connection.")]
        public float waterSourceRadius = 3.5f;

        [Header("Power")]
        [Tooltip("Watts drawn while spraying.")]
        public float wattsWhileSpraying = 20f;

        /// <summary>Whether this sprinkler is currently spraying.</summary>
        public bool IsActive { get; private set; }

        /// <summary>0..1 tank level, for a panel or a glow.</summary>
        public float WaterFill01 => waterCapacity > 0f ? Mathf.Clamp01(waterStored / waterCapacity) : 0f;

        /// <summary>"SPRAYING", "NO POWER", "DRY", "REFILLING" - one word for a prompt.</summary>
        public string Status
        {
            get
            {
                if (!enabled) return "OFF";
                if (_power != null && !_power.IsPowered) return "NO POWER";
                if (waterStored <= 0.01f && !HasWaterSource) return "DRY";
                return IsActive ? "SPRAYING" : "REFILLING";
            }
        }

        /// <summary>True when a pipe or tank is in reach of this mast.</summary>
        public bool HasWaterSource { get; private set; }

        private PowerConsumer _power;
        private float _sourceTimer;
        private MeshRenderer _gauge;
        private MeshRenderer _beacon;
        private Transform _head;
        private float _headAngle;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _power = GetComponent<PowerConsumer>();
            CacheVisuals();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            _sourceTimer += dt;
            if (_sourceTimer >= 1f)
            {
                _sourceTimer = 0f;
                HasWaterSource = requiresWaterConnection ? CheckWaterSource() : true;
            }

            if (HasWaterSource && waterStored < waterCapacity)
                waterStored = Mathf.Min(waterCapacity,
                    waterStored + Mathf.Max(0f, refillLitresPerSecond) * dt);

            bool powered = _power == null || _power.IsPowered;
            bool wet = waterStored > 0.005f;
            IsActive = powered && wet;

            if (IsActive)
                waterStored = Mathf.Max(0f, waterStored - Mathf.Max(0f, litresPerSecond) * dt);

            SpinHead(dt);
            RefreshVisuals();
        }

        // ── Water by hand ────────────────────────────────────────────────────

        /// <summary>Pour from a filled Liquid Canister. Returns the litres accepted.</summary>
        public bool TryFillFromCanister(ItemStack stack, out float poured)
        {
            poured = 0f;
            if (stack == null || stack.IsEmpty || stack.item == null) return false;
            if (!(stack.item is LiquidCanister)) return false;

            var carried = LiquidCanister.CarriedLiquid(stack);
            if (carried == null || carried.Value != LiquidType.Water) return false;

            float space = Mathf.Max(0f, waterCapacity - waterStored);
            if (space <= 0.001f) return false;

            int wantedMl = Mathf.RoundToInt(Mathf.Min(litresPerCanisterClick, space) * 1000f);
            if (wantedMl <= 0) return false;

            if (!LiquidCanister.RemoveMl(stack, wantedMl)) return false;

            poured = wantedMl / 1000f;
            waterStored = Mathf.Min(waterCapacity, waterStored + poured);
            return true;
        }

        /// <summary>Straight litres, for a pipe, a debug fill or a test.</summary>
        public float TryAddWater(float litres)
        {
            if (litres <= 0f) return 0f;
            float space = Mathf.Max(0f, waterCapacity - waterStored);
            float added = Mathf.Min(space, litres);
            waterStored += added;
            return added;
        }

        /// <summary>
        /// Look for plumbing. A tank or pipe within reach counts: the mast is not a fluid
        /// network member itself, it only needs to sit against one, which keeps the
        /// plumbing graph untouched by a garden ornament.
        /// </summary>
        private bool CheckWaterSource()
        {
            var hits = Physics.OverlapSphere(transform.position, Mathf.Max(0.5f, waterSourceRadius));
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col == null || col.gameObject == gameObject) continue;

                // Every pipe and tank in the project derives from FluidNode, so one
                // check covers plumbing without naming each piece of it.
                if (col.GetComponentInParent<VoxelEngine.Fluids.FluidNode>() != null) return true;
            }
            return false;
        }

        // ── Visuals ─────────────────────────────────────────────────────────

        /// <summary>Build the mast, head, sight glass and beacon. Editor-time only.</summary>
        public void RebuildVisuals()
        {
            var existing = transform.Find("Generated_SprinklerVisuals");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            if (!Application.isPlaying) BuildVisuals();
            CacheVisuals();
            RefreshVisuals();
        }

        private void BuildVisuals()
        {
            var root = new GameObject("Generated_SprinklerVisuals").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            Color darkSteel = new Color(0.19f, 0.21f, 0.24f);
            Color midSteel = new Color(0.34f, 0.37f, 0.41f);
            Color brass = new Color(0.62f, 0.50f, 0.28f);
            Color glassBlue = new Color(0.24f, 0.68f, 0.92f);

            // Weighted base and mast.
            MakePart(root, PrimitiveType.Cylinder, "Base",
                new Vector3(0f, 0.04f, 0f), new Vector3(0.42f, 0.04f, 0.42f), darkSteel);
            MakePart(root, PrimitiveType.Cylinder, "BaseRing",
                new Vector3(0f, 0.07f, 0f), new Vector3(0.30f, 0.03f, 0.30f), brass);
            MakePart(root, PrimitiveType.Cylinder, "Mast",
                new Vector3(0f, 0.34f, 0f), new Vector3(0.07f, 0.28f, 0.07f), midSteel);

            // Sight glass on the mast's front - the tank level is visible from outside.
            MakePart(root, PrimitiveType.Cube, "TankBody",
                new Vector3(0f, 0.26f, 0.10f), new Vector3(0.14f, 0.30f, 0.08f), midSteel);
            MakePart(root, PrimitiveType.Cube, "WaterGauge",
                new Vector3(0f, 0.26f, 0.145f), new Vector3(0.07f, 0.26f, 0.02f), glassBlue);

            // Rotating head with three nozzles.
            var head = new GameObject("Head").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0f, 0.64f, 0f);
            MakePart(head, PrimitiveType.Cylinder, "HeadBody",
                Vector3.zero, new Vector3(0.13f, 0.05f, 0.13f), brass);
            MakePart(head, PrimitiveType.Cube, "Nozzle_A",
                new Vector3(0.13f, 0.01f, 0f), new Vector3(0.16f, 0.03f, 0.04f), midSteel);
            MakePart(head, PrimitiveType.Cube, "Nozzle_B",
                new Vector3(-0.07f, 0.01f, 0.11f), new Vector3(0.16f, 0.03f, 0.04f), midSteel, new Vector3(0f, 120f, 0f));
            MakePart(head, PrimitiveType.Cube, "Nozzle_C",
                new Vector3(-0.07f, 0.01f, -0.11f), new Vector3(0.16f, 0.03f, 0.04f), midSteel, new Vector3(0f, 240f, 0f));

            // Status beacon on the base.
            MakePart(root, PrimitiveType.Cube, "StatusBeacon",
                new Vector3(0.16f, 0.09f, 0.16f), new Vector3(0.05f, 0.04f, 0.05f), new Color(0.32f, 0.86f, 0.42f));
        }

        private void CacheVisuals()
        {
            var vis = transform.Find("Generated_SprinklerVisuals");
            var gauge = vis != null ? vis.Find("WaterGauge") : null;
            _gauge = gauge != null ? gauge.GetComponent<MeshRenderer>() : null;
            var beacon = vis != null ? vis.Find("StatusBeacon") : null;
            _beacon = beacon != null ? beacon.GetComponent<MeshRenderer>() : null;
            _head = vis != null ? vis.Find("Head") : null;
        }

        private void SpinHead(float dt)
        {
            if (_head == null || !IsActive) return;
            _headAngle = Mathf.Repeat(_headAngle + dt * 90f, 360f);
            _head.localRotation = Quaternion.Euler(0f, _headAngle, 0f);
        }

        private void RefreshVisuals()
        {
            if (_gauge != null)
            {
                // The sight glass shortens with the tank, so "how much water is left" is
                // answered by the block itself rather than by a number in a panel.
                float level = Mathf.Max(0.02f, WaterFill01);
                _gauge.transform.localScale = new Vector3(0.07f, 0.26f * level, 0.02f);
                _gauge.transform.localPosition = new Vector3(0f, 0.13f + 0.13f * level, 0.145f);
            }

            if (_beacon != null)
            {
                Color tint = IsActive ? new Color(0.36f, 0.92f, 0.48f)
                    : waterStored <= 0.01f ? new Color(0.86f, 0.42f, 0.24f)
                    : new Color(0.86f, 0.72f, 0.24f);
                var block = new MaterialPropertyBlock();
                _beacon.GetPropertyBlock(block);
                block.SetColor(BaseColorId, tint);
                block.SetColor(ColorId, tint);
                _beacon.SetPropertyBlock(block);
            }
        }

        private static void MakePart(Transform parent, PrimitiveType type, string name,
            Vector3 pos, Vector3 scale, Color color, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && Application.isPlaying)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(ColorId, color);
                mr.SetPropertyBlock(mpb);
            }
        }
    }
}
