// Assets/Scripts/VoxelEngine/Networking/VoiceChat.cs
//
// 14.20.0-dev - milestone 7, phase 2: proximity VOICE.
//
// The microphone end of the same relay that carries text chat: capture ->
// resample to 16 kHz -> IMA ADPCM -> one unreliable broadcast per 40 ms ->
// server proximity filter -> a spatialized AudioSource on the speaker's head.
// Nothing here talks to the transport; NetworkBootstrap owns that, exactly as
// the rest of the networking layer does.
//
// Decisions worth keeping:
// - BUILD, not buy. The roadmap left build-vs-buy open until this phase. A
//   Fish-Net voice asset would bring its own transport, its own identity model
//   and a licence; this pipeline reuses the handshake, the player ids and the
//   proximity rule that already exist, in roughly 600 lines total.
// - Unreliable channel. A re-sent 40 ms of speech arrives too late to be worth
//   hearing, and every frame decodes standalone, so loss costs one frame.
// - The capture loop runs even while muted/not transmitting: the mic ring must
//   keep draining or the first words after keying up would be stale audio.
//
// Added automatically by NetworkBootstrap - there is no scene setup step.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Settings;

namespace VoxelEngine.Networking
{
    public class VoiceChat : MonoBehaviour
    {
        public static VoiceChat Instance { get; private set; }

        /// <summary>True while the local microphone is actually sending.</summary>
        public static bool LocalTransmitting { get; private set; }

        /// <summary>Smoothed 0-1 microphone level, for the HUD meter. Live even
        /// when not transmitting, so the settings meter can be used to set the
        /// activation threshold.</summary>
        public static float LocalLevel { get; private set; }

        /// <summary>Last capture problem in plain words ("" when healthy).</summary>
        public static string LocalStatus { get; private set; } = "";

        /// <summary>Settings pages call this every frame while their input
        /// meter is on screen: the microphone then runs (and the level reads
        /// true) without a session, but nothing is ever transmitted. The mic
        /// closes again half a second after the page goes away - it is never
        /// held open behind the player's back.</summary>
        public static void RequestLocalMonitor() => _monitorUntil = Time.unscaledTime + 0.5f;

        private static float _monitorUntil;

        /// <summary>Everyone currently being heard. The HUD reads this.</summary>
        public static IReadOnlyList<VoicePlayback> Speakers => _speakerList;

        private static readonly List<VoicePlayback> _speakerList = new();
        private static readonly Dictionary<string, VoicePlayback> _speakers = new();

        // ── capture state ──
        private const int PacketsPerSecond = 25;        // 40 ms frames
        private const float OpenMicHangover = 0.45f;    // keeps word endings alive

        private AudioClip _micClip;
        private string _micDevice;
        private int _micRead;
        private float[] _captureBlock = System.Array.Empty<float>();
        private readonly float[] _frame = new float[VoiceCodec.FrameSamples];
        private readonly byte[] _encoded = new byte[VoiceCodec.EncodedLength(VoiceCodec.FrameSamples)];
        private int _encoderIndex;
        private ushort _sequence;
        private float _gateUntil;
        private float _nextDeviceCheck;

        // ── receive state ──
        private readonly float[] _decode = new float[VoiceCodec.FrameSamples * 2];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            StopCapture();
            ClearSpeakers();
            if (Instance == this) Instance = null;
        }

        // ─────────────────────────── per-frame ───────────────────────────

        private void Update()
        {
            bool online = NetworkSession.Mode != SessionMode.Offline
                          && NetworkBootstrap.Instance != null
                          && NetworkBootstrap.Instance.IsOnline;
            if (!online && _speakerList.Count > 0) ClearSpeakers();

            _canSend = online;
            if (!online)
            {
                // Never come back from a session already keyed up.
                _toggleLatched = false;
                if (Time.unscaledTime >= _monitorUntil)
                {
                    if (_micClip != null) StopCapture();
                    LocalTransmitting = false;
                    LocalLevel = 0f;
                    return;
                }
            }

            TickCapture();
            if (online) TickSpeakers();
        }

        /// <summary>False while the mic is only feeding the settings meter.</summary>
        private bool _canSend;

        /// <summary>Toggle-to-talk latch. Edge detection has to happen once per
        /// FRAME, not once per captured block - a frame can drain zero blocks or
        /// two, and either would miss or double a keypress.</summary>
        private bool _toggleLatched;

        // ─────────────────────────── microphone ───────────────────────────

