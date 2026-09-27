// Assets/Scripts/VoxelEngine/GridSystem/Wheels/WheelSlipFx.cs
//
// THE TELL. A wheel that has lost traction must look like it.
//
// The solver already reports exactly how much force the ground refused, so nothing
// here guesses: slip above the surface's own threshold throws a plume tinted by
// that surface (pale dust on sand, dark spray on mud, nothing on ice but a faint
// mist). The emitter is built once, pooled with the hub, and sleeps completely
// while the wheel is gripping — an idle convoy costs one float compare per wheel.

using UnityEngine;
using VoxelEngine.Environment;

namespace VoxelEngine.GridSystem
{
    [RequireComponent(typeof(GridWheel))]
    [DisallowMultipleComponent]
    public class WheelSlipFx : MonoBehaviour
    {
        [Tooltip("Particles emitted per second at full slip.")]
        public float maxEmission = 46f;
        [Tooltip("Slip below this never emits, whatever the surface says.")]
        [Range(0f, 1f)] public float floorThreshold = 0.18f;

        private GridWheel _hub;
        private ParticleSystem _particles;
        private ParticleSystem.EmissionModule _emission;
        private ParticleSystemRenderer _renderer;
        private Material _material;

        private void Awake() => _hub = GetComponent<GridWheel>();

        private void LateUpdate()
        {
            if (_hub == null) return;
            float slip = _hub.IsGrounded ? _hub.WheelSlip : 0f;
            var profile = _hub.CurrentSurfaceProfile;
            float threshold = Mathf.Max(floorThreshold, profile != null ? profile.slipFxThreshold : 0.35f);

            if (slip <= threshold)
            {
                if (_particles != null && _emission.enabled) _emission.enabled = false;
                return;
            }

            EnsureEmitter();
            _particles.transform.position = _hub.GroundPoint;
            _emission.enabled = true;
            _emission.rateOverTime = Mathf.Lerp(0f, maxEmission, Mathf.InverseLerp(threshold, 1f, slip));

            Color tint = profile != null ? profile.plumeColor : new Color(0.62f, 0.54f, 0.40f, 0.55f);
            if (_material != null && _material.color != tint) _material.color = tint;
            var main = _particles.main;
            main.startColor = tint;
        }

        private void EnsureEmitter()
        {
            if (_particles != null) return;

            var go = new GameObject("WheelSlipPlume");
            go.transform.SetParent(transform, false);
            _particles = go.AddComponent<ParticleSystem>();

            var main = _particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 0.85f;
            main.startSpeed = 1.6f;
            main.startSize = Mathf.Max(0.4f, _hub.TireRadius * 0.35f);
            main.gravityModifier = -0.04f;                 // plumes drift, they do not fall
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;

            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = Mathf.Max(0.2f, _hub.TireRadius * 0.25f);

            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.8f));

            var alpha = _particles.colorOverLifetime;
            alpha.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
            alpha.color = new ParticleSystem.MinMaxGradient(gradient);

            _renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _material = new Material(shader) { name = "WheelSlipPlume" };
                _renderer.material = _material;
            }

            _emission = _particles.emission;
            _emission.enabled = false;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
