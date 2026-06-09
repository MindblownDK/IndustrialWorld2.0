// Assets/Scripts/VoxelEngine/Items/DroppedItem.cs
//
// Physical world-drop item. ALWAYS visible — uses a hardcoded bright material.

using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelEngine.Items
{
    public class DroppedItem : MonoBehaviour
    {
        public ItemStack stack;
        public float lifetime = 300f;

        private float _spawnTime;
        // Longer pickup grace so a player who drops an item can SEE it before
        // walking forward and instantly re-picking it (the previous 0.8s was
        // shorter than the typical post-drop step).
        private float _pickupDelay = 1.5f;
        private float _bobPhase;
        private Rigidbody _rb;
        private bool _settled;

        public static DroppedItem Spawn(ItemStack stack, Vector3 position, Vector3 tossDir)
        {
            if (stack == null || stack.IsEmpty) return null;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Drop_{stack.item.displayName}";
            // Bigger drop cube (0.5m vs 0.35m) so it's clearly visible at typical
            // player viewing distances. Also lift the spawn just above the toss
            // position so it never embeds in the floor / player capsule.
            go.transform.position = position + Vector3.up * 0.25f;
            go.transform.localScale = Vector3.one * 0.5f;
            go.layer = 0;

            // Force a 100% visible opaque material that works in URP/Standard/HDRP.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Color c = stack.item != null ? stack.item.iconTint : Color.white;
                // Guarantee enough brightness and full opacity.
                float brightness = c.r + c.g + c.b;
                if (c.a < 0.5f || brightness < 0.15f)
                    c = new Color(0.72f, 0.72f, 0.78f, 1f);
                c.a = 1f;

                // Prefer URP Unlit (no lighting math, always visible) then Simple Lit then Standard.
                string[] preferred = {
                    "Universal Render Pipeline/Unlit",
                    "Unlit/Color",
                    "Universal Render Pipeline/Simple Lit",
                    "Universal Render Pipeline/Lit",
                    "Standard"
                };
                Shader chosenShader = null;
                foreach (var sn in preferred)
                {
                    chosenShader = Shader.Find(sn);
                    if (chosenShader != null) break;
                }
                if (chosenShader == null)
                    chosenShader = Shader.Find("Hidden/InternalErrorShader");

                var mat = new Material(chosenShader);
                // Set every colour property that might exist on this shader.
                if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color"))       mat.SetColor("_Color", c);
                if (mat.HasProperty("_UnlitColor"))  mat.SetColor("_UnlitColor", c);
                // Ensure fully opaque surface mode.
                if (mat.HasProperty("_Surface"))     mat.SetFloat("_Surface", 0f);   // 0 = Opaque
                if (mat.HasProperty("_Blend"))       mat.SetFloat("_Blend",   0f);
                if (mat.HasProperty("_AlphaClip"))   mat.SetFloat("_AlphaClip", 0f);
                mat.renderQueue = -1; // default opaque queue
                mr.material             = mat;
                mr.shadowCastingMode    = ShadowCastingMode.Off;
                mr.receiveShadows       = false;
                mr.enabled              = true;
            }

            // Rigidbody for physics toss.
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.linearDamping = 3f;
            rb.angularDamping = 4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = tossDir.normalized * 2.5f + Vector3.up * 3f;

            // Larger trigger for walk-over pickup.
            var trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 2.5f;

            var di = go.AddComponent<DroppedItem>();
            di.stack = stack.Clone();
            di._spawnTime = Time.time;
            di._bobPhase = Random.value * Mathf.PI * 2f;
            di._rb = rb;

            Debug.Log($"[DroppedItem] Spawned {stack.item.displayName} x{stack.count} at {position}");
            return di;
        }

        private void Update()
        {
            if (Time.time - _spawnTime > lifetime) { Destroy(gameObject); return; }

            if (_rb != null && !_settled && _rb.linearVelocity.sqrMagnitude < 0.1f &&
                Time.time - _spawnTime > 1.5f)
            {
                _settled = true;
                _rb.isKinematic = true;
            }

            if (_settled)
            {
                float bob = Mathf.Sin(Time.time * 2.5f + _bobPhase) * 0.06f;
                transform.position += Vector3.up * bob * Time.deltaTime;
                transform.Rotate(Vector3.up, 50f * Time.deltaTime, Space.World);
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (Time.time - _spawnTime < _pickupDelay) return;
            if (stack == null || stack.IsEmpty) return;
            var inv = other.GetComponentInParent<Inventory>();
            if (inv != null) TryPickup(inv);
        }

        public bool TryPickup(Inventory inv)
        {
            if (inv == null || stack == null || stack.IsEmpty) return false;
            var leftover = inv.container.Insert(stack.Clone());
            if (leftover == null || leftover.count <= 0)
            {
                UI.BuildFeedbackHud.Show($"Picked up {stack.item.displayName}",
                    $"+{stack.count}", stack.item.icon, new Color(0.30f, 0.75f, 0.40f));
                FX.AudioManager.PlayUI(FX.SfxLibrary.Get(FX.Sfx.Pickup), 0.45f,
                    UnityEngine.Random.Range(0.97f, 1.06f));
                Destroy(gameObject);
                return true;
            }
            int picked = stack.count - leftover.count;
            if (picked > 0)
            {
                UI.BuildFeedbackHud.Show($"Picked up {stack.item.displayName}",
                    $"+{picked}", stack.item.icon, new Color(0.30f, 0.75f, 0.40f));
                FX.AudioManager.PlayUI(FX.SfxLibrary.Get(FX.Sfx.Pickup), 0.45f,
                    UnityEngine.Random.Range(0.97f, 1.06f));
                stack.count = leftover.count;
            }
            return false;
        }
    }
}
