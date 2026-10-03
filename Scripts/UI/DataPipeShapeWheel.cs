// Assets/Scripts/UI/DataPipeShapeWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — DATA PIPE SHAPE WHEEL             ║
// ║                                                                  ║
// ║  14.41.0 - The data pipe borrows the energy pipe's nine conduit ║
// ║  fittings on the shared radial dial, with its OWN selection     ║
// ║  state so swapping between the two pipe items forgets nothing.  ║
// ║                                                                  ║
// ║  Importer, Exporter and External Storage stay separate blocks   ║
// ║  that snap onto open plug heads - the wheel is shapes only.     ║
// ║                                                                  ║
// ║  Owns the V + Scroll length adjustment for the straight run.    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Networks;
using VoxelEngine.Power;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    public sealed class DataPipeShapeWheel : MonoBehaviour
    {
        private struct VariantDescriptor
        {
            public EnergyPipeVariant Variant;
            public string Title;
            public string Blurb;
        }

        private static readonly VariantDescriptor[] Descriptors =
        {
            new() { Variant = EnergyPipeVariant.Straight,      Title = "STRAIGHT",      Blurb = "Straight data trunk. Hold V and scroll to set 1-5 m." },
            new() { Variant = EnergyPipeVariant.BendRight,     Title = "90 ELBOW",      Blurb = "Ninety-degree horizontal turn to the right." },
            new() { Variant = EnergyPipeVariant.BendUp,        Title = "90 RISER",      Blurb = "Ninety-degree vertical bend going upward." },
            new() { Variant = EnergyPipeVariant.StepUp,        Title = "VERT STEP",     Blurb = "S-curve rising one metre and continuing forward." },
            new() { Variant = EnergyPipeVariant.StepRight,     Title = "S-CURVE",       Blurb = "Horizontal S-curve offsetting one metre to the right." },
            new() { Variant = EnergyPipeVariant.BendLeftToUp,  Title = "LEFT TO UP",    Blurb = "Compound curve entering from the left and exiting up." },
            new() { Variant = EnergyPipeVariant.BendRightToUp, Title = "RIGHT TO UP",   Blurb = "Compound curve entering from the right and exiting up." },
            new() { Variant = EnergyPipeVariant.Junction4Way,  Title = "4-WAY CROSS",   Blurb = "Planar four-way network hub with plug heads." },
            new() { Variant = EnergyPipeVariant.Junction6Way,  Title = "6-WAY HUB",     Blurb = "Omni-directional three-dimensional six-way hub." }
        };

        private readonly RadialWheelController _wheel = new();
        private Inventory _inventory;
        private VisualElement _host;

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.GroupLabel = "DATA PIPE";
            _wheel.IdleTitle = "KEEP CURRENT";
            _wheel.IdleDescription = "Release in the centre to leave the fitting as it is";
            _wheel.IdleIcon = MachineShapeIcons.Cancel;
        }

        private void Start()
        {
            _inventory = GetComponentInParent<Inventory>();
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
        }

        private void OnDisable() => _wheel.Dispose();
        private void OnDestroy() => _wheel.Dispose();

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            bool holding = TryGetHeldDataPipe(out _, out _);

            if (holding && !_wheel.IsOpen) HandleLengthAdjust();

            _wheel.Tick(InputAction.BuildWheel, holding);

            if (holding && !_wheel.IsOpen && !UIState.IsBlocking)
            {
                string variantName = DataPipeSelection.GetVariantDisplayName(DataPipeSelection.Variant);
                string vHint = DataPipeSelection.Variant == EnergyPipeVariant.Straight
                    ? "  ·  HOLD V + SCROLL TO RESIZE" : string.Empty;
                _wheel.ShowPrompt($"[{GameSettings.GetKey(InputAction.BuildWheel)}]  DATA PIPES  ·  {variantName}{vHint}");
            }
            else _wheel.HidePrompt();
        }

        /// <summary>V plus scroll resizes the straight run. Ctrl is reserved for rotation.</summary>
        private void HandleLengthAdjust()
        {
            if (DataPipeSelection.Variant != EnergyPipeVariant.Straight) return;
            float scroll = GridInput.Scroll;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            bool vHeld = UnityEngine.InputSystem.Keyboard.current != null
                         && UnityEngine.InputSystem.Keyboard.current.vKey.isPressed;
#else
            bool vHeld = Input.GetKey(KeyCode.V);
#endif
            if (!vHeld || Mathf.Abs(scroll) <= 0.01f) return;

            int oldLen = DataPipeSelection.StraightLength;
            DataPipeSelection.AdjustLength(scroll > 0 ? 1 : -1);
            if (DataPipeSelection.StraightLength != oldLen)
                BuildFeedbackHud.Show("Pipe Length",
                    $"{DataPipeSelection.StraightLength} m (Hold V + Scroll)", null, UITheme.AccentGreen);
        }

        private VisualElement ResolveHost()
        {
            if (_host != null && _host.panel != null) return _host;
            var controller = GameUIController.Instance;
            var document = controller != null ? controller.GetComponent<UIDocument>() : FindAnyObjectByType<UIDocument>();
            _host = document != null ? document.rootVisualElement : null;
            return _host;
        }

        private void FillOptions(List<RadialOption> options)
        {
            for (int i = 0; i < Descriptors.Length; i++)
            {
                var d = Descriptors[i];
                options.Add(new RadialOption
                {
                    Title = d.Title,
                    Description = d.Blurb,
                    Icon = MachineShapeIcons.Pipe(d.Variant.ToString()),
                    Available = true,
                    Affordable = true,
                    Selected = d.Variant == DataPipeSelection.Variant
                });
            }
        }

        private bool Confirm(int index)
        {
            if (index < 0 || index >= Descriptors.Length) return true;
            DataPipeSelection.Variant = Descriptors[index].Variant;
            BuildFeedbackHud.Show("Data Pipe Shape",
                DataPipeSelection.GetVariantDisplayName(DataPipeSelection.Variant), null, UITheme.AccentGreen);
            return true;
        }

        private bool TryGetHeldDataPipe(out BlockItem block, out DataCable pipe)
        {
            block = null;
            pipe = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is BlockItem b) || b.placedPrefab == null)
                return false;

            pipe = b.placedPrefab.GetComponentInChildren<DataCable>(true);
            if (pipe == null) return false;
            block = b;
            return true;
        }
    }
}
