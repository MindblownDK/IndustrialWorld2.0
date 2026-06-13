// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Cockpit with terminal + grid size switching.

using UnityEngine;
using VoxelEngine.Settings;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        [Tooltip("Idle power draw while the cockpit is powered on.")]
        public float idleWatts = 50f;

        public override float PowerDraw => Enabled ? idleWatts : 0f;

        public Player.PlayerController Pilot { get; private set; }

        public bool IsFlightOnline => Enabled && Grid != null && Grid.HasPower;

        /// <summary>The cockpit the local player is currently seated in (null if on foot).</summary>
        public static GridCockpit ActivePilotSeat { get; private set; }

        private void Update()
        {
            if (Pilot == null) return;

            if (GameSettings.WasPressed(InputAction.ExitCockpit)) { Exit(); return; }

            // While a full UI panel (terminal/inventory) is open, release control + cursor.
            if (VoxelEngine.UI.UIState.IsBlocking || Grid == null)
            {
                Grid?.SetFlightInput(Vector3.zero, 0, 0, 0);
                return;
            }

            // Offline cockpit or unpowered grid: player can still exit/open terminal,
            // but flight/gyro/tool controls are fully locked out.
            if (!IsFlightOnline)
            {
                Grid.SetFlightInput(Vector3.zero, 0, 0, 0);
                Grid.DrillVoidMode = false;
                return;
            }

            // No blocking UI → we're actively flying: lock the cursor so mouse-look works
            // (the player controller that normally does this is disabled while seated).
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Z toggles inertia dampeners (auto-brake to a stop when not thrusting).
            if (GridInput.ZPressed) Grid.DampenersOn = !Grid.DampenersOn;

            // P toggles ALL landing gear on the grid (lock ⇆ unlock).
            if (GridInput.PPressed) ToggleAllLandingGear();

            ReadFlightInput();
        }

        private void ReadFlightInput()
        {
            // Translation — WASD + Space/Ctrl (relative to the cockpit's facing).
            float fwd   = (Held(InputAction.Forward) ? 1 : 0) - (Held(InputAction.Back) ? 1 : 0);
            float right = (Held(InputAction.Right) ? 1 : 0)   - (Held(InputAction.Left) ? 1 : 0);
            bool descendHeld = Held(InputAction.Down)
                               || (PilotHoldingGridBlock() && (Held(InputAction.Crouch) || CKeyHeld()));
            float up    = (Held(InputAction.Jump) ? 1 : 0)    - (descendHeld ? 1 : 0);
            Vector3 thrust = new Vector3(right, up, fwd);

            // Rotation — Q/E roll + mouse look (yaw/pitch) via gyroscopes.
            // Roll is a steady digital key (full 1.0) while yaw/pitch come from small mouse
            // deltas, so raw roll felt FAR too aggressive. Scale it down to match the feel
            // of mouse turning (tunable: lower = gentler roll).
            const float ROLL_SENS = 0.35f;
            float roll = ((GridInput.Q ? 1 : 0) - (GridInput.E ? 1 : 0)) * ROLL_SENS;

            Vector2 md = GridInput.MouseDelta;
            float mouseX = md.x, mouseY = md.y;

            if (GridInput.Alt)
            {
                // FREE-LOOK: hold Alt to look around the cockpit without turning the ship.
                FreeLook(mouseX, mouseY);
                Grid.SetFlightInput(thrust, 0f, 0f, roll); // no gyro turn while free-looking
                return;
            }
            ResetFreeLook();

            float sens = 0.06f;
            float yaw   = Mathf.Clamp(mouseX * sens, -1f, 1f);
            float pitch = Mathf.Clamp(-mouseY * sens, -1f, 1f);

            Grid.SetFlightInput(thrust, yaw, pitch, roll);

            // Scroll cycles between TOOL GROUPS (Drill ⇄ Weapon).
            float scroll = GridInput.Scroll;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int n = Grid.ToolGroupCount;
                if (n > 0) Grid.SelectedToolIndex = ((Grid.SelectedToolIndex + (scroll > 0 ? 1 : -1)) % n + n) % n;
            }

            // While the Drill group is selected: LMB = mine + collect, RMB = mine + VOID (faster).
            Grid.DrillVoidMode = GridInput.Mouse1 && !GridInput.Mouse0;
        }

        private bool PilotHoldingGridBlock()
        {
            if (GridBuilder.HoldingGridBlock) return true;
            if (Pilot == null) return false;

            var inventory = Pilot.GetComponentInParent<VoxelEngine.Items.Inventory>();
            if (inventory == null) return false;

            var stack = inventory.ActiveStack;
            return stack != null && !stack.IsEmpty && stack.item is GridBlockItem;
        }

        private static bool CKeyHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.cKey.isPressed;
