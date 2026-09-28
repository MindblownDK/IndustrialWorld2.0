// Assets/Scripts/VoxelEngine/Building/Tiered/CodeLock.cs
//
// 13.17.0-dev: keypad code lock for doors, gates, garage doors and hatches.
// Crafted as an item, fitted with a right-click on the piece, operated through
// CodeLockHud. One lock per piece. The lock rides the leaf (or lid) it guards
// and shows its state on a light: red = locked, green = open to use.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building.Tiered
{
    public sealed class CodeLock : MonoBehaviour
    {
        public const string ItemId = "code_lock";

        public string code = "";
        public bool isLocked;
        public bool authorized;

        private Renderer _led;
        private bool _visualBuilt;

        public bool HasCode => !string.IsNullOrEmpty(code);

        /// <summary>True when the guarded piece may be operated without the keypad.</summary>
        public bool AllowsUse => !HasCode || !isLocked || authorized;

        public void ApplyCode(string newCode)
        {
            code = newCode;
            isLocked = true;
            authorized = true;
            RefreshLed();
        }

        public bool TryEnter(string attempt)
        {
            if (!HasCode || attempt != code) return false;
            authorized = true;
            RefreshLed();
            return true;
        }

        public void SetLockedState(bool locked)
        {
            isLocked = locked;
            RefreshLed();
        }

        public void RefreshLed()
        {
            if (_led == null) return;
            _led.material.color = HasCode && isLocked
                ? new Color(0.82f, 0.16f, 0.10f)
                : new Color(0.28f, 0.78f, 0.30f);
        }

        /// <summary>Detach the lock and hand the item back (authorized players only).</summary>
        public void RemoveAndRefund(Inventory inventory)
        {
            var item = ResolveItem();
            if (item != null && inventory != null) inventory.Add(item, 1);
            Destroy(gameObject);
        }

        /// <summary>Runtime-safe item lookup, same route the save system uses.</summary>
        public static ItemDefinition ResolveItem()
        {
            foreach (var catalog in Resources.LoadAll<ItemPersistenceCatalog>(""))
                if (catalog != null && catalog.items != null)
                    foreach (var item in catalog.items)
                        if (item != null && item.itemId == ItemId) return item;
            foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>())
                if (item != null && item.itemId == ItemId) return item;
            return null;
        }

        /// <summary>
        /// Fits a lock onto a lockable piece root. Returns null when the piece
        /// already carries one. Mount point and facing depend on the family:
        /// swinging leaves carry the lock along, the garage keeps it beside the
        /// opening, the hatch wears it flat on the lid.
        /// </summary>
        public static CodeLock Attach(GameObject pieceRoot)
        {
            if (pieceRoot == null) return null;
            if (pieceRoot.GetComponentInChildren<CodeLock>(true) != null) return null;

            var placed = pieceRoot.GetComponent<PlacedTieredBlock>();
            var family = placed != null && placed.definition != null
                ? placed.definition.family : BuildFamily.Door;

            Transform parent = pieceRoot.transform;
            Vector3 pos;
            Vector3 euler = Vector3.zero;
            float scale = 1f;
            switch (family)
            {
                case BuildFamily.Gate:
                    parent = FindChild(pieceRoot, "Generated_DoorHinge") ?? parent;
                    pos = new Vector3(2.05f, 1.85f, 0.40f);
                    scale = 1.35f;   // gates are big; the lock reads from a distance
                    break;
                case BuildFamily.BigGate:
                    parent = FindChild(pieceRoot, "Generated_DoorHinge") ?? parent;
                    pos = new Vector3(5.40f, 2.05f, 0.55f);
                    scale = 1.7f;
                    break;
                case BuildFamily.GarageDoor:
                    // On the side of the opening so it does not float in the
                    // gap once the shutter has rolled up into its drum.
                    pos = new Vector3(3.16f, 1.45f, 0.40f);
                    break;
                case BuildFamily.DoubleDoor:
                    parent = FindChild(pieceRoot, "Generated_DoorHinge") ?? parent;
                    pos = new Vector3(2.72f, 1.55f, 0.28f);
                    break;
                case BuildFamily.HatchLid:
                    parent = FindChild(pieceRoot, "Generated_HatchPivot") ?? parent;
                    pos = new Vector3(0.72f, 0.26f, 1.85f);
                    euler = new Vector3(-90f, 0f, 0f);   // keypad face up, flat on the lid
                    break;
                default:   // Door and anything door-like
                    parent = FindChild(pieceRoot, "Generated_DoorHinge") ?? parent;
                    pos = new Vector3(2.02f, 1.95f, 0.26f);
                    break;
            }

            var mount = new GameObject("Generated_CodeLock");
            mount.transform.SetParent(parent, false);
            mount.transform.localPosition = pos;
            mount.transform.localRotation = Quaternion.Euler(euler);
            mount.transform.localScale = Vector3.one * scale;
            return mount.AddComponent<CodeLock>();
        }

        private static Transform FindChild(GameObject root, string childName)
            => root.transform.Find(childName);

        private void Awake()
        {
            BuildVisual();
            RefreshLed();
        }

        private void BuildVisual()
        {
            if (_visualBuilt || transform.childCount > 0)
            {
                if (_led == null)
                {
                    var existing = transform.Find("LockLight");
                    if (existing != null) _led = existing.GetComponent<Renderer>();
                }
                _visualBuilt = true;
                return;
            }
            _visualBuilt = true;

            // Dark case; its collider doubles as the interaction target.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "LockBody";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.46f, 0.62f, 0.17f);
            Paint(body, new Color(0.145f, 0.175f, 0.15f));

            // Keypad studs on both faces so the lock reads from either side.
            foreach (float side in new[] { -1f, 1f })
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        var key = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        key.name = "LockKey";
                        key.transform.SetParent(transform, false);
                        key.transform.localPosition = new Vector3(
                            (col - 1) * 0.125f, 0.05f - row * 0.135f, side * 0.096f);
                        key.transform.localScale = new Vector3(0.095f, 0.10f, 0.03f);
                        Destroy(key.GetComponent<Collider>());
                        Paint(key, new Color(0.72f, 0.75f, 0.70f));
                    }

            var led = GameObject.CreatePrimitive(PrimitiveType.Cube);
            led.name = "LockLight";
            led.transform.SetParent(transform, false);
            led.transform.localPosition = new Vector3(0f, 0.245f, 0f);
            led.transform.localScale = new Vector3(0.34f, 0.075f, 0.185f);
            Destroy(led.GetComponent<Collider>());
            _led = led.GetComponent<Renderer>();
        }

        private static void Paint(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }
    }
}
