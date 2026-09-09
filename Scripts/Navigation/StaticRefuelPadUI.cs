// Assets/Scripts/VoxelEngine/Navigation/StaticRefuelPadUI.cs
//
// THE GROUND PAD PANEL — the same four questions every refuel surface owes a player: what is this
// place called, who is standing at it, what are they getting, and why is it that speed.
//
// It is a separate file from `GridConnectorUI` on purpose rather than a branch inside it, because the
// two pads answer different questions about supply: a grid connector cites another grid's batteries,
// a ground pad cites the base's wire network and says so. Merging them would have produced one panel
// that lies about half its lines on half its visits.
//
// Every figure is read off `StaticRefuelPad` / `GridRouteAutopilot`. Nothing here computes a transfer.

using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.Navigation
{
    public static class StaticRefuelPadUI
    {
        private static readonly Color OkInk = new Color(0.42f, 0.80f, 0.52f);
        private static readonly Color WarnInk = new Color(0.92f, 0.60f, 0.12f);

        // The same field-rebuild discipline as the connector panel: a text field that commits on every
        // keystroke is a waymark registry that re-registers forty times a second.
        static TextField _draft;
        static EntityId _draftFor;

        public static VisualElement BuildPanel(StaticRefuelPad pad)
        {
            var p = VoxelEngine.UI.UITheme.MachinePanel();
            p.name = "StaticRefuelPadPanel";
            p.style.width = 500;

            if (pad == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Body("No refuel pad under the cursor."));
                return p;
            }

            bool on = pad.Enabled;
            string state = !on ? "OFFLINE"
                : pad.QueueDepth == 0 ? "YARD CLEAR"
                : pad.QueueDepth + " PULLED IN";
            Color ink = !on ? VoxelEngine.UI.UITheme.AccentRed
                : pad.Head != null && !pad.LastFlow.IsIdle ? VoxelEngine.UI.UITheme.AccentGreen
                : pad.QueueDepth > 0 ? VoxelEngine.UI.UITheme.AccentAmber
                : VoxelEngine.UI.UITheme.AccentDim;

            var (hdr, _, _, _) = VoxelEngine.UI.UITheme.HeaderRow("⛽ " + pad.WaymarkLabel, state, ink);
            p.Add(hdr);
            p.Add(VoxelEngine.UI.UITheme.AccentDivider(VoxelEngine.UI.UITheme.AccentCyan));

            var (powerPill, _) = VoxelEngine.UI.UITheme.MachineToggle(on, v =>
            {
                pad.enabled = v;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            });
            var enableRow = Row();
            enableRow.Add(VoxelEngine.UI.UITheme.StatLabel("Pad"));
            enableRow.Add(powerPill);
            p.Add(enableRow);

            // ── Name, the waymark ────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Waymark Name"));
            var nameRow = Row();
            if (_draft == null || _draftFor != pad.GetEntityId())
            {
                _draft = new TextField { value = pad.waymarkName ?? "" };
                _draft.style.flexGrow = 1;
                _draft.style.marginRight = 6;
                _draftFor = pad.GetEntityId();
            }
            nameRow.Add(_draft);
            nameRow.Add(VoxelEngine.UI.UITheme.SmallButton("SET", () =>
            {
                pad.Rename(_draft != null ? _draft.value : "");
                _draft = null;                    // rebuild so the field shows what was accepted
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.AccentCyan));
            p.Add(nameRow);
            p.Add(VoxelEngine.UI.UITheme.Muted("Shuttles and ground rigs fly or drive to this name. It is saved "
                + "with the pad, so a reload keeps every schedule pointed here."));

            // ── What the base is being asked for ──────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Plumbing"));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⚡", "Demand", pad.powerWatts.ToString("0") + " W while pumping · idle 0 W",
                pad.BaseCanServe ? VoxelEngine.UI.UITheme.AccentCyan : VoxelEngine.UI.UITheme.AccentRed));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⌬", "Fittings", pad.litresPerSecond.ToString("0") + " L/s  ·  "
                + pad.itemSlotsPerSecond + " items/s", VoxelEngine.UI.UITheme.AccentCyan));
            p.Add(VoxelEngine.UI.UITheme.StatRow("◎", "Plumbing", pad.SupplyLine,
                pad.TankPlumbed && pad.PowerConnected ? OkInk : WarnInk));
            p.Add(VoxelEngine.UI.UITheme.StatRow("◍", "Pad tank", pad.TankLitres.ToString("0") + " / "
                + pad.tankCapacityLitres.ToString("0") + " L " + pad.TankType,
                pad.TankFill01 > 0.01f ? OkInk : WarnInk));
            p.Add(VoxelEngine.UI.UITheme.StatRow("◈", "Hydrogen", pad.GasStored.ToString("0") + " units",
                pad.GasFill01 > 0.01f ? OkInk : WarnInk));
            p.Add(VoxelEngine.UI.UITheme.StatRow("▣", "Drum", pad.DrumItems + " items in "
                + (pad.Drum != null ? pad.Drum.Size : 0) + " slots",
                pad.DrumItems > 0 ? OkInk : VoxelEngine.UI.UITheme.TextSecondary));
            p.Add(VoxelEngine.UI.UITheme.Muted("Base pipes fill this pad and base pipes empty it — belts and "
                + "conveyors too, through the faces below. A ground pad has no magnetic lock and no soft "
                + "capture either: what is standing at it is standing at it, so the rated rate is the rate."));
            if (!pad.BaseCanServe)
                p.Add(VoxelEngine.UI.UITheme.Muted("⚠ The base cannot sustain this draw. The pad refuses the leg "
                    + "rather than trickle-charging a ship on borrowed watts."));

            // ── Head of the queue ────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("In Service"));
            var head = pad.Head;
            if (head?.grid == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("Nothing at the pad. A grid enters the queue by being "
                    + "standing within " + pad.captureRadiusMetres.ToString("0.0") + " m — parked, landed, "
                    + "or driven in by its own loop."));
            }
            else
            {
                bool hasAuto = head.grid.TryGetComponent<GridRouteAutopilot>(out var ap);
                p.Add(VoxelEngine.UI.UITheme.StatRow("▸", "Visitor", head.shipLabel, VoxelEngine.UI.UITheme.AccentCyan));
                p.Add(VoxelEngine.UI.UITheme.StatRow("→", "Flow", head.lastFlow.Describe(),
                    head.lastFlow.IsIdle ? VoxelEngine.UI.UITheme.TextSecondary : OkInk));
                if (hasAuto)
                {
                    p.Add(VoxelEngine.UI.UITheme.StatRow("◍", "Its targets", ap.DeficitForPad(),
                        ap.TargetsMet ? OkInk : WarnInk));
                    p.Add(VoxelEngine.UI.UITheme.StatRow("⧗", "Waiting",
                        (Time.time - head.arrivedAt).ToString("0") + " s of "
                        + (pad.visitTimeoutSeconds > 0.01f ? pad.visitTimeoutSeconds.ToString("0") + " s" : "no timeout"),
                        VoxelEngine.UI.UITheme.TextSecondary));
                }
                else
                {
                    p.Add(VoxelEngine.UI.UITheme.Muted("No autopilot on this grid: it will sit here until its crew "
                        + "moves it, and the pad will not throw it off."));
                }
            }

            // ── The queue ────────────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Queue · " + pad.QueueDepth));
            var q = pad.Queue;
            if (q.Count > 0)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.name = "PanelScroll_PadQueue";
                scroll.style.maxHeight = 128f;
                VoxelEngine.UI.UITheme.StyleScroller(scroll);
                for (int i = 0; i < q.Count; i++)
                {
                    var visit = q[i];
                    if (visit?.grid == null) continue;
                    var row = Row();
                    row.Add(VoxelEngine.UI.UITheme.StatRow((i + 1).ToString(), visit.shipLabel,
                        i == 0 ? "AT THE HOSE" : "waiting · " + (Time.time - visit.arrivedAt).ToString("0") + " s",
                        i == 0 ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.TextSecondary));
                    if (i > 0)
                    {
                        var g = visit.grid;
                        row.Add(VoxelEngine.UI.UITheme.SmallButton("UP", () =>
                        {
                            pad.BumpToFront(g);
                            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                        }, VoxelEngine.UI.UITheme.BgSlot));
                    }
                    var gg = visit.grid;
                    row.Add(VoxelEngine.UI.UITheme.SmallButton("✕", () =>
                    {
                        pad.Eject(gg);
                        VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                    }, VoxelEngine.UI.UITheme.BgSlot));
                    scroll.Add(row);
                }
                p.Add(scroll);
            }

            // ── Log ──────────────────────────────────────────────────────────
            var log = pad.Log;
            if (log != null && log.Count > 0)
            {
                p.Add(VoxelEngine.UI.UITheme.Spacer(4));
                p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Pad Log"));
                for (int i = log.Count - 1; i >= 0; i--)
                    p.Add(VoxelEngine.UI.UITheme.Muted(log[i]));
            }

            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.UI.UITheme.Muted("One visitor at a time, because that is what a hose is. The ship "
                + "leaves when its own target is met — the pad serves, the shuttle decides."));
            // ── Port faces ───────────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            VoxelEngine.UI.GameUIController.Instance?.AppendMachinePorts(p, pad);

            VoxelEngine.UI.UITheme.AnimatePanelBoot(p);
            return p;
        }

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            return r;
        }
    }
}
