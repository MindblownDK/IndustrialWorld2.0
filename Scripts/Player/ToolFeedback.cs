// Assets/Scripts/VoxelEngine/Player/ToolFeedback.cs
//
// Visual feedback for mining/chopping. Camera punch + impact particles + held-tool swing.
// Attach to the camera. PlayerInteractionTool calls Trigger(...) after every successful hit.

using System.Collections;
using UnityEngine;

namespace VoxelEngine.Player
{
    public class ToolFeedback : MonoBehaviour
    {
        [Header("Camera punch")]
        public float punchAmount = 0.06f;     // metres of camera shove
        public float punchAngle  = 4f;         // degrees of pitch kick
        public float recoverTime = 0.18f;

        [Header("Hit particles")]
        public Color particleColor = new Color(0.85f, 0.78f, 0.6f);
        public int   particleCount = 14;
        public float particleLife  = 0.5f;
        public float particleSpeed = 3.5f;

        // Internal swing state
        private Coroutine _running;
        private HeldToolView _heldView;

        public void Trigger(Vector3 hitPoint, Vector3 hitNormal, Color tint)
        {
            // Camera kick
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(KickRoutine());

            // Held viewmodel swing
            if (_heldView == null) _heldView = GetComponentInParent<HeldToolView>();
            _heldView?.DoSwing();

            // Impact particles
            SpawnImpactBurst(hitPoint, hitNormal, tint);
        }

        public void Trigger(Vector3 hitPoint, Vector3 hitNormal) =>
            Trigger(hitPoint, hitNormal, particleColor);

        // ---- Camera kick ----
        private IEnumerator KickRoutine()
        {
            Vector3 baseLocalPos = transform.localPosition;
            Quaternion baseLocalRot = transform.localRotation;

            // Snap to kicked pose
            Vector3 kickOffset    = -transform.forward * punchAmount + transform.up * (punchAmount * 0.5f);
            Quaternion kickRot    = baseLocalRot * Quaternion.Euler(-punchAngle, 0, 0);

            transform.localPosition = baseLocalPos + transform.parent.InverseTransformVector(kickOffset);
            transform.localRotation = kickRot;

            float t = 0f;
            while (t < recoverTime)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0, 1, t / recoverTime);
                transform.localPosition = Vector3.Lerp(transform.localPosition, baseLocalPos, u);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, baseLocalRot, u);
                yield return null;
            }
            transform.localPosition = baseLocalPos;
            transform.localRotation = baseLocalRot;
            _running = null;
        }

        // ---- Particles ----
        private void SpawnImpactBurst(Vector3 pos, Vector3 normal, Color color)
        {
            var go = new GameObject("Impact");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(normal);

            var ps = go.AddComponent<ParticleSystem>();

            // Unity 6 auto-starts the freshly-added system. We MUST stop+clear it before
            // touching `main.duration` or it throws "Setting the duration while system is still playing...".
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration         = particleLife;
            main.loop             = false;
            main.startLifetime    = particleLife;
            main.startSpeed       = particleSpeed;
            main.startSize        = 0.08f;
            main.startColor       = color;
            main.maxParticles     = particleCount;
            main.simulationSpace  = ParticleSystemSimulationSpace.World;
            main.gravityModifier  = 1.5f;

            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)particleCount) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle  = 30f;
            shape.radius = 0.05f;

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            // Simple unlit material; "Sprites/Default" ships with every Unity install.
            var matSh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            rend.material = new Material(matSh);

            ps.Play(true);
            Destroy(go, particleLife + 0.2f);
        }
    }
}
