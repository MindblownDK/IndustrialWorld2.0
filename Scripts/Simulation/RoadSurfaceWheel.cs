using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Hold-to-choose the paving surface, built to the same interaction contract as
    /// <see cref="ConveyorShapeWheel"/>: hold the build-wheel key while the paver is in hand, the
    /// ring opens, move to hover a segment, release to commit. Two surfaces only, so the ring is a
    /// left/right pair rather than a full circle — asphalt road for vehicles, stone pathway for
    /// feet around the base.
    ///
    /// The choice lives in <see cref="RoadSurfaceSelection"/> rather than on the tool, so it
    /// survives switching hotbar slots exactly the way the conveyor shape does.
    /// </summary>
    public sealed class RoadSurfaceWheel : MonoBehaviour
    {
        private struct Segment
        {
            public RoadSurfaceKind Kind;
            public string Title;
            public string Blurb;
            public string IconText;
        }

        private static readonly Segment[] Segments =
        {
            new Segment
            {
                Kind = RoadSurfaceKind.Asphalt,
                Title = "ASPHALT ROAD",
                Blurb = "Hot mix. Carries vehicles, wears under wheels.",
                IconText = "\u25AC",
            },
            new Segment
            {
                Kind = RoadSurfaceKind.Pathway,
                Title = "STONE PATHWAY",
                Blurb = "Cobble. For feet only — no traction, costs stone.",
                IconText = "\u25A6",
            },
        };

        private Inventory _inventory;
        private VisualElement _uiRoot;
        private VisualElement _prompt;
        private Label _promptLabel;
        private VisualElement _overlay;
        private readonly VisualElement[] _cards = new VisualElement[2];
        private bool _open;
        private bool _wasBlocking;
        private int _hovered = -1;
        private static int _openCount;

        public static RoadSurfaceKind Selected => RoadSurfaceSelection.Kind;

        /// <summary>True while any surface wheel holds the input block. The paver swallows its own
        /// tick while this is set, so the click that releases the wheel cannot also commit a plan.</summary>
        public static bool IsAnyOpen => _openCount > 0;

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            if (!HoldingPaver())
            {
                if (_open) Close(commit: false);
                HidePrompt();
                return;
            }

            bool held = GameSettings.IsHeld(InputAction.BuildWheel);
            if (!_open && !UIState.IsBlocking)
            {
                ShowPrompt();
                if (held) Open();
            }
            else if (_open)
            {
                HidePrompt();
                if (!held) Close(commit: true);
                else PollHover();
            }
            else HidePrompt();
        }

        private void OnDisable() { if (_open) Close(commit: false); RemoveUi(); }
        private void OnDestroy() { if (_open) Close(commit: false); RemoveUi(); }

        private bool HoldingPaver()
        {
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            return !stack.IsEmpty && stack.item is RoadPaverTool;
        }

        // ── UI ───────────────────────────────────────────────────────────

        private bool EnsureUiRoot()
        {
            if (_uiRoot != null && _uiRoot.panel != null) return true;
            var document = FindAnyObjectByType<UIDocument>();
            if (document == null || document.rootVisualElement == null) return false;
            _uiRoot = document.rootVisualElement;
            return true;
        }

        private void ShowPrompt()
        {
            if (!EnsureUiRoot()) return;
            if (_prompt == null || _prompt.parent == null)
            {
                _prompt = new VisualElement { name = "RoadSurfacePrompt" };
                _prompt.style.position = Position.Absolute;
                _prompt.style.left = Length.Percent(50f);
                _prompt.style.top = Length.Percent(62f);
                _prompt.style.translate = new Translate(Length.Percent(-50f), 0f);
                _prompt.style.paddingLeft = 12f; _prompt.style.paddingRight = 12f;
                _prompt.style.paddingTop = 5f; _prompt.style.paddingBottom = 5f;
                _prompt.style.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 0.82f);
                _prompt.style.borderTopLeftRadius = 8f; _prompt.style.borderTopRightRadius = 8f;
                _prompt.style.borderBottomLeftRadius = 8f; _prompt.style.borderBottomRightRadius = 8f;
                _prompt.style.borderTopWidth = 1f; _prompt.style.borderBottomWidth = 1f;
                _prompt.style.borderLeftWidth = 1f; _prompt.style.borderRightWidth = 1f;
                _prompt.style.borderTopColor = UITheme.AccentCyan;
                _prompt.style.borderBottomColor = UITheme.AccentCyan;
                _prompt.style.borderLeftColor = UITheme.AccentCyan;
                _prompt.style.borderRightColor = UITheme.AccentCyan;
                _prompt.pickingMode = PickingMode.Ignore;

                _promptLabel = new Label();
                _promptLabel.style.color = Color.white;
                _promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _promptLabel.style.fontSize = 12f;
                _promptLabel.style.whiteSpace = WhiteSpace.NoWrap;
                _prompt.Add(_promptLabel);
                _uiRoot.Add(_prompt);
            }
            _promptLabel.text = "[" + GameSettings.GetKey(InputAction.BuildWheel) + "]  ROAD SURFACE  \u00b7  "
                                + Segments[(int)Selected].Title;
            _prompt.style.display = DisplayStyle.Flex;
        }

        private void HidePrompt()
        {
            if (_prompt != null) _prompt.style.display = DisplayStyle.None;
        }

        private void Open()
        {
            if (!EnsureUiRoot()) return;
            _open = true;
            _openCount++;
            _wasBlocking = true;
            // Without this the cursor stays locked and hidden at screen centre while playing, which
            // puts the half-screen hover test exactly ON the left/right boundary and makes the two
            // cards flash against each other every frame. PushBlock frees the cursor the same way
            // the conveyor shape wheel frees it, and re-locks it when the last block pops.
            VoxelEngine.UI.UIState.PushBlock();
            _hovered = (int)Selected;
            BuildOverlay();
        }

        private void Close(bool commit)
        {
            if (_open) _openCount = System.Math.Max(0, _openCount - 1);
            _open = false;
            if (_wasBlocking) { _wasBlocking = false; VoxelEngine.UI.UIState.PopBlock(); }
            if (commit && _hovered >= 0 && _hovered < Segments.Length)
            {
                var seg = Segments[_hovered];
                RoadSurfaceSelection.Kind = seg.Kind;
                BuildFeedbackHud.Show("Road Surface", seg.Title, null, UITheme.AccentCyan);
            }
            if (_overlay != null && _overlay.parent != null) _overlay.RemoveFromHierarchy();
            _overlay = null;
        }

        private void RemoveUi()
        {
            if (_prompt != null && _prompt.parent != null) _prompt.RemoveFromHierarchy();
            _prompt = null;
            if (_overlay != null && _overlay.parent != null) _overlay.RemoveFromHierarchy();
            _overlay = null;
        }

        private void BuildOverlay()
        {
            if (_overlay != null && _overlay.parent != null) _overlay.RemoveFromHierarchy();

            _overlay = new VisualElement { name = "RoadSurfaceWheel" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0f; _overlay.style.right = 0f;
            _overlay.style.top = 0f; _overlay.style.bottom = 0f;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.flexDirection = FlexDirection.Row;
            _overlay.pickingMode = PickingMode.Ignore;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < Segments.Length; i++)
            {
                var card = BuildCard(i);
                _cards[i] = card;
                row.Add(card);
            }

            _overlay.Add(row);
            _uiRoot.Add(_overlay);
            RefreshCards();
        }

        private VisualElement BuildCard(int index)
        {
            var seg = Segments[index];
            var card = new VisualElement();
            card.style.width = 230f;
            card.style.marginLeft = 14f; card.style.marginRight = 14f;
            card.style.paddingLeft = 16f; card.style.paddingRight = 16f;
            card.style.paddingTop = 14f; card.style.paddingBottom = 14f;
            card.style.alignItems = Align.Center;
            card.style.borderTopLeftRadius = 12f; card.style.borderTopRightRadius = 12f;
            card.style.borderBottomLeftRadius = 12f; card.style.borderBottomRightRadius = 12f;
            card.style.borderTopWidth = 2f; card.style.borderBottomWidth = 2f;
            card.style.borderLeftWidth = 2f; card.style.borderRightWidth = 2f;
            card.pickingMode = PickingMode.Ignore;

            var icon = new Label(seg.IconText);
            icon.style.fontSize = 34f;
            icon.style.color = Color.white;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(icon);

            var title = new Label(seg.Title);
            title.style.fontSize = 15f;
            title.style.color = Color.white;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 6f;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(title);

            var blurb = new Label(seg.Blurb);
            blurb.style.fontSize = 11f;
            blurb.style.color = new Color(0.75f, 0.80f, 0.85f, 1f);
            blurb.style.marginTop = 4f;
            blurb.style.unityTextAlign = TextAnchor.MiddleCenter;
            blurb.style.whiteSpace = WhiteSpace.Normal;
            card.Add(blurb);

            return card;
        }

        private void RefreshCards()
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                if (card == null) continue;
                bool on = i == _hovered;
                card.style.backgroundColor = on
                    ? new Color(0.10f, 0.20f, 0.26f, 0.95f)
                    : new Color(0.05f, 0.07f, 0.09f, 0.85f);
                var c = on ? UITheme.AccentCyan : new Color(0.30f, 0.36f, 0.42f, 0.9f);
                card.style.borderTopColor = c; card.style.borderBottomColor = c;
                card.style.borderLeftColor = c; card.style.borderRightColor = c;
                card.style.scale = on ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one;
            }
        }

        /// <summary>Two segments, so the hover is simply which half of the screen the mouse is on.
        /// No per-element hit testing: the overlay is picking-mode Ignore so it never eats the
        /// click that commits the plan underneath it.</summary>
        private void PollHover()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            float mx = mouse != null ? mouse.position.ReadValue().x : Screen.width * 0.5f;
#else
            float mx = Input.mousePosition.x;
#endif
            int wanted = mx < Screen.width * 0.5f ? 0 : 1;
            if (wanted != _hovered) { _hovered = wanted; RefreshCards(); }
        }
    }
}