        private void TickCapture()
        {
            if (!GameSettings.VoiceEnabled)
            {
                if (_micClip != null) StopCapture();
                _toggleLatched = false;
                LocalTransmitting = false;
                LocalLevel = Mathf.MoveTowards(LocalLevel, 0f, Time.unscaledDeltaTime * 3f);
                return;
            }

            if (GameSettings.VoiceToggleToTalk)
            {
                // The talk key never latches while a text field owns the
                // keyboard - typing the bound letter must not open the mic.
                if (!VoxelEngine.UI.UIState.TextInputActive
                    && GameSettings.WasPressed(InputAction.PushToTalk))
                    _toggleLatched = !_toggleLatched;
            }
            else _toggleLatched = false;

            if (_micClip == null)
            {
                // Device hot-plugging is real; retry on a slow cadence instead
                // of hammering the audio subsystem every frame.
                if (Time.unscaledTime < _nextDeviceCheck) return;
                _nextDeviceCheck = Time.unscaledTime + 1.5f;
                StartCapture();
                if (_micClip == null) return;
            }

            if (!Microphone.IsRecording(_micDevice)) { StopCapture(); return; }

            int clipLength = _micClip.samples;
            int block = _captureBlock.Length;
            if (block <= 0 || clipLength <= 0) { StopCapture(); return; }

            int position = Microphone.GetPosition(_micDevice);
            if (position < 0) return;

            int available = position - _micRead;
            if (available < 0) available += clipLength;

            // Fell more than a buffer behind (alt-tab, a long frame hitch):
            // skip to live audio rather than playing back the past.
            if (available >= clipLength - block) { _micRead = position - position % block; return; }

            while (available >= block)
            {
                _micClip.GetData(_captureBlock, _micRead);
                _micRead += block;
                if (_micRead >= clipLength) _micRead = 0;
                available -= block;
                ProcessBlock();
            }
        }

        private void StartCapture()
        {
            LocalStatus = "";
            string[] devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                LocalStatus = "No microphone detected.";
                return;
            }

            string wanted = GameSettings.VoiceDevice;
            _micDevice = devices[0];
            if (!string.IsNullOrEmpty(wanted))
                for (int i = 0; i < devices.Length; i++)
                    if (devices[i] == wanted) { _micDevice = devices[i]; break; }

            // Honour what the device can actually do, then resample to the
            // codec rate ourselves - asking for an unsupported rate silently
            // gives a clip that runs at the wrong speed on some drivers.
            Microphone.GetDeviceCaps(_micDevice, out int minRate, out int maxRate);
            int rate = VoiceCodec.SampleRate;
            if (minRate > 0 && rate < minRate) rate = minRate;
            if (maxRate > 0 && rate > maxRate) rate = maxRate;
            // One captured block must map to exactly one codec frame, so the
            // rate has to divide evenly by the packet rate. Every standard rate
            // already does; round up for anything exotic a driver reports.
            if (rate % PacketsPerSecond != 0)
            {
                rate += PacketsPerSecond - rate % PacketsPerSecond;
                if (maxRate > 0 && rate > maxRate) rate = maxRate - maxRate % PacketsPerSecond;
            }
            if (rate < PacketsPerSecond) { LocalStatus = "Microphone rate unsupported."; return; }

            _micClip = Microphone.Start(_micDevice, true, 1, rate);
            if (_micClip == null)
            {
                LocalStatus = "Microphone could not be opened.";
                return;
            }

