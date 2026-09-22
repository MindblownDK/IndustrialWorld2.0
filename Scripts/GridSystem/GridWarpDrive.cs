// Assets/Scripts/VoxelEngine/GridSystem/GridWarpDrive.cs
//
// THE Warp Drive — the ONLY warp in the game, and it is deliberately expensive.
//
// Real space has no free teleports: interplanetary flight is flown (real Keplerian
// orbits, floating origin, frame switches). This block is the one legitimate shortcut:
//
//   • It CHARGES over time, drawing a heavy sustained power load (grid-wide power).
//   • Once charged, a pilot can trigger it (InputAction.WarpDrive, default N) to
//     jump the whole ship to the aimed planet (arriving in co-moving orbit) — or a
//     fixed range straight ahead when no planet is in the target cone.
//   • It requires vacuum (it is a space drive), has a cooldown, and its recipe +
//     research are authored by Voxel Engine Setup (Step 27) so the item/prefab/
//     recipe/research are non-destructive.
//
// The jump itself is a floating-origin teleport: SpaceOrigin.TeleportCosmic re-anchors
// the scene at the destination, the reference frame re-selects the nearest body, and
// the grid arrives co-moving with that frame (scene velocity zeroed).
//
// FUEL (12.24.0-dev): the spin-up above is just the coils warming — the jump itself
// is bought with stored energy. Every drive carries an internal battery fed from the
// grid bus (see RECHARGE), and all enabled drives on a grid POOL their stores: range
// is pooled-kWh divided by Wh-per-km, so a far jump with a thin bank needs more drives.
// Consumption drains the pool proportionally, never one drive first.
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.UI;

namespace VoxelEngine.GridSystem
{
    public class GridWarpDrive : GridBlock
    {
        [Header("Warp Drive")]
        [Tooltip("Seconds of continuous charging to reach full charge.")]
        public float chargeSeconds = 45f;

        [Tooltip("Max power draw (W) while recharging the internal battery or spinning up.")]
        public float powerDrawWatts = 45000f;

        [Header("Warp Battery")]
        [Tooltip("Internal energy store per drive (Wh). Enabled drives on a grid pool their stores; range is pool divided by price.")]
        public float warpCapacityWh = 10000f;
        [Tooltip("Energy price of distance (Wh per km). At default tuning one full drive jumps exactly one fixed hop.")]
        public float energyPerKmWh = 4f;
        [Tooltip("Recharge the internal battery from the grid bus (draws up to the max above).")]
        public bool recharging = true;
        [Tooltip("Banked jump energy (Wh). Persisted with the grid.")]
        public float warpStoredWh;

        [Tooltip("Cooldown after a jump before the drive can charge again.")]
        public float cooldownSeconds = 180f;

        [Tooltip("Fixed jump range (km) when no planet is targeted.")]
        public float jumpRangeKm = 2500f;

        [Tooltip("Minimum jump range (km) — no short-hop abuse.")]
        public float minJumpKm = 400f;

        [Tooltip("Half-angle (deg) of the planet target-acquisition cone from the pilot's aim.")]
        public float targetConeDeg = 14f;

        [Tooltip("Half-angle (deg) of the singularity lock cone — aim directly at the black hole or quasar beacon to jump to its standoff corridor.")]
        public float singularityLockConeDeg = 4f;

        [Tooltip("Arrival altitude above the target planet's surface (km).")]
        public float arrivalAltitudeKm = 90f;

