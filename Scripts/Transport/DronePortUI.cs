// Assets/Scripts/VoxelEngine/Transport/DronePortUI.cs
//
// Panel for the Drone Port: link status, the current flight, and lifetime traffic.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Transport
{
    public static class DronePortUI
    {
        private static readonly List<Chest> _providers  = new();
        private static readonly List<Chest> _requesters = new();

        public static VisualElement BuildPanel(DronePort port)
        {
            var p = T.MachinePanel();
            p.style.width = 470;

            bool powered = port != null && port.IsPowered;

            var (hdr, _, _, _) = T.HeaderRow("Drone Port",
                powered ? "ONLINE" : "NO POWER",
                powered ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(powered ? T.AccentCyan : T.AccentAmber));
            p.Add(T.Spacer(6));

            if (port == null) return p;

            if (!powered)
            {
                var warn = T.Muted("No power. A port needs power to load and launch a drone. " +
                                   "A drone already in the air still completes its trip.");
                warn.style.whiteSpace = WhiteSpace.Normal;
                warn.style.marginBottom = 8;
                p.Add(warn);
            }

            var net = DroneNetwork.Instance;
            int linked = net != null ? net.LinkedPortCount(port) : 0;

            p.Add(T.StatRow("", "Linked ports", linked.ToString(),
                linked > 0 ? T.TextSecondary : T.AccentAmber));
            p.Add(T.StatRow("", "Link range", port.linkRange.ToString("0") + " m"));
            p.Add(T.StatRow("", "Payload", port.payloadPerTrip + " per trip"));

            // Which logistic chests this port actually serves. A port is a bridge, not a
            // store, so this is the honest measure of whether it can do anything.
            port.CollectLocalChests(_providers, _requesters);
            p.Add(T.StatRow("", "Chests in service range",
                _providers.Count + " providing  ·  " + _requesters.Count + " requesting",
                (_providers.Count + _requesters.Count) > 0 ? T.TextSecondary : T.AccentAmber));

            p.Add(T.Spacer(8));
            p.Add(T.AccentDivider(T.AccentCyan));
            p.Add(T.Spacer(6));

            // ── Current flight ──────────────────────────────────────────────
            if (port.IsBlocked)
            {
                var blocked = T.Muted("Drone landed but could not unload " + port.CargoCount + " x " +
                                      (port.CargoItem != null ? port.CargoItem.displayName : "cargo") +
                                      ". Holding it and retrying — free some space at either end.");
                blocked.style.whiteSpace = WhiteSpace.Normal;
                blocked.style.color = new StyleColor(T.AccentAmber);
                p.Add(blocked);
            }
            else if (port.IsInFlight)
            {
                string cargo = port.CargoCount + " x " +
                               (port.CargoItem != null ? port.CargoItem.displayName : "cargo");
                string target = port.CurrentPartner != null ? port.CurrentPartner.portName : "destination";

                p.Add(T.StatRow("", "In flight", cargo, T.AccentCyan));
                p.Add(T.StatRow("", "Bound for", target));

                float t = port.FlightTotal > 0f
                    ? Mathf.Clamp01(1f - (port.FlightRemaining / port.FlightTotal))
                    : 0f;
                var (bar, _) = T.ProgressBar(t, T.AccentCyan, 8, true);
                bar.style.marginTop = 4;
                p.Add(bar);

                var eta = T.Muted(port.FlightRemaining.ToString("0.0") + " s remaining");
                eta.style.marginTop = 4;
                p.Add(eta);
            }
            else
            {
                var idle = T.Muted(linked == 0
                    ? "Idle. No other port is within link range — build a second port at the far site."
                    : "Idle. A drone launches when a requester near another port needs something " +
                      "this port's providers can supply.");
                idle.style.whiteSpace = WhiteSpace.Normal;
                p.Add(idle);
            }

            p.Add(T.Spacer(8));
            p.Add(T.StatRow("", "Trips completed", port.TripsCompleted.ToString()));
            p.Add(T.StatRow("", "Items delivered", port.ItemsDelivered.ToString()));

            p.Add(T.Spacer(6));
            var note = T.Muted("Drones only carry what the local wireless network cannot. " +
                               "Anything a provider within " + DronePort.ServiceRadius.ToString("0") +
                               " m of the requester can already supply never takes a flight.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize = 9;
            p.Add(note);

            return p;
        }
    }
}
