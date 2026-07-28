// Assets/Scripts/VoxelEngine/Player/HeldToolView.cs
//
// Viewmodel for whatever the player has in their active hotbar slot.
// Lives as a child of the camera; bottom-right offset; swings on tool hit.
//
// Auto-generates a primitive mesh per item type if no custom viewmodel prefab exists.
// You can later add a per-item field "viewmodelPrefab" on ItemDefinition to override.

using System.Collections;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Player
{
    public class HeldToolView : MonoBehaviour
    {
        [Header("Refs")]
        public Inventory inventory;
        public Transform anchor;          // attach point on the camera; auto-created if null

        [Header("Pose")]
        public Vector3 idleLocalPos = new Vector3(0.30f, -0.28f, 0.55f);
        public Vector3 idleLocalEuler = new Vector3(8, -15, -5);
        public Vector3 swingLocalPos = new Vector3(0.10f, -0.10f, 0.45f);
        public Vector3 swingLocalEuler = new Vector3(-30, -30, -10);
        public float swingDuration = 0.18f;

        [Header("Bobbing (idle / walking)")]
        public float bobAmplitude = 0.012f;
        public float bobSpeedWalking = 8f;

        // Internals
        private GameObject _viewModel;
        private ItemDefinition _shownItem;
        private Coroutine _swing;
        private float _bobT;
        private CharacterController _cc;
        private PlayerController _pc;
        private bool   _wasGrounded;
        private float  _landDip;
        private float  _recoilT;

        private void Awake()
        {
            if (anchor == null)
            {
                anchor = new GameObject("ViewmodelAnchor").transform;
                anchor.SetParent(transform, false);
            }
            anchor.localPosition = idleLocalPos;
            anchor.localRotation = Quaternion.Euler(idleLocalEuler);
            _cc = GetComponentInParent<CharacterController>();
            _pc = GetComponentInParent<PlayerController>();
        }

        private void Start()
        {
            // Wait until Start so Inventory.Awake (which initialises the container) has run.
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (inventory != null)
            {
                inventory.OnActiveSlotChanged += Refresh;
                if (inventory.container != null)
                    inventory.container.OnChanged += Refresh;
            }
            Refresh();
        }
        private void OnDestroy()
        {
            if (inventory != null)
            {
                inventory.OnActiveSlotChanged -= Refresh;
                inventory.container.OnChanged -= Refresh;
            }
        }

        private void Update()
        {
            if (anchor == null) return;
            float dt = Time.deltaTime;
            Vector3 pos = idleLocalPos;
            Quaternion rot = Quaternion.Euler(idleLocalEuler);

            bool grounded = _pc != null ? _pc.IsGrounded : (_cc != null && _cc.isGrounded);
            float hSpeed = _cc != null ? new Vector2(_cc.velocity.x, _cc.velocity.z).magnitude : 0f;
            float vSpeed = _cc != null ? _cc.velocity.y : 0f;
            bool sprint = _pc != null && _pc.IsSprinting;
            bool slide  = _pc != null && _pc.IsSliding;
            bool fly    = _pc != null && _pc.IsFlying;

            // Landing impact dip (triggered the frame we touch down).
            if (grounded && !_wasGrounded && _pc != null)
                _landDip = Mathf.Clamp01(_pc.LastAirDownSpeed / 14f);
            _wasGrounded = grounded;
            _landDip = Mathf.MoveTowards(_landDip, 0f, dt * 4f);

            if (fly)
            {
                // Fly: gentle drift sway + slight nose-up tilt.
                _bobT += dt * 3f;
                pos += new Vector3(Mathf.Sin(_bobT * 0.7f) * 0.012f, Mathf.Sin(_bobT) * 0.006f, 0f);
                rot *= Quaternion.Euler(-12f, 0f, Mathf.Sin(_bobT * 0.5f) * 4f);
            }
            else if (!grounded)
            {
                // Air (jump/fall): drop slightly, tilt with vertical velocity.
                pos += new Vector3(0f, -0.04f, 0.02f);
                rot *= Quaternion.Euler(Mathf.Clamp(-vSpeed * 1.5f, -30f, 30f), 0f, 0f);
            }
            else
            {
                // Grounded locomotion bob (idle/walk/run).
                _bobT += dt * (1f + hSpeed * 0.45f) * bobSpeedWalking;
                float amp = bobAmplitude * Mathf.Clamp01(hSpeed * 0.3f) * (sprint ? 1.5f : 1f);
                pos += new Vector3(0f, Mathf.Sin(_bobT) * amp, 0f);
                if (sprint) { pos += new Vector3(0f, 0f, 0.03f); rot *= Quaternion.Euler(6f, 0f, 0f); }   // forward lean
                if (slide)  { rot *= Quaternion.Euler(-18f, 0f, 8f); pos += new Vector3(0f, -0.05f, 0f); } // slide lean
            }

            // Landing dip + decaying ranged recoil layered on top.
            pos += new Vector3(0f, -_landDip * 0.05f, 0f);
            rot *= Quaternion.Euler(_landDip * 15f, 0f, 0f);
            if (_recoilT > 0f)
            {
                _recoilT = Mathf.MoveTowards(_recoilT, 0f, dt * 6f);
                rot *= Quaternion.Euler(_recoilT * 20f, 0f, 0f);
                pos += new Vector3(0f, 0f, _recoilT * 0.04f);
            }

            // A running melee swing coroutine owns the anchor until it finishes.
            if (_swing == null)
            {
                anchor.localPosition = pos;
                anchor.localRotation = rot;
            }
        }

        public void Refresh()
        {
            if (inventory == null || inventory.container == null) return;
            ItemStack stack;
            try { stack = inventory.ActiveStack; }
            catch { return; }                            // container may not be ready yet
            ItemDefinition item = (stack == null || stack.IsEmpty) ? null : stack.item;

            if (item == _shownItem && _viewModel != null) return;
            if (_viewModel != null) Destroy(_viewModel);
            _shownItem = item;
            if (item == null) return;

            // Use custom prefab if assigned, otherwise fall back to procedural builder
            if (item.viewmodelPrefab != null)
            {
                _viewModel = Instantiate(item.viewmodelPrefab);
            }
            else
            {
                _viewModel = BuildViewmodelFor(item);
            }

            _viewModel.transform.SetParent(anchor, false);
            _viewModel.transform.localPosition = Vector3.zero;
            _viewModel.transform.localRotation = Quaternion.identity;

            // Make every renderer ignore world lighting overrides + put on a high render queue
            // so it draws on top of nothing weird (still respects depth though).
            foreach (var r in _viewModel.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // Turn off any colliders inside the viewmodel.
            foreach (var c in _viewModel.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
        }

        // Called by ToolFeedback after a successful hit.
        public void DoSwing()
        {
            if (_viewModel == null) return;
            if (_swing != null) StopCoroutine(_swing);
            _swing = StartCoroutine(SwingRoutine());
        }

        // Called by the combat hook when a ranged weapon is fired.
        public void DoRecoil() { _recoilT = 1f; }

        private IEnumerator SwingRoutine()
        {
            Vector3 baseP = idleLocalPos;
            Quaternion baseR = Quaternion.Euler(idleLocalEuler);
            Vector3 toP   = swingLocalPos;
            Quaternion toR = Quaternion.Euler(swingLocalEuler);

            // Forward (impact)
            float t = 0f;
            float halfDur = swingDuration * 0.4f;
            while (t < halfDur)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0, 1, t / halfDur);
                anchor.localPosition = Vector3.Lerp(baseP, toP, u);
                anchor.localRotation = Quaternion.Slerp(baseR, toR, u);
                yield return null;
            }
            // Recover
            t = 0f;
            float recDur = swingDuration * 0.6f;
            while (t < recDur)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0, 1, t / recDur);
                anchor.localPosition = Vector3.Lerp(toP, baseP, u);
                anchor.localRotation = Quaternion.Slerp(toR, baseR, u);
                yield return null;
            }
            anchor.localPosition = baseP;
            anchor.localRotation = baseR;
            _swing = null;
        }

        // ============================================================
        //          Procedural viewmodel mesh builder (fallback)
        // ============================================================
        private static GameObject BuildViewmodelFor(ItemDefinition item)
        {
            var root = new GameObject("Held_" + item.name);
            
            // 1. If we have an icon but no prefab, use a Quad with the icon sprite
            if (item.icon != null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.SetParent(root.transform, false);
                quad.transform.localScale = new Vector3(0.25f, 0.25f, 1f);
                quad.transform.localRotation = Quaternion.Euler(0, 180, 0); // Face player
                
                var renderer = quad.GetComponent<Renderer>();
                // Use a basic transparent shader
                var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
                var mat = new Material(sh);
                if (item.icon.texture != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", item.icon.texture);
                    else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", item.icon.texture);
                }
                
                // Set transparency
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent in URP
                mat.renderQueue = 3000;
                renderer.sharedMaterial = mat;
                
                var col = quad.GetComponent<Collider>();
                if (col != null) { col.enabled = false; Object.Destroy(col); }
                
                return root;
            }

            Color tint = item.iconTint;
            switch (item)
            {
                case ToolItem tool when tool.toolType == ToolType.Pickaxe:
                    BuildPickaxe(root, tint, tool.miningTier);
                    break;
                case ToolItem tool when tool.toolType == ToolType.Axe:
                    BuildAxe(root, tint, tool.miningTier);
                    break;
                case ToolItem tool:
                    BuildSword(root, tint);
                    break;
                case BlockItem bi:
                    BuildBlockCube(root, tint);
                    // If the block has a custom material/texture, apply it to the viewmodel cube too.
                    if (bi.placedMaterial != null || bi.texture != null)
                    {
                        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        {
                            if (bi.placedMaterial != null) r.sharedMaterial = bi.placedMaterial;
                            if (bi.texture != null)
                            {
                                var m = r.sharedMaterial;
                                if (m != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", bi.texture);
                                else if (m != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", bi.texture);
                            }
                        }
                    }
                    break;
                default:
                    BuildSphere(root, tint);
                    break;
            }
            return root;
        }

        // ----- primitives -----
        private static GameObject AddPrimitive(GameObject parent, PrimitiveType t, Vector3 pos, Vector3 euler, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(t);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(sh) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            // Strip collider for safety. Use Destroy (NOT DestroyImmediate): the viewmodel is
            // rebuilt from inside item-pickup, which happens during a physics OnTriggerStay
            // callback where DestroyImmediate is illegal. Disable it now so it's inert this frame.
            var col = go.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            return go;
        }

        private static void BuildPickaxe(GameObject root, Color tint, int tier)
        {
            // Handle (cylinder)
            AddPrimitive(root, PrimitiveType.Cylinder,
                pos: new Vector3(0, -0.05f, 0),
                euler: new Vector3(-15, 0, 0),
                scale: new Vector3(0.04f, 0.18f, 0.04f),
                color: new Color(0.42f, 0.30f, 0.18f));

            // Pickaxe head (elongated cube) — tier color
            Color headColor = tier switch
            {
                <= 1 => new Color(0.55f, 0.40f, 0.25f), // wood-ish
                2    => new Color(0.5f, 0.5f, 0.55f),    // stone gray
                3    => new Color(0.78f, 0.78f, 0.85f),  // iron
                _    => new Color(0.55f, 0.6f, 0.7f),    // steel
            };
            AddPrimitive(root, PrimitiveType.Cube,
                pos: new Vector3(0, 0.10f, -0.04f),
                euler: new Vector3(-30, 0, 0),
                scale: new Vector3(0.32f, 0.04f, 0.06f),
                color: headColor);
        }

        private static void BuildAxe(GameObject root, Color tint, int tier)
        {
            // Handle
            AddPrimitive(root, PrimitiveType.Cylinder,
                pos: new Vector3(0, -0.05f, 0),
                euler: new Vector3(-15, 0, 0),
                scale: new Vector3(0.04f, 0.18f, 0.04f),
                color: new Color(0.42f, 0.30f, 0.18f));

            Color headColor = tier <= 1 ? new Color(0.55f, 0.40f, 0.25f)
                                        : new Color(0.78f, 0.78f, 0.85f);
            // Axe head (a wedge made from 2 cubes)
            AddPrimitive(root, PrimitiveType.Cube,
                pos: new Vector3(0.06f, 0.10f, -0.04f),
                euler: new Vector3(-30, 0, 30),
                scale: new Vector3(0.18f, 0.04f, 0.10f),
                color: headColor);
        }

        private static void BuildSword(GameObject root, Color tint)
        {
            AddPrimitive(root, PrimitiveType.Cylinder,
                pos: new Vector3(0, -0.05f, 0),
                euler: new Vector3(0, 0, 0),
                scale: new Vector3(0.03f, 0.06f, 0.03f),
                color: new Color(0.3f, 0.2f, 0.1f));
            AddPrimitive(root, PrimitiveType.Cube,
                pos: new Vector3(0, 0.20f, 0),
                euler: new Vector3(0, 0, 0),
                scale: new Vector3(0.04f, 0.36f, 0.01f),
                color: tint);
        }

        private static void BuildBlockCube(GameObject root, Color tint)
        {
            AddPrimitive(root, PrimitiveType.Cube,
                pos: new Vector3(0, 0, 0),
                euler: new Vector3(15, 25, 0),
                scale: new Vector3(0.16f, 0.16f, 0.16f),
                color: tint);
        }

        private static void BuildSphere(GameObject root, Color tint)
        {
            AddPrimitive(root, PrimitiveType.Sphere,
                pos: Vector3.zero, euler: Vector3.zero,
                scale: Vector3.one * 0.10f, color: tint);
        }
    }
}