#else
            return Input.GetKey(KeyCode.C);
#endif
        }

        private static bool Held(InputAction a) => GameSettings.IsHeld(a);

        /// <summary>P key — if ANY landing gear is locked, unlock them all; otherwise lock them all.</summary>
        private void ToggleAllLandingGear()
        {
            if (Grid == null) return;
            bool anyLocked = false;
            foreach (var kv in Grid.Blocks)
                if (kv.Value is GridLandingGear lg && lg.IsLocked) { anyLocked = true; break; }

            foreach (var kv in Grid.Blocks)
            {
                if (!(kv.Value is GridLandingGear lg)) continue;
                if (anyLocked) lg.Unlock();
                else           lg.TryLock();
            }
        }

        // ── Free-look (hold Alt) ───────────────────────────────────────────────
        private float _lookYaw, _lookPitch;
        private bool  _freeLooking;

        private void FreeLook(float mouseX, float mouseY)
        {
            var pivot = Pilot != null ? Pilot.cameraPivot : null;
            if (pivot == null) return;

            float sens = 2.0f;
            _lookYaw   = Mathf.Clamp(_lookYaw   + mouseX * sens, -120f, 120f);
            _lookPitch = Mathf.Clamp(_lookPitch - mouseY * sens, -80f, 80f);
            // Offset the camera relative to the cockpit's facing (player is parented to it).
            pivot.localRotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
            _freeLooking = true;
        }

        private void ResetFreeLook()
        {
            if (!_freeLooking) return;
            _freeLooking = false;
            _lookYaw = 0f; _lookPitch = 0f;
            var pivot = Pilot != null ? Pilot.cameraPivot : null;
            if (pivot != null) pivot.localRotation = Quaternion.identity;
        }

        public void Enter(Player.PlayerController player)
        {
            if (Pilot != null) return;

            Pilot = player;
            player.enabled = false;
            // The player uses a CharacterController (not a Rigidbody) — disable it
            // so the seated player doesn't collide while piloting. Guard both so a
            // different player rig never throws.
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            // Parent the player to the cockpit so they ride along with the grid as
            // it moves/rolls/rotates (fixes "ship rolls away, player stays put").
            _originalParent = player.transform.parent;
            player.transform.SetParent(transform, worldPositionStays: true);
            player.transform.position = transform.position;
            player.transform.localRotation = Quaternion.identity;

            // Take control of the grid for flight + mark this as the active seat so
            // the "I" key opens the master terminal instead of the player inventory.
            if (Grid != null) Grid.ActiveCockpit = this;
            ActivePilotSeat = this;

            // Rebuild the HUD now so the on-foot hotbar is hidden immediately on entry
            // (BuildHotbar skips while ActivePilotSeat != null) — the ship toolbar replaces it.
            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
        }

        private Transform _originalParent;

        /// <summary>Open the Space-Engineers-style master terminal for this grid.</summary>
        public void OpenTerminal()
        {
            VoxelEngine.UI.GameUIController.Instance?.OpenGridTerminal(Grid);
        }

        public void Exit()
        {
            if (Pilot == null) return;

            // Unparent the player from the grid and drop them beside the cockpit.
            Pilot.transform.SetParent(_originalParent, worldPositionStays: true);
            Pilot.transform.position = transform.position + transform.up * 1.2f + transform.right * 1.5f;

            Pilot.enabled = true;
            var cc = Pilot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            var rb = Pilot.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;
            Pilot = null;
            if (Grid != null && Grid.ActiveCockpit == this) Grid.ActiveCockpit = null;
            if (ActivePilotSeat == this) ActivePilotSeat = null;
            VoxelEngine.UI.GameUIController.Instance?.CloseAll();
        }

        public void SwitchToSmallGrid()
        {
            GridSizeSwitcher.Instance?.SwitchGrid(Grid, GridSize.Small);
        }

        public void SwitchToLargeGrid()
        {
            GridSizeSwitcher.Instance?.SwitchGrid(Grid, GridSize.Large);
        }
    }
}