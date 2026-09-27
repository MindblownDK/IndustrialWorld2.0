// Assets/Scripts/VoxelEngine/Building/Tiered/ClimbableLadder.cs

using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// A climbable volume in front of a ladder.
    ///
    /// The player controller already has one switch that hands movement to
    /// something else — the mount flag it uses for vehicles — so climbing rides
    /// on that rather than threading a second state through the walk update.
    /// While the player is inside the volume and the ladder is deployed, this
    /// drives the CharacterController directly: forward and back climb, the
    /// player stays pinned to the ladder plane, and stepping out or jumping
    /// hands control straight back.
    ///
    /// The release path is deliberately paranoid. A player frozen on a ladder
    /// that got destroyed under them is unrecoverable, so the flag is dropped on
    /// disable, on destroy, and whenever the occupant stops being reachable.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ClimbableLadder : MonoBehaviour
    {
        [Min(0.5f)] public float climbSpeed = 3.2f;
        [Min(0.1f)] public float pinStrength = 6f;
        /// <summary>Set by the owning hatch; a furled ladder is not climbable.</summary>
        public bool deployed = true;

        private PlayerController _rider;
        private CharacterController _riderBody;

        private void Reset()
        {
            var box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.2f, 4f, 0.9f);
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void OnTriggerExit(Collider other)
        {
            if (_rider != null && other.transform.root == _rider.transform.root) Release();
        }

        private void OnTriggerStay(Collider other)
        {
            if (!deployed) { Release(); return; }
            if (_rider == null)
            {
                var candidate = other.GetComponentInParent<PlayerController>();
                if (candidate == null) return;
                Engage(candidate);
            }
        }

        private void Engage(PlayerController player)
        {
            _rider = player;
            _riderBody = player.GetComponent<CharacterController>();
            _rider.IsMounted = true;
        }

        public void Release()
        {
            if (_rider != null) _rider.IsMounted = false;
            _rider = null;
            _riderBody = null;
        }

        private void LateUpdate()
        {
            if (_rider == null) return;
            if (!deployed || _riderBody == null || !_rider.isActiveAndEnabled) { Release(); return; }

            // Jumping off is the universal escape, and it must work even if the
            // trigger exit never fires because the player clipped through it.
            if (JumpPressed()) { Release(); return; }

            float axis = ClimbAxis();
            // The ladder's own up, not the world's: this game has planets, and a
            // hatch on the far side of one still has to be climbable.
            Vector3 up = transform.up;
            Vector3 motion = up * (axis * climbSpeed * Time.deltaTime);

            // Pin the climber to the ladder plane so they cannot drift off sideways.
            Vector3 toPlane = Vector3.ProjectOnPlane(transform.position - _rider.transform.position, up);
            Vector3 normal = transform.forward;
            Vector3 offNormal = Vector3.Project(toPlane, normal);
            motion += offNormal * Mathf.Clamp01(pinStrength * Time.deltaTime);

            _riderBody.Move(motion);
        }

        private static float ClimbAxis()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return 0f;
            float v = 0f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;
            return v;
#else
            return Input.GetAxisRaw("Vertical");
#endif
        }

        private static bool JumpPressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }
    }
}