            _micRead = 0;
            _encoderIndex = 0;
            _captureBlock = new float[rate / PacketsPerSecond];
        }

        private void StopCapture()
        {
            if (_micClip != null)
            {
                if (!string.IsNullOrEmpty(_micDevice)) Microphone.End(_micDevice);
                Destroy(_micClip);
                _micClip = null;
            }
            _captureBlock = System.Array.Empty<float>();
            _toggleLatched = false;
            LocalTransmitting = false;
        }

        /// <summary>One captured block: resample, measure, gate, encode, send.</summary>
        private void ProcessBlock()
        {
            Resample(_captureBlock, _frame);

            float sum = 0f;
            for (int i = 0; i < _frame.Length; i++) sum += _frame[i] * _frame[i];
            float rms = Mathf.Sqrt(sum / _frame.Length);
            LocalLevel = Mathf.Clamp01(Mathf.Max(LocalLevel * 0.75f, rms * 8f));

            bool open;
            switch (GameSettings.VoiceMode)
            {
                case VoiceTalkMode.OpenMic:
                    if (rms >= GameSettings.VoiceActivation) _gateUntil = Time.unscaledTime + OpenMicHangover;
                    open = Time.unscaledTime < _gateUntil;
                    break;

                case VoiceTalkMode.Toggle:
                    // Latched in Update; the latch already respects text fields.
                    open = _toggleLatched;
                    break;

                default:
                    // Push to talk never fires while a text field owns the keyboard -
                    // typing the bound letter must not go on the air.
                    open = !VoxelEngine.UI.UIState.TextInputActive
                           && GameSettings.IsHeld(InputAction.PushToTalk);
                    break;
            }

            LocalTransmitting = open && _canSend;
            if (!open) { _encoderIndex = 0; return; }
            if (!_canSend) return;   // monitoring only - the meter is live, the wire is not

            int length = VoiceCodec.Encode(_frame, _frame.Length, _encoded, ref _encoderIndex);
            if (length <= 0) return;

            var boot = NetworkBootstrap.Instance;
            if (boot != null) boot.SendVoiceFrame(_encoded, length, _sequence++);
        }

        /// <summary>Linear resample from the capture rate into one codec frame.
        /// Both lengths are fixed per device, so this is a straight walk.</summary>
        private static void Resample(float[] src, float[] dst)
        {
            int n = src.Length;
            if (n == dst.Length)
            {
                System.Array.Copy(src, dst, n);
                return;
            }
            float ratio = (float)(n - 1) / Mathf.Max(1, dst.Length - 1);
            for (int i = 0; i < dst.Length; i++)
            {
                float x = i * ratio;
                int i0 = (int)x;
                int i1 = i0 + 1 < n ? i0 + 1 : n - 1;
                float t = x - i0;
                dst[i] = src[i0] + (src[i1] - src[i0]) * t;
            }
        }

        // ─────────────────────────── receive ───────────────────────────

        /// <summary>Main thread: one relayed frame from the server. Unknown or
        /// muted speakers are dropped here, so no audio object is ever created
        /// for someone the player chose not to hear.</summary>
        public static void Deliver(string senderId, string senderName, byte[] data, int length, ushort sequence)
        {
            var self = Instance;
            if (self == null || data == null) return;
            if (string.IsNullOrEmpty(senderId)) return;
            if (senderId == PlayerIdentity.LocalId) return;          // never hear yourself
            if (length <= 0 || length > VoiceCodec.MaxPacketBytes) return;
            if (!GameSettings.VoiceEnabled) return;
            if (GameSettings.IsPlayerMuted(senderId)) return;

            if (!_speakers.TryGetValue(senderId, out var playback) || playback == null)
            {
                playback = VoicePlayback.Create(senderId, senderName, NetworkBootstrap.VoiceRange);
                _speakers[senderId] = playback;
                _speakerList.Add(playback);
            }
            if (!playback.AcceptSequence(sequence)) return;   // late or duplicate

            int samples = VoiceCodec.Decode(data, length, self._decode);
            if (samples <= 0) return;

            playback.SetVolume(GameSettings.VoiceVolume);
            playback.Enqueue(self._decode, samples, senderName);
        }

        /// <summary>Keeps every live voice on its speaker's head and retires the
        /// ones that stopped talking.</summary>
        private void TickSpeakers()
        {
            if (_speakerList.Count == 0) return;
            var cam = Camera.main;
            Vector3 listener = cam != null ? cam.transform.position : transform.position;
            float volume = GameSettings.VoiceVolume;

            for (int i = _speakerList.Count - 1; i >= 0; i--)
            {
                var playback = _speakerList[i];
                if (playback == null)
                {
                    _speakerList.RemoveAt(i);
                    PruneDeadSpeakers();
                    continue;
                }
                if (playback.Idle > 8f || GameSettings.IsPlayerMuted(playback.PlayerId))
                {
                    _speakers.Remove(playback.PlayerId);
                    _speakerList.RemoveAt(i);
                    Destroy(playback.gameObject);
                    continue;
                }

                playback.SetVolume(volume);
                var avatar = PlayerAvatar.Find(playback.PlayerId);
                playback.Follow(avatar != null ? avatar.VoiceAnchor() : null, listener);
            }
        }

        /// <summary>Drops dictionary entries whose audio object is gone - a
        /// scene change destroys them without going through the retire path.</summary>
        private static void PruneDeadSpeakers()
        {
            if (_speakers.Count == 0) return;
            List<string> dead = null;
            foreach (var pair in _speakers)
                if (pair.Value == null) (dead ??= new List<string>()).Add(pair.Key);
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) _speakers.Remove(dead[i]);
        }

        private static void ClearSpeakers()
        {
            for (int i = 0; i < _speakerList.Count; i++)
                if (_speakerList[i] != null) Destroy(_speakerList[i].gameObject);
            _speakerList.Clear();
            _speakers.Clear();
        }
    }
}
