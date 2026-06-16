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
//  ║     • Engine pistons bob at CurrentRPM                             ║
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
    /// </summary>
    [RequireComponent(typeof(GridBlock))]
    public class MaritimeAnimator : MonoBehaviour
    {
        // Named pivots created by MaritimeMeshBuilder. Null if not present.
        private Transform _spinPivot;       // propeller blades / wheel rotor
        private Transform _turboSpin;       // turbo compressor wheel
        private Transform[] _pistons;       // engine piston rods
        private Transform _gearRotor;       // gearbox gear
        private Transform _generatorRotor;  // generator coil rotor
        private Transform _helmWheel;       // helm steering wheel
        private Transform _crankshaft;      // engine crankshaft (visible pulley)
        private Transform _shaftSpin;       // drive shaft visual spin

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

            // Pistons are named Piston_0, Piston_1, etc.
            var list = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < 12; i++)
            {
                var p = FindDeep($"Piston_{i}");
                if (p == null) break;
                list.Add(p);
            }
            _pistons = list.ToArray();
            _pistonBaseY = new float[_pistons.Length];
            for (int i = 0; i < _pistons.Length; i++)
                _pistonBaseY[i] = _pistons[i].localPosition.y;
            _pistonBaseCached = true;
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
            }
        }

        // ── Individual animators ──────────────────────────────────────

        private void AnimatePropeller(GridBlock block, float dt)
        {
            if (_spinPivot == null) return;
            float rpm = 0f;
            if (block is GridPropeller p) rpm = p.CurrentRPM;
            else if (block is GridElectricalPropeller ep) rpm = ep.CurrentRPM;
            SpinZ(_spinPivot, rpm, dt);
        }

        private void AnimateWaterwheel(GridWaterwheel ww, float dt)
        {
            if (_spinPivot == null) return;
            SpinX(_spinPivot, ww.CurrentRPM, dt);
        }

        private void AnimateGearbox(GridGearbox gb, float dt)
        {
            if (_gearRotor == null) return;
            SpinY(_gearRotor, gb.OutputRPM, dt);
        }

        private void AnimateGenerator(GridMaritimeGenerator gen, float dt)
        {
            if (_generatorRotor == null) return;
            SpinY(_generatorRotor, gen.CurrentRPM, dt);
        }

        private void AnimateTurbo(GridTurbocharger tc, float dt)
        {
            if (_turboSpin == null) return;
            SpinZ(_turboSpin, tc.TurboRPM, dt);
        }

        private void AnimateEngine(GridMaritimeEngine eng, float dt)
        {
            // Pistons bob up/down at engine RPM (firing order simulated via phase offset).
            if (_pistons != null && _pistons.Length > 0)
            {
                float cycleRPM = eng.CurrentRPM;
                float phase = (Time.time * cycleRPM * 6f) % 360f; // 6° per RPM·sec
                for (int i = 0; i < _pistons.Length; i++)
                {
                    float strokeOffset = i * (360f / _pistons.Length); // firing order spread
                    float angle = (phase + strokeOffset) * Mathf.Deg2Rad;
                    float bob = Mathf.Sin(angle) * 0.015f; // ±1.5cm travel
                    var p = _pistons[i];
                    var pos = p.localPosition;
                    pos.y = _pistonBaseY[i] + bob;
                    p.localPosition = pos;
                }
            }

            // Crankshaft pulley spins.
            if (_crankshaft != null)
                SpinZ(_crankshaft, eng.CurrentRPM, dt);
        }

        private float[] _pistonBaseY;
        private bool _pistonBaseCached;

        private void AnimateDriveShaft(GridDriveShaft ds, float dt)
        {
            if (_shaftSpin == null) return;
            // Spin around the shaft's long axis (Z).
            SpinZ(_shaftSpin, ds.CurrentRPM, dt);
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

        private float _currentHelmAngle, _targetHelmAngle;

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
