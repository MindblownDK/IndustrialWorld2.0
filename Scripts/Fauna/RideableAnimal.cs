// Assets/Scripts/VoxelEngine/Fauna/RideableAnimal.cs
//
// A mountable creature (Phase 3d). Extends PassiveAnimal: a riderless horse grazes
// and wanders like any livestock, but a player can hop on and steer it directly
// (WASD + Shift to gallop + Space to jump). The mount/dismount contract mirrors
// GridCockpit.Enter/Exit — the player's CharacterController is disabled and the
// rider is parented to the horse so they ride along; the PlayerController keeps
// running mouse-look + camera while its own locomotion is suspended via IsMounted.
// Radial-gravity aligned so riding works anywhere on a spherical world.

using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Fauna
{
    [RequireComponent(typeof(Rigidbody))]
    public class RideableAnimal : PassiveAnimal
    {
        [Header("Riding")]
        public float rideSpeed           = 9f;
        public float rideSprintMultiplier = 1.7f;
        public float rideAccel           = 14f;
        public float turnSmooth          = 8f;
        public float rideJumpHeight      = 1.6f;
        [Tooltip("Local position the rider is anchored to while mounted.")]
        public Vector3 seatLocalPos      = new Vector3(0f, 1.65f, 0.05f);

        public VoxelEngine.Player.PlayerController Rider { get; private set; }

        private Transform _originalParent;
        private Collider[] _selfColliders;
        private bool _rideGrounded;

        protected override void Awake()
        {
            base.Awake();
            species = AnimalSpecies.Horse;
            wanderSpeed = 1.5f;   // horses amble a little quicker while grazing
            _selfColliders = GetComponentsInChildren<Collider>();
        }

        // ── Mount (mirrors GridCockpit.Enter) ─────────────────────
        public void Enter(VoxelEngine.Player.PlayerController player)
        {
            if (Rider != null || player == null || player.IsMounted) return;

            Rider = player;

            // Suspend the player's own locomotion but keep mouse-look + camera. Disable the
            // CharacterController so the carried rider doesn't collide with the terrain.
            player.ResetVelocity();
            player.IsMounted = true;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // Parent the rider to the horse so they move with it.
            _originalParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            player.transform.localPosition = seatLocalPos;
            player.transform.localRotation = Quaternion.identity;

            VoxelEngine.UI.BuildFeedbackHud.Show("Mounted",
                "WASD ride   Shift gallop   Space jump   F dismount", null, new Color(0.50f, 0.85f, 1f));
        }

        // ── Dismount (mirrors GridCockpit.Exit) ───────────────────
        public void Exit()
        {
            if (Rider == null) return;
            var player = Rider;

            player.transform.SetParent(_originalParent, worldPositionStays: true);
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            player.transform.position = transform.position + up * 1.4f + transform.right * 1.3f;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            player.ResetVelocity();
            player.IsMounted = false;
            Rider = null;
        }

        private void Update()
        {
            if (Rider == null) return;
            if (GameSettings.WasPressed(InputAction.ExitCockpit)) Exit();
        }

        protected override void FixedUpdate()
        {
            if (Rider == null) { base.FixedUpdate(); return; }   // graze/wander when riderless
            MountedFixedUpdate();
        }

        private void MountedFixedUpdate()
        {
            if (VoxelEngine.UI.UIState.IsBlocking) return;       // pause while a menu is open
            float dt = Time.fixedDeltaTime;
            Vector3 pos = transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(pos);
            Vector3 grav = VoxelEngine.Cosmos.GravityProvider.GetGravity(pos);

            // Steering relative to where the rider is looking.
            Vector2 wish = GetMoveInput();
            Vector3 wishDir = Rider.transform.right * wish.x + Rider.transform.forward * wish.y;
            wishDir = Vector3.ProjectOnPlane(wishDir, up);
            if (wishDir.sqrMagnitude > 0.001f) wishDir = wishDir.normalized;

            bool sprint = GameSettings.IsHeld(InputAction.Sprint);
            float spd = wish.sqrMagnitude > 0.001f ? rideSpeed * (sprint ? rideSprintMultiplier : 1f) : 0f;

            _rideGrounded = RideGroundCheck(pos, up);

            Vector3 v = _rb.linearVelocity;
            Vector3 radial = Vector3.Project(v, up);
            Vector3 tangent = v - radial;
            tangent = Vector3.MoveTowards(tangent, wishDir * spd, rideAccel * dt);

            // Jump (radial-up impulse) when grounded.
            if (GameSettings.IsHeld(InputAction.Jump) && _rideGrounded)
            {
                float gravMag = Mathf.Max(0.1f, grav.magnitude);
                radial = up * Mathf.Sqrt(2f * gravMag * rideJumpHeight);
                _rideGrounded = false;
            }
            else
            {
                radial += grav * dt;
            }
            _rb.linearVelocity = tangent + radial;

            // Face the travel direction.
            if (spd > 0.01f && wishDir != Vector3.zero)
            {
                Quaternion look = Quaternion.LookRotation(wishDir, up);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, look, turnSmooth * dt));
            }
        }

        // Radial-down ground probe that ignores the horse's own colliders.
        private bool RideGroundCheck(Vector3 pos, Vector3 up)
        {
            var hits = UnityEngine.Physics.RaycastAll(pos + up * 0.3f, -up, 1.6f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (System.Array.IndexOf(_selfColliders, hits[i].collider) >= 0) continue;
                return true;
            }
            return false;
        }

        private static Vector2 GetMoveInput()
        {
            float x = (GameSettings.IsHeld(InputAction.Right)  ? 1 : 0) - (GameSettings.IsHeld(InputAction.Left)  ? 1 : 0);
            float y = (GameSettings.IsHeld(InputAction.Forward) ? 1 : 0) - (GameSettings.IsHeld(InputAction.Back) ? 1 : 0);
            return new Vector2(x, y);
        }
    }
}
