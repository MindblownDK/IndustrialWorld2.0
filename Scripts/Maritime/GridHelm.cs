// Assets/Scripts/VoxelEngine/Maritime/GridHelm.cs
//
// Helm (Ship's Wheel) — the dedicated maritime control station.
//
//   • Walk up and press E to take the helm.
//   • W = throttle up (gas pedal), release = idle.
//   • A / D = steer left / right.
//   • Mouse-look is free (hold to look around, doesn't turn the ship).
//   • Press E again to release.
//
// While active the Helm drives the parent grid's MaritimePropulsionSystem:
//   system.Throttle  = 0..1
//   system.Steer     = -1..+1
//   system.HelmActive = true
//
// The Helm does NOT parent the player or move the camera — it's a "control
// panel" you stand at, keeping the implementation simple and conflict-free
// with the flight Cockpit. Part 3 will add full cockpit-maritime integration.

using UnityEngine;
using VoxelEngine.GridSystem;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.Maritime
{
    public class GridHelm : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Hull; // passive — no torque role

        [Header("Helm")]
        [Tooltip("How far the player can be and still interact (metres).")]
        public float interactionRadius = 3f;
        [Tooltip("How fast the throttle ramps up/down (per second).")]
        public float throttleRampSpeed = 1.5f;
        [Tooltip("How fast the steering returns to centre when no key is held.")]
        public float steerReturnSpeed = 4f;

        /// <summary>Is a player currently at the helm?</summary>
        public bool IsActive { get; private set; }

        /// <summary>The player currently at the helm (null if inactive).</summary>
        public Player.PlayerController Pilot { get; private set; }

        /// <summary>Current persistent throttle setting 0..1 (survives key release).</summary>
        public float ThrottleSetting { get; private set; }

        private MaritimePropulsionSystem _maritime;
        private Transform _pilotTransform;
        private bool _eWasPressed;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Helm";
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            ReleaseHelm();
        }

        private void Update()
        {
            bool ePressed = EPressedThisFrame;

            if (!IsActive)
            {
                // Look for a nearby player who pressed E.
                if (ePressed && !_eWasPressed)
                {
                    var player = FindNearbyPlayer();
                    if (player != null) TakeHelm(player);
                }
            }
            else
            {
                // Pilot left or disconnected?
                if (Pilot == null || _pilotTransform == null ||
                    Vector3.Distance(_pilotTransform.position, transform.position) > interactionRadius * 1.5f)
                {
                    ReleaseHelm();
                }
                else if (ePressed && !_eWasPressed)
                {
                    ReleaseHelm();
                    return;
                }
                else
                {
                    ReadHelmInput();
                }
            }

            _eWasPressed = ePressed;
        }

        private void ReadHelmInput()
        {
            // ── Throttle (gas-pedal feel) ──────────────────────────────
            bool fwd = GameSettings.IsHeld(InputAction.Forward);
            bool back = GameSettings.IsHeld(InputAction.Back);

            float target = fwd ? 1f : (back ? 0f : ThrottleSetting);
            if (fwd)
                ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 1f, throttleRampSpeed * Time.deltaTime);
            else if (back)
                ThrottleSetting = Mathf.MoveTowards(ThrottleSetting, 0f, throttleRampSpeed * Time.deltaTime);

            // ── Steering ───────────────────────────────────────────────
            bool left = GameSettings.IsHeld(InputAction.Left);
            bool right = GameSettings.IsHeld(InputAction.Right);
            float steerTarget = (right ? 1f : 0f) - (left ? 1f : 0f);
            float steer = Mathf.MoveTowards(_currentSteer, steerTarget, steerReturnSpeed * Time.deltaTime);

            _currentSteer = steer;

            // ── Push to the maritime system ────────────────────────────
            if (_maritime == null && Grid != null) _maritime = Grid.Maritime;
            if (_maritime != null)
            {
                _maritime.Throttle = ThrottleSetting;
                _maritime.Steer = _currentSteer;
                _maritime.HelmActive = true;
            }
        }

        private float _currentSteer;

        // ── Enter / Exit ──────────────────────────────────────────────
        private void TakeHelm(Player.PlayerController player)
        {
            IsActive = true;
            Pilot = player;
            _pilotTransform = player.transform;

            // Disable the player's movement so they "stand at the wheel".
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            // Lock the cursor for steering.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            if (Grid != null) _maritime = Grid.Maritime;
        }

        private void ReleaseHelm()
        {
            if (!IsActive) { IsActive = false; return; }

            // Zero the maritime controls.
            if (_maritime != null)
            {
                _maritime.Throttle = 0f;
                _maritime.Steer = 0f;
                _maritime.HelmActive = false;
            }
            ThrottleSetting = 0f;
            _currentSteer = 0f;

            // Re-enable the player.
            if (Pilot != null)
            {
                Pilot.enabled = true;
                var cc = Pilot.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            IsActive = false;
            Pilot = null;
            _pilotTransform = null;
        }

        // ── Helpers ───────────────────────────────────────────────────
        private Player.PlayerController FindNearbyPlayer()
        {
            // Simple distance check — avoids raycast/interaction-system coupling.
            var players = Object.FindObjectsByType<Player.PlayerController>(FindObjectsInactive.Exclude);
            float bestDist = interactionRadius;
            Player.PlayerController best = null;
            foreach (var p in players)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        private static bool EPressedThisFrame
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.E);
#endif
            }
        }
    }
}