        // ── Runtime state ─────────────────────────────────────────
        public float Charge01 { get; private set; }
        public float Cooldown01 { get; private set; }
        public bool IsCharging { get; private set; }
        public bool IsReady => Charge01 >= 1f && Cooldown01 <= 0f;
        public float CurrentChargeWatts { get; private set; }
        public float Fill01 => warpCapacityWh > 0f ? Mathf.Clamp01(warpStoredWh / warpCapacityWh) : 0f;
        public bool WantsCharge => Enabled && Grid != null && warpStoredWh < warpCapacityWh - 0.001f;
        private bool _autoRecharge;
        /// <summary>Autopilot override: charge for the leg without touching the player's toggle.</summary>
        public void SetAutoRecharge(bool v) => _autoRecharge = v;
        public bool RechargeEffective => (recharging || _autoRecharge) && WantsCharge;
        public bool GridStarved => Grid != null && Grid.PowerAvailability01 < ChargeStallPowerFraction
            && ((recharging || _autoRecharge) || IsCharging);

        public override float PowerDraw => (Enabled && Grid != null && (RechargeEffective || IsCharging)) ? powerDrawWatts : 0f;

        private const float ChargeStallPowerFraction = 0.35f; // below this grid power availability, charge stalls

        private void Update()
        {
            if (Grid == null) return;

            if (Cooldown01 > 0f)
                Cooldown01 = Mathf.Max(0f, Cooldown01 - Time.deltaTime / Mathf.Max(1f, cooldownSeconds));

            // Battery: bank what the bus actually delivers (draw × availability), so a
            // starved grid charges slowly instead of pretending. Runs whether or not
            // the coils are spinning — fuel and spin-up are independent.
            CurrentChargeWatts = 0f;
            if (RechargeEffective && Grid != null)
            {
                float got = powerDrawWatts * Mathf.Clamp01(Grid.PowerAvailability01);
                warpStoredWh = Mathf.Min(warpCapacityWh, warpStoredWh + got * Time.deltaTime / 3600f);
                CurrentChargeWatts = got;
            }

            if (!IsCharging || !Enabled) return;

            // Charging consumes grid power; without a sufficient bus the drive stalls.
            float availability = Grid.PowerAvailability01;
            if (availability >= ChargeStallPowerFraction)
                Charge01 = Mathf.MoveTowards(Charge01, 1f, Time.deltaTime / Mathf.Max(1f, chargeSeconds));

            if (Charge01 >= 1f)
            {
                IsCharging = false;
                BuildFeedbackHud.Show("Warp Drive", "CHARGED — press [N] to jump", null, new Color(0.55f, 0.85f, 1f));
            }
        }

        /// <summary>Begin charging (no-op when already charging, ready, or on cooldown).</summary>
        public void BeginCharge()
        {
            if (IsReady || IsCharging) return;
            if (Cooldown01 > 0f)
            {
                BuildFeedbackHud.Show("Warp Drive", $"Cooling down — {Mathf.CeilToInt(Cooldown01 * cooldownSeconds)}s", null, new Color(1f, 0.7f, 0.25f));
                return;
            }
            if (Grid == null || !AtmosphereManager.IsInSpace(transform.position))
            {
                BuildFeedbackHud.Show("Warp Drive", "Requires vacuum — reach space first", null, new Color(1f, 0.7f, 0.25f));
                return;
            }
            Charge01 = 0f;
            IsCharging = true;
            BuildFeedbackHud.Show("Warp Drive", "Charging… power draw " + VoxelEngine.Items.PowerFormat.Watts(powerDrawWatts), null, new Color(0.55f, 0.85f, 1f));
        }

        /// <summary>Cancel charging (drains the accumulated charge).</summary>
        public void CancelCharge()
        {
            IsCharging = false;
            Charge01 = 0f;
        }

