// Assets/Scripts/VoxelEngine/Networking/CrusaderAnimator.cs
//
// 14.17.0-dev - the crusader moves. Runtime locomotion for the rigged avatar:
// a PlayableGraph built straight from the clips in Resources/PlayerAnimations,
// no AnimatorController asset, no editor step, self-healing like the rest of
// the avatar - if the clips folder is missing the driver simply disables
// itself and the model stays in bind pose exactly as before.
//
// States and their sources (all Mixamo, humanoid-retargeted):
//   Idle.fbx           - standing
//   Sad_idle.fbx       - standing at low health (<= 35%)
//   Walking.fbx        - slow movement  ─┐ blended continuously
//   Running.fbx        - fast movement  ─┘ by measured speed
//   Running_slide.fbx  - the slide flag mirrored by PlayerAvatar
//   Jumping.fbx        - airborne (vertical speed threshold)
//
// Motion is derived from the avatar's own transform - NetworkTransform already
// moves and smooths it on remote machines - so the driver needs no wire data
// beyond the slide flag and the health the avatar mirrors anyway. Planar speed
// is measured against the avatar's OWN up axis, never world up: spherical
// planets make world-up meaningless almost everywhere.
//
// 14.21.1 - FOOT GROUNDING. The rig's height was solved exactly once, at
// build time, from the BIND pose (CrusaderModel measures the lowest baked
// vertex and lifts the rig by it). Then 14.17.0 put animation on top: every
// Mixamo clip carries its own hip height, so the moment Idle or Walking takes
// over, the solved offset is for a pose the model is no longer in - the
// avatar settles half a metre above the ground and stays there. The fix is to
// re-solve it every frame from the CURRENT animated pose: measure the lowest
// foot bone and shift the rig so the sole sits back on the avatar's pivot.
// Skipped while airborne, where the feet are supposed to leave the floor.
//
// 14.22.0 - CROUCH. Crouching used to squash the model vertically (a 0.62
// scale on the root). If crouch clips are present they are used instead and
// the squash is never applied; without them the squash stays exactly as it
// was, so this is additive.
//
// Root motion stays OFF: the slide (and the future weapon pack clips) are not
// authored in place, and the network transform owns all movement - humanoid
// retargeting simply drops the root translation for us.
//
// The arm-out building pose is applied in LateUpdate, AFTER the animator has
// written the bones, so the pose survives animation instead of being
// overwritten by it. It re-derives the swing from the CURRENT animated pose
// each frame, so the raised arm still breathes with the underlying animation.

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VoxelEngine.Networking
{
    public class CrusaderAnimator : MonoBehaviour
    {
        private const int IDLE = 0, SAD = 1, WALK = 2, RUN = 3, SLIDE = 4, JUMP = 5;
        // Weapon-stance slots (14.18.0): full-body overrides while a weapon is
        // held. Sword uses the sword-and-shield pack; rifle/pistol slots come
        // when those clips land.
        private const int S_IDLE = 6, S_WALK = 7, S_RUN = 8, S_JUMP = 9, ATTACK = 10;
        // Crouch (14.22.0): optional. Missing clips fall back to the old squash.
        private const int C_IDLE = 11, C_WALK = 12;
        private const int CLIP_COUNT = 13;

        /// <summary>Mirrored slide flag (set by PlayerAvatar).</summary>
        public bool Sliding;
        /// <summary>Low-health flag (set by PlayerAvatar from the health mirror).</summary>
        public bool LowHealth;
        /// <summary>Arm-out building pose (set through CrusaderModel.SetBuildPose).</summary>
        public bool BuildPose;
        /// <summary>Weapon stance: 0 none, 1 sword (set by PlayerAvatar from the held item).</summary>
        public int Stance;
        /// <summary>Mirrored crouch flag (set by PlayerAvatar). Only honoured
        /// when crouch clips exist - see HasCrouchClips.</summary>
        public bool Crouched;

        /// <summary>True when real crouch animation is available, so the avatar
        /// knows not to fall back to squashing the model.</summary>
        public bool HasCrouchClips => HasClips && _hasClip[C_IDLE];

        /// <summary>True once the graph is running with at least the idle clip.</summary>
        public bool HasClips { get; private set; }

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly float[] _weights = new float[CLIP_COUNT];
        private readonly bool[] _hasClip = new bool[CLIP_COUNT];
        private readonly AnimationClipPlayable[] _playables = new AnimationClipPlayable[CLIP_COUNT];
        private readonly float[] _lengths = new float[CLIP_COUNT];
        private float _settle;          // grace period after spawn/teleport
        private float _diagAt = -1f;    // one-shot console diagnostic
        private bool _wasAirborne;
        private bool _wasSliding;
        private float _attackTime;      // remaining one-shot attack window
        private int _lastLoggedStance = -1;
        private bool _attackLogged;
        private readonly float[] _targets = new float[CLIP_COUNT];

        private Transform _avatarRoot;   // the PlayerAvatar transform (network-moved)
        private Transform _upperArm;     // right upper-arm bone (building pose)
        private Transform _hand;         // right hand bone (pose direction reference)
        private Vector3 _lastPos;
        private bool _hasLastPos;

        // ── foot grounding (14.21.1) ──
        private Transform _leftFoot, _rightFoot;
        private float _bindLocalY;      // the bind-pose height CrusaderModel solved
        private float _bindFootY;       // where the lowest foot bone sat in that pose
        private float _groundFix;       // smoothed correction currently applied
        private bool _canGround;
        private bool _airborneNow;
        private float _speed;            // smoothed planar m/s
        private float _vertical;         // smoothed vertical m/s

        public void Initialize(Animator animator, Transform avatarRoot,
            Transform upperArm, Transform hand)
        {
            _avatarRoot = avatarRoot;
            _upperArm = upperArm;
            _hand = hand;

            var clips = new AnimationClip[CLIP_COUNT];
            clips[IDLE]  = LoadClip("Idle");
            clips[SAD]   = LoadClip("Sad_idle");
            clips[WALK]  = LoadClip("Walking");
            clips[RUN]   = LoadClip("Running");
            clips[SLIDE] = LoadClip("Running_slide");
            clips[JUMP]  = LoadClip("Jumping");
            const string sword = "Pro Sword and Shield Pack/sword and shield ";
            clips[S_IDLE] = LoadClip(sword + "idle");
            clips[S_WALK] = LoadClip(sword + "walk");
            clips[S_RUN]  = LoadClip(sword + "run");
            clips[S_JUMP] = LoadClip(sword + "jump");
            clips[ATTACK] = LoadClip(sword + "slash");
            // Mixamo exports these under several names depending on how they
            // were downloaded; accept any of them rather than demanding one.
            clips[C_IDLE] = LoadClip("Crouch_idle", "Crouching_idle", "Crouch idle", "Crouching");
            clips[C_WALK] = LoadClip("Crouched_walking", "Crouch_walk", "Crouched walking", "Crouch_walking");

            if (animator == null || clips[IDLE] == null)
            {
                // No clips yet (folder not under Resources) - bind pose, as before.
                enabled = false;
                return;
            }

            // Never let renderer-visibility culling freeze the skeleton - the
            // skinned bounds on a rescaled Mixamo rig are not trustworthy.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // Self-diagnosis: humanoid clips are required for a humanoid avatar.
            for (int i = 0; i < CLIP_COUNT; i++)
                if (clips[i] != null && !clips[i].humanMotion)
                    Debug.LogWarning("[Crusader] Animation clip slot " + i
                        + " is not Humanoid - select its FBX, Rig tab, Animation Type: Humanoid, Apply.");

            _graph = PlayableGraph.Create("CrusaderAnimator");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, CLIP_COUNT);
            for (int i = 0; i < CLIP_COUNT; i++)
            {
                _hasClip[i] = clips[i] != null;
                var clip = _hasClip[i] ? clips[i] : clips[IDLE];
                var playable = AnimationClipPlayable.Create(_graph, clip);
                playable.SetApplyFootIK(false);
                _playables[i] = playable;
                _lengths[i] = Mathf.Max(0.01f, clip.length);
                _graph.Connect(playable, 0, _mixer, i);
                _mixer.SetInputWeight(i, i == IDLE ? 1f : 0f);
            }
            _weights[IDLE] = 1f;
            var output = AnimationPlayableOutput.Create(_graph, "Crusader", animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _settle = 0.75f;   // ignore the spawn snap - it looks like a huge fall
            _diagAt = Time.time + 4f;
            HasClips = true;

            PrepareFootGrounding();
        }

        /// <summary>Play the swing clip once, full body. 14.18.4: any held-item
        /// swing plays it - with a sword it reads as a slash, with a pickaxe or
        /// axe as the working chop. Safe no-op when the clip is missing.</summary>
        public void PlayAttack()
        {
            if (!HasClips || !_hasClip[ATTACK]) return;
            if (!_attackLogged)
            {
                _attackLogged = true;   // once per stance session - no spam on auto-swing
                Debug.Log("[Crusader] attack replicated (slash clip "
                    + _lengths[ATTACK].ToString("F2") + "s)");
            }
            // 14.18.6: the pack slash is a full theatrical windup-and-recover,
            // far longer than the actual swing cadence (sword cooldown 0.45 s).
            // Play it fast enough to fit a snappy window so the animation keeps
            // up with the action instead of dragging behind it.
            const float window = 0.55f;
            float speed = Mathf.Clamp(_lengths[ATTACK] * 0.85f / window, 1f, 3.5f);
            _playables[ATTACK].SetSpeed(speed);
            _playables[ATTACK].SetTime(0.0);
            _attackTime = _lengths[ATTACK] * 0.85f / speed;   // release just before the clip ends
        }

        /// <summary>Owner avatars are invisible to their own player - stop paying
        /// for animation they can never see.</summary>
        public void StopForOwner()
        {
            enabled = false;
            if (_graph.IsValid()) _graph.Stop();
        }

        /// <summary>Load the first clip inside one FBX under Resources/PlayerAnimations.
        /// Addressed by FILE name, because Mixamo names every clip inside
        /// "mixamo.com" - the file name is the only reliable identity.</summary>
        private static AnimationClip LoadClip(params string[] files)
        {
            for (int f = 0; f < files.Length; f++)
            {
                var all = Resources.LoadAll<AnimationClip>("PlayerAnimations/" + files[f]);
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null) return all[i];
            }
            return null;
        }

        private void Update()
        {
            if (!HasClips || _avatarRoot == null) return;

            float dt = Time.deltaTime;
            var pos = _avatarRoot.position;
            if (!_hasLastPos) { _lastPos = pos; _hasLastPos = true; }

            // Spawn snaps and respawn teleports are not falling. A player never
            // legitimately moves 3 m in one frame - reset the measurement.
            if ((pos - _lastPos).sqrMagnitude > 9f)
            {
                _lastPos = pos;
                _speed = 0f;
                _vertical = 0f;
                _settle = 0.4f;
            }

            if (dt > 0.0001f)
            {
                var v = (pos - _lastPos) / dt;
                var up = _avatarRoot.up;                    // spherical planets: never world up
                float vy = Vector3.Dot(v, up);
                var planar = v - up * vy;
                float smooth = 1f - Mathf.Exp(-10f * dt);
                _speed = Mathf.Lerp(_speed, planar.magnitude, smooth);
                _vertical = Mathf.Lerp(_vertical, vy, smooth);
            }
            _lastPos = pos;
            if (_settle > 0f) _settle -= dt;

            // Loop the cyclic clips by hand: import-side "Loop Time" no longer
            // matters, and a clip that sat at weight zero can never again be
            // caught frozen on its final frame.
            for (int i = 0; i < CLIP_COUNT; i++)
            {
                if (i == JUMP || i == S_JUMP || i == ATTACK) continue;   // one-shots hold
                double t = _playables[i].GetTime();
                if (t >= _lengths[i]) _playables[i].SetTime(t % _lengths[i]);
            }

            // The stance redirects the core locomotion slots when its clips exist.
            bool sword = Stance == 1;
            if (Stance != _lastLoggedStance)
            {
                _lastLoggedStance = Stance;
                _attackLogged = false;
                Debug.Log("[Crusader] stance -> " + Stance + (sword ? " (sword)" : " (none)")
                    + " swordClips=" + (_hasClip[S_IDLE] ? "loaded" : "MISSING"));
            }
            int idleSlot = sword && _hasClip[S_IDLE] ? S_IDLE : IDLE;
            int walkSlot = sword && _hasClip[S_WALK] ? S_WALK : WALK;
            int runSlot  = sword && _hasClip[S_RUN]  ? S_RUN  : RUN;
            int jumpSlot = sword && _hasClip[S_JUMP] ? S_JUMP : JUMP;

            // Target weights for this frame.
            for (int i = 0; i < CLIP_COUNT; i++) _targets[i] = 0f;
            bool airborne = _settle <= 0f && Mathf.Abs(_vertical) > 2.2f;
            if (airborne && !_wasAirborne) _playables[jumpSlot].SetTime(0.0);   // replay, never a stale frame
            if (Sliding && !_wasSliding) _playables[SLIDE].SetTime(0.0);        // slides start at the drop
            _wasAirborne = airborne;
            _airborneNow = airborne;
            _wasSliding = Sliding;
            if (_attackTime > 0f) _attackTime -= dt;
            bool attacking = _attackTime > 0f && _hasClip[ATTACK];

            bool crouching = Crouched && _hasClip[C_IDLE] && !airborne && !Sliding && !attacking;

            if (airborne && _hasClip[jumpSlot]) _targets[jumpSlot] = 1f;
            else if (attacking) _targets[ATTACK] = 1f;
            else if (Sliding && _hasClip[SLIDE]) _targets[SLIDE] = 1f;
            else if (crouching)
            {
                // A crouch-walk clip is optional: without one, a crouching
                // player who shuffles stays in the crouch idle rather than
                // standing up to walk.
                bool moving = _speed > 0.4f && _hasClip[C_WALK];
                _targets[moving ? C_WALK : C_IDLE] = 1f;
            }
            else if (_speed > 0.4f)
            {
                float run = Mathf.Clamp01((_speed - 2.0f) / 2.5f);
                if (!_hasClip[runSlot]) run = 0f;
                else if (!_hasClip[walkSlot]) run = 1f;
                _targets[walkSlot] = 1f - run;
                _targets[runSlot] = run;
            }
            else if (LowHealth && _hasClip[SAD]) _targets[SAD] = 1f;   // hurt beats stance
            else _targets[idleSlot] = 1f;

            // Crossfade and normalize. Attacks blend much faster than
            // locomotion - a swing must snap in, not ease in.
            float k = 1f - Mathf.Exp(-9f * dt);
            float kFast = 1f - Mathf.Exp(-22f * dt);
            for (int i = 0; i < CLIP_COUNT; i++)
                _weights[i] = Mathf.Lerp(_weights[i], _targets[i], i == ATTACK ? kFast : k);
            float total = 0f;
            for (int i = 0; i < CLIP_COUNT; i++) total += _weights[i];
            if (total < 0.0001f) { _weights[IDLE] = 1f; total = 1f; }
            for (int i = 0; i < CLIP_COUNT; i++)
                _mixer.SetInputWeight(i, _weights[i] / total);

            // One console line a few seconds after spawn - cheap ground truth
            // if an avatar ever animates wrong again.
            if (_diagAt > 0f && Time.time >= _diagAt)
            {
                _diagAt = -1f;
                int top = 0;
                for (int i = 1; i < CLIP_COUNT; i++) if (_weights[i] > _weights[top]) top = i;
                Debug.Log("[Crusader] anim check: speed=" + _speed.ToString("F2")
                    + " vertical=" + _vertical.ToString("F2")
                    + " state=" + top + " (0 idle,1 sad,2 walk,3 run,4 slide,5 jump,6-9 sword,10 attack)"
                    + " stance=" + Stance + " sliding=" + Sliding + " lowHealth=" + LowHealth);
            }
        }

        // ─────────────────────── foot grounding ───────────────────────

        /// <summary>Records what the bind pose looked like, which is the pose
        /// CrusaderModel solved the rig's height against. Everything after this
        /// is measured as a difference from that reference, so a rig with an
        /// unusual scale or an unusual skeleton still lands correctly.</summary>
        private void PrepareFootGrounding()
        {
            _canGround = false;
            var parent = transform.parent;
            if (parent == null) return;

            _leftFoot = FindBone("LeftToeBase") ?? FindBone("LeftFoot");
            _rightFoot = FindBone("RightToeBase") ?? FindBone("RightFoot");
            if (_leftFoot == null && _rightFoot == null) return;   // unknown skeleton: do nothing

            _bindLocalY = transform.localPosition.y;
            if (!TryLowestFoot(parent, out _bindFootY)) return;
            _groundFix = 0f;
            _canGround = true;
        }

        /// <summary>Shortest name wins, so toe bones never lose to their own
        /// children and "LeftFoot" never matches "LeftFootIK_target".</summary>
        private Transform FindBone(string suffix)
        {
            Transform best = null;
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].name.EndsWith(suffix, System.StringComparison.Ordinal)) continue;
                if (best == null || all[i].name.Length < best.name.Length) best = all[i];
            }
            return best;
        }

        private bool TryLowestFoot(Transform parent, out float y)
        {
            y = 0f;
            bool any = false;
            if (_leftFoot != null)
            {
                y = parent.InverseTransformPoint(_leftFoot.position).y;
                any = true;
            }
            if (_rightFoot != null)
            {
                float r = parent.InverseTransformPoint(_rightFoot.position).y;
                y = any ? Mathf.Min(y, r) : r;
                any = true;
            }
            return any;
        }

        /// <summary>Puts the sole back on the ground after the animator has
        /// written the bones. The correction is the difference between where
        /// the lowest foot sits NOW and where it sat in the bind pose that the
        /// rig's height was solved against - no absolute assumption about how
        /// tall the model is or where its hips belong.</summary>
        private void ApplyFootGrounding()
        {
            if (!_canGround) return;
            var parent = transform.parent;
            if (parent == null) return;

            float dt = Time.deltaTime;
            float target = _groundFix;

            // Airborne feet are SUPPOSED to leave the floor; hold the last
            // correction through a jump instead of gluing the model down.
            if (!_airborneNow && _settle <= 0f && TryLowestFoot(parent, out float footY))
            {
                target = _bindFootY - footY;
                // A clip that wants more than this is wrong, not expressive.
                target = Mathf.Clamp(target, -0.6f, 0.6f);
            }

            // Eased, so a walk/idle crossfade does not step the body.
            _groundFix = dt > 0f
                ? Mathf.Lerp(_groundFix, target, 1f - Mathf.Exp(-18f * dt))
                : target;

            var local = transform.localPosition;
            local.y = _bindLocalY + _groundFix;
            transform.localPosition = local;
        }

        private void LateUpdate()
        {
            // Feet first: the grounding offset moves the whole rig, and the
            // building pose below is solved from world-space bone positions.
            ApplyFootGrounding();

            // Building pose - applied after the animator so it wins the frame.
            if (!BuildPose || _upperArm == null || _avatarRoot == null) return;
            Vector3 from = _hand != null && _hand != _upperArm
                ? _hand.position - _upperArm.position
                : _avatarRoot.right;
            if (from.sqrMagnitude < 1e-6f) return;
            from.Normalize();
            Vector3 to = (_avatarRoot.forward * 0.94f - _avatarRoot.up * 0.20f).normalized;
            _upperArm.rotation = Quaternion.FromToRotation(from, to) * _upperArm.rotation;
        }

        private void OnDestroy()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
