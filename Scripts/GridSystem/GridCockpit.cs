// Assets/Scripts/VoxelEngine/GridSystem/GridCockpit.cs
//
// Cockpit with terminal + grid size switching.

using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.GridSystem
{
    public class GridCockpit : GridBlock
    {
        [Header("Cockpit")]
        public Player.PlayerController Pilot { get; private set; }

        /// <summary>The cockpit the local player is currently seated in (null if on foot).</summary>
        public static GridCockpit ActivePilotSeat { get; private set; }

        private void Update()
        {
            if (Pilot == null) return;

            if (GameSettings.WasPressed(InputAction.ExitCockpit)) { Exit(); return; }

            // Don't fly while a UI panel (terminal/inventory) is open.
            if (VoxelEngine.UI.UIState.IsBlocking || Grid == null) { Grid?.SetFlightInput(Vector3.zero, 0, 0, 0); return; }

            ReadFlightInput();
        }

        private void ReadFlightInput()
        {
            // Translation — WASD + Space/Ctrl (relative to the cockpit's facing).
            float fwd   = (Held(InputAction.Forward) ? 1 : 0) - (Held(InputAction.Back) ? 1 : 0);
            float right = (Held(InputAction.Right) ? 1 : 0)   - (Held(InputAction.Left) ? 1 : 0);
            float up    = (Held(InputAction.Jump) ? 1 : 0)    - (Held(InputAction.Down) ? 1 : 0);
            Vector3 thrust = new Vector3(right, up, fwd);

            // Rotation — Q/E roll + mouse look (yaw/pitch) via gyroscopes.
            float roll = (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0);

            float mouseX = 0f, mouseY = 0f;
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null) { var d = m.delta.ReadValue(); mouseX = d.x; mouseY = d.y; }
#else
            mouseX = Input.GetAxis("Mouse X"); mouseY = Input.GetAxis("Mouse Y");
#endif
            float sens = 0.06f;
            float yaw   = Mathf.Clamp(mouseX * sens, -1f, 1f);
            float pitch = Mathf.Clamp(-mouseY * sens, -1f, 1f);

            Grid.SetFlightInput(thrust, yaw, pitch, roll);

            // Scroll cycles between fire tools (Drill → Weapon 1 → Weapon 2 → …).
            float scroll;
#if ENABLE_INPUT_SYSTEM
            scroll = m != null ? m.scroll.ReadValue().y : 0f;
#else
            scroll = Input.mouseScrollDelta.y;
#endif
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int n = Grid.GetFireTools().Count;
                if (n > 0) Grid.SelectedToolIndex = ((Grid.SelectedToolIndex + (scroll > 0 ? 1 : -1)) % n + n) % n;
            }
        }

        private static bool Held(InputAction a) => GameSettings.IsHeld(a);

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