        /// <summary>
        /// Execute the warp. Returns true when a jump happened.
        /// </summary>
        public bool TryWarp()
        {
            if (!IsReady)
            {
                if (IsCharging)
                    BuildFeedbackHud.Show("Warp Drive", $"Charging… {Mathf.RoundToInt(Charge01 * 100f)}%", null, new Color(0.55f, 0.85f, 1f));
                else if (Cooldown01 > 0f)
                    BuildFeedbackHud.Show("Warp Drive", $"Cooling down — {Mathf.CeilToInt(Cooldown01 * cooldownSeconds)}s", null, new Color(1f, 0.7f, 0.25f));
                else
                    BuildFeedbackHud.Show("Warp Drive", "Not charged — press [N] to charge", null, new Color(1f, 0.7f, 0.25f));
                return false;
            }

            var origin = SpaceOrigin.Instance;
            var registry = CosmicRegistry.Instance;
            if (origin == null || registry == null || !registry.IsReady)
            {
                BuildFeedbackHud.Show("Warp Drive", "No valid star map", null, new Color(1f, 0.7f, 0.25f));
                return false;
            }

            // Pilot aim (cockpit forward; falls back to grid forward).
            Transform aimFrame = Grid != null && Grid.ActiveCockpit != null
                ? Grid.ActiveCockpit.transform
                : transform;
            Vector3 aimDir = aimFrame.forward.normalized;

            double3 gridCosmic = origin.GetCosmicKm(transform.position);
            double3 destination = gridCosmic + CosmicRegistry.ToDouble3(aimDir) * jumpRangeKm;
            BodyInstance targetPlanet = null;
            SingularityInstance targetSingularity = null;

            // Planet acquisition: nearest body inside the aim cone within 20 000 km.
            BodyInstance nearest = null;
            double nearestDist = double.MaxValue;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var b = registry.Bodies[i];
                if (b == null || b.settings == null) continue;
                double3 abs = registry.CosmicPositionOf(b);
                double d = math.length(abs - gridCosmic);
                if (d < nearestDist) { nearestDist = d; nearest = b; }
            }
            if (nearest != null && nearestDist < 20000d)
            {
                double3 toTarget = registry.CosmicPositionOf(nearest) - gridCosmic;
                double angleDeg = AngleDeg(CosmicRegistry.ToDouble3(aimDir), toTarget);
                if (angleDeg <= targetConeDeg && nearestDist > minJumpKm * 2d)
                {
                    targetPlanet = nearest;
                    double surfaceRadiusKm = nearest.settings.radiusKm;
                    double3 radial = math.normalizesafe(toTarget, new double3(0d, 1d, 0d));
                    destination = registry.CosmicPositionOf(nearest) + radial * (surfaceRadiusKm + arrivalAltitudeKm);
                }
            }

            // Singularity lock (Phase 5): aim at the black hole / quasar beacon (any range,
            // narrow cone) to jump to its standoff corridor — the deep-space remnant is a
            // real destination, not a skybox ornament.
            if (targetPlanet == null && registry.Singularities != null)
            {
                double bestAngle = singularityLockConeDeg;
                for (int i = 0; i < registry.Singularities.Count; i++)
                {
                    var s = registry.Singularities[i];
                    if (s == null) continue;
                    double3 toS = s.positionKmD - gridCosmic;
                    double d = math.length(toS);
                    if (d < 2d) continue;
                    double angleDeg = AngleDeg(CosmicRegistry.ToDouble3(aimDir), toS);
                    if (angleDeg <= bestAngle) { bestAngle = angleDeg; targetSingularity = s; }
                }
                if (targetSingularity != null)
                {
                    double3 toS = targetSingularity.positionKmD - gridCosmic;
                    double3 fromSing = -math.normalizesafe(toS, new double3(0d, 1d, 0d));
                    // Arrive on the near side (home side) of the remnant, at the authored
                    // standoff from the horizon, pulled gently into the equatorial plane so
                    // quasar jets are never on the arrival line.
                    double3 arrivalDir = fromSing;
                    Vector3 axis = targetSingularity.discAxis.sqrMagnitude > 0.001f
                        ? targetSingularity.discAxis.normalized
                        : Vector3.up;
                    double3 axisD = new double3(axis.x, axis.y, axis.z);
                    double align = math.dot(arrivalDir, axisD);
                    if (math.abs(align) > 0.85d)
                    {
                        double3 proj = arrivalDir - axisD * align;
                        double3 projN = math.normalizesafe(proj, axisD);
                        arrivalDir = math.normalize(projN * 0.85d + axisD * math.sign(align) * math.sqrt(1d - 0.85d * 0.85d));
                    }
                    double standoff = targetSingularity.eventHorizonKm
                                      + System.Math.Max(500d, targetSingularity.standoffArrivalKm);
                    destination = targetSingularity.positionKmD + arrivalDir * standoff;
                }
            }

