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

        // 17.20.0-dev perf: this Update used to run FindObjectsByType over the
        // WHOLE scene and allocate a fresh List on EVERY transmitter EVERY frame
        // - a steady GC drip that shows up as periodic hitching. The scene scan
        // now runs on a slow staggered clock into reused buffers; the per-frame
        // part is only the cached-list walk and the delivery itself, which must
        // stay per-frame because receivers zero their intake each LateUpdate.
        private const float ScanInterval = 0.5f;
        private readonly List<PowerReceiver> _receivers = new();
        private readonly List<PowerReceiver> _scanBuffer = new();
        private float _nextScan;

        private void Awake()
        {
            _consumer = GetComponent<PowerConsumer>();
            if (_consumer == null) _consumer = gameObject.AddComponent<PowerConsumer>();
            _nextScan = Time.time + Random.Range(0f, ScanInterval);   // stagger co-located transmitters
        }

        private void Update()
        {
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + ScanInterval;
                Rescan();
            }

            Vector3 here = transform.position;
            float rangeSqr = range * range;

            // Pass 1: total the live demand of the cached in-range receivers.
            float demand = 0f;
            int inRange = 0;
            for (int i = _receivers.Count - 1; i >= 0; i--)
            {
                var r = _receivers[i];
                if (r == null) { _receivers.RemoveAt(i); continue; }   // destroyed since the scan
                if ((r.transform.position - here).sqrMagnitude > rangeSqr) continue;
                inRange++;
                demand += r.requestedWatts;
            }

            // How much power do we want to draw from the cable network?
            float drain = Mathf.Min(demand / Mathf.Max(0.01f, efficiency), maxBroadcastWatts);
            _consumer.wattsPerSecond = drain;

            // Distribute what we actually GET (consumer.IsPowered tells us if network served us).
            CurrentBroadcastWatts = _consumer.IsPowered ? drain * efficiency : 0f;
            float perRecv = inRange > 0 ? CurrentBroadcastWatts / inRange : 0f;
            for (int i = 0; i < _receivers.Count; i++)
            {
                var r = _receivers[i];
                if (r == null) continue;
                if ((r.transform.position - here).sqrMagnitude > rangeSqr) continue;
                r.SetReceivedThisFrame(perRecv);
            }
        }

        private void Rescan()
        {
            _receivers.Clear();
            Vector3 here = transform.position;
            float rangeSqr = range * range;
            var all = Object.FindObjectsByType<PowerReceiver>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if ((r.transform.position - here).sqrMagnitude > rangeSqr) continue;
                _receivers.Add(r);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.30f, 0.60f, 0.95f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
