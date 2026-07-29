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
        private PlayerWaterState _water;
        private float  _airTimer;
        private Vector3 _curPos;
        private Quaternion _curRot;
        private bool   _posInit;
        private Transform _swingPivot;

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
            _water = GetComponentInParent<PlayerWaterState>();
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

            // --- read state ---
            bool rawGrounded = _pc != null ? _pc.IsGrounded : (_cc != null && _cc.isGrounded);
            // Hysteresis: CharacterController.isGrounded jitters while standing still, which
            // used to snap the viewmodel between the grounded and air poses every frame (the
            // "fast up-down flicker"). Only go airborne after a short grace off the ground.
            if (rawGrounded) _airTimer = 0f; else _airTimer += dt;
            bool grounded = _airTimer < 0.12f;

            float hSpeed = _cc != null ? new Vector2(_cc.velocity.x, _cc.velocity.z).magnitude : 0f;
            float vSpeed = _cc != null ? _cc.velocity.y : 0f;
            bool sprint = _pc != null && _pc.IsSprinting;
            bool slide  = _pc != null && _pc.IsSliding;
            bool fly    = _pc != null && _pc.IsFlying;
            bool swim   = _water != null && _water.IsSwimming;

            if (rawGrounded && !_wasGrounded && _pc != null)
                _landDip = Mathf.Clamp01(_pc.LastAirDownSpeed / 14f);
            _wasGrounded = rawGrounded;
            _landDip = Mathf.MoveTowards(_landDip, 0f, dt * 4f);

            // --- target pose ---
            Vector3 pos = idleLocalPos;
            Quaternion rot = Quaternion.Euler(idleLocalEuler);

            if (swim)
            {
                _bobT += dt * 4f;
                pos += new Vector3(Mathf.Sin(_bobT * 0.8f) * 0.010f, Mathf.Abs(Mathf.Sin(_bobT)) * 0.012f, 0f);
                rot *= Quaternion.Euler(-8f, 0f, Mathf.Sin(_bobT * 0.5f) * 2.5f);
            }
            else if (fly)
            {
                _bobT += dt * 3f;
                pos += new Vector3(Mathf.Sin(_bobT * 0.7f) * 0.010f, Mathf.Sin(_bobT) * 0.005f, 0f);
                rot *= Quaternion.Euler(-6f, 0f, Mathf.Sin(_bobT * 0.5f) * 2.5f);
            }
            else if (!grounded)
            {
                pos += new Vector3(0f, -0.025f, 0.015f);
                rot *= Quaternion.Euler(Mathf.Clamp(-vSpeed * 0.8f, -10f, 10f), 0f, 0f);
            }
            else
            {
                _bobT += dt * (1f + hSpeed * 0.4f) * bobSpeedWalking;
                float amp = bobAmplitude * Mathf.Clamp01(hSpeed * 0.3f) * (sprint ? 1.4f : 1f);
                pos += new Vector3(0f, Mathf.Sin(_bobT) * amp, 0f);
                if (sprint) { pos += new Vector3(0f, 0f, 0.02f); rot *= Quaternion.Euler(4f, 0f, 0f); }
                if (slide)  { rot *= Quaternion.Euler(-12f, 0f, 6f); pos += new Vector3(0f, -0.04f, 0f); }
            }

            pos += new Vector3(0f, -_landDip * 0.04f, 0f);
            rot *= Quaternion.Euler(_landDip * 10f, 0f, 0f);
            if (_recoilT > 0f)
            {
                _recoilT = Mathf.MoveTowards(_recoilT, 0f, dt * 6f);
                rot *= Quaternion.Euler(_recoilT * 12f, 0f, 0f);
                pos += new Vector3(0f, 0f, _recoilT * 0.03f);
            }

            // --- smooth toward target (frame-rate independent) so motion is fluid & visible, never snapped ---
            if (!_posInit) { _curPos = pos; _curRot = rot; _posInit = true; }
            _curPos = Vector3.Lerp(_curPos, pos, 1f - Mathf.Exp(-dt * 14f));
            _curRot = Quaternion.Slerp(_curRot, rot, 1f - Mathf.Exp(-dt * 16f));

            // The anchor always reflects movement state; the swing arc runs on _swingPivot.
            anchor.localPosition = _curPos;
            anchor.localRotation = _curRot;
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

            // Parent the tool under a swing pivot at the grip so swings rotate around the
            // grip and the BLADE/HEAD leads the arc (not the handle).
            if (_swingPivot == null)
            {
                _swingPivot = new GameObject("SwingPivot").transform;
                _swingPivot.SetParent(anchor, false);
                _swingPivot.localPosition = new Vector3(0f, -0.12f, 0f);
                _swingPivot.localRotation = Quaternion.identity;
            }
            _viewModel.transform.SetParent(_swingPivot, false);
            _viewModel.transform.localPosition = new Vector3(0f, 0.12f, 0f);
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
            if (_swingPivot == null) yield break;
            Quaternion baseR = Quaternion.identity;
            Quaternion toR   = Quaternion.Euler(90f, 14f, 10f); // blade-led chop: sweeps from up to forward

            float t = 0f;
            float halfDur = swingDuration * 0.4f;
            while (t < halfDur)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0, 1, t / halfDur);
                _swingPivot.localRotation = Quaternion.Slerp(baseR, toR, u);
                yield return null;
            }
            t = 0f;
            float recDur = swingDuration * 0.6f;
            while (t < recDur)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0, 1, t / recDur);
                _swingPivot.localRotation = Quaternion.Slerp(toR, baseR, u);
                yield return null;
            }
            _swingPivot.localRotation = baseR;
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
                // Weapons are more specific than ToolItem — handle before the tool cases.
                case VoxelEngine.Combat.WeaponItem wpn when wpn.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Ranged && wpn.range > 20f:
                    BuildRifle(root, tint);
                    break;
                case VoxelEngine.Combat.WeaponItem wpn when wpn.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Ranged:
                    BuildPistol(root, tint);
                    break;
                case VoxelEngine.Combat.WeaponItem wpn when wpn.attackMode == VoxelEngine.Combat.WeaponItem.AttackMode.Thrown:
                    BuildGrenade(root, tint);
                    break;
                case VoxelEngine.Combat.WeaponItem wpn:
                    BuildSword(root, tint);
                    break;
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
            Color wood  = new Color(0.28f, 0.18f, 0.10f);
            Color metal = new Color(0.42f, 0.34f, 0.22f);
            // Pommel
            AddPrimitive(root, PrimitiveType.Sphere, new Vector3(0, -0.21f, 0), Vector3.zero, Vector3.one * 0.045f, metal);
            // Grip (wrapped leather)
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0, -0.12f, 0), Vector3.zero, new Vector3(0.035f, 0.06f, 0.035f), wood);
            // Crossguard (horizontal bar)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, -0.035f, 0), new Vector3(0, 90, 0), new Vector3(0.16f, 0.028f, 0.045f), metal);
            // Blade (tapered: wide base + narrow tip)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.18f, 0), Vector3.zero, new Vector3(0.05f, 0.40f, 0.012f), tint);
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.44f, 0), Vector3.zero, new Vector3(0.032f, 0.14f, 0.01f), tint);
            // Fuller (bright center ridge)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.18f, 0.006f), Vector3.zero, new Vector3(0.012f, 0.38f, 0.004f), Color.Lerp(tint, Color.white, 0.35f));
        }

        private static void BuildPistol(GameObject root, Color tint)
        {
            Color metal = new Color(0.35f, 0.36f, 0.38f);
            Color dark  = new Color(0.16f, 0.16f, 0.18f);
            Color gripC = new Color(0.20f, 0.14f, 0.09f);
            // Slide (top)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.06f, 0.05f), Vector3.zero, new Vector3(0.05f, 0.05f, 0.22f), metal);
            // Frame
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.005f, 0.02f), Vector3.zero, new Vector3(0.044f, 0.04f, 0.15f), dark);
            // Barrel / muzzle
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0, 0.06f, 0.18f), new Vector3(90, 0, 0), new Vector3(0.02f, 0.03f, 0.02f), dark);
            // Grip (angled)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, -0.07f, -0.03f), new Vector3(15, 0, 0), new Vector3(0.04f, 0.11f, 0.05f), gripC);
            // Magazine
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, -0.085f, 0.035f), new Vector3(8, 0, 0), new Vector3(0.034f, 0.07f, 0.03f), dark);
            // Trigger guard
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, -0.03f, 0.035f), Vector3.zero, new Vector3(0.016f, 0.04f, 0.03f), dark);
            // Sights
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.10f, 0.14f), Vector3.zero, new Vector3(0.008f, 0.018f, 0.012f), metal);
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.10f, -0.03f), Vector3.zero, new Vector3(0.03f, 0.014f, 0.012f), metal);
        }

        private static void BuildRifle(GameObject root, Color tint)
        {
            Color metal = new Color(0.30f, 0.31f, 0.33f);
            Color dark  = new Color(0.14f, 0.14f, 0.16f);
            Color wood  = new Color(0.22f, 0.16f, 0.10f);
            // Receiver (long body)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.04f, 0f), Vector3.zero, new Vector3(0.05f, 0.06f, 0.34f), metal);
            // Long barrel
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0, 0.06f, 0.24f), new Vector3(90, 0, 0), new Vector3(0.018f, 0.16f, 0.018f), dark);
            // Muzzle
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0, 0.06f, 0.37f), new Vector3(90, 0, 0), new Vector3(0.022f, 0.02f, 0.022f), dark);
            // Stock
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.02f, -0.21f), new Vector3(-5, 0, 0), new Vector3(0.05f, 0.09f, 0.12f), wood);
            // Grip
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, -0.05f, -0.04f), new Vector3(15, 0, 0), new Vector3(0.04f, 0.09f, 0.05f), wood);
            // Magazine (curved)
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, -0.085f, 0.06f), new Vector3(-12, 0, 0), new Vector3(0.034f, 0.11f, 0.05f), dark);
            // Scope
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0, 0.11f, 0.02f), new Vector3(90, 0, 0), new Vector3(0.018f, 0.13f, 0.018f), dark);
            // Front sight
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0, 0.09f, 0.22f), Vector3.zero, new Vector3(0.008f, 0.02f, 0.012f), metal);
        }

        private static void BuildGrenade(GameObject root, Color tint)
        {
            Color body = (tint != Color.white) ? tint : new Color(0.30f, 0.34f, 0.20f); // military green-steel
            Color dark = new Color(0.16f, 0.18f, 0.12f);
            Color fuse = new Color(1.0f, 0.55f, 0.12f); // lit fuse tip
            // Oval body
            AddPrimitive(root, PrimitiveType.Sphere, new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.072f, 0.092f, 0.072f), body);
            // Segmented belt around the middle
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0f, 0.0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.075f, 0.012f, 0.075f), dark);
            // Top neck + cap
            AddPrimitive(root, PrimitiveType.Cylinder, new Vector3(0f, 0.078f, 0f), Vector3.zero, new Vector3(0.034f, 0.03f, 0.034f), dark);
            // Pull lever (spoon) on the side
            AddPrimitive(root, PrimitiveType.Cube, new Vector3(0.05f, 0.075f, 0f), new Vector3(0f, 0f, -22f), new Vector3(0.012f, 0.062f, 0.03f), dark);
            // Lit fuse tip
            AddPrimitive(root, PrimitiveType.Sphere, new Vector3(0f, 0.115f, 0f), Vector3.zero, Vector3.one * 0.02f, fuse);
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
