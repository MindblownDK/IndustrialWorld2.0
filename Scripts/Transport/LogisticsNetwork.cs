// Assets/Scripts/VoxelEngine/Transport/LogisticsNetwork.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Wireless request/fulfilment routing between logistic chests.
    ///
    /// 11.2.0-dev gave the chest a <c>portLock</c>: a Provider only ever outputs, a
    /// Requester only ever inputs. That made the intent of a chest explicit, but the items
    /// still had to walk — a Requester sitting across the base from a Provider stayed empty
    /// unless the player ran a pipe between them.
    ///
    /// This network closes that loop. Each Requester publishes its own request list, and the
    /// network moves matching stock from the nearest Provider that has it, within a fixed
    /// radius, at a metered rate. The request list is separate from the chest's port filters:
    /// a Requester's ports are OUTPUTS that feed the pipes downstream of it, while its request
    /// list decides what the network delivers in. A Provider is the mirror — its ports are
    /// INPUTS that pipes and belts fill, and the network hands that stock out wirelessly.
    ///
    /// Design notes:
    /// <list type="bullet">
    ///   <item>One singleton ticks every requester on a timer rather than each chest polling,
    ///         so cost scales with the number of logistic chests, not with frame rate.</item>
    ///   <item>Registration is explicit (the chest registers itself when it wakes), so there
    ///         is never a scene-wide search on the hot path.</item>
    ///   <item>A transfer is capped per tick, so a large Provider drains smoothly instead of
    ///         teleporting its whole contents in one frame.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class LogisticsNetwork : MonoBehaviour
    {
        public static LogisticsNetwork Instance { get; private set; }

        /// <summary>Seconds between fulfilment passes.</summary>
        public const float TickInterval = 1f;

        /// <summary>How far a Requester can reach for stock, in metres.</summary>
        public const float DefaultRange = 48f;

        /// <summary>Most items moved into one Requester in a single pass.</summary>
        public const int MaxItemsPerTransfer = 16;

        private readonly List<Chest> _providers = new();
        private readonly List<Chest> _requesters = new();
        private float _timer;

        /// <summary>Items moved by the last completed pass. Read by the panel for its readout.</summary>
        public int LastPassTransferred { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var existing = FindFirstObjectByType<LogisticsNetwork>();
            if (existing != null) { Instance = existing; return; }
            var go = new GameObject("LogisticsNetwork");
            Instance = go.AddComponent<LogisticsNetwork>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Registration ────────────────────────────────────────────────────

        /// <summary>
        /// Add a chest to the network under its current lock. Called when a logistic chest
        /// wakes and again whenever its lock changes, so a chest is always in exactly one
        /// list — or neither, when it is an ordinary free chest.
        /// </summary>
        public void Register(Chest chest)
        {
            if (chest == null) return;
            Unregister(chest);
            // A Buffer plays both parts, so it lands in BOTH lists. The fulfilment pass
            // guards against the obvious consequence (buffers feeding each other in circles)
            // by refusing buffer-to-buffer transfers.
            if (chest.SuppliesNetwork)      _providers.Add(chest);
            if (chest.RequestsFromNetwork)  _requesters.Add(chest);
        }

        public void Unregister(Chest chest)
        {
            if (chest == null) return;
            _providers.Remove(chest);
            _requesters.Remove(chest);
        }

        // ── Fulfilment pass ─────────────────────────────────────────────────

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < TickInterval) return;
            _timer = 0f;
            RunFulfilmentPass();
        }

        /// <summary>
        /// Move stock from Providers into every Requester that is asking for something.
        /// Public so the setup harness and the panel's manual refresh can drive one pass
        /// without waiting for the timer.
        /// </summary>
        public void RunFulfilmentPass()
        {
            PruneDestroyed();
            LastPassTransferred = 0;
            if (_providers.Count == 0 || _requesters.Count == 0) return;

            foreach (var requester in _requesters)
            {
                if (requester == null || requester.container == null) continue;
                LastPassTransferred += FulfilOne(requester);
            }
        }

        /// <summary>
        /// Satisfy a single Requester. Returns how many items arrived.
        /// </summary>
        private int FulfilOne(Chest requester)
        {
            // The chest's own request list — deliberately NOT its port filters, which
            // govern what leaves it down a pipe rather than what the network brings in.
            var wanted = requester.Requests;
            if (wanted.Count == 0) return 0;

            int moved = 0;
            foreach (var item in wanted)
            {
                if (item == null) continue;
                if (!requester.container.HasSpace(item, 1)) continue;

                // A plain Requester pulls without limit; a Buffer only tops up to its stock
                // target, so it cannot drain the providers it also supplies from.
                int shortfall = requester.ShortfallOf(item);
                if (shortfall <= 0) continue;

                var provider = FindNearestProviderWith(requester, item);
                if (provider == null) continue;

                moved += Transfer(provider, requester, item, shortfall);
            }
            return moved;
        }

        /// <summary>
        /// The closest in-range Provider holding <paramref name="item"/>. Distance is
        /// measured squared to keep the scan allocation- and sqrt-free.
        /// </summary>
        private Chest FindNearestProviderWith(Chest requester, ItemDefinition item)
        {
            Chest best = null;
            float bestSqr = DefaultRange * DefaultRange;
            Vector3 origin = requester.transform.position;

            foreach (var provider in _providers)
            {
                if (provider == null || provider.container == null) continue;
                if (provider == requester) continue;

                // A Buffer must never be supplied BY another Buffer: two buffers each
                // wanting the same item would otherwise pass one stack back and forth
                // forever, and a chain of them would drain the real providers unevenly.
                // Buffers are stocked by true Providers only.
                if (provider.portLock == PortLockMode.Buffer &&
                    requester.portLock == PortLockMode.Buffer) continue;

                float sqr = (provider.transform.position - origin).sqrMagnitude;
                if (sqr > bestSqr) continue;
                if (CountOf(provider.container, item) <= 0) continue;

                bestSqr = sqr;
                best = provider;
            }
            return best;
        }

        /// <summary>
        /// Move up to <see cref="MaxItemsPerTransfer"/> of one item, and never more than
        /// <paramref name="limit"/> (the destination's remaining shortfall). The removal is driven by
        /// what the destination actually accepted, so a full Requester can never make stock
        /// disappear from the Provider.
        /// </summary>
        private static int Transfer(Chest from, Chest to, ItemDefinition item, int limit)
        {
            // Move the Provider's OWN item instance, not the definition the Requester asked
            // with. The container's Remove compares by reference, so handing it a different
            // asset that merely means the same thing would remove nothing and duplicate the
            // stock into the Requester.
            var stocked = FindStocked(from.container, item);
            if (stocked == null) return 0;

            int available = CountOf(from.container, stocked);
            if (available <= 0) return 0;

            int want = Mathf.Min(Mathf.Min(available, MaxItemsPerTransfer), limit);
            if (want <= 0) return 0;

            item = stocked;

            // Count through the identity-aware helper on both sides: the container's own
            // CountOf compares by reference, which would miscount the moment two assets
            // describe the same item.
            int before   = CountOf(to.container, item);
            to.container.Insert(new ItemStack(item, want));
            int accepted = CountOf(to.container, item) - before;
            if (accepted <= 0) return 0;

            from.container.Remove(item, accepted);
            return accepted;
        }

        /// <summary>
        /// The container's own asset instance for an item that merely matches by identity.
        /// Returns null when the container holds none of it.
        /// </summary>
        private static ItemDefinition FindStocked(ItemContainer container, ItemDefinition item)
        {
            if (container == null || item == null) return null;
            for (int i = 0; i < container.Size; i++)
            {
                var slot = container.GetSlot(i);
                if (!slot.IsEmpty && ItemIdentity.Same(slot.item, item)) return slot.item;
            }
            return null;
        }

        /// <summary>Total count of an item in a container, matched by identity rather than reference.</summary>
        private static int CountOf(ItemContainer container, ItemDefinition item)
        {
            if (container == null || item == null) return 0;
            int total = 0;
            for (int i = 0; i < container.Size; i++)
            {
                var slot = container.GetSlot(i);
                if (slot.IsEmpty || !ItemIdentity.Same(slot.item, item)) continue;
                total += slot.count;
            }
            return total;
        }

        /// <summary>Drop chests destroyed since the last pass without leaving holes in the lists.</summary>
        private void PruneDestroyed()
        {
            for (int i = _providers.Count - 1; i >= 0; i--)
                if (_providers[i] == null) _providers.RemoveAt(i);
            for (int i = _requesters.Count - 1; i >= 0; i--)
                if (_requesters[i] == null) _requesters.RemoveAt(i);
        }

        // ── Readouts for the panel ──────────────────────────────────────────

        /// <summary>
        /// Every registered logistic chest, providers first then requesters. A Buffer appears
        /// in both lists, so callers that care about identity must de-duplicate; callers that
        /// test <c>SuppliesNetwork</c> / <c>RequestsFromNetwork</c> per chest (as the drone
        /// ports do) are unaffected. Exposed so the drone layer can find the chests near a
        /// port without repeating the registration bookkeeping.
        /// </summary>
        public IEnumerable<Chest> AllChests
        {
            get
            {
                PruneDestroyed();
                foreach (var c in _providers) yield return c;
                foreach (var c in _requesters)
                    if (!c.SuppliesNetwork) yield return c;   // a Buffer was already yielded above
            }
        }

        public int ProviderCount  { get { PruneDestroyed(); return _providers.Count; } }
        public int RequesterCount { get { PruneDestroyed(); return _requesters.Count; } }

        /// <summary>
        /// How much of <paramref name="item"/> is reachable from <paramref name="requester"/>
        /// across every in-range Provider. The panel shows this so an unfulfilled request
        /// reads as "none in range" rather than looking broken.
        /// </summary>
        public int AvailableFor(Chest requester, ItemDefinition item)
        {
            if (requester == null || item == null) return 0;
            PruneDestroyed();

            int total = 0;
            float rangeSqr = DefaultRange * DefaultRange;
            Vector3 origin = requester.transform.position;

            foreach (var provider in _providers)
            {
                if (provider == null || provider == requester) continue;
                if (provider.portLock == PortLockMode.Buffer &&
                    requester.portLock == PortLockMode.Buffer) continue;   // matches FindNearestProviderWith
                if ((provider.transform.position - origin).sqrMagnitude > rangeSqr) continue;
                total += CountOf(provider.container, item);
            }
            return total;
        }
    }
}
