// Assets/Scripts/VoxelEngine/Networking/VoiceCodec.cs
//
// 14.20.0-dev - milestone 7, phase 2: the voice codec.
//
// IMA ADPCM at 4 bits per sample: 16 kHz mono speech costs 8 KB/s on the wire
// instead of 32 KB/s raw, with no native plugin, no licence and no platform
// exceptions. Opus would compress harder, but it cannot ship as pure C# and a
// managed port would cost more CPU than the bandwidth it saves at 2-8 players.
//
// EVERY frame is self-contained: predictor and step index travel in a three
// byte header, so a packet lost on the unreliable channel costs exactly one
// 40 ms frame and never corrupts the ones after it. That property is the whole
// reason the header exists - without it the decoder would drift forever after
// the first drop.
//
// Allocation-free by contract: callers own the buffers, this class only fills
// them.

namespace VoxelEngine.Networking
{
    public static class VoiceCodec
    {
        /// <summary>Capture and playback rate. Speech is fully intelligible at
        /// 16 kHz and it halves the cost of 32 kHz.</summary>
        public const int SampleRate = 16000;

        /// <summary>Samples per transmitted frame - 40 ms. Long enough to keep
        /// the packet rate at 25/s, short enough that one lost packet is not
        /// audible as a gap.</summary>
        public const int FrameSamples = 640;

        /// <summary>Header: predictor (int16 LE) + step index (byte).</summary>
        public const int HeaderBytes = 3;

        /// <summary>Exact encoded size of a frame of <paramref name="sampleCount"/> samples.</summary>
        public static int EncodedLength(int sampleCount) => HeaderBytes + (sampleCount + 1) / 2;

        /// <summary>Largest frame we will ever accept from the wire. Anything
        /// bigger is a malformed or hostile packet and is dropped on arrival.</summary>
        public const int MaxPacketBytes = 1024;

        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37,
            41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173,
            190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894,
            6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289,
            16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };

        private static readonly int[] IndexTable =
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };

        /// <summary>Encodes <paramref name="count"/> float samples (-1..1) into
        /// <paramref name="dst"/>. <paramref name="stepIndex"/> carries the
        /// adaptation state between frames for quality, but is written into the
        /// header so the decoder never needs the previous packet. Returns the
        /// number of bytes written, or 0 when the buffers do not fit.</summary>
        public static int Encode(float[] src, int count, byte[] dst, ref int stepIndex)
        {
            if (src == null || dst == null || count <= 0) return 0;
            if (count > src.Length) count = src.Length;
            int needed = EncodedLength(count);
            if (dst.Length < needed) return 0;

            int index = stepIndex < 0 ? 0 : (stepIndex > 88 ? 88 : stepIndex);
            int predictor = Clamp16(Mathf16(src[0]));

            dst[0] = (byte)(predictor & 0xFF);
            dst[1] = (byte)((predictor >> 8) & 0xFF);
            dst[2] = (byte)index;

            int write = HeaderBytes;
            byte pending = 0;
            bool high = false;

            for (int i = 0; i < count; i++)
            {
                int sample = Clamp16(Mathf16(src[i]));
                int step = StepTable[index];
                int diff = sample - predictor;

                int code = 0;
                if (diff < 0) { code = 8; diff = -diff; }

                int temp = step;
                if (diff >= temp) { code |= 4; diff -= temp; }
                temp >>= 1;
                if (diff >= temp) { code |= 2; diff -= temp; }
                temp >>= 1;
                if (diff >= temp) { code |= 1; }

                // Reconstruct exactly as the decoder will, so both sides walk
                // the same predictor - the invariant the whole scheme rests on.
                int delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                predictor = Clamp16((code & 8) != 0 ? predictor - delta : predictor + delta);

                index += IndexTable[code];
                if (index < 0) index = 0;
                else if (index > 88) index = 88;

                if (high) { dst[write++] = (byte)(pending | (code << 4)); high = false; }
                else { pending = (byte)code; high = true; }
            }

            if (high) dst[write++] = pending;
            stepIndex = index;
            return write;
        }

        /// <summary>Decodes a frame into <paramref name="dst"/> as floats
        /// (-1..1). Returns the sample count, or 0 for a malformed packet.</summary>
        public static int Decode(byte[] src, int length, float[] dst)
        {
            if (src == null || dst == null) return 0;
            if (length <= HeaderBytes || length > src.Length) return 0;

            int predictor = (short)(src[0] | (src[1] << 8));
            int index = src[2];
            if (index < 0 || index > 88) return 0;

            int payload = length - HeaderBytes;
            int samples = payload * 2;
            if (samples > dst.Length) samples = dst.Length;

            int read = HeaderBytes;
            bool high = false;
            byte current = 0;

            for (int i = 0; i < samples; i++)
            {
                int code;
                if (high) { code = (current >> 4) & 0x0F; high = false; }
                else
                {
                    if (read >= length) return i;
                    current = src[read++];
                    code = current & 0x0F;
                    high = true;
                }

                int step = StepTable[index];
                int delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                predictor = Clamp16((code & 8) != 0 ? predictor - delta : predictor + delta);

                index += IndexTable[code];
                if (index < 0) index = 0;
                else if (index > 88) index = 88;

                dst[i] = predictor * (1f / 32768f);
            }

            return samples;
        }

        private static int Mathf16(float v)
        {
            float s = v * 32767f;
            return s >= 0f ? (int)(s + 0.5f) : (int)(s - 0.5f);
        }

        private static int Clamp16(int v) => v < -32768 ? -32768 : (v > 32767 ? 32767 : v);
    }
}
