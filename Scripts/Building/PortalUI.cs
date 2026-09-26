// Assets/Scripts/VoxelEngine/Building/PortalUI.cs
//
// The Portal Controller's panel: identity (custom name + code — the two portals'
// shared secret), live state, the shape the controller sees, the power bill for
// keeping the aperture open, and the OPEN/CLOSE switch. Static-block panel in the
// house style, mounted by GameUIController like every other machine.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Building
{
    public static class PortalUI
    {
        private static readonly Color PortalCyan = new Color(0.35f, 0.75f, 1f);

        public static VisualElement BuildPanel(PortalControllerBlock portal)
        {
            var p = T.MachinePanel();
            p.name = "PortalControllerPanel";
            VoxelEngine.UI.StarshipTheme.Frame(p, PortalCyan);

            var (hdr, _, _, stateLabel) = T.HeaderRow("PORTAL", StateWord(portal), PortalCyan);
            p.Add(hdr);
            p.Add(VoxelEngine.UI.StarshipTheme.HullDivider(PortalCyan));

            // ── Identity: name + code define the shared network ──
            var idCaption = new Label("NETWORK IDENTITY  ·  NAME + CODE MUST MATCH");
            idCaption.style.fontSize = 11;
            idCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            idCaption.style.letterSpacing = 1f;
            idCaption.style.color = new StyleColor(PortalCyan);
            p.Add(idCaption);

            var nameField = new TextField("Name") { value = portal.portalName ?? "" };
            nameField.style.marginTop = 4;
            IndustrialWorld.Navigation.NavigationFieldStyle.Apply(nameField);
            nameField.RegisterValueChangedCallback(evt =>
            {
                portal.portalName = evt.newValue ?? "";
                portal.RefreshLink();
            });
            p.Add(nameField);

            var codeField = new TextField("Code") { value = portal.portalCode ?? "" };
            codeField.style.marginTop = 2;
            IndustrialWorld.Navigation.NavigationFieldStyle.Apply(codeField);
            codeField.RegisterValueChangedCallback(evt =>
            {
                portal.portalCode = evt.newValue ?? "";
                portal.RefreshLink();
            });
            p.Add(codeField);

            // ── Network routing: labels make endpoints human-readable while
            // persistent endpoint ids keep the selected route unambiguous. ──
            p.Add(T.Spacer(6));
            var routeCaption = new Label("NETWORK ROUTING");
            routeCaption.style.fontSize = 11;
            routeCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            routeCaption.style.letterSpacing = 1f;
            routeCaption.style.color = new StyleColor(PortalCyan);
            p.Add(routeCaption);

            var endpointField = new TextField("This endpoint") { value = portal.endpointLabel ?? "" };
            endpointField.style.marginTop = 4;
            IndustrialWorld.Navigation.NavigationFieldStyle.Apply(endpointField);
            endpointField.RegisterValueChangedCallback(evt => portal.endpointLabel = evt.newValue ?? "");
            p.Add(endpointField);

            const string AutoDestination = "AUTO  ·  single matching endpoint";
            var destinationIds = new List<string> { "" };
            var destinationChoices = new List<string> { AutoDestination };
            foreach (var destination in portal.GetNetworkDestinations())
            {
                destinationIds.Add(destination.EndpointId);
                destinationChoices.Add(destination.EndpointPickerLabel);
            }
            int destinationIndex = 0;
            string selectedId = portal.selectedDestinationId ?? "";
            for (int i = 1; i < destinationIds.Count; i++)
                if (destinationIds[i] == selectedId) { destinationIndex = i; break; }
            var destinationField = new DropdownField("Destination", destinationChoices, destinationIndex);
            destinationField.style.marginTop = 2;
            IndustrialWorld.Navigation.NavigationFieldStyle.Apply(destinationField);
            destinationField.RegisterValueChangedCallback(evt =>
            {
                int choice = destinationChoices.IndexOf(evt.newValue);
                portal.SelectDestination(choice >= 0 && choice < destinationIds.Count ? destinationIds[choice] : "");
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            });
            p.Add(destinationField);

            var routeButtons = new VisualElement();
            routeButtons.style.flexDirection = FlexDirection.Row;
            var refreshRoute = T.SmallButton("REFRESH ENDPOINTS", () =>
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel(), T.BgSlot);
            refreshRoute.style.flexGrow = 1f;
            routeButtons.Add(refreshRoute);
            p.Add(routeButtons);
            if (portal.NetworkDestinationCount > 1 && string.IsNullOrWhiteSpace(portal.selectedDestinationId))
                p.Add(T.Muted("This network has multiple endpoints. Select one before transit; no arbitrary routing."));

            // ── Shape ──
            p.Add(T.Spacer(6));
            var shapeCaption = new Label("APERTURE");
            shapeCaption.style.fontSize = 11;
            shapeCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            shapeCaption.style.letterSpacing = 1f;
            shapeCaption.style.color = new StyleColor(PortalCyan);
            p.Add(shapeCaption);

            var shapeRow = T.StatRow("\u25ef", "Shape", "\u2014", T.TextSecondary);
            Label shapeVal = StatValue(shapeRow);
            p.Add(shapeRow);
            var cellsRow = T.StatRow("\u2637", "Interior cells", "\u2014", T.TextSecondary);
            Label cellsVal = StatValue(cellsRow);
            p.Add(cellsRow);
            var reasonRow = T.StatRow("\u26a0", "Scan", "\u2014", T.AccentAmber);
            Label reasonVal = StatValue(reasonRow);
            p.Add(reasonRow);

            // ── Power ──
            p.Add(T.Spacer(6));
            var powerCaption = new Label("POWER  ·  THE BIG PORTAL TAX");
            powerCaption.style.fontSize = 11;
            powerCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            powerCaption.style.letterSpacing = 1f;
            powerCaption.style.color = new StyleColor(PortalCyan);
            p.Add(powerCaption);

            var nowRow = T.StatRow("\u26a1", "Drawing now", "\u2014", T.AccentAmber);
            Label nowVal = StatValue(nowRow);
            p.Add(nowRow);
            var openRow = T.StatRow("\u23f1", "Open drain", "\u2014", T.AccentAmber);
            Label openVal = StatValue(openRow);
            p.Add(openRow);
            var chargeRow = T.StatRow("\ud83d\udd0b", "Charge", "\u2014", T.AccentCyan);
            Label chargeVal = StatValue(chargeRow);
            p.Add(chargeRow);

            // ── Link + control ──
            p.Add(T.Spacer(6));
            var linkRow = T.StatRow("\u21c4", "Linked to", "\u2014", PortalCyan);
            Label linkVal = StatValue(linkRow);
            p.Add(linkRow);

            p.Add(T.Spacer(4));
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            var openBtn = T.SmallButton(portal.IsOpen ? "CLOSE PORTAL" : "OPEN PORTAL", () =>
            {
                if (portal.IsOpen) portal.ClosePortal(null);
                else portal.OpenPortal();
                VoxelEngine.UI.GameUIController.Instance?.RefreshCurrentPanel();
            }, portal.IsOpen ? T.AccentAmber : T.BgSlot);
            openBtn.style.flexGrow = 1f;
            btnRow.Add(openBtn);
            p.Add(btnRow);

            p.Add(T.Muted("Build a closed loop of Portal Frames (square or ring, up to 64x64), mount the controller within 8 m, feed it power. NAME + CODE define a portal network; label each endpoint and select the destination for a deterministic route."));

            p.schedule.Execute(() =>
            {
                if (portal == null || p.panel == null) return;
                stateLabel.text = StateWord(portal);
                if (shapeVal != null)
                    shapeVal.text = portal.IsValid
                        ? $"{portal.InteriorWide} x {portal.InteriorHigh}"
                        : "\u2014";
                if (cellsVal != null)
                    cellsVal.text = portal.IsValid ? portal.InteriorCells.ToString() : "\u2014";
                if (reasonVal != null)
                {
                    reasonVal.text = portal.IsValid ? "sealed" : (portal.InvalidReason ?? "\u2014");
                    reasonVal.style.color = new StyleColor(portal.IsValid ? T.AccentGreen : T.AccentAmber);
                }
                if (nowVal != null)
                    nowVal.text = VoxelEngine.Items.PowerFormat.Watts(portal.CurrentWatts);
                if (openVal != null)
                    openVal.text = portal.IsValid
                        ? VoxelEngine.Items.PowerFormat.Watts(portal.openBaseWatts + portal.openWattsPerCell * portal.InteriorCells) + " /s"
                        : "\u2014";
                if (chargeVal != null)
                    chargeVal.text = $"{portal.Charge01 * 100f:0}%" + (portal.Cooldown01 > 0f ? " · cooling" : "");
                if (linkVal != null)
                {
                    linkVal.text = portal.Linked != null ? portal.Linked.EndpointDisplayName
                        : portal.IsOpen
                            ? portal.RequiresDestinationSelection ? "select destination" : "destination unavailable"
                            : "\u2014";
                    linkVal.style.color = new StyleColor(portal.Linked != null ? T.AccentGreen : T.TextSecondary);
                }
            }).Every(250);

            return p;
        }

        private static Label StatValue(VisualElement row)
        {
            Label last = null;
            if (row == null) return null;
            foreach (var child in row.Children())
                if (child is Label l) last = l;
            return last;
        }

        private static string StateWord(PortalControllerBlock portal)
        {
            if (portal == null) return "\u2014";
            if (portal.IsOpen) return "OPEN";
            if (portal.Cooldown01 > 0f) return "COOLDOWN";
            if (!portal.IsPowered) return "NO POWER";
            if (!portal.IsValid) return "NO PORTAL";
            if (portal.Charge01 >= 1f) return "READY";
            return "CHARGING";
        }
    }
}
