// Assets/Scripts/VoxelEngine/Simulation/RoadSurfaceWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — ROAD SURFACE WHEEL                 ║
// ║                                                                  ║
// ║  What the paver lays next, on the shared radial dial.            ║
// ║                                                                  ║
// ║  The drawbridge card is not a third paving material: the road    ║
// ║  laid is still asphalt. What it selects is what the paver does   ║
// ║  when that road reaches water — an ordinary crossing is a fixed  ║
// ║  culvert forever, this one opens for shipping. It belongs on     ║
// ║  this wheel because it answers the same question the other two   ║
// ║  cards answer: what kind of road am I laying.                    ║
// ║                                                                  ║
// ║  The choice lives in RoadSurfaceSelection rather than on the     ║
// ║  tool, so it survives switching hotbar slots.                    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Simulation
{
    public sealed class RoadSurfaceWheel : MonoBehaviour
    {
        private struct Segment
        {
            public RoadSurfaceKind Kind;
            public string Title;
            public string Blurb;
        }

        private static readonly Segment[] Segments =
        {
            new() { Kind = RoadSurfaceKind.Asphalt, Title = "ASPHALT ROAD",
                    Blurb = "Hot mix. Carries vehicles, wears under wheels." },
            new() { Kind = RoadSurfaceKind.Pathway, Title = "STONE PATHWAY",
                    Blurb = "Cobble. For feet only — no traction, costs stone." },
            new() { Kind = RoadSurfaceKind.Bridge,  Title = "DRAWBRIDGE ROAD",
                    Blurb = "Asphalt, but water crossings open for shipping. Costs iron and stone." },
        };

        private static int _openCount;

        public static RoadSurfaceKind Selected => RoadSurfaceSelection.Kind;

        /// <summary>
        /// True while any surface wheel holds the input block. The paver swallows its
        /// own tick while this is set, so the click that releases the wheel cannot
        /// also commit a plan.
        /// </summary>
        public static bool IsAnyOpen => _openCount > 0;

        private readonly RadialWheelController _wheel = new();
        private Inventory _inventory;
        private VisualElement _host;
        private bool _countedOpen;

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.GroupLabel = "ROAD SURFACE";
            _wheel.IdleTitle = "KEEP CURRENT";
            _wheel.IdleDescription = "Release in the centre to leave the surface as it is";
            _wheel.IdleIcon = MachineShapeIcons.Cancel;
        }

        private void OnDisable() { ReleaseCount(); _wheel.Dispose(); }
        private void OnDestroy() { ReleaseCount(); _wheel.Dispose(); }

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            bool holding = HoldingPaver();

            _wheel.Tick(InputAction.BuildWheel, holding);

            // Mirror the dial's own state into the static counter the paver reads.
            if (_wheel.IsOpen && !_countedOpen) { _countedOpen = true; _openCount++; }
            else if (!_wheel.IsOpen && _countedOpen) ReleaseCount();

            if (holding && !_wheel.IsOpen && !UIState.IsBlocking)
                _wheel.ShowPrompt($"[{GameSettings.GetKey(InputAction.BuildWheel)}]  ROAD SURFACE  ·  " +
                                  TitleOf(RoadSurfaceSelection.Kind));
            else
                _wheel.HidePrompt();
        }

        private void ReleaseCount()
        {
            if (!_countedOpen) return;
            _countedOpen = false;
            _openCount = Mathf.Max(0, _openCount - 1);
        }

        private static string TitleOf(RoadSurfaceKind kind)
        {
            for (int i = 0; i < Segments.Length; i++)
                if (Segments[i].Kind == kind) return Segments[i].Title;
            return kind.ToString().ToUpperInvariant();
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
            for (int i = 0; i < Segments.Length; i++)
            {
                options.Add(new RadialOption
                {
                    Title = Segments[i].Title,
                    Description = Segments[i].Blurb,
                    Icon = MachineShapeIcons.RoadSurface(Segments[i].Kind.ToString()),
                    Available = true,
                    Affordable = true,
                    Selected = Segments[i].Kind == RoadSurfaceSelection.Kind
                });
            }
        }

        private bool Confirm(int index)
        {
            if (index < 0 || index >= Segments.Length) return true;
            RoadSurfaceSelection.Kind = Segments[index].Kind;
            BuildFeedbackHud.Show("Road Surface", Segments[index].Title, null, UITheme.AccentCyan);
            return true;
        }

        private bool HoldingPaver()
        {
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            return stack != null && !stack.IsEmpty && stack.item is RoadPaverTool;
        }
    }
}
