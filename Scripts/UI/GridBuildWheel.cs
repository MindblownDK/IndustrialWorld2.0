// Assets/Scripts/VoxelEngine/UI/GridBuildWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║               INDUSTRIAL WORLD — GRID BUILD WHEEL                 ║
// ║                                                                  ║
// ║  The grid build menu, as a dial. Hold the build key with a grid   ║
// ║  block in hand and the ring lists every grid block you are        ║
// ║  carrying, grouped by what the block is for. Release over one     ║
// ║  and it is in your hand.                                         ║
// ║                                                                  ║
// ║  Nothing is crafted and nothing is granted here: the wheel only   ║
// ║  moves a stack you already own into the active hotbar slot, so    ║
// ║  the economy is untouched and a missing block reads as a missing  ║
// ║  wedge rather than a free one.                                    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Radial block picker for grid construction. Runs on the same dial as every other
    /// wheel in the game, so it inherits the hold-flick-release feel, the pinned state
    /// and the group swap without re-implementing any of it.
    /// </summary>
    public sealed class GridBuildWheel : MonoBehaviour
    {
        /// <summary>Wedges per page. Twelve still leaves 30 degrees a wedge, which is as
        /// fine as a flick can be aimed with any accuracy.</summary>
        private const int PageSize = 12;

        private enum Role
        {
            /// <summary>Not a group of items: the cockpit's own page, which chooses the
            /// shell the next cockpit wears.</summary>
            Cockpit = 0,
            Flight = 1,
            Power = 2,
            Structure = 3,
            Industry = 4,
            Utility = 5
        }

        private static readonly string[] RoleNames = { "COCKPIT", "FLIGHT", "POWER", "STRUCTURE", "INDUSTRY", "UTILITY" };

        private static readonly string[] RoleBlurbs =
        {
            "Which shell the cockpit you are holding will wear.",
            "Cockpits, thrusters, gyros and gear - what makes the construct move.",
            "Batteries, reactors and panels - what keeps it alive.",
            "Armour, glass and hull plating.",
            "Cargo, tanks, drills and machines.",
            "Screens, lights, beacons and everything else."
        };

        private readonly RadialWheelController _wheel = new();
        private readonly List<PageRef> _pages = new(8);
        private readonly List<GridBlockItem> _items = new(PageSize);

        private Inventory _inventory;
        private VisualElement _host;
        private int _pageIndex;
        // Reading the inventory to page it costs a walk over forty slots, so it is done on a
        // timer rather than every frame the player happens to hold a block.
        private float _nextPageScan;

        private struct PageRef
        {
            public Role Role;
            public int Page;
            public int PageCount;
        }

        private void Awake()
        {
            _wheel.ResolveHost = ResolveHost;
            _wheel.BuildOptions = FillOptions;
            _wheel.ConfirmOption = Confirm;
            _wheel.SwapGroup = NextPage;
            _wheel.GroupLabel = "BUILD";
            _wheel.SwapHint = "TAB  GROUP";
            _wheel.IdleTitle = "KEEP CURRENT";
            _wheel.IdleDescription = "Release in the centre to keep the block in hand";
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

            bool context = TryGetHeldBlock(out _);

            // The caption has to be settled before the dial is ticked: the wheel reads its
            // group label when it opens, and a page swap only re-reads it through here.
            if (context)
            {
                EnsurePages();
                _wheel.GroupLabel = LabelForCurrentPage();

                // The hub is the one icon a player reads without aiming at a wedge. On the
                // cockpit page it draws the shell currently armed, side on, so the choice
                // can be checked at a glance instead of by reading the label.
                bool cockpitPage = _pages.Count > 0 && _pages[_pageIndex].Role == Role.Cockpit;
                _wheel.IdleIcon = cockpitPage
                    ? CockpitShellIcons.For(CockpitShellBuilder.SelectedStyle)
                    : MachineShapeIcons.Cancel;
                _wheel.IdleTitle = cockpitPage
                    ? CockpitShellBuilder.NameOf(CockpitShellBuilder.SelectedStyle).ToUpperInvariant()
                    : "KEEP CURRENT";
                _wheel.IdleDescription = cockpitPage
                    ? "Armed. Release here to keep it."
                    : "Release in the centre to keep the block in hand";
            }

            _wheel.Tick(InputAction.BuildWheel, context);

            if (context && !_wheel.IsOpen && !UIState.IsBlocking)
            {
                _wheel.ShowPrompt("[" + GameSettings.GetKey(InputAction.BuildWheel) + "]  BUILD  ·  "
                                  + RoleNames[(int)CurrentPageRole]);
            }
            else
            {
                _wheel.HidePrompt();
            }
        }

        private VisualElement ResolveHost()
        {
            if (_host != null && _host.panel != null) return _host;
            var controller = GameUIController.Instance;
            var document = controller != null ? controller.GetComponent<UIDocument>() : FindAnyObjectByType<UIDocument>();
            _host = document != null ? document.rootVisualElement : null;
            return _host;
        }

        // ══════════════════════════════════════════════════════════════════
        //  PAGES
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rebuild the page list from the inventory. A page is one role of blocks, split
        /// into chunks when a player carries more than a ring can show, so nothing is ever
        /// silently missing from the wheel.
        /// </summary>
        private void EnsurePages()
        {
            if (_pages.Count > 0 && Time.unscaledTime < _nextPageScan) return;
            _nextPageScan = Time.unscaledTime + 0.25f;

            _pages.Clear();

            // A cockpit has one choice to make and it belongs on its own page, first: which
            // shell the block wears. The page shows whenever a cockpit is anywhere in the
            // pack, not only while one is in the hand - a player who owns a cockpit can arm
            // the shell before they ever raise it, and a cockpit held on the hotbar can be
            // switched to the other hull without first crafting anything.
            if (HasCockpitInPack())
                _pages.Add(new PageRef { Role = Role.Cockpit, Page = 0, PageCount = 1 });

            for (int r = (int)Role.Flight; r < RoleNames.Length; r++)
            {
                var role = (Role)r;
                int count = Collect(role).Count;
                if (count == 0) continue;

                int pageCount = (count + PageSize - 1) / PageSize;
                for (int page = 0; page < pageCount; page++)
                {
                    _pages.Add(new PageRef
                    {
                        Role = role,
                        Page = page,
                        PageCount = pageCount
                    });
                }
            }
            if (_pageIndex >= _pages.Count) _pageIndex = 0;
        }

        private string LabelForCurrentPage()
        {
            if (_pages.Count == 0) return "BUILD";
            var page = _pages[_pageIndex];
            string role = RoleNames[(int)page.Role];
            return page.PageCount > 1
                ? "BUILD  ·  " + role + "  " + (page.Page + 1) + "/" + page.PageCount
                : "BUILD  ·  " + role;
        }

        private Role CurrentPageRole
        {
            get
            {
                if (_pages.Count == 0) EnsurePages();
                return _pages.Count == 0 ? Role.Utility : _pages[_pageIndex].Role;
            }
        }

        private void NextPage()
        {
            EnsurePages();
            if (_pages.Count == 0) return;
            _pageIndex = (_pageIndex + 1) % _pages.Count;
            // The caption is part of what the dial repaints, so it has to be current before
            // the invalidate, not on the next frame.
            _wheel.GroupLabel = LabelForCurrentPage();
            _wheel.Invalidate();
        }

        /// <summary>Every carried grid block of a role, alphabetical so a wedge does not
        /// move around between frames.</summary>
        private List<GridBlockItem> Collect(Role role)
        {
            _items.Clear();
            if (_inventory == null || _inventory.container == null) return _items;

            var slots = _inventory.container.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var stack = slots[i];
                if (stack == null || stack.IsEmpty) continue;
                if (!(stack.item is GridBlockItem block) || block.blockPrefab == null) continue;
                if (RoleOf(block) != role) continue;
                if (!Contains(block)) _items.Add(block);
            }

            _items.Sort((a, b) => string.CompareOrdinal(a.displayName, b.displayName));
            return _items;
        }

        private bool Contains(GridBlockItem item)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] == item) return true;
            return false;
        }

        /// <summary>
        /// What a block is for, read from the component on its prefab rather than from a
        /// hand-kept list, so a block authored tomorrow lands in the right group without
        /// this file being touched. Anything unrecognised is a utility block.
        /// </summary>
        private static Role RoleOf(GridBlockItem block)
        {
            var prefab = block.blockPrefab;
            if (prefab == null) return Role.Utility;

            if (prefab.GetComponent<GridCockpit>() != null) return Role.Flight;
            if (prefab.GetComponent<GridThruster>() != null) return Role.Flight;
            if (prefab.GetComponent<GridGyroscope>() != null) return Role.Flight;
            if (prefab.GetComponent<GridWheel>() != null) return Role.Flight;

            if (prefab.GetComponent<GridBattery>() != null) return Role.Power;
            if (prefab.GetComponent<GridPortableReactor>() != null) return Role.Power;
            if (prefab.GetComponent<GridSolarPanel>() != null) return Role.Power;

            if (prefab.GetComponent<GridArmorBlock>() != null) return Role.Structure;

            if (prefab.GetComponent<GridCargoContainer>() != null) return Role.Industry;
            if (prefab.GetComponent<GridLiquidTank>() != null) return Role.Industry;
            if (prefab.GetComponent<GridGasTank>() != null) return Role.Industry;

            if (prefab.GetComponent<GridScreenBlock>() != null) return Role.Utility;

            // A plain block with no specialist component is still a block the player can
            // build. Hull plating that carries no script is structure, everything else is a
            // utility fitting.
            return prefab.GetComponent<GridBlock>() != null && IsHullLike(block) ? Role.Structure : Role.Utility;
        }

        /// <summary>True when a cockpit block is carried in the hotbar or the backpack.</summary>
        private bool HasCockpitInPack()
        {
            if (TryGetHeldBlock(out var held) && IsCockpit(held)) return true;
            if (_inventory == null || _inventory.container == null) return false;

            var slots = _inventory.container.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var stack = slots[i];
                if (stack == null || stack.IsEmpty) continue;
                if (stack.item is GridBlockItem block && IsCockpit(block)) return true;
            }
            return false;
        }

        private static bool IsCockpit(GridBlockItem block)
            => block != null && block.blockPrefab != null
               && block.blockPrefab.GetComponent<GridCockpit>() != null;

        private static bool IsHullLike(GridBlockItem block)
        {
            string name = (block.displayName ?? string.Empty).ToLowerInvariant();
            return name.Contains("armor") || name.Contains("armour")
                || name.Contains("hull") || name.Contains("glass")
                || name.Contains("plate") || name.Contains("frame");
        }

        // ══════════════════════════════════════════════════════════════════
        //  WHEEL
        // ══════════════════════════════════════════════════════════════════

        private void FillOptions(List<RadialOption> options)
        {
            EnsurePages();
            if (_pages.Count == 0) return;

            var page = _pages[_pageIndex];
            _wheel.GroupLabel = LabelForCurrentPage();

            if (page.Role == Role.Cockpit) { FillShellOptions(options); return; }

            var items = Collect(page.Role);
            int start = page.Page * PageSize;

            string heldId = TryGetHeldBlock(out var held) ? held.itemId : null;

            for (int i = 0; i < PageSize; i++)
            {
                int index = start + i;
                if (index >= items.Count) break;

                var block = items[index];

                // A cockpit wedge wears the hull it builds, not the generic flight wedge:
                // the ring is where a player picks the block, so it is where the block
                // should look like itself.
                var cockpit = block.blockPrefab != null ? block.blockPrefab.GetComponent<GridCockpit>() : null;
                var icon = cockpit != null ? CockpitShellIcons.For(cockpit.shellStyle) : IconFor(page.Role);

                options.Add(new RadialOption
                {
                    Title = block.displayName.ToUpperInvariant(),
                    Description = block.description,
                    Icon = icon,
                    Available = true,
                    Affordable = true,
                    Selected = heldId != null && block.itemId == heldId
                });
            }
        }

        /// <summary>One wedge per shell, with the armed one lit. A cockpit has no other
        /// choice to make, so this page is the whole of its configuration.</summary>
        private static void FillShellOptions(List<RadialOption> options)
        {
            var styles = CockpitShellBuilder.Styles;
            for (int i = 0; i < styles.Length; i++)
            {
                int style = styles[i];
                options.Add(new RadialOption
                {
                    Title = CockpitShellBuilder.NameOf(style).ToUpperInvariant(),
                    Description = CockpitShellBuilder.BlurbOf(style),
                    Icon = CockpitShellIcons.For(style),
                    Available = true,
                    Affordable = true,
                    Selected = style == CockpitShellBuilder.SelectedStyle
                });
            }
        }

        private bool Confirm(int index)
        {
            EnsurePages();
            if (_pages.Count == 0) return true;

            var page = _pages[_pageIndex];

            if (page.Role == Role.Cockpit)
            {
                if (index < 0 || index >= CockpitShellBuilder.Styles.Length) return true;
                int style = CockpitShellBuilder.Styles[index];
                CockpitShellBuilder.SelectedStyle = style;
                BuildFeedbackHud.Show(CockpitShellBuilder.NameOf(style),
                    "Armed - the next cockpit you place wears this shell",
                    null, UITheme.AccentCyan);
                return true;
            }

            var items = Collect(page.Role);
            int absolute = page.Page * PageSize + index;
            if (absolute < 0 || absolute >= items.Count) return true;

            Select(items[absolute]);
            return true;
        }

        /// <summary>
        /// Put the chosen block in the player's hand. A block already on the hotbar just
        /// selects its slot; a block dug out of the backpack swaps places with the active
        /// slot, so the wheel never has to refuse a pick the player can see.
        /// </summary>
        private void Select(GridBlockItem block)
        {
            if (_inventory == null || _inventory.container == null || block == null) return;

            var container = _inventory.container;
            int hotbarSlot = -1;
            int backpackSlot = -1;

            for (int i = 0; i < container.Slots.Count; i++)
            {
                var stack = container.GetSlot(i);
                if (stack == null || stack.IsEmpty || stack.item != block) continue;
                if (i < Inventory.HOTBAR_SIZE) { hotbarSlot = i; break; }
                if (backpackSlot < 0) backpackSlot = i;
            }

            if (hotbarSlot < 0 && backpackSlot >= 0)
            {
                int target = Mathf.Clamp(_inventory.activeHotbarIndex, 0, Inventory.HOTBAR_SIZE - 1);
                var outgoing = container.GetSlot(target);
                var incoming = container.GetSlot(backpackSlot);
                container.SetSlot(target, incoming);
                container.SetSlot(backpackSlot, outgoing);
                hotbarSlot = target;
            }

            if (hotbarSlot < 0) return;

            _inventory.SetActiveHotbar(hotbarSlot);
            BuildFeedbackHud.Show(block.displayName, HeldBlurb(block), block.icon, UITheme.AccentCyan);
        }

        private static string HeldBlurb(GridBlockItem block)
        {
            string size = block.gridSize == GridSize.Small ? "Small grid" : "Large grid";
            string mass = Mathf.RoundToInt(block.blockMass) + " kg";
            return size + "  ·  " + mass + "  ·  " + block.blockHP.ToString("0") + " HP";
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONTEXT
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The wheel owns the build key while a grid block is in hand, except for the
        /// blocks that carry shape variants: those keep the shape wheel, which is the more
        /// specific choice for them.
        /// </summary>
        private bool TryGetHeldBlock(out GridBlockItem block)
        {
            block = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is GridBlockItem gridBlock)) return false;
            if (gridBlock.SupportsShapeVariants) return false;
            block = gridBlock;
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ICONS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>One drawn mark per role: the same isometric convention as every other
        /// wheel, so the dial reads as one family.</summary>
        private static Texture2D IconFor(Role role)
        {
            // The cockpit page's icon is the shell itself, drawn side on.
            if (role == Role.Cockpit) return CockpitShellIcons.For(CockpitShellBuilder.SelectedStyle);

            return IconAtlas.Get("buildrole:" + (int)role, b => DrawRoleIcon(b, role));
        }

        private static void DrawRoleIcon(LineArtBuilder b, Role role)
        {
            switch (role)
            {
                case Role.Flight:
                    // A wedge with a thrust cone behind it.
                    b.Closed(LineArtBuilder.Iso(0.3f, 0f, 0.5f),
                             LineArtBuilder.Iso(1.1f, 0f, 0.5f),
                             LineArtBuilder.Iso(0.7f, 0.85f, 0.5f));
                    b.IsoBox(0.62f, 0f, 0.32f, 0.16f, 0.28f, 0.36f);
                    break;

                case Role.Power:
                    // A cell with a terminal on top.
                    b.IsoBox(0.28f, 0f, 0.22f, 0.72f, 0.78f, 0.56f);
                    b.IsoFaceLine(0.72f, 0.78f, 0.34f, 0.62f, 0.66f, 0.62f);
                    b.IsoFaceLine(0.72f, 0.78f, 0.34f, 0.42f, 0.66f, 0.42f);
                    break;

                case Role.Structure:
                    // A plated corner: two faces, one seam.
                    b.IsoBox(0.24f, 0f, 0.24f, 0.8f, 0.8f, 0.8f);
                    b.IsoFaceLine(0.8f, 0.8f, 0.32f, 0.32f, 0.32f, 0.78f);
                    break;

                case Role.Industry:
                    // A crate with a lid line.
                    b.IsoBox(0.26f, 0f, 0.26f, 0.76f, 0.62f, 0.76f);
                    b.IsoFaceLine(0.76f, 0.62f, 0.36f, 0.72f, 0.64f, 0.72f);
                    break;

                default:
                    // A screen on a stand.
                    b.IsoBox(0.28f, 0.34f, 0.5f, 0.72f, 0.5f, 0.1f);
                    b.Line(LineArtBuilder.Iso(0.64f, 0.34f, 0.5f), LineArtBuilder.Iso(0.64f, 0f, 0.5f));
                    b.Line(LineArtBuilder.Iso(0.44f, 0f, 0.5f), LineArtBuilder.Iso(0.84f, 0f, 0.5f));
                    break;
            }
        }
    }
}
