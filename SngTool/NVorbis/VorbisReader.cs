using System;
using System.Collections.Generic;
using System.IO;
using NVorbis.Contracts;

namespace NVorbis
{
    /// <summary>
    /// Implements an easy to use wrapper around <see cref="IContainerReader"/> and <see cref="IStreamDecoder"/>.
    /// </summary>
    public sealed class VorbisReader : IVorbisReader
    {
        private readonly List<IStreamDecoder> _decoders;
        private readonly IContainerReader _containerReader;
        private readonly bool _leaveOpen;

        private IStreamDecoder _streamDecoder;

        /// <inheritdoc/>
        public event NewStreamEventHandler? NewStream;

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified file.
        /// </summary>
        /// <param name="fileName">The file to read from.</param>
        public VorbisReader(string fileName)
            : this(VorbisConfig.Default, File.OpenRead(fileName), leaveOpen: false)
        {
        }

        /// <summary>
        /// Creates a new instance of <see cref="VorbisReader"/> reading from the specified stream, optionally taking ownership of it.
        /// </summary>
        /// <param name="config">The configuration instance.</param>
        /// <param name="stream">The <see cref="Stream"/> to read from.</param>
        /// <param name="leaveOpen"><see langword="false"/> to dispose the stream when disposed, otherwise <see langword="true"/>.</param>
        public VorbisReader(VorbisConfig config, Stream stream, bool leaveOpen)
        {
            _decoders = new List<IStreamDecoder>();

            Ogg.ContainerReader containerReader = new(config, stream, leaveOpen);
            containerReader.NewStreamCallback = ProcessNewStream;

            _leaveOpen = leaveOpen;
            _containerReader = containerReader;

            _streamDecoder = null!;
        }

        /// <inheritdoc cref="VorbisReader(VorbisConfig, Stream, bool)"/>
        public VorbisReader(Stream stream, bool leaveOpen) : this(VorbisConfig.Default, stream, leaveOpen)
        {
        }

        /// <inheritdoc />
        public void Initialize()
        {
            if (!_containerReader.TryInit() || _decoders.Count == 0)
            {
                _containerReader.NewStreamCallback = null;
                _containerReader.Dispose();

                throw new InvalidDataException("Could not load the specified container.");
            }
            _streamDecoder = _decoders[0];
        }

        private bool ProcessNewStream(IPacketProvider packetProvider)
        {
            StreamDecoder decoder = new(packetProvider);
            decoder.ClipSamples = true;
            decoder.SkipTags = false;

            NewStreamEventArgs ea = new(decoder);
            NewStream?.Invoke(this, ref ea);

            decoder.Initialize();

            if (!ea.IgnoreStream)
            {
                _decoders.Add(decoder);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cleans up this instance.
        /// </summary>
        public void Dispose()
        {
            if (_decoders != null)
            {
                foreach (IStreamDecoder decoder in _decoders)
                {
                    decoder.Dispose();
                }
                _decoders.Clear();
            }

            if (_containerReader != null)
            {
                _containerReader.NewStreamCallback = null;
                if (!_leaveOpen)
                {
                    _containerReader.Dispose();
                }
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<IStreamDecoder> Streams => _decoders;

        #region Convenience Helpers

        // Since most uses of VorbisReader are for single-stream audio files,
        // we can make life simpler for users by exposing the first stream's properties and methods here.

        /// <inheritdoc/>
        public int Channels => _streamDecoder.Channels;

        /// <inheritdoc/>
        public int SampleRate => _streamDecoder.SampleRate;

        /// <inheritdoc/>
        public int UpperBitrate => _streamDecoder.UpperBitrate;

        /// <summary>
        /// Gets the nominal bitrate of the stream, if specified. 
        /// May be calculated from <see cref="LowerBitrate"/> and <see cref="UpperBitrate"/>.
        /// </summary>
        public int NominalBitrate => _streamDecoder.NominalBitrate;

        /// <inheritdoc/>
        public int LowerBitrate => _streamDecoder.LowerBitrate;

        /// <inheritdoc/>
        public ITagData Tags => _streamDecoder.Tags;

        /// <inheritdoc/>
        public long ContainerOverheadBits => _containerReader?.ContainerBits ?? 0;

        /// <inheritdoc/>
        public long ContainerWasteBits => _containerReader?.WasteBits ?? 0;

        /// <inheritdoc/>
        public int StreamIndex => _decoders.IndexOf(_streamDecoder);

        /// <inheritdoc/>
        public TimeSpan TotalTime => _streamDecoder.TotalTime;

        /// <inheritdoc/>
        public long TotalSamples => _streamDecoder.TotalSamples;

        /// <inheritdoc/>
        public TimeSpan TimePosition
        {
            get => _streamDecoder.TimePosition;
            set => _streamDecoder.TimePosition = value;
        }

        /// <inheritdoc/>
        public long SamplePosition
        {
            get => _streamDecoder.SamplePosition;
            set => _streamDecoder.SamplePosition = value;
        }

        /// <inheritdoc/>
        public bool IsEndOfStream => _streamDecoder.IsEndOfStream;

        /// <inheritdoc/>
        public bool ClipSamples
        {
            get => _streamDecoder.ClipSamples;
            set => _streamDecoder.ClipSamples = value;
        }

        /// <inheritdoc/>
        public bool HasClipped => _streamDecoder.HasClipped;

        /// <inheritdoc/>
        public IStreamStats StreamStats => _streamDecoder.Stats;

        /// <inheritdoc/>
        public bool CanSeek => _containerReader.CanSeek;

        /// <summary>
        /// Searches for the next stream in a concatenated file.
        /// Will raise <see cref="NewStream"/> for the found stream, 
        /// and will add it to <see cref="Streams"/> if not marked as ignored.
        /// </summary>
        /// <returns><see langword="true"/> if a new stream was found, otherwise <see langword="false"/>.</returns>
        public bool FindNextStream()
        {
            if (_containerReader == null) return false;
            return _containerReader.FindNextStream();
        }
        
        /// <inheritdoc/>
        public bool SwitchStreams(int index)
        {
            if (index < 0 || index >= _decoders.Count) throw new ArgumentOutOfRangeException(nameof(index));

            IStreamDecoder newDecoder = _decoders[index];
            IStreamDecoder oldDecoder = _streamDecoder;
            if (newDecoder == oldDecoder) return false;

            // carry-through the clipping setting
            newDecoder.ClipSamples = oldDecoder.ClipSamples;

            _streamDecoder = newDecoder;

            return newDecoder.Channels != oldDecoder.Channels || newDecoder.SampleRate != oldDecoder.SampleRate;
        }

        /// <inheritdoc/>
        public void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            _streamDecoder.SeekTo(timePosition, seekOrigin);
        }

        /// <inheritdoc/>
        public void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            _streamDecoder.SeekTo(samplePosition, seekOrigin);
        }

        /// <inheritdoc/>
        public int ReadSamples(Span<float> buffer)
        {
            // don't allow non-aligned reads (always on a full sample boundary!)
            int count = buffer.Length - buffer.Length % _streamDecoder.Channels;
            if (count != 0)
            {
                return _streamDecoder.Read(buffer.Slice(0, count));
            }
            return 0;
        }

        /// <inheritdoc/>
        public int ReadSamples(Span<float> buffer, int samplesToRead, int channelStride)
        {
            // don't allow non-aligned reads (always on a full sample boundary!)
            int count = buffer.Length - buffer.Length % _streamDecoder.Channels;
            if (count != 0)
            {
                return _streamDecoder.Read(buffer.Slice(0, count), samplesToRead, channelStride);
            }
            return 0;
        }

        #endregion
    }
}
