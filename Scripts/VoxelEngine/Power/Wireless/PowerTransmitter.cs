// Assets/Scripts/VoxelEngine/Power/Wireless/PowerTransmitter.cs
//
// Broadcasts a fraction of the watts available on its local cable network to every
// PowerReceiver inside `range`. Inefficient: only `efficiency` (e.g. 0.5 = 50%) of the
// drained power arrives at receivers (the rest is "lost in the air").

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power.Wireless
{
    public class PowerTransmitter : MonoBehaviour
    {
        [Tooltip("Maximum watts this transmitter can broadcast per second.")]
        public float maxBroadcastWatts = 2000f;
        [Tooltip("Fraction of broadcast power that actually reaches the receivers (0..1).")]
        [Range(0.05f, 1f)] public float efficiency = 0.5f;
        [Tooltip("World-space radius within which receivers will pick up our signal.")]
        public float range = 30f;

        // Connected as a PowerConsumer to the local cable network so we can drain power.
        private PowerConsumer _consumer;
        [System.NonSerialized] public float CurrentBroadcastWatts; // for HUD/tooltip

        private void Awake()
        {
            _consumer = GetComponent<PowerConsumer>();
            if (_consumer == null) _consumer = gameObject.AddComponent<PowerConsumer>();
        }

        private void Update()
        {
            // Find all receivers in range; total their demand.
            float demand = 0f;
            var receivers = new List<PowerReceiver>();
            var all = Object.FindObjectsByType<PowerReceiver>(FindObjectsInactive.Exclude);
            foreach (var r in all)
            {
                if (r == null) continue;
                float d = (r.transform.position - transform.position).sqrMagnitude;
                if (d > range * range) continue;
                receivers.Add(r);
                demand += r.requestedWatts;
            }

            // How much power do we want to draw from the cable network?
            float drain = Mathf.Min(demand / Mathf.Max(0.01f, efficiency), maxBroadcastWatts);
            _consumer.wattsPerSecond = drain;

            // Distribute what we actually GET (consumer.IsPowered tells us if network served us).
            CurrentBroadcastWatts = _consumer.IsPowered ? drain * efficiency : 0f;
            float perRecv = receivers.Count > 0 ? CurrentBroadcastWatts / receivers.Count : 0f;
            foreach (var r in receivers) r.SetReceivedThisFrame(perRecv);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.30f, 0.60f, 0.95f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
