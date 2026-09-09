// Assets/Scripts/VoxelEngine/Navigation/GridConnectorUI.cs
//
// THE PAD PANEL — what the person standing next to a connector is owed: a name field, a queue they
// can read and rearrange, the flow the head of that queue is actually getting, and a short log so
// "why is this shuttle still here" has an answer in the panel instead of in the source code.
//
// The panel never computes a transfer and never reports a target the ship has not met. Every line
// here is read off `GridConnectorBlock` and `GridRouteAutopilot`, because a pad panel that kept its
// own copy of "how full is that ship" would be the second most common bug report in this game.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoxelEngine.Navigation
{
    public static class GridConnectorUI
    {
        private static readonly Color OkInk = new Color(0.42f, 0.80f, 0.52f);
        private static readonly Color WarnInk = new Color(0.92f, 0.60f, 0.12f);
        private static readonly Color BadInk = new Color(0.82f, 0.22f, 0.18f);

        // Set on the panel, read by the rename button: a text field that commits on every keystroke is
        // a waymark registry that re-registers 40 times a second.
        static TextField _draft;
        static EntityId _draftFor;   // identity, not instance id: EntityId is what this
                                    // engine hands out for "the same object" now.

        public static VisualElement BuildPanel(GridConnectorBlock connector)
        {
            var p = VoxelEngine.UI.UITheme.MachinePanel();
            p.name = "ConnectorPanel";
            p.style.width = 500;

            if (connector == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Body("No connector under the cursor."));
                return p;
            }

            bool on = connector.Enabled;
            string state = !on ? "OFFLINE"
                : connector.QueueDepth == 0 ? "NO VISITORS"
                : connector.QueueDepth + " IN QUEUE";
            Color ink = !on ? VoxelEngine.UI.UITheme.AccentRed
                : connector.Head != null && !connector.LastFlow.IsIdle ? VoxelEngine.UI.UITheme.AccentGreen
                : connector.QueueDepth > 0 ? VoxelEngine.UI.UITheme.AccentAmber
                : VoxelEngine.UI.UITheme.AccentDim;

            var (hdr, _, _, _) = VoxelEngine.UI.UITheme.HeaderRow("⚓ " + connector.blockName, state, ink);
            p.Add(hdr);
            p.Add(VoxelEngine.UI.UITheme.AccentDivider(VoxelEngine.UI.UITheme.AccentCyan));

            // ── Power / enable ───────────────────────────────────────────────
            var (powerPill, _) = VoxelEngine.UI.UITheme.MachineToggle(on, v =>
            {
                connector.Enabled = v;
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            });
            var enableRow = Row();
            enableRow.Add(VoxelEngine.UI.UITheme.StatLabel("Connector"));
            enableRow.Add(powerPill);
            p.Add(enableRow);

            // ── Name, the waymark ────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Waymark Name"));
            var nameRow = Row();
            if (_draft == null || _draftFor != connector.GetEntityId())
            {
                _draft = new TextField { value = connector.waymarkName ?? "" };
                _draft.style.flexGrow = 1;
                _draft.style.marginRight = 6;
                _draftFor = connector.GetEntityId();
            }
            nameRow.Add(_draft);
            nameRow.Add(VoxelEngine.UI.UITheme.SmallButton("SET", () =>
            {
                string wanted = _draft != null ? _draft.value : "";
                connector.Rename(wanted);
                _draft = null;                 // rebuild the field so it shows what was accepted
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, VoxelEngine.UI.UITheme.AccentCyan));
            p.Add(nameRow);
            if (!string.IsNullOrWhiteSpace(connector.waymarkName) && connector.IsNameFree(connector.waymarkName)
                && GridWaymark.FindSource(connector.waymarkName) != null)
            {
                // Unreachable in normal play; kept because a name that resolves to two pads is the
                // one way this system can lie to a pilot.
                p.Add(VoxelEngine.UI.UITheme.Muted("⚠ Another block answers this name too. The first one found is flown to."));
            }
            else
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("Shuttles fly to this name. Saved with the block, so a reload keeps every schedule pointed here."));
            }

            // ── The fitting ──────────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("The Fitting"));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⚡", "Power", connector.powerWatts.ToString("0") + " W each way",
                connector.powerWatts > 0.01f ? VoxelEngine.UI.UITheme.AccentCyan : VoxelEngine.UI.UITheme.TextSecondary));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⌬", "Gas & fuel", connector.gasLitresPerSecond.ToString("0") + " L/s  ·  "
                + connector.itemSlotsPerSecond + " items/s", VoxelEngine.UI.UITheme.AccentCyan));
            p.Add(VoxelEngine.UI.UITheme.StatRow("◎", "Source", connector.drawFromSurplusOnly
                    ? "generation surplus only" : "surplus and stored charge",
                connector.drawFromSurplusOnly ? OkInk : WarnInk));
            p.Add(VoxelEngine.UI.UITheme.StatRow("⚙", "Capture", connector.offersMagneticLock
                    ? "magnetic lock · " + connector.captureRadiusCells.ToString("0") + " cell soft radius"
                    : "soft capture · " + connector.captureRadiusCells.ToString("0") + " cells",
                connector.offersMagneticLock ? OkInk : WarnInk));
            if (connector.offersMagneticLock)
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("A hovering ship is served at "
                    + (connector.SoftRate * 100f).ToString("0") + "% rate. That is the fitting, not a fault: "
                    + "flanges that are not mated cannot pump hard."));
            }

            // ── Head of the queue: what is being moved, to whom ──────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("In Service"));
            var head = connector.Head;
            if (head?.grid == null)
            {
                p.Add(VoxelEngine.UI.UITheme.Muted("Nothing at the pad. A ship enters the queue by flying "
                    + "into the capture envelope — usually because its own loop told it to."));
            }
            else
            {
                bool hasAuto = head.grid.TryGetComponent<GridRouteAutopilot>(out var ap);
                p.Add(VoxelEngine.UI.UITheme.StatRow("▸", "Ship", head.shipLabel
                    + (head.locked ? " · LOCKED" : " · SOFT"), VoxelEngine.UI.UITheme.AccentCyan));
                p.Add(VoxelEngine.UI.UITheme.StatRow("→", "Flow", head.lastFlow.Describe(),
                    head.lastFlow.IsIdle ? VoxelEngine.UI.UITheme.TextSecondary : OkInk));
                if (hasAuto)
                {
                    p.Add(VoxelEngine.UI.UITheme.StatRow("◍", "Its targets", ap.DeficitForPad(),
                        ap.TargetsMet ? OkInk : WarnInk));
                    p.Add(VoxelEngine.UI.UITheme.StatRow("⧗", "Waiting",
                        (Time.time - head.arrivedAt).ToString("0") + " s of "
                        + (connector.visitTimeoutSeconds > 0.01f ? connector.visitTimeoutSeconds.ToString("0") + " s" : "no timeout"),
                        VoxelEngine.UI.UITheme.TextSecondary));
                }
                else
                {
                    p.Add(VoxelEngine.UI.UITheme.Muted("Nobody at the controls and no autopilot on this grid: it will sit here "
                        + "until its own crew leaves it, and the pad will not throw it off."));
                }
            }

            // ── The queue ────────────────────────────────────────────────────
            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Queue · " + connector.QueueDepth));
            var q = connector.Queue;
            if (q.Count > 0)
            {
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.name = "PanelScroll_ConnectorQueue";
                scroll.style.maxHeight = 128f;
                VoxelEngine.UI.UITheme.StyleScroller(scroll);
                for (int i = 0; i < q.Count; i++)
                {
                    var visit = q[i];
                    if (visit?.grid == null) continue;
                    var row = Row();
                    row.Add(VoxelEngine.UI.UITheme.StatRow((i + 1).ToString(), visit.shipLabel,
                        i == 0 ? "IN SERVICE" : "waiting · " + (Time.time - visit.arrivedAt).ToString("0") + " s",
                        i == 0 ? VoxelEngine.UI.UITheme.AccentGreen : VoxelEngine.UI.UITheme.TextSecondary));
                    if (i > 0)
                    {
                        var g = visit.grid;
                        row.Add(VoxelEngine.UI.UITheme.SmallButton("UP", () =>
                        {
                            connector.BumpToFront(g);
                            VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                        }, VoxelEngine.UI.UITheme.BgSlot));
                    }
                    var gg = visit.grid;
                    row.Add(VoxelEngine.UI.UITheme.SmallButton("✕", () =>
                    {
                        connector.Eject(gg);
                        VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
                    }, VoxelEngine.UI.UITheme.BgSlot));
                    scroll.Add(row);
                }
                p.Add(scroll);
            }

            // ── Log ──────────────────────────────────────────────────────────
            var log = connector.Log;
            if (log != null && log.Count > 0)
            {
                p.Add(VoxelEngine.UI.UITheme.Spacer(4));
                p.Add(VoxelEngine.GridSystem.UI.GridUIHelpers.SectionTitle("Pad Log"));
                for (int i = log.Count - 1; i >= 0; i--)
                    p.Add(VoxelEngine.UI.UITheme.Muted(log[i]));
            }

            p.Add(VoxelEngine.UI.UITheme.Spacer(4));
            p.Add(VoxelEngine.UI.UITheme.Muted("One transfer at a time, because that is what a fitting is. A ship "
                + "leaves when its own target is met — the pad serves, the shuttle decides."));
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
