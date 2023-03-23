using System;
using NLayer.Decoder;

namespace NLayer
{
    public class MpegFrameDecoder
    {
        private Layer1Decoder? _layer1Decoder;
        private Layer2Decoder? _layer2Decoder;
        private Layer3Decoder? _layer3Decoder;
        private float[]? _eqFactors;

        // channel buffers for getting data out of the decoders...
        // we do it this way so the stereo interleaving code is in one place: DecodeFrame(...)
        // if we ever add support for multi-channel, we'll have to add a pass after the initial
        //  stereo decode (since multi-channel basically uses the stereo channels as a reference)
        private float[] _ch0, _ch1;

        public MpegFrameDecoder()
        {
            _ch0 = new float[1152];
            _ch1 = new float[1152];
        }

        /// <summary>
        /// Set the equalizer.
        /// </summary>
        /// <param name="eq">The equalizer, represented by an array of 32 adjustments in dB.</param>
        public void SetEQ(float[]? eq)
        {
            if (eq != null)
            {
                var factors = new float[32];
                for (int i = 0; i < eq.Length; i++)
                    // convert from dB -> scaling
                    factors[i] = (float)Math.Pow(2, eq[i] / 6);

                _eqFactors = factors;
            }
            else
            {
                _eqFactors = null;
            }
        }

        /// <summary>
        /// Stereo mode used in decoding.
        /// </summary>
        public StereoMode StereoMode { get; set; }

        /// <summary>
        /// Decode the Mpeg frame into provided buffer.
        /// Result varies with different <see cref="StereoMode"/>:
        /// <list type="bullet">
        /// <item>
        /// <description>For <see cref="StereoMode.Both"/>, sample data on both two channels will occur in turn (left first).</description>
        /// </item>
        /// <item>
        /// <description>For <see cref="StereoMode.LeftOnly"/> and <see cref="StereoMode.RightOnly"/>, only data on
        /// specified channel will occur.</description>
        /// </item>
        /// <item>
        /// <description>For <see cref="StereoMode.DownmixToMono"/>, two channels will be down-mixed into single channel.</description>
        /// </item>
        /// </list>
        /// </summary>
        /// <param name="frame">The Mpeg frame to be decoded.</param>
        /// <param name="destination">The buffer to fill with PCM samples.</param>
        /// <returns>The actual amount of samples read.</returns>
        public int DecodeFrame(MpegFrame frame, Span<float> destination)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            LayerDecoderBase decoder;
            switch (frame.Layer)
            {
                case MpegLayer.LayerI:
                    if (_layer1Decoder == null)
                        _layer1Decoder = new Layer1Decoder();
                    decoder = _layer1Decoder;
                    break;

                case MpegLayer.LayerII:
                    if (_layer2Decoder == null)
                        _layer2Decoder = new Layer2Decoder();
                    decoder = _layer2Decoder;
                    break;

                case MpegLayer.LayerIII:
                    if (_layer3Decoder == null)
                        _layer3Decoder = new Layer3Decoder();
                    decoder = _layer3Decoder;
                    break;

                default:
                    return 0;
            }

            frame.Reset();

            decoder.SetEQ(_eqFactors);
            decoder.StereoMode = StereoMode;

            int decodedCount = decoder.DecodeFrame(frame, _ch0, _ch1);

            float[] ch0 = _ch0;
            float[] ch1 = _ch1;

            if (frame.ChannelMode == MpegChannelMode.Mono ||
                decoder.StereoMode != StereoMode.Both)
            {
                ch0.AsSpan(0, decodedCount).CopyTo(destination);
            }
            else
            {
                // This is kinda annoying...  if we're doing a downmix,
                // we should technically only output a single channel
                // The problem is, our caller is probably expecting stereo output.  Grrrr....

                // TODO: optimize
                for (int i = 0; i < decodedCount; i++)
                {
                    destination[i * 2 + 0] = ch0[i];
                    destination[i * 2 + 1] = ch1[i];
                }
                decodedCount *= 2;
            }

            return decodedCount;
        }

        /// <summary>
        /// Reset the decoder.
        /// </summary>
        public void Reset()
        {
            // the synthesis filters need to be cleared

            _layer1Decoder?.ResetForSeek();
            _layer2Decoder?.ResetForSeek();
            _layer3Decoder?.ResetForSeek();
        }
    }
}
