// Assets/Scripts/VoxelEngine/Power/Wind/NacelleRoofLid.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  NACELLE ROOF LID — hinged service hatch on T-Series nacelles.  ║
// ║                                                                  ║
// ║  Sits ON the "RoofLid" object inside the nacelle prefab. This   ║
// ║  object is the hinge PIVOT: every child underneath it (plate,   ║
// ║  stripe, hinges — or anything you add) swings with the lid.     ║
// ║  Edit the look freely in the prefab; tune the swing here:       ║
// ║    • openAngle — degrees the lid swings up (default 75)         ║
// ║    • easeSpeed — hydraulic ease-out speed                       ║
// ║                                                                  ║
// ║  The WindTurbineController drives TargetOpen when the player    ║
// ║  approaches holding a Gearbox / Generator for this turbine.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public class NacelleRoofLid : MonoBehaviour
    {
        [Header("Swing")]
        [Tooltip("How far the lid swings open, in degrees. Hinge sits at the FRONT (rotor side); positive angles lift the rear edge up and over.")]
        [Range(0f, 170f)] public float openAngle = 75f;

        [Tooltip("Ease-out speed of the swing. Higher = snappier hydraulics.")]
        public float easeSpeed = 3.5f;

        /// <summary>Set by the WindTurbineController — true while the lid should be open.</summary>
        public bool TargetOpen { get; set; }

        /// <summary>Current hinge angle in degrees (read-only, for FX hooks).</summary>
        public float CurrentAngle => _angle;

        private float _angle;
        private Quaternion _closedRotation;

        private void Awake()
        {
            // Whatever pose the lid was authored in counts as "closed".
            _closedRotation = transform.localRotation;
        }

        private void Update()
        {
            float target = TargetOpen ? openAngle : 0f;
            if (Mathf.Approximately(_angle, target)) return;

            // Heavy hydraulic ease — fast to start, settles softly.
            _angle = Mathf.Lerp(_angle, target, 1f - Mathf.Exp(-Time.deltaTime * Mathf.Max(0.1f, easeSpeed)));
            if (Mathf.Abs(_angle - target) < 0.01f) _angle = target;

            // Hinge axis = local X at the front edge; positive pitch lifts the
            // rear edge up so the lid tips forward over the rotor side.
            transform.localRotation = _closedRotation * Quaternion.Euler(_angle, 0f, 0f);
        }
    }
}
