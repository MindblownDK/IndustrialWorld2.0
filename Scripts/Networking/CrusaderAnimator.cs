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
        private const int CLIP_COUNT = 6;

        /// <summary>Mirrored slide flag (set by PlayerAvatar).</summary>
        public bool Sliding;
        /// <summary>Low-health flag (set by PlayerAvatar from the health mirror).</summary>
        public bool LowHealth;
        /// <summary>Arm-out building pose (set through CrusaderModel.SetBuildPose).</summary>
        public bool BuildPose;

        /// <summary>True once the graph is running with at least the idle clip.</summary>
        public bool HasClips { get; private set; }

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly float[] _weights = new float[CLIP_COUNT];
        private readonly bool[] _hasClip = new bool[CLIP_COUNT];

        private Transform _avatarRoot;   // the PlayerAvatar transform (network-moved)
        private Transform _upperArm;     // right upper-arm bone (building pose)
        private Transform _hand;         // right hand bone (pose direction reference)
        private Vector3 _lastPos;
        private bool _hasLastPos;
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

            if (animator == null || clips[IDLE] == null)
            {
                // No clips yet (folder not under Resources) - bind pose, as before.
                enabled = false;
                return;
            }

            _graph = PlayableGraph.Create("CrusaderAnimator");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            _mixer = AnimationMixerPlayable.Create(_graph, CLIP_COUNT);
            for (int i = 0; i < CLIP_COUNT; i++)
            {
                _hasClip[i] = clips[i] != null;
                var playable = AnimationClipPlayable.Create(_graph, _hasClip[i] ? clips[i] : clips[IDLE]);
                playable.SetApplyFootIK(false);
                _graph.Connect(playable, 0, _mixer, i);
                _mixer.SetInputWeight(i, i == IDLE ? 1f : 0f);
            }
            _weights[IDLE] = 1f;
            var output = AnimationPlayableOutput.Create(_graph, "Crusader", animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            HasClips = true;
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
        private static AnimationClip LoadClip(string file)
        {
            var all = Resources.LoadAll<AnimationClip>("PlayerAnimations/" + file);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null) return all[i];
            return null;
        }

        private void Update()
        {
            if (!HasClips || _avatarRoot == null) return;

            float dt = Time.deltaTime;
            var pos = _avatarRoot.position;
            if (!_hasLastPos) { _lastPos = pos; _hasLastPos = true; }
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

            // Target weights for this frame.
            float idleW = 0f, sadW = 0f, walkW = 0f, runW = 0f, slideW = 0f, jumpW = 0f;
            bool airborne = Mathf.Abs(_vertical) > 2.2f;
            if (airborne && _hasClip[JUMP]) jumpW = 1f;
            else if (Sliding && _hasClip[SLIDE]) slideW = 1f;
            else if (_speed > 0.4f)
            {
                float run = Mathf.Clamp01((_speed - 2.0f) / 2.5f);
                if (!_hasClip[RUN]) run = 0f;
                else if (!_hasClip[WALK]) run = 1f;
                walkW = 1f - run;
                runW = run;
            }
            else if (LowHealth && _hasClip[SAD]) sadW = 1f;
            else idleW = 1f;

            // Crossfade and normalize.
            float k = 1f - Mathf.Exp(-9f * dt);
            _weights[IDLE]  = Mathf.Lerp(_weights[IDLE],  idleW,  k);
            _weights[SAD]   = Mathf.Lerp(_weights[SAD],   sadW,   k);
            _weights[WALK]  = Mathf.Lerp(_weights[WALK],  walkW,  k);
            _weights[RUN]   = Mathf.Lerp(_weights[RUN],   runW,   k);
            _weights[SLIDE] = Mathf.Lerp(_weights[SLIDE], slideW, k);
            _weights[JUMP]  = Mathf.Lerp(_weights[JUMP],  jumpW,  k);
            float total = 0f;
            for (int i = 0; i < CLIP_COUNT; i++) total += _weights[i];
            if (total < 0.0001f) { _weights[IDLE] = 1f; total = 1f; }
            for (int i = 0; i < CLIP_COUNT; i++)
                _mixer.SetInputWeight(i, _weights[i] / total);
        }

        private void LateUpdate()
        {
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
