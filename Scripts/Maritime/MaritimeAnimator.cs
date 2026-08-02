// Assets/Scripts/VoxelEngine/Maritime/MaritimeAnimator.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║   MARITIME ANIMATOR — drives all visual motion on maritime blocks.║
//  ║                                                                    ║
//  ║   This is the ONLY per-block MonoBehaviour in the maritime stack   ║
//  ║   that runs in Update(). It's intentionally lightweight: it just   ║
//  ║   rotates / bobs named child pivots that the MaritimeMeshBuilder   ║
//  ║   created. The heavy physics still runs in Burst jobs.             ║
//  ║                                                                    ║
//  ║   Animations driven:                                               ║
//  ║     • Propeller blades spin at CurrentRPM                          ║
//  ║     • Turbocharger compressor spins at TurboRPM                    ║
//  ║     • Engine pistons pump at their firing order, slid along the    ║
//  ║       cached bore axis so V-bank tilt animates correctly           ║
//  ║     • Engine crankshaft + output shaft share one deterministic     ║
//  ║       crank angle (shaft always linked to crankshaft)              ║
//  ║     • MGO sea-water pump pulley belt-driven off the crank          ║
//  ║     • Waterwheel paddles rotate at CurrentRPM                      ║
//  ║     • Gearbox gear rotates at OutputRPM                            ║
//  ║     • Generator rotor spins at CurrentRPM                          ║
//  ║     • Helm wheel rotates with steer input                          ║
//  ║     • Exhaust smoke density varies (handled in ExhaustPipe)        ║
//  ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Maritime
{
    /// <summary>
    /// Lightweight per-block visual driver. Attached automatically by the mesh
    /// builder to any block with animatable parts. Finds named pivot children
    /// and rotates them based on the block's live state.
    ///
    /// NOTE: No [RequireComponent] — that blocks StripMissingScripts from
    /// cleaning broken script refs on prefabs (Unity can't remove a component
    /// that another component depends on).
    /// </summary>
    public class MaritimeAnimator : MonoBehaviour
    {
        // ── Engine timing tables ─────────────────────────────────────
        // Firing-order crank phases (degrees of crank rotation, 720° cycle)
        // indexed by cylinder number. Piston POSITION is a pure function of
        // crank angle, so these phases double as the crank-pin offsets.
        /// <summary>Inline-4, firing order 1-3-4-2.</summary>
        private static readonly float[] Inline4FiringPhases = { 0f, 540f, 180f, 360f };
        /// <summary>V8 cross-plane, firing order 1-8-4-3-6-5-7-2.</summary>
        private static readonly float[] V8FiringPhases = { 0f, 630f, 180f, 270f, 450f, 360f, 540f, 90f };
        /// <summary>V12 60° even-fire, firing order 1-12-5-8-3-10-6-7-2-11-4-9.</summary>
        private static readonly float[] V12FiringPhases = { 0f, 480f, 240f, 600f, 120f, 360f, 420f, 180f, 660f, 300f, 540f, 60f };

        // Named pivots created by MaritimeMeshBuilder. Null if not present.
        private Transform _spinPivot;       // propeller blades / wheel rotor
        private Transform _turboSpin;       // turbo compressor wheel
        private Transform[] _pistons;       // engine piston rods
        private Vector3[] _pistonBase;      // piston local rest positions
        private Vector3[] _pistonAxis;      // piston bore axis in parent space (handles V-tilt)
        private Transform _gearRotor;       // gearbox gear / transfer bevel
        private Transform _generatorRotor;  // generator coil rotor
        private Transform _helmWheel;       // helm steering wheel
        private Transform _crankshaft;      // engine crankshaft (visible pulley)
        private Transform _shaftSpin;       // drive shaft / output coupler visual spin
        private Transform _chainRotor;      // chain drive sprocket rotor
        private Transform _seaPump;         // MGO seawater pump pulley (accessory belt)

        private Quaternion _crankBaseRot = Quaternion.identity;
        private Quaternion _shaftBaseRot = Quaternion.identity;
        private Quaternion _pumpBaseRot = Quaternion.identity;

        // Shared deterministic crank angle. One accumulator drives the
        // crankshaft, the output shaft and every piston so they never drift
        // apart — the output shaft is ALWAYS linked to the crankshaft.
        private float _crankAngleDeg;

        private float _currentHelmAngle, _targetHelmAngle;

        private GridBlock _block;

        private void Awake()
        {
            _block = GetComponent<GridBlock>();
            CachePivots();
        }

        private void CachePivots()
        {
            _spinPivot = FindDeep("SpinPivot");
            _turboSpin = FindDeep("TurboSpin");
            _gearRotor = FindDeep("GearRotor");
            _generatorRotor = FindDeep("GenRotor");
            _helmWheel = FindDeep("HelmWheel");
            _crankshaft = FindDeep("CrankPulley");
            _shaftSpin = FindDeep("ShaftSpin");
            _chainRotor = FindDeep("ChainRotor");
            _seaPump = FindDeep("SeaPump");

            if (_crankshaft != null) _crankBaseRot = _crankshaft.localRotation;
            if (_shaftSpin != null) _shaftBaseRot = _shaftSpin.localRotation;
            if (_seaPump != null) _pumpBaseRot = _seaPump.localRotation;

            // Pistons are named Piston_0, Piston_1, etc. Cache the rest pose and
            // the bore travel axis in PARENT space (piston.up through the parent's
            // inverse) so tilted V-bank pistons slide along their own bore.
            var list = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 12; i++)
            {
                var p = FindDeep($"Piston_{i}");
                if (p == null) break;
                list.Add(p);
            }
            _pistons = list.ToArray();
            _pistonBase = new Vector3[_pistons.Length];
            _pistonAxis = new Vector3[_pistons.Length];
            for (int i = 0; i < _pistons.Length; i++)
            {
                var p = _pistons[i];
                _pistonBase[i] = p.localPosition;
                _pistonAxis[i] = p.parent != null
                    ? p.parent.InverseTransformDirection(p.up)
                    : Vector3.up;
            }
        }

        private Transform FindDeep(string name)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t != transform && t.name == name)
                    return t;
            return null;
        }

        private void Update()
        {
            if (_block == null) return;
            float dt = Time.deltaTime;

            switch (_block)
            {
                case GridPropeller prop:
                    AnimatePropeller(prop, dt);
                    break;
                case GridElectricalPropeller ep:
                    AnimatePropeller(ep, dt);
                    break;
                case GridWaterwheel ww:
                    AnimateWaterwheel(ww, dt);
                    break;
                case GridGearbox gb:
                    AnimateGearbox(gb, dt);
                    break;
                case GridMaritimeGenerator gen:
                    AnimateGenerator(gen, dt);
                    break;
                case GridTurbocharger tc:
                    AnimateTurbo(tc, dt);
                    break;
                case GridMaritimeEngine eng:
                    AnimateEngine(eng, dt);
                    break;
                case GridHelm helm:
                    AnimateHelm(helm, dt);
                    break;
                case GridDriveShaft ds:
                    AnimateDriveShaft(ds, dt);
                    break;
                case GridShaftHousing housing:
                    AnimateShaftHousing(housing, dt);
                    break;
                case GridEncasedChainDrive cd:
                    AnimateChainDrive(cd, dt);
                    break;
                case GridRotationTransfer rt:
                    AnimateRotationTransfer(rt, dt);
                    break;
            }
        }

        // ── Individual animators ──────────────────────────────────────

        private void AnimatePropeller(GridBlock block, float dt)
        {
            if (_spinPivot == null) return;
            float rpm = 0f;
            if (block is GridPropeller p) rpm = p.CurrentRPM;
            else if (block is GridElectricalPropeller ep) rpm = ep.CurrentRPM;
            if (rpm > 0.5f) SpinZ(_spinPivot, rpm, dt);
        }

        private void AnimateWaterwheel(GridWaterwheel ww, float dt)
        {
            if (_spinPivot == null) return;
            if (ww.CurrentRPM > 0.5f) SpinX(_spinPivot, ww.CurrentRPM, dt);
        }

        private void AnimateGearbox(GridGearbox gb, float dt)
        {
            if (_gearRotor == null) return;
            if (gb.OutputRPM > 0.5f) SpinY(_gearRotor, gb.OutputRPM, dt);
        }

        private void AnimateGenerator(GridMaritimeGenerator gen, float dt)
        {
            if (_generatorRotor != null && gen.CurrentRPM > 0.5f) SpinY(_generatorRotor, gen.CurrentRPM, dt);
            if (_shaftSpin != null && gen.CurrentRPM > 0.5f) SpinZ(_shaftSpin, gen.CurrentRPM, dt);
        }

        private void AnimateTurbo(GridTurbocharger tc, float dt)
        {
            if (_turboSpin == null) return;
            if (tc.TurboRPM > 1f) SpinZ(_turboSpin, tc.TurboRPM, dt);
        }

        private void AnimateEngine(GridMaritimeEngine eng, float dt)
        {
            // Only animate when the engine is actually running (fuel + enabled + exhaust).
            if (!eng.IsRunning) return;

            // engine_speed (0..1) is the single normalized driver: it controls the
            // crankshaft RPM and the piston playback rate simultaneously.
            float engineSpeed = eng.EngineSpeed01;
            float visualRpm = engineSpeed * eng.maxRPM * eng.ModuleSpeedCapMultiplier;
            if (visualRpm <= 0.5f) return;

            // Advance the shared crank angle — one accumulator for crank, shaft,
            // pump and pistons so the whole drivetrain visual stays in lock-step.
            _crankAngleDeg = (_crankAngleDeg + visualRpm * 6f * dt) % 720f;

            // Crankshaft + output shaft rotate from the SAME deterministic angle.
            if (_crankshaft != null)
                _crankshaft.localRotation = _crankBaseRot * Quaternion.Euler(0f, 0f, -_crankAngleDeg);
            if (_shaftSpin != null)
                _shaftSpin.localRotation = _shaftBaseRot * Quaternion.Euler(0f, 0f, -_crankAngleDeg);
            // MGO seawater pump pulley — belt-driven off the front accessory drive.
            if (_seaPump != null)
                _seaPump.localRotation = _pumpBaseRot * Quaternion.Euler(0f, 0f, -_crankAngleDeg * 1.6f);

            // Pistons pump at their firing-order phase, sliding along the cached
            // bore axis so V-bank tilt animates correctly.
            if (_pistons != null && _pistons.Length > 0)
            {
                float[] phases = FiringPhasesFor(_pistons.Length);
                for (int i = 0; i < _pistons.Length; i++)
                {
                    float phase = phases != null && i < phases.Length
                        ? phases[i]
                        : i * (720f / Mathf.Max(1, _pistons.Length));
                    // Slider-crank approximation: position follows cos(crank+phase).
                    float bob = Mathf.Cos((_crankAngleDeg + phase) * Mathf.Deg2Rad) * 0.018f;
                    _pistons[i].localPosition = _pistonBase[i] + _pistonAxis[i] * bob;
                }
            }
        }

        /// <summary>Firing-order crank phases for the discovered piston count.</summary>
        private static float[] FiringPhasesFor(int pistonCount) => pistonCount switch
        {
            4 => Inline4FiringPhases,   // Crude Inline-4  (1-3-4-2)
            8 => V8FiringPhases,        // HFO V8          (1-8-4-3-6-5-7-2)
            12 => V12FiringPhases,      // MGO V12         (1-12-5-8-3-10-6-7-2-11-4-9)
            _ => null,
        };

        private void AnimateDriveShaft(GridDriveShaft ds, float dt)
        {
            if (_shaftSpin == null) return;
            if (ds.CurrentRPM > 0.5f) SpinZ(_shaftSpin, ds.CurrentRPM, dt);
        }

        private void AnimateShaftHousing(GridShaftHousing housing, float dt)
        {
            if (_shaftSpin == null) return;
            if (housing.CurrentRPM > 0.5f) SpinZ(_shaftSpin, housing.CurrentRPM, dt);
        }

        private void AnimateChainDrive(GridEncasedChainDrive chainDrive, float dt)
        {
            if (_chainRotor != null && chainDrive.CurrentRPM > 0.5f) SpinX(_chainRotor, chainDrive.CurrentRPM, dt);
            if (_shaftSpin != null && chainDrive.CurrentRPM > 0.5f) SpinZ(_shaftSpin, chainDrive.CurrentRPM, dt);
        }

        private void AnimateRotationTransfer(GridRotationTransfer transfer, float dt)
        {
            if (_gearRotor != null && transfer.CurrentRPM > 0.5f) SpinY(_gearRotor, transfer.CurrentRPM, dt);
            if (_shaftSpin != null && transfer.CurrentRPM > 0.5f) SpinZ(_shaftSpin, transfer.CurrentRPM, dt);
        }

        private void AnimateHelm(GridHelm helm, float dt)
        {
            if (_helmWheel == null) return;
            // Rotate the wheel proportionally to steer input.
            float steer = 0f;
            if (helm.Grid?.Maritime != null)
                steer = helm.Grid.Maritime.Steer;
            _targetHelmAngle = steer * 180f; // ±180°
            _currentHelmAngle = Mathf.LerpAngle(_currentHelmAngle, _targetHelmAngle, dt * 6f);
            _helmWheel.localRotation = Quaternion.Euler(0, 0, _currentHelmAngle);
        }

        // ── Spin helpers (degrees per second from RPM) ─────────────────
        // RPM → degrees/sec = RPM * 6 (since 1 rev = 360°, 1 min = 60s → 6°/s per RPM)
        private void SpinZ(Transform t, float rpm, float dt)
        {
            if (t == null) return;
            t.Rotate(0, 0, rpm * 6f * dt, Space.Self);
        }
        private void SpinX(Transform t, float rpm, float dt)
        {
            if (t == null) return;
            t.Rotate(rpm * 6f * dt, 0, 0, Space.Self);
        }
        private void SpinY(Transform t, float rpm, float dt)
        {
            if (t == null) return;
            t.Rotate(0, rpm * 6f * dt, 0, Space.Self);
        }
    }
}
