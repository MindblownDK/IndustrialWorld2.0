// Assets/Scripts/VoxelEngine/Combat/EnemyRigAnimator.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║         PROCEDURAL ENEMY RIG ANIMATION (17.19.0-dev)             ║
// ║  Every hostile rig in the game is a procedural cube/part build   ║
// ║  with no skeleton and no clips. This component gives all of      ║
// ║  them a readable body: a speed-driven walk/run cycle, idle       ║
// ║  life, wing beats for the flyers, tail/segment waves, and -      ║
// ║  the combat point - VISIBLE ATTACK TELEGRAPHS: a windup pose     ║
// ║  with flaring eyes before every bite, cast, gaze or charge, so   ║
// ║  the player can see the attack coming and break range.           ║
// ║                                                                  ║
// ║  Parts are bound by NAME (the setup builders use consistent      ║
// ║  L/R and F/B naming), posed additively over their authored       ║
// ║  local transforms, and never touch scale or colliders. Speed     ║
// ║  comes from position deltas, so the SAME animator drives the     ║
// ║  host's simulated enemies and the guests' kinematic replicas.    ║
// ║  Telegraphs replicate over the existing EnemyCast channel.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Combat
{
    public class EnemyRigAnimator : MonoBehaviour
    {
        public enum TelegraphKind { None = 0, Windup = 1, Cast = 2, Gaze = 3, Charge = 4 }

        private enum Role { Torso, Head, Jaw, Arm, Leg, Wing, Tail, Eye }

        private sealed class Part
        {
            public Transform T;
            public Role Role;
            public int Side;        // +1 left, -1 right, 0 centre
            public bool Front;      // front-marked limb (diagonal gait pairing)
            public int Index;       // sequence index inside its role (tail/wing stagger)
            public bool Flip;       // mirrored jaw halves (BeakUpper opens the other way)
            public Quaternion AuthoredRot;
            public Vector3 AuthoredPos;
            public Quaternion AuthoredRootRot;
            public Vector3 AuthoredRootPos;
            public Vector3 SwingPivotRoot;
            public int SwingSide;
            public bool UseRootArmSwing;
            public Color BaseEyeColor;
            public Material EyeMat;
        }

        // ── tuning (code constants; no authored fields to overwrite) ──
        private const float LegSwingDeg = 30f;
        private const float ArmSwingDeg = 22f;
        private const float BobMetres = 0.045f;
        private const float TailWaveDeg = 7f;
        private const float WingFlapDeg = 16f;
        private const float WindupArmDeg = -55f;
        private const float CastArmDeg = -125f;
        private const float StrikeArmDeg = 75f;
        // The Ghoul is built from separate cubes under one root rather than arm
        // joints. Its attack pose rotates each complete side around the shoulder,
        // so the forearm, hand and claws read as one deliberate swipe.
        private const float GhoulWindupArmDeg = 48f;
        private const float GhoulStrikeArmDeg = -100f;
        private const float StrikeJawDeg = 32f;
        private const float StrikeSeconds = 0.22f;
        private const float GhoulStrikeSeconds = 0.34f;

        private readonly List<Part> _parts = new(48);
        private Damageable _owner;
        private bool _bound;
        private bool _isGhoulRig;
        private Vector3 _prevPos;
        private float _speed;
        private float _phase;
        private float _idleT;

        private TelegraphKind _telKind = TelegraphKind.None;
        private float _telT0 = -1f;
        private float _telDur;
        private float _strikeT0 = -1f;
        private float _strikeDuration = StrikeSeconds;
        private float _strikeWindupCarry;

        /// <summary>Idempotent attach: the enemy Awakes call this on the host,
        /// HostileSync calls it for guest replicas. Returns null only for a
        /// destroyed owner.</summary>
        public static EnemyRigAnimator Attach(Damageable owner)
        {
            if (owner == null) return null;
            var anim = owner.GetComponent<EnemyRigAnimator>();
            if (anim == null) anim = owner.gameObject.AddComponent<EnemyRigAnimator>();
            anim._owner = owner;
            if (!anim._bound) anim.Bind();
            return anim;
        }

        // ── public telegraph API (host AI calls; guests receive via cast) ──

        /// <summary>Local-only telegraph start (used by the guest replay).</summary>
        public void BeginTelegraph(TelegraphKind kind, float duration)
        {
            if (kind == TelegraphKind.None) return;
            _telKind = kind;
            _telT0 = Time.time;
            _telDur = Mathf.Max(0.1f, duration);
        }

        /// <summary>Telegraph + replicate over the existing EnemyCast channel.</summary>
        public void Telegraph(TelegraphKind kind, float duration)
        {
            BeginTelegraph(kind, duration);
            if (_owner != null)
                VoxelEngine.Networking.HostileSync.AnnounceCast(_owner,
                    VoxelEngine.Networking.HostileSync.CastTelegraph,
                    _owner.transform.position,
                    new Vector3((float)(int)kind, duration, 0f));
        }

        /// <summary>The attack's strike motion, replicated too.</summary>
        public void Strike()
        {
            BeginStrike();
            if (_owner != null)
                VoxelEngine.Networking.HostileSync.AnnounceCast(_owner,
                    VoxelEngine.Networking.HostileSync.CastStrike,
                    _owner.transform.position, Vector3.zero);
        }

        /// <summary>Local-only strike motion (used by the guest replay).</summary>
        public void BeginStrike()
        {
            float carriedWindup = _isGhoulRig && _telKind == TelegraphKind.Windup
                ? TelegraphWeight()
                : 0f;
            _strikeT0 = Time.time;
            _strikeDuration = _isGhoulRig ? GhoulStrikeSeconds : StrikeSeconds;
            _strikeWindupCarry = carriedWindup;
            _telKind = TelegraphKind.None;   // the windup hands over to the strike
        }

        private float TelegraphWeight()
        {
            if (_telKind == TelegraphKind.None || _telT0 < 0f) return 0f;
            float u = (Time.time - _telT0) / Mathf.Max(0.1f, _telDur);
            if (u >= 1f)
            {
                _telKind = TelegraphKind.None;
                _telT0 = -1f;
                return 0f;
            }
            return u < 0.25f ? Mathf.SmoothStep(0f, 1f, u * 4f)
                 : u > 0.8f ? Mathf.SmoothStep(1f, 0f, (u - 0.8f) * 5f)
                 : 1f;
        }

        // ── binding ────────────────────────────────────────────────────

        private void Bind()
        {
            _bound = true;
            _parts.Clear();
            _prevPos = transform.position;
            _isGhoulRig = GetComponent<EnemyGhoul>() != null;

            var all = GetComponentsInChildren<Transform>(true);
            Vector3 leftShoulder = _isGhoulRig ? ResolveGhoulArmPivot(all, 1) : Vector3.zero;
            Vector3 rightShoulder = _isGhoulRig ? ResolveGhoulArmPivot(all, -1) : Vector3.zero;
            int tailIdx = 0, wingIdx = 0, legIdx = 0, armIdx = 0;
            foreach (var t in all)
            {
                if (t == transform) continue;
                string n = t.name.ToLowerInvariant();
                Role role;
                if (Contains(n, "eye")) role = Role.Eye;
                else if (Contains(n, "wing")) role = Role.Wing;
                else if (Contains(n, "tail", "seg", "stinger", "tuft")) role = Role.Tail;
                else if (Contains(n, "leg", "thigh", "shin", "hoof", "foot", "paw")) role = Role.Leg;
                else if (Contains(n, "claw", "arm", "hand")
                    || (_isGhoulRig && Contains(n, "shoulder", "shldr"))) role = Role.Arm;
                else if (Contains(n, "beak", "jaw", "mouth", "snout")) role = Role.Jaw;
                else if (Contains(n, "head", "neck", "skull")) role = Role.Head;
                else if (Contains(n, "torso", "body", "chest", "hips", "rump", "hump", "belly", "core")) role = Role.Torso;
                else continue;   // decor (horns, plates, crests, bars) stays rigid

                Vector3 rootLocalPos = transform.InverseTransformPoint(t.position);
                Quaternion rootLocalRot = Quaternion.Inverse(transform.rotation) * t.rotation;
                var p = new Part
                {
                    T = t,
                    Role = role,
                    Side = SideOf(n),
                    Front = Contains(n, "fl", "fr"),   // explicit front-limb markers
                    Flip = role == Role.Jaw && Contains(n, "upper"),
                    AuthoredRot = t.localRotation,
                    AuthoredPos = t.localPosition,
                    AuthoredRootRot = rootLocalRot,
                    AuthoredRootPos = rootLocalPos,
                };
                if (_isGhoulRig && role == Role.Arm)
                {
                    p.SwingSide = p.Side != 0 ? p.Side : (rootLocalPos.x <= 0f ? 1 : -1);
                    p.SwingPivotRoot = p.SwingSide > 0 ? leftShoulder : rightShoulder;
                    p.UseRootArmSwing = true;
                }
                switch (role)
                {
                    case Role.Tail: p.Index = tailIdx++; break;
                    case Role.Wing: p.Index = wingIdx++; break;
                    case Role.Leg: p.Index = legIdx++; break;
                    case Role.Arm: p.Index = armIdx++; break;
                }
                if (role == Role.Eye)
                {
                    var ren = t.GetComponent<Renderer>();
                    if (ren != null)
                    {
                        // Per-instance material: the flare must never bleed into
                        // the shared asset (or every ghoul on the map glows).
                        p.EyeMat = ren.material;
                        p.BaseEyeColor = p.EyeMat.color;
                        p.EyeMat.EnableKeyword("_EMISSION");
                    }
                }
                _parts.Add(p);
            }
        }

        /// <summary>Resolve a shoulder in root-local space for the Ghoul's flat,
        /// unrigged prefab. The setup-built rig has ShldrL/ShldrR; the upper-arm
        /// fallback keeps edited prefabs usable without authoring new joints.</summary>
        private Vector3 ResolveGhoulArmPivot(Transform[] all, int side)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == transform) continue;
                string n = t.name.ToLowerInvariant();
                if (SideOf(n) == side && Contains(n, "shoulder", "shldr"))
                    return transform.InverseTransformPoint(t.position);
            }

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == transform) continue;
                string n = t.name.ToLowerInvariant();
                if (SideOf(n) == side && Contains(n, "uparm", "upperarm"))
                    return transform.InverseTransformPoint(t.position) + Vector3.up * 0.18f;
            }

            float highestY = float.NegativeInfinity;
            Vector3 highestArm = Vector3.zero;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == transform) continue;
                string n = t.name.ToLowerInvariant();
                if (SideOf(n) != side || !Contains(n, "arm", "hand", "claw")) continue;
                Vector3 rootPos = transform.InverseTransformPoint(t.position);
                if (rootPos.y <= highestY) continue;
                highestY = rootPos.y;
                highestArm = rootPos;
            }
            if (!float.IsNegativeInfinity(highestY)) return highestArm + Vector3.up * 0.12f;

            // The stock Ghoul dimensions; only reached for a customized rig whose
            // limbs carry no recognizable shoulder or side names.
            return new Vector3(side > 0 ? -0.24f : 0.24f, 1.18f, 0.04f);
        }

        private static bool Contains(string n, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (n.Contains(keys[i])) return true;
            return false;
        }

        /// <summary>+1 for left-marked, -1 for right-marked, 0 for centre.
        /// Strips trailing digits and rig suffixes first so the side letter is
        /// last: ThighL, LegFL, ClawL1 AND WingRBase / WingLTip all resolve.</summary>
        private static readonly string[] SideSuffixes = { "base", "mid", "tip", "feather", "tuft", "upper", "lower" };

        private static int SideOf(string lower)
        {
            string n = lower;
            bool stripped = true;
            while (stripped && n.Length > 0)
            {
                stripped = false;
                for (int s = 0; s < SideSuffixes.Length; s++)
                {
                    string suf = SideSuffixes[s];
                    if (n.Length > suf.Length && n.EndsWith(suf))
                    {
                        n = n.Substring(0, n.Length - suf.Length);
                        stripped = true;
                    }
                }
                while (n.Length > 0 && n[n.Length - 1] >= '0' && n[n.Length - 1] <= '9')
                {
                    n = n.Substring(0, n.Length - 1);
                    stripped = true;
                }
            }
            if (n.Length == 0) return 0;
            char c = n[n.Length - 1];
            if (c == 'l') return 1;
            if (c == 'r') return -1;
            return 0;
        }

        // ── the pose loop ──────────────────────────────────────────────

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0.0001f) return;

            // Speed from position deltas: identical on the host (rigidbody
            // driven) and on guest replicas (HostileSync easing), so one
            // animator serves both without any replicated velocity field.
            Vector3 delta = transform.position - _prevPos;
            _prevPos = transform.position;
            Vector3 up = VoxelEngine.Cosmos.GravityProvider.GetUp(transform.position);
            if (up.sqrMagnitude < 0.0001f) up = transform.up;
            float inst = Vector3.ProjectOnPlane(delta, up).magnitude / dt;
            if (inst > 40f) inst = 0f;            // teleport/respawn blink: ignore
            _speed = Mathf.Lerp(_speed, inst, 1f - Mathf.Exp(-8f * dt));
            _idleT += dt;

            // A sleeping hostile holds its pose (host-side distance sleep).
            if (_owner != null && HostileSleepManager.IsSleeping(_owner))
                return;

            float sf = Mathf.Clamp01(_speed / 2.8f);          // 0 idle .. 1 running
            _phase += dt * (2.2f + _speed * 2.6f);

            // Telegraph envelope: fast in, hold, fast out. BeginStrike reads the
            // same envelope so the Ghoul's windup flows directly into its swing.
            float tel = TelegraphWeight();
            float strike = 0f;
            float strikeTime01 = 0f;
            float ghoulStrikeSwing = 0f;
            if (_strikeT0 >= 0f)
            {
                float u = (Time.time - _strikeT0) / Mathf.Max(0.1f, _strikeDuration);
                if (u >= 1f)
                {
                    _strikeT0 = -1f;
                    _strikeWindupCarry = 0f;
                }
                else
                {
                    strikeTime01 = u;
                    strike = 1f - u * u;                // sharp body snap, soft return
                    if (_isGhoulRig)
                    {
                        // Deliberate pull-through: accelerate the arm from its reared
                        // pose, hold the forward claw at impact, then ease it home.
                        if (u < 0.32f) ghoulStrikeSwing = Mathf.SmoothStep(0f, 1f, u / 0.32f);
                        else if (u < 0.56f) ghoulStrikeSwing = 1f;
                        else ghoulStrikeSwing = Mathf.SmoothStep(1f, 0f, (u - 0.56f) / 0.44f);
                    }
                }
            }

            bool flyer = false;
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].Role == Role.Wing) { flyer = true; break; }

            Color warn = _telKind switch
            {
                TelegraphKind.Gaze => new Color(0.55f, 1f, 0.45f),
                TelegraphKind.Cast => new Color(1f, 0.55f, 0.15f),
                TelegraphKind.Charge => new Color(1f, 0.75f, 0.2f),
                _ => new Color(1f, 0.3f, 0.15f),
            };
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 26f);

            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                if (p.T == null) continue;
                float pitch = 0f, yaw = 0f, roll = 0f;
                Vector3 off = Vector3.zero;

                switch (p.Role)
                {
                    case Role.Leg:
                    {
                        float gait = _phase + (p.Side > 0 ? 0f : Mathf.PI)
                            + (p.Front ? 0f : Mathf.PI);        // diagonal pairs
                        pitch = Mathf.Sin(gait) * (6f + LegSwingDeg * sf);
                        if (flyer) pitch *= 0.35f;              // flyers tuck their legs
                        if (_telKind == TelegraphKind.Charge && p.Front)
                        {
                            // Pawing the ground: rapid alternating stamp.
                            pitch = Mathf.Sign(p.Side) * Mathf.Sin(Time.time * 15f) * 30f * tel;
                            off.y = -0.03f * tel;
                        }
                        break;
                    }
                    case Role.Arm:
                    {
                        int swingSide = p.UseRootArmSwing ? p.SwingSide : p.Side;
                        float gait = _phase + (swingSide > 0 ? Mathf.PI : 0f);
                        pitch = Mathf.Sin(gait) * (4f + ArmSwingDeg * sf);
                        if (flyer) pitch *= 0.3f;
                        break;
                    }
                    case Role.Torso:
                        off.y = Mathf.Sin(_phase * 2f) * BobMetres * sf
                              + Mathf.Sin(_idleT * 1.6f) * 0.008f;
                        roll = Mathf.Sin(_phase) * 2.5f * sf;
                        break;
                    case Role.Head:
                        pitch = -Mathf.Sin(_phase * 2f) * 3f * sf
                              + Mathf.Sin(_idleT * 0.7f) * 1.5f * (1f - sf);
                        yaw = Mathf.Sin(_idleT * 0.45f) * 4f * (1f - sf);
                        break;
                    case Role.Jaw:
                        pitch = Mathf.Abs(Mathf.Sin(_phase)) * 3f * sf;   // pant while running
                        break;
                    case Role.Wing:
                    {
                        float flap = Mathf.Sin(_phase * (flyer ? 0.9f : 0.4f) + p.Index * 0.5f);
                        roll = p.Side * flap * (WingFlapDeg + 10f * sf + (flyer ? 8f : 0f));
                        break;
                    }
                    case Role.Tail:
                        yaw = Mathf.Sin(_phase * 0.55f - p.Index * 0.65f) * (3f + TailWaveDeg * sf)
                            + Mathf.Sin(_idleT * 1.1f - p.Index * 0.5f) * 3f * (1f - sf);
                        break;
                    case Role.Eye:
                        if (p.EyeMat != null)
                        {
                            float flare = tel * pulse;
                            p.EyeMat.color = Color.Lerp(p.BaseEyeColor, warn, flare * 0.85f);
                            p.EyeMat.SetColor("_EmissionColor", warn * (flare * 2.2f));
                        }
                        continue;   // eyes glow only, never pose
                }

                // ── telegraph pose overlays ──
                if (tel > 0f)
                {
                    switch (_telKind)
                    {
                        case TelegraphKind.Windup:
                            if (p.Role == Role.Arm)
                                pitch += (p.UseRootArmSwing ? GhoulWindupArmDeg : WindupArmDeg) * tel;
                            else if (p.Role == Role.Head) pitch += -12f * tel;
                            else if (p.Role == Role.Jaw) pitch += 18f * tel;
                            else if (p.Role == Role.Torso) off.y -= 0.05f * tel;
                            break;
                        case TelegraphKind.Cast:
                            if (p.Role == Role.Arm) { pitch += CastArmDeg * tel; roll += p.Side * 20f * tel; }
                            else if (p.Role == Role.Head) pitch += -10f * tel;
                            else if (p.Role == Role.Torso) off.y += 0.04f * tel;
                            else if (p.Role == Role.Tail) pitch += -14f * tel;   // rear the tail
                            break;
                        case TelegraphKind.Gaze:
                            if (p.Role == Role.Head) pitch += -18f * tel;
                            else if (p.Role == Role.Torso) { off.y += 0.03f * tel; off.z -= 0.05f * tel; }
                            else if (p.Role == Role.Jaw) pitch += 10f * tel;
                            break;
                        case TelegraphKind.Charge:
                            if (p.Role == Role.Head) pitch += 22f * tel;         // head down, aimed
                            else if (p.Role == Role.Torso) off.y -= 0.09f * tel; // crouch
                            else if (p.Role == Role.Jaw) pitch += 14f * tel;
                            break;
                    }
                }

                // ── strike motion ──
                if (strike > 0f || ghoulStrikeSwing > 0f || _strikeWindupCarry > 0f)
                {
                    if (p.Role == Role.Arm)
                    {
                        if (p.UseRootArmSwing)
                        {
                            float carriedWindup = _strikeWindupCarry * (1f - strikeTime01);
                            pitch += GhoulWindupArmDeg * carriedWindup
                                   + GhoulStrikeArmDeg * ghoulStrikeSwing;
                        }
                        else pitch += StrikeArmDeg * strike;
                    }
                    else if (p.Role == Role.Head) pitch += 18f * strike;
                    else if (p.Role == Role.Jaw) pitch += StrikeJawDeg * strike;
                    else if (p.Role == Role.Torso) off.z += 0.07f * strike;
                    else if (p.Role == Role.Tail) pitch += 16f * strike;
                }

                // Mirrored jaw halves (BeakUpper/BeakLower) open in opposite
                // directions from one shared jaw value.
                if (p.Role == Role.Jaw && p.Flip) pitch = -pitch;

                if (p.UseRootArmSwing)
                {
                    // The Ghoul's arms are several sibling meshes, not a bone chain.
                    // Apply one shoulder-rooted transform to every piece on that side
                    // so the upper arm, forearm, hand and claws stay connected in motion.
                    Quaternion swing = Quaternion.Euler(pitch, yaw, roll);
                    Vector3 rootPose = p.SwingPivotRoot
                        + swing * (p.AuthoredRootPos - p.SwingPivotRoot) + off;
                    p.T.position = transform.TransformPoint(rootPose);
                    p.T.rotation = transform.rotation * swing * p.AuthoredRootRot;
                }
                else
                {
                    p.T.localRotation = p.AuthoredRot * Quaternion.Euler(pitch, yaw, roll);
                    p.T.localPosition = p.AuthoredPos + off;
                }
            }
        }

        private void OnDisable()
        {
            // Restore authored poses and base eye colors so a disabled animator
            // (pooled prefab, editor) never freezes mid-swing.
            for (int i = 0; i < _parts.Count; i++)
            {
                var p = _parts[i];
                if (p.T == null) continue;
                p.T.localRotation = p.AuthoredRot;
                p.T.localPosition = p.AuthoredPos;
                if (p.EyeMat != null)
                {
                    p.EyeMat.color = p.BaseEyeColor;
                    p.EyeMat.SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }
}
