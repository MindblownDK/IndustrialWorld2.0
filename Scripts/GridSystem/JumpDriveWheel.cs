// Assets/Scripts/VoxelEngine/GridSystem/JumpDriveWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║              INDUSTRIAL WORLD — JUMP DRIVE DIAL                   ║
// ║                                                                  ║
// ║  Destination selection for the warp drive, on the same dial as   ║
// ║  every other radial selector in the game: hold the warp key,     ║
// ║  flick toward a world, release, and the drive plots the jump.    ║
// ║                                                                  ║
// ║  Before this, a destination could only be picked by right-       ║
// ║  clicking the drive block and reading a scrolling list — which   ║
// ║  meant leaving the cockpit view in the middle of a burn. The     ║
// ║  block panel still exists and is still the place for tuning;     ║
// ║  this is the one you use with a hand on the throttle.            ║
// ║                                                                  ║
// ║  Wedges are live: distance and price are recomputed while the    ║
// ║  dial is open, a destination the pool cannot reach is dimmed,    ║
// ║  and one that has gone dark is locked out entirely.              ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
using VoxelEngine.FX;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem
{
    public sealed class JumpDriveWheel : MonoBehaviour
    {
        /// <summary>Beyond this the dial is unreadable; the block panel owns the long tail.</summary>
        private const int MaxWedges = 12;

        public static JumpDriveWheel Instance { get; private set; }

        /// <summary>
        /// True when a dial exists to own the warp key. The cockpit defers its own
        /// press handler to this, so the key never both charges and opens the dial.
        /// </summary>
        public static bool OwnsWarpKey => Instance != null && Instance.isActiveAndEnabled;

        private readonly RadialWheelController _wheel = new();
        private readonly List<GridWarpDrive.WarpTarget> _targets = new(32);
        private readonly List<GridWarpDrive.WarpTarget> _wedgeTargets = new(MaxWedges);
        private readonly string[] _details = new string[3];
        private readonly bool[] _detailsOk = new bool[3];

        private GridWarpDrive _drive;
        private VisualElement _host;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.ConfirmCentre = ConfirmCentre;
            _wheel.GroupLabel = "JUMP DRIVE";
        }

        private void OnDestroy()
        {
            _wheel.Dispose();
            if (Instance == this) Instance = null;
        }

        private void OnDisable() => _wheel.Dispose();

        private void Update()
        {
            _drive = ResolveDrive();
            bool context = _drive != null;

            if (context)
            {
                _wheel.IdleTitle = CentreTitle();
                _wheel.IdleDescription = CentreDescription();
                _wheel.IdleIcon = CentreIcon();
                _wheel.IdleTitleColor = _drive.IsReady ? UITheme.AccentGreen : UITheme.AccentCyan;
                _wheel.SwapHint = DriveStatusLine();
            }

            _wheel.Tick(InputAction.WarpDrive, context);
        }

        /// <summary>
        /// The dial only exists for the pilot of a grid that actually carries an
        /// enabled drive. One drive per grid is engaged — the first enabled one,
        /// matching the cockpit's own rule.
        /// </summary>
        private GridWarpDrive ResolveDrive()
        {
            if (!GridCockpit.AnyPilotSeatActive) return null;
            var grid = GridCockpit.ActiveControlGrid;
            if (grid == null) return null;
            foreach (var block in grid.AllBlocks)
                if (block is GridWarpDrive candidate && candidate.Enabled) return candidate;
            return null;
        }

        private VisualElement ResolveHost()
        {
            if (_host != null && _host.panel != null) return _host;
            var controller = GameUIController.Instance;
            var document = controller != null ? controller.GetComponent<UIDocument>() : FindAnyObjectByType<UIDocument>();
            _host = document != null ? document.rootVisualElement : null;
            return _host;
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPTIONS
        // ══════════════════════════════════════════════════════════════════

        private void FillOptions(List<RadialOption> options)
        {
            _wedgeTargets.Clear();
            if (_drive == null) return;

            _drive.ChartedTargets(_targets);
            var origin = SpaceOrigin.Instance;
            var registry = CosmicRegistry.Instance;
            bool mapped = origin != null && registry != null && registry.IsReady;

            double3 shipKm = mapped ? origin.GetCosmicKm(_drive.transform.position) : double3.zero;
            float rate = _drive.Grid != null ? GridWarpDrive.EffectiveRateWhPerKm(_drive.Grid) : _drive.energyPerKmWh;
            float pooled = _drive.Grid != null ? GridWarpDrive.PooledStoredWh(_drive.Grid) : _drive.warpStoredWh;

            // Nearest first: the dial is a cockpit instrument, and the thing you are
            // most likely to jump to is the thing you are closest to.
            _targets.Sort((a, b) =>
            {
                if (!mapped) return 0;
                double da = math.length(_drive.TargetCentreKm(a, origin, registry) - shipKm);
                double db = math.length(_drive.TargetCentreKm(b, origin, registry) - shipKm);
                return da.CompareTo(db);
            });

            int count = Mathf.Min(MaxWedges, _targets.Count);
            for (int i = 0; i < count; i++)
            {
                var target = _targets[i];
                _wedgeTargets.Add(target);

                bool alive = !mapped || _drive.TargetAlive(target, registry);
                double distKm = 0d, jumpKm = 0d;
                double costKWh = 0d;
                bool affordable = false;

                if (mapped && alive)
                {
                    distKm = math.length(_drive.TargetCentreKm(target, origin, registry) - shipKm);
                    jumpKm = System.Math.Max(0d, distKm - System.Math.Max(0d, target.StandoffKm));
                    costKWh = jumpKm * rate / 1000d;
                    affordable = pooled >= jumpKm * rate - 0.01f;
                }

                _details[0] = mapped
                    ? (distKm >= 1d ? $"{distKm:N0} km" : $"{distKm * 1000d:N0} m")
                    : "NO STAR MAP";
                _detailsOk[0] = mapped && alive;
                _details[1] = mapped ? $"{costKWh:0.0} kWh  ({pooled / 1000f:0.0} stored)" : string.Empty;
                _detailsOk[1] = affordable;
                _details[2] = _drive.IsReady ? string.Empty : "DRIVE NOT READY";
                _detailsOk[2] = false;

                options.Add(new RadialOption
                {
                    Title = (target.DisplayName ?? "TARGET").ToUpperInvariant(),
                    Description = KindLine(target),
                    Icon = IconFor(target),
                    Details = (string[])_details.Clone(),
                    DetailsOk = (bool[])_detailsOk.Clone(),
                    Available = alive,
                    Affordable = affordable,
                    Selected = false
                });
            }
        }

        private string KindLine(GridWarpDrive.WarpTarget target)
        {
            if (target.RouteName != null) return "Route book — plotted arrival point";
            if (target.Beacon != null) return $"Powered beacon — {GridWarpDrive.BeaconRendezvousKm:0} km rendezvous";
            if (target.Body != null)
                return target.Body.isPlanet
                    ? $"Charted planet — {_drive.arrivalAltitudeKm:0} km arrival shelf"
                    : $"Charted moon — {_drive.arrivalAltitudeKm:0} km arrival shelf";
            return "Charted destination";
        }

        private static Texture2D IconFor(GridWarpDrive.WarpTarget target)
        {
            if (target.RouteName != null) return JumpTargetIcons.Route;
            if (target.Beacon != null) return JumpTargetIcons.Beacon;
            if (target.Body != null && !target.Body.isPlanet) return JumpTargetIcons.Moon;
            return JumpTargetIcons.Planet;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CENTRE — the drive's own charge / fire control
        // ══════════════════════════════════════════════════════════════════

        private string CentreTitle()
        {
            if (_drive == null) return "NO DRIVE";
            if (_drive.Cooldown01 > 0f) return "VENTING";
            if (_drive.IsReady) return "AIMED JUMP";
            return _drive.IsCharging ? "CANCEL SPIN-UP" : "BEGIN SPIN-UP";
        }

        private string CentreDescription()
        {
            if (_drive == null) return string.Empty;
            if (_drive.Cooldown01 > 0f) return "The coil is still venting from the last jump";
            if (_drive.IsReady) return "Left click to jump along the pilot's aim instead of a charted target";
            return _drive.IsCharging
                ? $"Spinning up — {_drive.Charge01 * 100f:0}% of {_drive.EffectiveChargeSeconds:0}s"
                : "Left click to start winding the coil";
        }

        private Texture2D CentreIcon()
        {
            if (_drive == null) return JumpTargetIcons.Cooldown;
            if (_drive.Cooldown01 > 0f) return JumpTargetIcons.Cooldown;
            return _drive.IsReady ? JumpTargetIcons.JumpReady : JumpTargetIcons.Spinup;
        }

        private string DriveStatusLine()
        {
            if (_drive == null) return string.Empty;
            float pooled = _drive.Grid != null ? GridWarpDrive.PooledStoredWh(_drive.Grid) : _drive.warpStoredWh;
            float capacity = _drive.Grid != null ? GridWarpDrive.PooledCapacityWh(_drive.Grid) : _drive.warpCapacityWh;
            double rangeKm = _drive.Grid != null ? GridWarpDrive.PoolRangeKm(_drive.Grid) : 0d;
            return $"BANK {pooled / 1000f:0.0} / {capacity / 1000f:0.0} kWh  ·  RANGE {rangeKm:N0} km";
        }

        private void ConfirmCentre()
        {
            if (_drive == null) return;
            if (_drive.IsReady) { _drive.TryWarp(); return; }
            if (_drive.IsCharging) { _drive.CancelCharge(); return; }
            _drive.BeginCharge();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONFIRM
        // ══════════════════════════════════════════════════════════════════

        private bool Confirm(int index)
        {
            if (_drive == null) return true;
            if (index < 0 || index >= _wedgeTargets.Count) return true;

            var target = _wedgeTargets[index];
            if (!_drive.IsReady && !_drive.IsCharging)
            {
                // A destination picked on a cold drive is not a failure — it is the
                // pilot saying "go there", so wind the coil and say so.
                _drive.BeginCharge();
                BuildFeedbackHud.Show("Jump Drive",
                    $"Spinning up for {target.DisplayName}", null, UITheme.AccentCyan);
                AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.6f, 1.02f);
                return true;
            }

            _drive.TryWarpTo(target);
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.7f, 1.06f);
            return true;
        }
    }
}
