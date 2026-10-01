// Assets/Scripts/VoxelEngine/Networking/VoicePlayback.cs
//
// 14.20.0-dev - milestone 7, phase 2: one speaking voice, in the world.
//
// Everything that makes proximity voice feel like a PLACE rather than a phone
// call lives here: the audio comes out of the speaker's head, not out of the
// middle of your screen. A fully spatialized AudioSource rides the speaker's
// head bone, so you hear which side someone stands on, hear them pass behind
// you, and hear them fade with distance.
//
// Decoded frames arrive on the main thread and are consumed by the audio
// thread through a streaming AudioClip. The ring buffer between them is the
// only shared state and it is the only thing under a lock - held for a copy
// and nothing else.
//
// Jitter: playback waits for a small cushion of audio before it starts, then
// keeps playing. It does NOT re-arm that cushion the instant the ring runs
// dry - see the starvation note on _playing (14.21.1): doing so stranded the
// last words of every sentence, because a talk spurt always ends with fewer
// than a cushion's worth of frames left in the ring.

using UnityEngine;

namespace VoxelEngine.Networking
{
    [RequireComponent(typeof(AudioSource))]
    public class VoicePlayback : MonoBehaviour
    {
        /// <summary>Audio kept in flight before playback starts, in frames.
        /// Three frames = 120 ms, enough to ride out ordinary jitter without
        /// a conversation feeling delayed.</summary>
        private const int PrimeFrames = 3;

        /// <summary>Ring capacity - 1.5 s. A speaker further behind than this
        /// is hopeless; the oldest audio is dropped rather than growing lag.</summary>
        private const int RingSamples = VoiceCodec.SampleRate * 3 / 2;

        private readonly object _gate = new object();
        private readonly float[] _ring = new float[RingSamples];
        private int _readPos, _writePos, _available;

        /// <summary>True while the stream is live. The cushion is required to
        /// START a talk spurt, never to CONTINUE one: the ring emptying is a
        /// normal event (a single late packet does it), and treating it as a
        /// stop meant the last one or two frames of every sentence sat in the
        /// ring waiting for a cushion that never came - the speaker had already
        /// stopped talking. Playback now only stops once the ring is empty AND
        /// has stayed empty, so nothing is ever left unplayed. (14.21.1)</summary>
        private bool _playing;
        private int _starved;
        /// <summary>Consecutive dry callbacks before the cushion is re-armed.
        /// At a typical DSP buffer this is roughly a quarter of a second.</summary>
        private const int StarveLimit = 12;
        private float _tail;           // last emitted sample - decayed to silence on underrun
        private int _rampIn;           // short fade-in when audio resumes after a gap

        private AudioSource _source;
        private AudioClip _clip;
        private Transform _anchor;
        private float _lastFrameTime = -999f;
        private float _loudness;
        private ushort _lastSequence;
        private bool _haveSequence;

        /// <summary>Stable id of the player this object speaks for.</summary>
        public string PlayerId { get; private set; }

        /// <summary>Display name, refreshed from every relayed frame.</summary>
        public string PlayerName { get; private set; }

        /// <summary>True while frames are still arriving - drives the HUD.</summary>
        public bool IsSpeaking => Time.unscaledTime - _lastFrameTime < 0.35f;

        /// <summary>Smoothed 0-1 loudness of what is being heard right now.</summary>
        public float Level => _loudness;

        /// <summary>Seconds since the last frame arrived; the owner uses this
        /// to retire voices that stopped talking.</summary>
        public float Idle => Time.unscaledTime - _lastFrameTime;

        public static VoicePlayback Create(string playerId, string playerName, float range)
        {
            var go = new GameObject("Voice:" + playerId);
            var playback = go.AddComponent<VoicePlayback>();
            playback.Setup(playerId, playerName, range);
            return playback;
        }

        private void Setup(string playerId, string playerName, float range)
        {
            PlayerId = playerId;
            PlayerName = string.IsNullOrEmpty(playerName) ? "Crusader" : playerName;

            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f;               // fully positional - this is the whole point
            _source.dopplerLevel = 0f;               // a voice is not a siren
            _source.spread = 25f;                    // a touch of width so a close speaker is not a pinpoint
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 4f;                // full volume in conversation range
            _source.maxDistance = Mathf.Max(8f, range);
            _source.bypassReverbZones = false;
            VoxelEngine.FX.AudioManager.Route(_source);

            // Streaming clip: the audio thread pulls straight out of the ring.
            _clip = AudioClip.Create("VoiceStream:" + playerId,
                VoiceCodec.SampleRate, 1, VoiceCodec.SampleRate, true, OnAudioRead);
            _source.clip = _clip;
            _source.Play();
        }

