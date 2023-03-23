namespace NLayer
{
    /// <summary>
    /// Defines a way of representing an MPEG frame to the decoder.
    /// </summary>
    public interface IMpegFrame
    {
        /// <summary>
        /// Gets the sample rate of this frame.
        /// </summary>
        int SampleRate { get; }

        /// <summary>
        /// Gets the samplerate index (directly from the header).
        /// </summary>
        int SampleRateIndex { get; }

        /// <summary>
        /// Gets the frame length in bytes.
        /// </summary>
        int FrameLength { get; }

        /// <summary>
        /// Gets the bit rate.
        /// </summary>
        int BitRate { get; }

        /// <summary>
        /// Gets the MPEG Version.
        /// </summary>
        MpegVersion Version { get; }

        /// <summary>
        /// Gets the MPEG Layer.
        /// </summary>
        MpegLayer Layer { get; }

        /// <summary>
        /// Gets the channel mode.
        /// </summary>
        MpegChannelMode ChannelMode { get; }

        /// <summary>
        /// Gets the number of samples in this frame.
        /// </summary>
        int ChannelModeExtension { get; }

        /// <summary>
        /// Gets the channel extension bits.
        /// </summary>
        int SampleCount { get; }

        /// <summary>
        /// Gets the bitrate index (directly from the header)
        /// </summary>
        int BitRateIndex { get; }

        /// <summary>
        /// Gets whether the Copyright bit is set.
        /// </summary>
        bool IsCopyrighted { get; }

        /// <summary>
        /// Gets whether a CRC is present.
        /// </summary>
        bool HasCrc { get; }

        /// <summary>
        /// Gets whether the CRC check failed (use error concealment strategy).
        /// </summary>
        bool IsCorrupted { get; }

        /// <summary>
        /// Provides sequential access to the bitstream in the frame (after the header and optional CRC).
        /// </summary>
        /// <param name="bitCount">The number of bits to read.</param>
        /// <returns>-1 if the end of the frame has been encountered, otherwise the bits requested.</returns>
        int ReadBits(int bitCount);

        /// <summary>
        /// Resets the frame to read it from the beginning.
        /// </summary>
        void Reset();
    }
}
