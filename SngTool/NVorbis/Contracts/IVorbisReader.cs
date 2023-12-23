using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis.Contracts
{
    /// <summary>
    /// Raised when a new stream has been encountered in the container.
    /// </summary>
    public delegate void NewStreamEventHandler(IVorbisReader reader, ref NewStreamEventArgs eventArgs);

    /// <summary>
    /// Describes the interface for <see cref="VorbisReader"/>.
    /// </summary>
    public interface IVorbisReader : IDisposable
    {
        /// <summary>
        /// Raised when a new stream has been encountered in the container.
        /// </summary>
        event NewStreamEventHandler NewStream;

        /// <summary>
        /// Gets the number of bits read that are related to framing and transport alone.
        /// </summary>
        long ContainerOverheadBits { get; }

        /// <summary>
        /// Gets the number of bits skipped in the container due to framing, ignored streams, or sync loss.
        /// </summary>
        long ContainerWasteBits { get; }

        /// <summary>
        /// Gets the list of <see cref="IStreamDecoder"/> instances associated with the loaded container.
        /// </summary>
        IReadOnlyList<IStreamDecoder> Streams { get; }

        /// <summary>
        /// Gets the currently-selected stream's index.
        /// </summary>
        int StreamIndex { get; }

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
        /// Gets the total duration of the decoded stream.
        /// </summary>
        TimeSpan TotalTime { get; }

        /// <summary>
        /// Gets the total number of samples in the decoded stream.
        /// </summary>
        long TotalSamples { get; }

        /// <inheritdoc cref="IStreamDecoder.ClipSamples"/>
        bool ClipSamples { get; set; }

        /// <summary>
        /// Gets or sets the current time position of the stream.
        /// </summary>
        TimeSpan TimePosition { get; set; }

        /// <summary>
        /// Gets or sets the current sample position of the stream.
        /// </summary>
        long SamplePosition { get; set; }

        /// <inheritdoc cref="IStreamDecoder.HasClipped"/>
        bool HasClipped { get; }

        /// <summary>
        /// Gets whether the current stream has ended.
        /// </summary>
        bool IsEndOfStream { get; }

        /// <summary>
        /// Gets the <see cref="IStreamStats"/> instance for this stream.
        /// </summary>
        IStreamStats StreamStats { get; }

        /// <summary>
        /// Gets the tag data from the stream's header.
        /// </summary>
        ITagData Tags { get; }

        /// <summary>
        /// Begin parsing the container in the stream.
        /// </summary>
        /// <exception cref="InvalidDataException">The Vorbis container could not be parsed.</exception>
        void Initialize();

        /// <summary>
        /// Searches for the next stream in a concatenated container. 
        /// Will raise <see cref="NewStream"/> for the found stream, 
        /// and will add it to <see cref="Streams"/> if not marked as ignored.
        /// </summary>
        /// <returns><see langword="true"/> if a new stream was found, otherwise <see langword="false"/>.</returns>
        bool FindNextStream();

        /// <summary>
        /// Switches to an alternate logical stream.
        /// </summary>
        /// <param name="index">The logical stream index to switch to</param>
        /// <returns>
        /// <see langword="true"/> if the properties of the logical stream differ from 
        /// those of the one previously being decoded. Otherwise, <see langword="false"/>.
        /// </returns>
        bool SwitchStreams(int index);

        /// <inheritdoc cref="IStreamDecoder.Read(Span{float})"/>
        int ReadSamples(Span<float> buffer);

        /// <inheritdoc cref="IStreamDecoder.Read(Span{float}, int, int)"/>
        int ReadSamples(Span<float> buffer, int samplesToRead, int channelStride);

        /// <inheritdoc cref="IStreamDecoder.SeekTo(TimeSpan, SeekOrigin)"/>
        void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin);

        /// <inheritdoc cref="IStreamDecoder.SeekTo(long, SeekOrigin)"/>
        void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin);
    }
}