        private void OnDestroy()
        {
            if (_source != null) _source.Stop();
            if (_clip != null) { Destroy(_clip); _clip = null; }
        }

        /// <summary>Wrap-safe reorder guard. UDP delivers out of order often
        /// enough to matter: a late frame played after a newer one is an
        /// audible stutter, and a duplicate is a stammer. Anything not strictly
        /// newer is dropped - except after a silence, where the speaker may have
        /// restarted their sequence and we simply resynchronise.</summary>
        public bool AcceptSequence(ushort sequence)
        {
            if (!_haveSequence || Idle > 1f)
            {
                _haveSequence = true;
                _lastSequence = sequence;
                return true;
            }
            int delta = (short)(sequence - _lastSequence);
            if (delta <= 0) return false;
            _lastSequence = sequence;
            return true;
        }

        /// <summary>Main thread: hand decoded samples to the audio thread.</summary>
        public void Enqueue(float[] samples, int count, string playerName)
        {
            if (samples == null || count <= 0) return;
            if (!string.IsNullOrEmpty(playerName)) PlayerName = playerName;
            _lastFrameTime = Time.unscaledTime;

            float sum = 0f;
            for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
            float rms = Mathf.Sqrt(sum / count);
            _loudness = Mathf.Clamp01(Mathf.Max(_loudness * 0.6f, rms * 6f));

            lock (_gate)
            {
                for (int i = 0; i < count; i++)
                {
                    _ring[_writePos] = samples[i];
                    _writePos = _writePos + 1 == RingSamples ? 0 : _writePos + 1;
                    if (_available == RingSamples) _readPos = _writePos;   // overrun: drop the oldest
                    else _available++;
                }
                if (!_playing && _available >= VoiceCodec.FrameSamples * PrimeFrames)
                {
                    _playing = true;
                    _starved = 0;
                }
            }
        }

        /// <summary>Sets the volume the listener chose, 0-2.</summary>
        public void SetVolume(float volume)
        {
            if (_source != null) _source.volume = Mathf.Clamp(volume, 0f, 2f);
        }

        /// <summary>Follows the speaker's head. Called by the voice manager so
        /// avatar lookup stays in one place.</summary>
        public void Follow(Transform anchor, Vector3 fallbackPosition)
        {
            _anchor = anchor;
            if (anchor == null) transform.position = fallbackPosition;
        }

        private void LateUpdate()
        {
            if (_anchor != null) transform.position = _anchor.position;
            if (!IsSpeaking) _loudness = Mathf.MoveTowards(_loudness, 0f, Time.unscaledDeltaTime * 3f);
        }

        // ─────────────────────────── audio thread ───────────────────────────

        private void OnAudioRead(float[] data)
        {
            int written = 0;
            lock (_gate)
            {
                if (_playing)
                {
                    int take = data.Length < _available ? data.Length : _available;
                    for (int i = 0; i < take; i++)
                    {
                        data[i] = _ring[_readPos];
                        _readPos = _readPos + 1 == RingSamples ? 0 : _readPos + 1;
                    }
                    _available -= take;
                    written = take;

                    // Dry is not the same as done. Only a ring that has been
                    // empty for several callbacks in a row means the speaker
                    // actually stopped - and by then there is nothing left to
                    // strand, because we only stop on an EMPTY ring.
                    if (take < data.Length) _starved++;
                    else _starved = 0;
                    if (_available == 0 && _starved >= StarveLimit) _playing = false;
                }
            }

            // Resuming after silence: fade the first samples in, so a packet
            // arriving mid-waveform does not announce itself with a click.
            const int Ramp = 48;
            if (written > 0 && _rampIn > 0)
            {
                int n = _rampIn < written ? _rampIn : written;
                for (int i = 0; i < n; i++) data[i] *= (Ramp - _rampIn + i) / (float)Ramp;
                _rampIn -= n;
            }

            // Underrun: decay the last real sample to silence rather than cutting.
            if (written > 0) _tail = data[written - 1];
            else _rampIn = Ramp;
            for (int i = written; i < data.Length; i++)
            {
                _tail *= 0.94f;
                data[i] = _tail;
            }
        }
    }
}