            // Locator lock (Phase 5): a Star Locator block projects a waypoint toward
            // any celestial destination (planet/moon/sun/singularity). Aim at the
            // waypoint marker and jump — any range. Skipped when a planet is already
            // targeted (direct planet lock wins).
            BodyInstance locatorBody = null;
            string locatorArrivalName = null;
            if (targetPlanet == null && targetSingularity == null)
            {
                var locator = GridLocatorBlock.ActiveLocator;
                if (locator != null && GridLocatorBlock.HasWaypoint && locator.Enabled
                    && locator.Grid != null && locator.Grid.HasPower)
                {
                    double3 toWP = origin.GetCosmicKm(GridLocatorBlock.WaypointScenePosition) - gridCosmic;
                    double wpDist = math.length(toWP);
                    if (wpDist > 2d)
                    {
                        double wpAngle = AngleDeg(CosmicRegistry.ToDouble3(aimDir), toWP);
                        if (wpAngle <= targetConeDeg && locator.TryGetArrival(gridCosmic, out var locArrival, out locatorBody, out locatorArrivalName))
                        {
                            destination = locArrival;
                        }
                    }
                }
            }

            // ── Fuel check: range is bought, not granted ────────────────────
            double distKm = math.length(destination - gridCosmic);
            float rate = Grid != null ? MaxRateWhPerKm(Grid) : energyPerKmWh;
            float costWh = (float)(distKm * rate);
            float pooled = Grid != null ? PooledStoredWh(Grid) : warpStoredWh;
            if (pooled < costWh - 0.01f)
            {
                BuildFeedbackHud.Show("Warp Drive",
                    $"Need {costWh / 1000f:0.0} kWh for {distKm:0} km — banked {pooled / 1000f:0.0} kWh. " +
                    "Recharge, or fit more drives.", null, new Color(1f, 0.7f, 0.25f));
                return false;
            }
            if (Grid != null) TryConsumePooledWh(Grid, costWh);
            else warpStoredWh = Mathf.Max(0f, warpStoredWh - costWh);

            // ── Execute: floating-origin teleport ─────────────────
            origin.TeleportCosmic(destination);
            origin.SetFrame(targetPlanet != null || locatorBody != null
                ? ResolveSceneBody(registry, targetPlanet != null ? targetPlanet : locatorBody)
                : null);

            // Arrive co-moving with the destination frame: zero scene velocity so the
            // grid hangs relative to the target (SE-style orbit arrival).
            if (Grid != null && Grid.Body != null)
            {
                Grid.Body.linearVelocity = Vector3.zero;
                Grid.Body.angularVelocity = Vector3.zero;
            }
            var pilot = Grid != null && Grid.ActiveCockpit != null ? Grid.ActiveCockpit.Pilot : null;
            if (pilot != null) pilot.ResetVelocity();

            IsCharging = false;
            Charge01 = 0f;
            Cooldown01 = 1f;

            string targetName = targetPlanet != null
                ? $"{targetPlanet.DisplayName} orbit ({arrivalAltitudeKm:0} km altitude)"
                : targetSingularity != null
                    ? $"{targetSingularity.DisplayName} standoff ({(int)targetSingularity.standoffArrivalKm:0} km from horizon)"
                    : locatorArrivalName != null
                        ? $"Locator: {locatorArrivalName}"
                        : $"{jumpRangeKm:0} km straight ahead";
            BuildFeedbackHud.Show("Warp Jump", $"Arrived: {targetName}", null, new Color(0.55f, 0.85f, 1f));
            Debug.Log($"[GridWarpDrive] Warp to {targetName} at {destination} km.");
            return true;
        }

