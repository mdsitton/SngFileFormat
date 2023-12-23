using System;
using System.IO;

namespace NVorbis.Contracts
{
    /// <summary>
    /// Describes a stream decoder instance for Vorbis data.
    /// </summary>
    public interface IStreamDecoder : IDisposable
    {
        /// <summary>
        /// Gets the number of channels in the stream.
        /// </summary>
        int Channels { get; }

        /// <summary>
        /// Gets the sample rate of the stream.
        /// </summary>
        int SampleRate { get; }

        /// <summary>
        /// Gets the upper bitrate limit for the stream, if specified.
        /// </summary>
        int UpperBitrate { get; }

        /// <summary>
        /// Gets the nominal bitrate of the stream, if specified. 
        /// May be calculated from <see cref="LowerBitrate"/> and <see cref="UpperBitrate"/>.
        /// </summary>
        int NominalBitrate { get; }

        /// <summary>
        /// Gets the lower bitrate limit for the stream, if specified.
        /// </summary>
        int LowerBitrate { get; }

        /// <summary>
        /// Gets the tag data from the stream's header.
        /// </summary>
        ITagData Tags { get; }

        /// <summary>
        /// Gets the total duration of the decoded stream.
        /// </summary>
        TimeSpan TotalTime { get; }

        /// <summary>
        /// Gets the total number of samples in the decoded stream.
        /// </summary>
        long TotalSamples { get; }

        /// <summary>
        /// Gets or sets the current time position of the stream.
        /// </summary>
        TimeSpan TimePosition { get; set; }

        /// <summary>
        /// Gets or sets the current sample position of the stream.
        /// </summary>
        long SamplePosition { get; set; }

        /// <summary>
        /// Gets or sets whether to clip samples between negative one and one.
        /// </summary>
        bool ClipSamples { get; set; }

        /// <summary>
        /// Gets or sets whether the decoder should skip parsing tags.
        /// </summary>
        bool SkipTags { get; set; }

        /// <summary>
        /// Gets whether any samples have been clipped between negative one and one.
        /// </summary>
        /// <remarks>
        /// Depends on <see cref="ClipSamples"/> being <see langword="true"/>.
        /// </remarks>
        bool HasClipped { get; }

        /// <summary>
        /// Gets whether the decoder has reached the end of the stream.
        /// </summary>
        bool IsEndOfStream { get; }

        /// <summary>
        /// Gets the <see cref="IStreamStats"/> instance for this stream.
        /// </summary>
        IStreamStats Stats { get; }

        /// <summary>
        /// Begin parsing the stream.
        /// </summary>
        /// <exception cref="InvalidDataException">The stream header could not be parsed.</exception>
        void Initialize();

        /// <summary>
        /// Seeks the stream by the specified duration.
        /// </summary>
        /// <param name="timePosition">The relative time to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin);

        /// <summary>
        /// Seeks the stream by the specified sample count.
        /// </summary>
        /// <param name="samplePosition">The relative sample position to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin);

        /// <summary>
        /// Reads interleaved samples into the specified buffer.
        /// </summary>
        /// <param name="buffer">
        /// The buffer to read the samples into. Length must be a multiple of <see cref="Channels"/>.
        /// </param>
        /// <returns>
        /// The amount of samples read.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The buffer is too small or the length is not a multiple of <see cref="Channels"/>.
        /// </exception>
        /// <remarks>
        /// The <paramref name="buffer"/> is interleaved by channel
        /// (Left, Right, Left, Right, Left, Right).
        /// </remarks>
        int Read(Span<float> buffer);

        /// <summary>
        /// Reads non-interleaved samples into the specified buffer.
        /// </summary>
        /// <param name="buffer">
        /// The buffer to read the samples into, 
        /// of which length must be a multiple of <see cref="Channels"/>.
        /// </param>
        /// <param name="samplesToRead">
        /// The amount of samples to read per channel.
        /// </param>
        /// <param name="channelStride">
        /// The offset in values between each channel in the buffer.
        /// </param>
        /// <returns>
        /// The amount of samples read.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// The buffer is too small or the length is not a multiple of <see cref="Channels"/>.
        /// </exception>
        /// <remarks>
        /// The <paramref name="buffer"/> is not interleaved
        /// (Left, Left, Left, Right, Right, Right).
        /// </remarks>
        int Read(Span<float> buffer, int samplesToRead, int channelStride);
    }
}
