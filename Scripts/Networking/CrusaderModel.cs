// Assets/Scripts/VoxelEngine/Networking/CrusaderModel.cs
//
// 14.13.0-dev - Multiplayer milestone 6 begins: Real Crusaders.
// 14.14.0-dev - the honest body: armor is DISPLAY, not identity.
// 14.15.0-dev - the real body: the avatar now prefers the rigged character
//              at Resources/Player.fbx (VoxelEngineAssets/Resources/) -
//              instantiated at runtime, auto-scaled to 1.85 m with feet on
//              the ground, tattoos projected onto it, skin customizable per
//              player (six tones, picked in the multiplayer menu, synced).
//              The held tool rides an anchor on the rig's right-hand BONE,
//              so future animations carry the tool for free. If the FBX is
//              missing (not yet moved into a Resources folder) the primitive
//              warrior below still builds - nothing ever breaks.
//
// Anatomy contract (both bodies): 1.85 m, pivot at the feet, +Z forward,
// matching PlayerController. "RightHand" anchors the held tool, "BackAnchor"
// marks the jetpack/oxygen mount, "ArmorRig" holds every armor plate and is
// inactive until SetArmor(tier >= 1) - armor shows ONLY while worn, tinted
// by tier: quilted cloth, hardened leather, iron, steel, gilded, void-metal.
//
// Tattoos on either body: the brand rune in faded red, and the chest ink
// reading "The lion with little pecker develops big roar - CalleTheLion".
//
// Crouch: PlayerAvatar squashes the "Crusader" root - feet pivot makes a
// plain Y scale correct for the rig exactly as for the primitives.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Networking
{
    public static class CrusaderModel
    {
        public const string RootName = "Crusader";
        public const string RigName = "Rig";
        public const string RightArmPivotName = "RightArmPivot";
        public const string RightHandName = "RightHand";
        public const string BackAnchorName = "BackAnchor";
        public const string ArmorRigName = "ArmorRig";
        public const string ArmorRigRName = "ArmorRigR";

        private const float Height = 1.85f;
        private const string RigResourceName = "Player";   // Resources/Player.fbx

        public const int SkinToneCount = 6;

        private static readonly Dictionary<int, Material> _tierMats = new();

        /// <summary>The six selectable skin tints (multiplied over the rig's own
        /// texture). Index 2 is the default.</summary>
        public static Color SkinToneColor(int index)
        {
            switch (Mathf.Clamp(index, 0, SkinToneCount - 1))
            {
                case 0:  return new Color(0.98f, 0.86f, 0.76f);
                case 1:  return new Color(0.94f, 0.76f, 0.62f);
                case 3:  return new Color(0.62f, 0.42f, 0.28f);
                case 4:  return new Color(0.42f, 0.28f, 0.18f);
                case 5:  return new Color(0.28f, 0.18f, 0.12f);
                default: return new Color(0.85f, 0.64f, 0.48f);   // 2: tan
            }
        }

        /// <summary>Build the warrior under this avatar root if he is not already
        /// there. Idempotent and cheap when built; hides the legacy placeholder
        /// capsule instead of destroying it, so the prefab stays untouched.</summary>
        public static Transform EnsureBuilt(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;
            // 14.46.2: a dedicated server needs the avatar's TRANSFORM, not
            // its body - every material this builder makes would hit the
            // stripped-shader warning. All visual consumers (animator, held
            // item, nameplate) already tolerate a missing body.
            if (NetworkSession.IsDedicated) return null;
            var existing = avatarRoot.Find(RootName);
            if (existing != null) return existing;

            var legacyBody = avatarRoot.Find("Body");
            if (legacyBody != null) legacyBody.gameObject.SetActive(false);
            var legacyVisor = avatarRoot.Find("Visor");
            if (legacyVisor != null) legacyVisor.gameObject.SetActive(false);

            var root = new GameObject(RootName).transform;
            root.SetParent(avatarRoot, false);

            var runeRed = Mat(new Color(0.62f, 0.09f, 0.07f), 0.00f, 0.15f);

            Transform armPivot = null;
            if (!TryBuildRig(root, runeRed))
                armPivot = BuildPrimitiveBody(root, runeRed);

            BuildArmorRigs(root, armPivot);

            // ── equipment anchor (jetpack + oxygen tank) ──
            var back = new GameObject(BackAnchorName).transform;
            back.SetParent(root, false);
            back.localPosition = new Vector3(0f, 1.22f, -0.18f);

            // On the rigged body the anchor rides the spine (like the tattoos),
            // so back gear follows the chest once animations land.
            var rigT = root.Find(RigName);
            if (rigT != null)
            {
                var spineB = FindBoneEndingIn(rigT.gameObject, "Spine2");
                if (spineB == null) spineB = FindBoneEndingIn(rigT.gameObject, "Spine1");
                if (spineB == null) spineB = FindBoneEndingIn(rigT.gameObject, "Spine");
                if (spineB != null) back.SetParent(spineB, true);
            }

            return root;
        }

        /// <summary>Show or hide the armor display. Tier 0 = bare warrior; tiers
        /// 1-6 activate the plate rigs and tint them so the tier reads at a
        /// glance. On the primitive body the helmet replaces hair and beard.</summary>
        public static void SetArmor(Transform avatarRoot, int tier)
        {
            var root = EnsureBuilt(avatarRoot);
            if (root == null) return;

            bool worn = tier > 0;
            var rig = root.Find(ArmorRigName);
            if (rig != null) rig.gameObject.SetActive(worn);
            var rigR = root.Find(RightArmPivotName + "/" + ArmorRigRName);
            if (rigR != null) rigR.gameObject.SetActive(worn);

            var hairT = root.Find("Hair");
            if (hairT != null) hairT.gameObject.SetActive(!worn);
            var beardT = root.Find("Beard");
            if (beardT != null) beardT.gameObject.SetActive(!worn);

            if (!worn) return;
            var mat = TierMaterial(Mathf.Clamp(tier, 1, 6));
            TintPlates(rig, mat);
            TintPlates(rigR, mat);
        }

        /// <summary>14.49.0/14.51.0 - the personal crest. The icon REPLACES
        /// the default chest symbols (the brand rune on bare skin, the red
        /// cross on the tabard front) and the chest text REPLACES the default
        /// chest-ink motto; clear either and the default comes straight back.
        /// Positions are root-local like the armor rigs, so both body
        /// variants wear it the same.</summary>
        public const string CrestName = "PlayerCrest";

        public static void SetCrest(Transform avatarRoot, Texture2D icon, string chestText)
        {
            var root = EnsureBuilt(avatarRoot);
            if (root == null) return;

            // ── chest text: the ink itself. A custom line takes the motto's
            // place on the skin; empty restores the motto verbatim. ──
            var inkT = FindDeep(root, "ChestInk");   // may ride a spine bone on the rigged body
            if (inkT != null)
            {
                var ink = inkT.GetComponent<TextMesh>();
                if (ink != null)
                    ink.text = string.IsNullOrEmpty(chestText) ? DefaultChestInk : chestText;
            }

            // ── default symbols step aside while a custom icon is worn ──
            // 14.53.0 - FindDeep, not root.Find: on the rigged body the rune
            // rides the spine bone (exactly like the ink), so a root-level
            // search never found it and the rune stayed put under the icon.
            bool hasIcon = icon != null;
            var rune = FindDeep(root, "BrandRune");
            if (rune != null) rune.gameObject.SetActive(!hasIcon);
            var armorRig = root.Find(ArmorRigName);
            if (armorRig != null)
            {
                var crossV = armorRig.Find("CrossVF");
                if (crossV != null) crossV.gameObject.SetActive(!hasIcon);
                var crossH = armorRig.Find("CrossHF");
                if (crossH != null) crossH.gameObject.SetActive(!hasIcon);
            }

            var crest = FindDeep(root, CrestName);
            if (!hasIcon)
            {
                if (crest != null) Object.Destroy(crest.gameObject);
                return;
            }

            // ── icon: one textured quad TATTOOED onto the chest (14.52.0).
            // It hangs under the ChestInk anchor, which rides the spine bone
            // on the rigged body - so the crest moves with every animation
            // (hits, deaths, the lot) instead of floating in root space while
            // the body animates through it. Ink local space is already turned
            // to read from the front, so the quad needs no flip of its own;
            // -Z in ink space points OUT of the chest. The primitive fallback
            // body parents identically - its ink just never moves. ──
            if (crest == null)
            {
                crest = new GameObject(CrestName).transform;
                if (inkT != null) crest.SetParent(inkT, false);
                else crest.SetParent(root, false);   // no ink anchor - root fallback
            }
            // 14.53.0 - FLUSH placement, enforced every call: the quad sits a
            // finger's width above the motto and a single centimeter off the
            // skin, tilted 10 degrees so it lies along the chest plane the
            // way the pecs actually slope. The old spot (14cm up, 3.5cm out)
            // was at the clavicle, where the chest curves away - the icon
            // read as a floating card, not a tattoo.
            if (inkT != null && crest.parent == inkT)
            {
                crest.localPosition = new Vector3(0f, 0.085f, -0.012f);
                crest.localRotation = Quaternion.Euler(10f, 0f, 0f);
            }
            else
            {
                crest.localPosition = new Vector3(0f, 1.40f, 0.165f);
                crest.localRotation = Quaternion.Euler(-10f, 180f, 0f);
            }
            var iconT = crest.Find("CrestIcon");
            if (iconT == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "CrestIcon";
                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                iconT = go.transform;
                iconT.SetParent(crest, false);
                iconT.localPosition = Vector3.zero;
                iconT.localRotation = Quaternion.identity;
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                iconT.GetComponent<MeshRenderer>().material = new Material(shader);
            }
            iconT.localScale = new Vector3(0.16f, 0.16f, 1f);
            var mat = iconT.GetComponent<MeshRenderer>().material;
            mat.mainTexture = icon;
            mat.color = Color.white;
        }

        /// <summary>Apply a player's chosen skin tone. On the rigged body the tint
        /// multiplies every rig material; on the primitive fallback it recolors the
        /// bare-skin parts. Tattoos and armor keep their own colors.</summary>
        public static void SetSkinTone(Transform avatarRoot, int toneIndex)
        {
            var root = EnsureBuilt(avatarRoot);
            if (root == null) return;
            var tone = SkinToneColor(toneIndex);

            var rig = root.Find(RigName);
            if (rig != null)
            {
                foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.materials)
                        Tint(m, tone);
                return;
            }

            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!SkinParts.Contains(r.gameObject.name)) continue;
                // "Abs" keeps its slightly darker shading relative to the rest.
                Tint(r.material, r.gameObject.name == "Abs" ? tone * 0.92f : tone);
            }
        }

        /// <summary>Show or hide the back gear: jetpack (dark pack, orange trim,
        /// twin nozzles) and oxygen tank (white bottle, cyan cap) on the back
        /// anchor. Built lazily on first need; toggled by the equip mirror.</summary>
        public static void SetBackGear(Transform avatarRoot, bool hasJetpack, bool hasOxygen)
        {
            var root = EnsureBuilt(avatarRoot);
            if (root == null) return;
            var back = FindDeep(root, BackAnchorName);   // may ride a spine bone
            if (back == null) return;

            var jet = back.Find("GearJetpack");
            if (hasJetpack && jet == null) jet = BuildJetpackGear(back);
            var oxy = back.Find("GearOxygen");
            if (hasOxygen && oxy == null) oxy = BuildOxygenGear(back);

            if (jet != null) jet.gameObject.SetActive(hasJetpack);
            if (oxy != null) oxy.gameObject.SetActive(hasOxygen);
        }

        private static Transform BuildJetpackGear(Transform back)
        {
            var metal  = Mat(new Color(0.20f, 0.21f, 0.24f), 0.60f, 0.45f);
            var trim   = Mat(new Color(0.85f, 0.45f, 0.10f), 0.20f, 0.40f);
            var nozzle = Mat(new Color(0.10f, 0.10f, 0.12f), 0.70f, 0.30f);

            var jet = new GameObject("GearJetpack").transform;
            jet.SetParent(back, false);
            Part(jet, PrimitiveType.Cube,     "PackBody", new Vector3(0f, -0.02f, -0.02f),   new Vector3(0.34f, 0.42f, 0.15f), metal);
            Part(jet, PrimitiveType.Cube,     "PackTrim", new Vector3(0f, 0.16f, -0.025f),   new Vector3(0.355f, 0.06f, 0.155f), trim);
            Part(jet, PrimitiveType.Cylinder, "NozzleL",  new Vector3(-0.10f, -0.27f, -0.02f), new Vector3(0.075f, 0.05f, 0.075f), nozzle);
            Part(jet, PrimitiveType.Cylinder, "NozzleR",  new Vector3( 0.10f, -0.27f, -0.02f), new Vector3(0.075f, 0.05f, 0.075f), nozzle);
            return jet;
        }

        private static Transform BuildOxygenGear(Transform back)
        {
            var bottle = Mat(new Color(0.88f, 0.90f, 0.92f), 0.55f, 0.65f);
            var cap    = Mat(new Color(0.25f, 0.60f, 0.85f), 0.40f, 0.55f);

            var oxy = new GameObject("GearOxygen").transform;
            oxy.SetParent(back, false);
            Part(oxy, PrimitiveType.Capsule, "TankBody", new Vector3(0.15f, 0.03f, -0.045f), new Vector3(0.10f, 0.15f, 0.10f), bottle);
            Part(oxy, PrimitiveType.Sphere,  "TankCap",  new Vector3(0.15f, 0.20f, -0.045f), new Vector3(0.06f, 0.05f, 0.06f), cap);
            return oxy;
        }

        /// <summary>Raise or rest the right arm - the building pose. Works on both
        /// bodies: the rig swings its right upper-arm BONE from wherever the bind
        /// pose put it to forward-and-slightly-down (axis-agnostic world-space
        /// swing), the primitive body rotates its arm pivot. The rest rotation is
        /// remembered on the bone so the pose always restores exactly.</summary>
        public static void SetBuildPose(Transform avatarRoot, bool posed)
        {
            var root = EnsureBuilt(avatarRoot);
            if (root == null) return;

            var rigT = root.Find(RigName);
            if (rigT != null)
            {
                // While the locomotion graph runs, the animator rewrites bones
                // every frame - the driver re-applies the pose in LateUpdate.
                var driver = rigT.GetComponent<CrusaderAnimator>();
                if (driver != null && driver.HasClips) { driver.BuildPose = posed; return; }

                var upper = FindBoneEndingIn(rigT.gameObject, "RightArm");
                if (upper == null) return;
                var state = upper.GetComponent<BuildPoseState>();
                if (posed)
                {
                    if (state != null && state.posed) return;
                    if (state == null)
                    {
                        state = upper.gameObject.AddComponent<BuildPoseState>();
                        state.original = upper.localRotation;
                    }
                    var hand = FindBoneEndingIn(rigT.gameObject, RightHandName);
                    Vector3 from = hand != null && hand != upper
                        ? (hand.position - upper.position).normalized
                        : root.right;
                    Vector3 to = (root.forward * 0.94f - root.up * 0.20f).normalized;
                    upper.rotation = Quaternion.FromToRotation(from, to) * upper.rotation;
                    state.posed = true;
                }
                else if (state != null && state.posed)
                {
                    upper.localRotation = state.original;
                    state.posed = false;
                }
                return;
            }

            var pivot = root.Find(RightArmPivotName);
            if (pivot == null) return;
            var pState = pivot.GetComponent<BuildPoseState>();
            if (posed)
            {
                if (pState != null && pState.posed) return;
                if (pState == null)
                {
                    pState = pivot.gameObject.AddComponent<BuildPoseState>();
                    pState.original = pivot.localRotation;
                }
                pivot.localRotation = Quaternion.Euler(-80f, 0f, 0f) * pState.original;
                pState.posed = true;
            }
            else if (pState != null && pState.posed)
            {
                pivot.localRotation = pState.original;
                pState.posed = false;
            }
        }

        /// <summary>Remembers a bone's rest rotation while the building pose holds it.</summary>
        private class BuildPoseState : MonoBehaviour
        {
            public Quaternion original;
            public bool posed;
        }

        /// <summary>Depth-first search by exact name - finds the hand anchor no
        /// matter which bone it was parented under.</summary>
        public static Transform FindDeep(Transform node, string name)
        {
            if (node == null) return null;
            if (node.name == name) return node;
            for (int i = 0; i < node.childCount; i++)
            {
                var hit = FindDeep(node.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>The head of whichever body this avatar ended up with - the
        /// rig's head bone ("mixamorig:Head"), the primitive body's "Head", or
        /// null when neither exists. Proximity voice speaks from here, so a
        /// crouching or sliding player's voice stays attached to their face
        /// (14.20.0). Shortest matching name wins, so "HeadTop_End" and other
        /// leaf bones never steal the anchor.</summary>
        public static Transform FindHeadAnchor(Transform avatarRoot)
        {
            if (avatarRoot == null) return null;
            var root = avatarRoot.Find(RootName);
            if (root == null) root = avatarRoot;

            Transform best = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("Head") && (best == null || t.name.Length < best.name.Length))
                    best = t;
            return best;
        }

        // ─────────────────────────── the rigged body ───────────────────────────

        /// <summary>Instantiate Resources/Player under the root, normalize it to
        /// 1.85 m with feet at the pivot, anchor the hand on the right-hand bone
        /// and place the tattoos. Returns false (and cleans up) when the resource
        /// is absent or unusable - the primitive body takes over.</summary>
        private static bool TryBuildRig(Transform root, Material runeRed)
        {
            var prefab = Resources.Load<GameObject>(RigResourceName);
            if (prefab == null) return false;

            var rigGo = Object.Instantiate(prefab);
            rigGo.name = RigName;
            var rig = rigGo.transform;

            // Measure BEFORE parenting, at the origin with identity rotation
            // (14.15.2). A skinned mesh is displayed where its BONES put it, so
            // renderer.bounds is the only truthful box - local bounds pushed
            // through the renderer transform (14.15.1) landed somewhere else
            // entirely, which floated the players and put the tattoos at the
            // shins. At origin/identity, world bounds ARE model space and the
            // avatar's spawn rotation cannot inflate anything.
            var prefabScale = rig.localScale;   // keep any import scale the asset carries
            rig.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            foreach (var col in rigGo.GetComponentsInChildren<Collider>(true))
                Object.Destroy(col);   // display only - never block rays or physics

            var renderers = rigGo.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Object.Destroy(rigGo); return false; }

            // Ground truth (14.15.3): bake each skinned mesh EXACTLY as displayed
            // and measure its real vertices. renderer.bounds on a skinned mesh is
            // just the import-time conservative box moved by the root bone - it
            // read too big in every axis, which made the avatar too small, lifted
            // it off the ground and pushed the tattoos off the chest. BakeMesh
            // needs no Read/Write flag: the baked copy is created readable.
            var points = new List<Vector3>(16384);
            foreach (var smr in rigGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked, true);   // scale baked in; add rotation + position
                    var vs = baked.vertices;
                    var bp = smr.transform.position;
                    var br = smr.transform.rotation;
                    for (int i = 0; i < vs.Length; i++) points.Add(bp + br * vs[i]);
                }
                catch { }
                finally { Object.Destroy(baked); }
            }
            foreach (var mr in rigGo.GetComponentsInChildren<MeshRenderer>(true))
            {
                var b = mr.bounds;   // static meshes: exact at origin/identity
                points.Add(b.min); points.Add(b.max);
            }
            if (points.Count == 0)   // last resort: the conservative boxes
                foreach (var r in renderers)
                {
                    var b = r.bounds;
                    points.Add(b.min); points.Add(b.max);
                }

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < points.Count; i++)
            {
                min = Vector3.Min(min, points[i]);
                max = Vector3.Max(max, points[i]);
            }
            float rawHeight = max.y - min.y;
            if (rawHeight < 0.05f) { Object.Destroy(rigGo); return false; }

            // Chest front from the SAME true points: band at 57-84% of height,
            // torso width only, so T-pose arms and forward toes never count.
            float yLo = min.y + 0.57f * rawHeight;
            float yHi = min.y + 0.84f * rawHeight;
            float xC = (min.x + max.x) * 0.5f;
            float xHalf = 0.09f * rawHeight;
            float chestFrontModel = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p.y < yLo || p.y > yHi) continue;
                if (Mathf.Abs(p.x - xC) > xHalf) continue;
                if (p.z > chestFrontModel) chestFrontModel = p.z;
            }
            if (chestFrontModel <= float.MinValue) chestFrontModel = max.z;

            float s = Height / rawHeight;
            rig.SetParent(root, false);   // keeps the local pose: origin, identity
            rig.localScale = prefabScale * s;
            rig.localPosition = new Vector3(
                -(min.x + max.x) * 0.5f * s,
                -min.y * s,
                -(min.z + max.z) * 0.5f * s);

            // Held-tool anchor on the right-hand bone (Mixamo: "mixamorig:RightHand").
            // Shortest matching name wins so finger bones never steal the anchor.
            Transform handBone = null;
            foreach (var t in rigGo.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith(RightHandName)
                    && (handBone == null || t.name.Length < handBone.name.Length))
                    handBone = t;
            var hand = new GameObject(RightHandName).transform;
            if (handBone != null)
            {
                hand.SetParent(handBone, false);   // rides the bone through any animation
            }
            else
            {
                hand.SetParent(root, false);
                hand.localPosition = new Vector3(0.30f, 1.05f, 0.10f);
            }

            // Locomotion driver (14.17.0): plays the Resources/PlayerAnimations
            // clips through a runtime playable graph. A harmless no-op (bind
            // pose, exactly as before) until that folder exists under Resources.
            var animator = rigGo.GetComponent<Animator>();
            if (animator == null) animator = rigGo.AddComponent<Animator>();
            animator.applyRootMotion = false;   // slide/pack clips are not in-place
            var upperArmBone = FindBoneEndingIn(rigGo, "RightArm");
            var driver = rigGo.AddComponent<CrusaderAnimator>();
            driver.Initialize(animator, root.parent != null ? root.parent : root,
                upperArmBone, handBone);

            // Tattoos hug the actual chest surface: the chest-band sample from
            // above, recentered and scaled exactly like the rig itself.
            float chestFront = (chestFrontModel - (min.z + max.z) * 0.5f) * s;
            BuildTattoos(root, runeRed,
                chestInkPos: new Vector3(0f, 1.31f, chestFront + 0.004f),   // 14.53.0 - closer to the skin
                runePos: new Vector3(-0.16f, 1.47f, chestFront + 0.008f),
                runeRot: Quaternion.identity);

            // Ride the spine so future animations carry the ink with the chest.
            // 14.52.0 - broadened search: exact suffixes first, then any bone
            // whose name merely CONTAINS spine/chest (case-insensitive,
            // highest one wins = closest to the chest), so a rig with
            // unconventional bone names still tattoos instead of floating.
            var spine = FindBoneEndingIn(rigGo, "Spine2");
            if (spine == null) spine = FindBoneEndingIn(rigGo, "Spine1");
            if (spine == null) spine = FindBoneEndingIn(rigGo, "Spine");
            if (spine == null) spine = FindBoneContaining(rigGo, "spine");
            if (spine == null) spine = FindBoneContaining(rigGo, "chest");
            if (spine != null)
            {
                var inkT = root.Find("ChestInk");
                if (inkT != null) inkT.SetParent(spine, true);
                var runeT = root.Find("BrandRune");
                if (runeT != null) runeT.SetParent(spine, true);
            }
            return true;
        }

        /// <summary>Loose fallback: any bone whose name contains the needle
        /// (case-insensitive). The HIGHEST match wins - for spine chains that
        /// is the bone nearest the chest, which is where tattoos live.</summary>
        private static Transform FindBoneContaining(GameObject rigGo, string needle)
        {
            Transform best = null;
            foreach (var t in rigGo.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().Contains(needle)
                    && (best == null || t.position.y > best.position.y))
                    best = t;
            return best;
        }

        /// <summary>Shortest bone name wins so "Spine" never grabs a longer twin.
        /// Explicit null checks - never coalesce UnityEngine.Objects.</summary>
        private static Transform FindBoneEndingIn(GameObject rigGo, string suffix)
        {
            Transform best = null;
            foreach (var t in rigGo.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith(suffix) && (best == null || t.name.Length < best.name.Length))
                    best = t;
            return best;
        }

        // ─────────────────────────── the primitive fallback ───────────────────────────

        private static readonly HashSet<string> SkinParts = new()
        {
            "ThighL", "ThighR", "Waist", "Chest", "Abs", "PecL", "PecR",
            "DeltL", "DeltR", "ArmL", "HandL", "ArmR", "HandR", "Neck", "Head"
        };

        /// <summary>The hand-built bare warrior (14.14.0) - kept as the fallback so
        /// the avatar still works before Player.fbx lands in a Resources folder.
        /// Returns the right-arm pivot for the arm-mounted armor rig.</summary>
        private static Transform BuildPrimitiveBody(Transform root, Material runeRed)
        {
            var skin    = Mat(new Color(0.78f, 0.58f, 0.45f), 0.00f, 0.28f);
            var skinDim = Mat(new Color(0.72f, 0.52f, 0.40f), 0.00f, 0.22f);
            var hair    = Mat(new Color(0.13f, 0.10f, 0.08f), 0.00f, 0.30f);
            var eyeInk  = Mat(new Color(0.06f, 0.05f, 0.05f), 0.00f, 0.40f);
            var chain   = Mat(new Color(0.44f, 0.46f, 0.50f), 0.70f, 0.35f);
            var boot    = Mat(new Color(0.35f, 0.24f, 0.14f), 0.05f, 0.25f);

            // ── boots (worn leather, calf high - bare legs above) ──
            Part(root, PrimitiveType.Cube,    "BootL",     new Vector3(-0.11f, 0.06f, 0.04f), new Vector3(0.17f, 0.12f, 0.29f), boot);
            Part(root, PrimitiveType.Cube,    "BootR",     new Vector3( 0.11f, 0.06f, 0.04f), new Vector3(0.17f, 0.12f, 0.29f), boot);
            Part(root, PrimitiveType.Cube,    "BootCuffL", new Vector3(-0.11f, 0.27f, 0f),    new Vector3(0.19f, 0.30f, 0.21f), boot);
            Part(root, PrimitiveType.Cube,    "BootCuffR", new Vector3( 0.11f, 0.27f, 0f),    new Vector3(0.19f, 0.30f, 0.21f), boot);

            // ── legs (bare) ──
            Part(root, PrimitiveType.Capsule, "ThighL",    new Vector3(-0.11f, 0.68f, 0f),    new Vector3(0.18f, 0.27f, 0.18f), skin);
            Part(root, PrimitiveType.Capsule, "ThighR",    new Vector3( 0.11f, 0.68f, 0f),    new Vector3(0.18f, 0.27f, 0.18f), skin);

            // ── chain briefs ──
            Part(root, PrimitiveType.Cube,    "Briefs",    new Vector3(0f, 0.98f, 0f),        new Vector3(0.34f, 0.16f, 0.25f), chain);
            Part(root, PrimitiveType.Cube,    "BriefsBelt",new Vector3(0f, 1.06f, 0f),        new Vector3(0.36f, 0.05f, 0.26f), boot);

            // ── torso (bare, muscular) ──
            Part(root, PrimitiveType.Cube,    "Waist",     new Vector3(0f, 1.13f, 0f),        new Vector3(0.30f, 0.14f, 0.21f), skin);
            Part(root, PrimitiveType.Cube,    "Chest",     new Vector3(0f, 1.32f, 0f),        new Vector3(0.40f, 0.28f, 0.24f), skin);
            Part(root, PrimitiveType.Cube,    "Abs",       new Vector3(0f, 1.15f, 0.105f),    new Vector3(0.22f, 0.22f, 0.03f), skinDim);
            Part(root, PrimitiveType.Sphere,  "PecL",      new Vector3(-0.10f, 1.38f, 0.115f),new Vector3(0.16f, 0.12f, 0.10f), skin);
            Part(root, PrimitiveType.Sphere,  "PecR",      new Vector3( 0.10f, 1.38f, 0.115f),new Vector3(0.16f, 0.12f, 0.10f), skin);
            Part(root, PrimitiveType.Sphere,  "DeltL",     new Vector3(-0.26f, 1.44f, 0f),    new Vector3(0.17f, 0.15f, 0.17f), skin);
            Part(root, PrimitiveType.Sphere,  "DeltR",     new Vector3( 0.26f, 1.44f, 0f),    new Vector3(0.17f, 0.15f, 0.17f), skin);

            // ── arms (bare; right arm on its pose pivot) ──
            Part(root, PrimitiveType.Capsule, "ArmL",      new Vector3(-0.30f, 1.14f, 0f),    new Vector3(0.12f, 0.26f, 0.12f), skin);
            Part(root, PrimitiveType.Sphere,  "HandL",     new Vector3(-0.30f, 0.86f, 0f),    new Vector3(0.13f, 0.13f, 0.13f), skin);

            var armPivot = new GameObject(RightArmPivotName).transform;
            armPivot.SetParent(root, false);
            armPivot.localPosition = new Vector3(0.30f, 1.44f, 0f);
            Part(armPivot, PrimitiveType.Capsule, "ArmR",  new Vector3(0f, -0.30f, 0f),       new Vector3(0.12f, 0.26f, 0.12f), skin);
            Part(armPivot, PrimitiveType.Sphere,  "HandR", new Vector3(0f, -0.58f, 0f),       new Vector3(0.13f, 0.13f, 0.13f), skin);

            var handAnchor = new GameObject(RightHandName).transform;
            handAnchor.SetParent(armPivot, false);
            handAnchor.localPosition = new Vector3(0f, -0.62f, 0.06f);
            handAnchor.localRotation = Quaternion.Euler(10f, -20f, 0f);

            // ── head (bare: hair, beard, eyes) ──
            Part(root, PrimitiveType.Cylinder, "Neck",     new Vector3(0f, 1.50f, 0f),        new Vector3(0.14f, 0.05f, 0.14f), skin);
            Part(root, PrimitiveType.Sphere,   "Head",     new Vector3(0f, 1.70f, 0f),        new Vector3(0.26f, 0.30f, 0.28f), skin);
            Part(root, PrimitiveType.Sphere,   "Hair",     new Vector3(0f, 1.77f, -0.02f),    new Vector3(0.27f, 0.21f, 0.29f), hair);
            Part(root, PrimitiveType.Sphere,   "Beard",    new Vector3(0f, 1.61f, 0.075f),    new Vector3(0.18f, 0.12f, 0.12f), hair);
            Part(root, PrimitiveType.Cube,     "EyeL",     new Vector3(-0.055f, 1.72f, 0.132f), new Vector3(0.035f, 0.016f, 0.012f), eyeInk);
            Part(root, PrimitiveType.Cube,     "EyeR",     new Vector3( 0.055f, 1.72f, 0.132f), new Vector3(0.035f, 0.016f, 0.012f), eyeInk);

            BuildTattoos(root, runeRed,
                chestInkPos: new Vector3(0f, 1.29f, 0.128f),
                runePos: new Vector3(-0.375f, 1.30f, 0f),
                runeRot: Quaternion.Euler(0f, 90f, 0f));   // strokes face out of the arm
            return armPivot;
        }

        // ─────────────────────────── tattoos ───────────────────────────

        /// <summary>The crew motto every chest wears until its owner writes
        /// their own line (14.51.0 - custom chest text swaps in and out of
        /// this same ink, so one constant is the single source of truth).</summary>
        public const string DefaultChestInk = "The lion with little pecker\ndevelops big roar\n- CalleTheLion";

        private static void BuildTattoos(Transform root, Material runeRed,
            Vector3 chestInkPos, Vector3 runePos, Quaternion runeRot)
        {
            // Chest ink: the crew motto, dark ink, readable from the front like any
            // nameplate (TextMesh faces are -Z readable, so it turns its back to
            // the model's forward).
            var inkGo = new GameObject("ChestInk");
            inkGo.transform.SetParent(root, false);
            inkGo.transform.localPosition = chestInkPos;
            inkGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var ink = inkGo.AddComponent<TextMesh>();
            ink.text = DefaultChestInk;
            ink.characterSize = 0.010f;
            ink.fontSize = 30;
            ink.anchor = TextAnchor.MiddleCenter;
            ink.alignment = TextAlignment.Center;
            ink.color = new Color(0.16f, 0.12f, 0.10f, 0.95f);

            // Brand rune in faded red, drawn from strokes: a vertical staff with
            // branch tips, the crossing X, and foot hooks.
            var rune = new GameObject("BrandRune").transform;
            rune.SetParent(root, false);
            rune.localPosition = runePos;
            rune.localRotation = runeRot;

            void Stroke(string name, Vector3 pos, float zRot, Vector3 scale)
            {
                var t = Part(rune, PrimitiveType.Cube, name, pos, scale, runeRed);
                t.localRotation = Quaternion.Euler(0f, 0f, zRot);
            }

            Stroke("Staff",  new Vector3(0f, 0.010f, 0f),      0f,  new Vector3(0.013f, 0.170f, 0.006f));
            Stroke("TipL",   new Vector3(-0.022f, 0.085f, 0f),  35f, new Vector3(0.012f, 0.060f, 0.006f));
            Stroke("TipR",   new Vector3( 0.022f, 0.085f, 0f), -35f, new Vector3(0.012f, 0.060f, 0.006f));
            Stroke("CrossA", new Vector3(0f, -0.010f, 0f),      40f, new Vector3(0.012f, 0.175f, 0.006f));
            Stroke("CrossB", new Vector3(0f, -0.010f, 0f),     -40f, new Vector3(0.012f, 0.175f, 0.006f));
            Stroke("HookL",  new Vector3(-0.048f, -0.088f, 0f), -50f, new Vector3(0.012f, 0.050f, 0.006f));
            Stroke("HookR",  new Vector3( 0.048f, -0.088f, 0f),  50f, new Vector3(0.012f, 0.050f, 0.006f));
        }

        // ─────────────────────────── armor rigs ───────────────────────────

        /// <summary>Every plate the tier tint touches is named "Plate*"; fixed-color
        /// pieces (tabard, cross, slits, crown, belt) keep their own materials.
        /// armPivot is null on the rigged body - arm plates are primitive-only,
        /// because bind-pose arms would not line up with fixed plates.</summary>
        private static void BuildArmorRigs(Transform root, Transform armPivot)
        {
            var steel   = TierMaterial(4);   // placeholder until SetArmor tints
            var dark    = Mat(new Color(0.04f, 0.04f, 0.05f), 0.20f, 0.20f);
            var cloth   = Mat(new Color(0.90f, 0.88f, 0.82f), 0.00f, 0.10f);
            var cross   = Mat(new Color(0.72f, 0.10f, 0.08f), 0.00f, 0.15f);
            var leather = Mat(new Color(0.30f, 0.20f, 0.12f), 0.05f, 0.25f);
            var gold    = Mat(new Color(0.85f, 0.68f, 0.25f), 0.90f, 0.70f);

            var rig = new GameObject(ArmorRigName).transform;
            rig.SetParent(root, false);

            Part(rig, PrimitiveType.Cube, "PlateGreaveL", new Vector3(-0.11f, 0.55f, 0f), new Vector3(0.21f, 0.42f, 0.21f), steel);
            Part(rig, PrimitiveType.Cube, "PlateGreaveR", new Vector3( 0.11f, 0.55f, 0f), new Vector3(0.21f, 0.42f, 0.21f), steel);
            Part(rig, PrimitiveType.Cube, "PlateFaulds",  new Vector3(0f, 0.90f, 0f),     new Vector3(0.42f, 0.18f, 0.30f), steel);
            Part(rig, PrimitiveType.Cube, "ArmorBelt",    new Vector3(0f, 0.99f, 0f),     new Vector3(0.48f, 0.08f, 0.33f), leather);

            Part(rig, PrimitiveType.Cube, "PlateCuirass", new Vector3(0f, 1.28f, 0f),      new Vector3(0.46f, 0.44f, 0.30f), steel);
            Part(rig, PrimitiveType.Cube, "TabardF",      new Vector3(0f, 1.22f, 0.160f),  new Vector3(0.30f, 0.52f, 0.02f), cloth);
            Part(rig, PrimitiveType.Cube, "TabardB",      new Vector3(0f, 1.22f, -0.160f), new Vector3(0.30f, 0.52f, 0.02f), cloth);
            Part(rig, PrimitiveType.Cube, "CrossVF",      new Vector3(0f, 1.23f, 0.174f),  new Vector3(0.07f, 0.34f, 0.012f), cross);
            Part(rig, PrimitiveType.Cube, "CrossHF",      new Vector3(0f, 1.32f, 0.174f),  new Vector3(0.22f, 0.07f, 0.012f), cross);
            Part(rig, PrimitiveType.Cube, "CrossVB",      new Vector3(0f, 1.23f, -0.174f), new Vector3(0.07f, 0.34f, 0.012f), cross);
            Part(rig, PrimitiveType.Cube, "CrossHB",      new Vector3(0f, 1.32f, -0.174f), new Vector3(0.22f, 0.07f, 0.012f), cross);

            Part(rig, PrimitiveType.Sphere,  "PlatePauldronL", new Vector3(-0.27f, 1.46f, 0f), new Vector3(0.23f, 0.17f, 0.23f), steel);
            Part(rig, PrimitiveType.Sphere,  "PlatePauldronR", new Vector3( 0.27f, 1.46f, 0f), new Vector3(0.23f, 0.17f, 0.23f), steel);

            Part(rig, PrimitiveType.Cylinder, "PlateHelm", new Vector3(0f, 1.71f, 0f),     new Vector3(0.34f, 0.145f, 0.34f), steel);
            Part(rig, PrimitiveType.Cylinder, "CrownBand", new Vector3(0f, 1.845f, 0f),    new Vector3(0.355f, 0.012f, 0.355f), gold);
            Part(rig, PrimitiveType.Cube,     "EyeSlit",   new Vector3(0f, 1.75f, 0.155f), new Vector3(0.20f, 0.030f, 0.035f), dark);
            Part(rig, PrimitiveType.Cube,     "FaceSlit",  new Vector3(0f, 1.69f, 0.158f), new Vector3(0.030f, 0.14f, 0.030f), dark);

            if (armPivot != null)
            {
                Part(rig, PrimitiveType.Capsule, "PlateArmL",      new Vector3(-0.30f, 1.14f, 0f), new Vector3(0.15f, 0.27f, 0.15f), steel);
                Part(rig, PrimitiveType.Sphere,  "PlateGauntletL", new Vector3(-0.30f, 0.86f, 0f), new Vector3(0.17f, 0.17f, 0.17f), steel);

                var rigR = new GameObject(ArmorRigRName).transform;
                rigR.SetParent(armPivot, false);
                Part(rigR, PrimitiveType.Capsule, "PlateArmR",      new Vector3(0f, -0.30f, 0f), new Vector3(0.15f, 0.27f, 0.15f), steel);
                Part(rigR, PrimitiveType.Sphere,  "PlateGauntletR", new Vector3(0f, -0.58f, 0f), new Vector3(0.17f, 0.17f, 0.17f), steel);
                rigR.gameObject.SetActive(false);
            }

            rig.gameObject.SetActive(false);
        }

        private static void TintPlates(Transform rig, Material mat)
        {
            if (rig == null || mat == null) return;
            foreach (var r in rig.GetComponentsInChildren<MeshRenderer>(true))
                if (r.gameObject.name.StartsWith("Plate")) r.sharedMaterial = mat;
        }

        /// <summary>One shared material per tier - the read at a glance:
        /// 1 quilted cloth, 2 hardened leather, 3 iron, 4 steel, 5 gilded,
        /// 6 void-metal.</summary>
        private static Material TierMaterial(int tier)
        {
            if (_tierMats.TryGetValue(tier, out var cached) && cached != null) return cached;
            Color c; float metallic, smooth;
            switch (tier)
            {
                case 1:  c = new Color(0.72f, 0.62f, 0.46f); metallic = 0.00f; smooth = 0.15f; break;
                case 2:  c = new Color(0.52f, 0.34f, 0.18f); metallic = 0.25f; smooth = 0.30f; break;
                case 3:  c = new Color(0.45f, 0.47f, 0.50f); metallic = 0.70f; smooth = 0.35f; break;
                case 5:  c = new Color(0.83f, 0.66f, 0.24f); metallic = 0.95f; smooth = 0.75f; break;
                case 6:  c = new Color(0.16f, 0.15f, 0.24f); metallic = 0.90f; smooth = 0.85f; break;
                default: c = new Color(0.64f, 0.67f, 0.72f); metallic = 0.85f; smooth = 0.60f; break;   // 4: steel
            }
            var m = Mat(c, metallic, smooth);
            _tierMats[tier] = m;
            return m;
        }

        // ─────────────────────────── helpers ───────────────────────────

        private static Transform Part(Transform parent, PrimitiveType type, string name,
            Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);   // display only - never block rays or physics
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        private static void Tint(Material m, Color c)
        {
            if (m == null) return;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        private static Material Mat(Color c, float metallic, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            return m;
        }
    }
}