        private static CelestialBody ResolveSceneBody(CosmicRegistry registry, BodyInstance instance)
        {
            if (registry.SceneBodies != null && registry.SceneBodies.TryGetValue(instance, out var body))
                return body;
            return null;
        }

        private static double AngleDeg(double3 a, double3 b)
        {
            double la = math.length(a), lb = math.length(b);
            if (la < 1e-12 || lb < 1e-12) return 0d;
            double dot = math.clamp(math.dot(a, b) / (la * lb), -1d, 1d);
            return math.acos(dot) * 57.29577951308232d;
        }

        // ── Pooled store ────────────────────────────────────────────────
        // Every enabled warp drive on a grid throws its battery into one pot.
        // Costing uses the WORST rate in the pool (never strand a jump), and
        // consumption drains proportionally so all drives land equally empty.

        public static float PooledStoredWh(GridEntity grid)
        {
            if (grid == null) return 0f;
            float total = 0f;
            foreach (var block in grid.AllBlocks)
                if (block is GridWarpDrive w && w.Enabled) total += Mathf.Max(0f, w.warpStoredWh);
            return total;
        }

        public static float PooledCapacityWh(GridEntity grid)
        {
            if (grid == null) return 0f;
            float total = 0f;
            foreach (var block in grid.AllBlocks)
                if (block is GridWarpDrive w && w.Enabled) total += Mathf.Max(0f, w.warpCapacityWh);
            return total;
        }

        public static int PooledDriveCount(GridEntity grid)
        {
            if (grid == null) return 0;
            int n = 0;
            foreach (var block in grid.AllBlocks)
                if (block is GridWarpDrive w && w.Enabled) n++;
            return n;
        }

        public static float MaxRateWhPerKm(GridEntity grid)
        {
            if (grid == null) return 4f;
            float worst = 0f;
            bool any = false;
            foreach (var block in grid.AllBlocks)
                if (block is GridWarpDrive w && w.Enabled)
                {
                    if (!any || w.energyPerKmWh > worst) worst = w.energyPerKmWh;
                    any = true;
                }
            return any ? Mathf.Max(0.01f, worst) : 4f;
        }

        /// <summary>Drain wh from the pool proportionally. Returns false and drains
        /// nothing when the pool is short — the caller refuses the jump instead.</summary>
        public static bool TryConsumePooledWh(GridEntity grid, float wh)
        {
            if (grid == null || wh <= 0f) return true;
            float total = PooledStoredWh(grid);
            if (total < wh - 0.01f) return false;
            foreach (var block in grid.AllBlocks)
                if (block is GridWarpDrive w && w.Enabled && w.warpStoredWh > 0f)
                    w.warpStoredWh = Mathf.Max(0f, w.warpStoredWh - wh * (w.warpStoredWh / total));
            return true;
        }

        /// <summary>How far the current pool flies, km. Zero when the pot is empty.</summary>
        public static double PoolRangeKm(GridEntity grid)
        {
            float rate = MaxRateWhPerKm(grid);
            return rate > 0f ? PooledStoredWh(grid) / rate : 0d;
        }

        /// <summary>Save-load restore: banked energy, recharge toggle and cooldown.
        /// The spin-up never persists — a loaded drive re-spools in 45 seconds.</summary>
        public void RestorePersistentState(float storedWh, bool recharging, float cooldown01)
        {
            warpStoredWh = Mathf.Clamp(storedWh, 0f, Mathf.Max(0f, warpCapacityWh));
            this.recharging = recharging;
            Cooldown01 = Mathf.Clamp01(cooldown01);
            IsCharging = false;
            Charge01 = 0f;
        }

        public override void OnRemoved()
        {
            IsCharging = false;
            Charge01 = 0f;
            base.OnRemoved();
        }
    }
}
