// Assets/Scripts/VoxelEngine/Items/DeathLootBag.cs
//
// 14.36.0-dev - Death drops its dues. When a player dies, everything they
// carried (hotbar + backpack, 40 slots) spills into ONE loot bag at the
// death position instead of vanishing with the corpse.
//
// The bag remembers WHICH slot every stack came from: its container is the
// same 40-slot shape as the player inventory, items parked at their original
// indices, so TAKE ALL puts every stack back exactly where it was lost from.
//
// Ownership is a courtesy, not a lock: ANYONE who finds the bag may loot it
// (full-loot PvP - a kill is worth the fight), but only the OWNER sees the
// recovery beacon: a light column over the bag, rendered purely locally and
// only within 1 km. Nothing about the beacon travels the wire - every machine
// knows the bag's owner and simply refuses to draw the column for anyone else.
//
// Lifetime: a bag never expires and persists with the world save (unlike
// DroppedItem's 300 s). It despawns the moment its last stack is taken.
//
// Networking rides BagSync (the DropSync pattern): the dying machine spawns
// and announces, every mutation re-announces the whole payload (a bag is
// small and rare - whole-state is simpler than deltas at 2-8 players), and
// a join snapshot carries all live bags.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Items
{
    public class DeathLootBag : MonoBehaviour
    {
        public const float BeaconVisibleRangeM = 1000f;

        /// <summary>Wire/save identity, "playerId:bag:serial".</summary>
        public string BagId { get; private set; }
        /// <summary>Stable player id of the player who died.</summary>
        public string OwnerId { get; private set; }
        /// <summary>Display name at the time of death (for the panel title).</summary>
        public string OwnerName { get; private set; }

        /// <summary>40 slots mirroring the player inventory layout - stacks sit
        /// at the index they were lost from.</summary>
        public ItemContainer container;

        private static readonly Dictionary<string, DeathLootBag> _byId = new();
        private static long _serial;

        private bool _mutating;          // guard: bulk edits announce once, not per slot
        private GameObject _beacon;      // owner-only light column
        private Light _beaconLight;
        private float _nextBeaconCheck;

        public static IEnumerable<DeathLootBag> All => _byId.Values;

        public static DeathLootBag Find(string id)
            => !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var bag) && bag != null ? bag : null;

        // ─────────────────────────────────────────────────────────────────
        //  Spawning
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Death seam: move every carried stack of the local player
        /// into a fresh bag at their feet. No items carried = no bag.</summary>
        public static void SpawnFromDeath(Component playerRoot)
        {
            if (playerRoot == null) return;
            var inv = playerRoot.GetComponent<Inventory>();
            if (inv == null || inv.container == null) return;

            bool anything = false;
            for (int i = 0; i < inv.container.Size; i++)
                if (!inv.container.GetSlot(i).IsEmpty) { anything = true; break; }
            if (!anything) return;

            string ownerId = Networking.PlayerIdentity.LocalId;
            string ownerName = Networking.PlayerIdentity.LocalName;
            string id = ownerId + ":bag:" + DateTime.UtcNow.Ticks + ":" + _serial++;

            var bag = Create(id, ownerId, ownerName, playerRoot.transform.position);
            bag._mutating = true;
            for (int i = 0; i < inv.container.Size && i < bag.container.Size; i++)
            {
                var s = inv.container.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                bag.container.SetSlot(i, s);
                inv.container.SetSlot(i, new ItemStack());
            }
            bag._mutating = false;
            inv.container.RaiseChanged();

            Networking.BagSync.AnnounceSpawned(bag);
            UI.BuildFeedbackHud.Show("Death",
                "Your belongings dropped in a bag - follow the beacon", null,
                new Color(0.95f, 0.75f, 0.25f));
        }

        /// <summary>Remote/snapshot/save spawn: a bag that already has an
        /// identity and a serialized payload.</summary>
        public static DeathLootBag SpawnExisting(string id, string ownerId,
            string ownerName, Vector3 pos, string payloadJson)
        {
            var existing = Find(id);
            if (existing != null) { existing.ApplyPayloadJson(payloadJson); return existing; }
            var bag = Create(id, ownerId, ownerName, pos);
            bag.ApplyPayloadJson(payloadJson);
            return bag;
        }

        private static DeathLootBag Create(string id, string ownerId, string ownerName, Vector3 pos)
        {
            var go = new GameObject("DeathLootBag " + (ownerName ?? "?"));
            var bag = go.AddComponent<DeathLootBag>();
            bag.BagId = id;
            bag.OwnerId = ownerId ?? "";
            bag.OwnerName = string.IsNullOrEmpty(ownerName) ? "Fallen Crusader" : ownerName;
            bag.container = new ItemContainer(bag.OwnerName + "'s Remains", Inventory.TOTAL_SIZE);
            bag.container.OnChanged += bag.OnContainerChanged;

            // Settle onto the ground along local gravity so a mid-air death
            // doesn't leave the bag floating.
            Vector3 up = Cosmos.GravityProvider.GetUp(pos);
            if (Physics.Raycast(pos + up * 0.6f, -up, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore))
                pos = ground.point;
            go.transform.position = pos;
            go.transform.up = up;

            bag.BuildVisual();
            _byId[id] = bag;
            return bag;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Looting
        // ─────────────────────────────────────────────────────────────────

        /// <summary>TAKE ALL: every stack returns to the exact slot it was lost
        /// from; an occupied slot falls back to normal insertion; whatever
        /// cannot fit stays in the bag.</summary>
        public void TakeAll(Inventory inv)
        {
            if (inv == null || inv.container == null) return;
            _mutating = true;
            bool leftover = false;
            for (int i = 0; i < container.Size; i++)
            {
                var s = container.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                if (i < inv.container.Size && inv.container.GetSlot(i).IsEmpty)
                {
                    inv.container.SetSlot(i, s);
                    container.SetSlot(i, new ItemStack());
                    continue;
                }
                var rest = inv.container.Insert(s);
                container.SetSlot(i, rest ?? new ItemStack());
                if (rest != null && !rest.IsEmpty) leftover = true;
            }
            inv.container.RaiseChanged();
            container.RaiseChanged();   // UI watchers refresh; announce comes once below
            _mutating = false;
            if (leftover)
                UI.BuildFeedbackHud.Show("Loot bag",
                    "Some items did not fit - the rest stays in the bag", null, Color.yellow);
            AfterMutation();
        }

        private void OnContainerChanged()
        {
            if (_mutating) return;    // bulk edit announces once at the end
            AfterMutation();
        }

        private void AfterMutation()
        {
            for (int i = 0; i < container.Size; i++)
                if (!container.GetSlot(i).IsEmpty)
                {
                    Networking.BagSync.AnnounceUpdated(this);
                    return;
                }
            Despawn();   // emptied: the bag is done
        }

        /// <summary>Remove this bag. Announces unless a remote event is being
        /// applied (BagSync gates that), then destroys the object.</summary>
        public void Despawn()
        {
            Networking.BagSync.HandleDespawn(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (!string.IsNullOrEmpty(BagId) && _byId.TryGetValue(BagId, out var cur) && cur == this)
                _byId.Remove(BagId);
            UI.GameUIController.Instance?.NotifyLootBagGone(this);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Payload (wire + save share one format)
        // ─────────────────────────────────────────────────────────────────

        [Serializable] private class BagPayload { public List<BagSlotRecord> slots = new(); }
        [Serializable] private class BagSlotRecord { public int slot; public string json; }

        public string ToPayloadJson()
        {
            var persistence = Persistence.WorldStatePersistence.Instance;
            if (persistence == null) return "";
            var payload = new BagPayload();
            for (int i = 0; i < container.Size; i++)
            {
                var s = container.GetSlot(i);
                if (s == null || s.IsEmpty) continue;
                payload.slots.Add(new BagSlotRecord { slot = i, json = persistence.CaptureStackJson(s) });
            }
            return JsonUtility.ToJson(payload);
        }

        public void ApplyPayloadJson(string json)
        {
            var persistence = Persistence.WorldStatePersistence.Instance;
            if (persistence == null || string.IsNullOrEmpty(json)) return;
            BagPayload payload = null;
            try { payload = JsonUtility.FromJson<BagPayload>(json); } catch { }
            if (payload == null) return;

            _mutating = true;
            for (int i = 0; i < container.Size; i++) container.SetSlot(i, new ItemStack());
            foreach (var rec in payload.slots)
            {
                if (rec == null || rec.slot < 0 || rec.slot >= container.Size) continue;
                var stack = persistence.RestoreStackJson(rec.json);
                if (stack != null && !stack.IsEmpty) container.SetSlot(rec.slot, stack);
            }
            container.RaiseChanged();   // UI watchers refresh; the guard below stays up
            _mutating = false;

            // A remote update can empty the bag - vanish without re-announcing.
            for (int i = 0; i < container.Size; i++)
                if (!container.GetSlot(i).IsEmpty) return;
            Despawn();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Visual: the sack for everyone, the beacon for the owner alone
        // ─────────────────────────────────────────────────────────────────

        private void BuildVisual()
        {
            var leather = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { color = new Color(0.33f, 0.23f, 0.13f) };
            var rope = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { color = new Color(0.55f, 0.45f, 0.28f) };

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(body.GetComponent<Collider>());
            body.name = "Sack";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.62f, 0.48f, 0.62f);
            body.transform.localPosition = new Vector3(0f, 0.24f, 0f);
            body.GetComponent<MeshRenderer>().sharedMaterial = leather;

            var knot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(knot.GetComponent<Collider>());
            knot.name = "Knot";
            knot.transform.SetParent(transform, false);
            knot.transform.localScale = new Vector3(0.16f, 0.14f, 0.16f);
            knot.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            knot.GetComponent<MeshRenderer>().sharedMaterial = rope;

            // One collider on the root so the interact ray resolves the bag
            // from any part of the sack.
            var col = gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.3f, 0f);
            col.radius = 0.55f;

            // The owner-only recovery beacon: a tall translucent column plus a
            // warm light. Built for everyone, DRAWN for the owner alone (see
            // Update) - visibility is a local decision, never wire state.
            _beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_beacon.GetComponent<Collider>());
            _beacon.name = "OwnerBeacon";
            _beacon.transform.SetParent(transform, false);
            _beacon.transform.localScale = new Vector3(0.14f, 14f, 0.14f);
            _beacon.transform.localPosition = new Vector3(0f, 14f, 0f);
            var beamRenderer = _beacon.GetComponent<MeshRenderer>();
            beamRenderer.sharedMaterial = Rendering.RuntimeMaterials.MakeTranslucentLit(
                new Color(0.98f, 0.76f, 0.22f, 0.38f));
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lightGo = new GameObject("BeaconLight");
            lightGo.transform.SetParent(_beacon.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.95f, 0f); // near the bag
            _beaconLight = lightGo.AddComponent<Light>();
            _beaconLight.type = LightType.Point;
            _beaconLight.color = new Color(0.98f, 0.76f, 0.22f);
            _beaconLight.range = 7f;
            _beaconLight.intensity = 2.2f;

            _beacon.SetActive(false);
        }

        private void Update()
        {
            if (_beacon == null || Time.time < _nextBeaconCheck) return;
            _nextBeaconCheck = Time.time + 0.5f;   // a gate, not an effect - 2 Hz is plenty

            bool visible = false;
            if (Networking.PlayerIdentity.LocalId == OwnerId)
            {
                var player = Player.PlayerStats.Instance;
                if (player != null)
                    visible = (player.transform.position - transform.position).sqrMagnitude
                              <= BeaconVisibleRangeM * BeaconVisibleRangeM;
            }
            if (_beacon.activeSelf != visible) _beacon.SetActive(visible);
        }
    }
}
