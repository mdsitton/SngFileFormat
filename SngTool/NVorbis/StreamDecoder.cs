using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using NVorbis.Contracts;
using NVorbis.Ogg;

namespace NVorbis
{
    /// <summary>
    /// Implements a stream decoder for Vorbis data.
    /// </summary>
    public sealed class StreamDecoder : IStreamDecoder, IPacketGranuleCountProvider
    {
        private const int MaxPooledBuffers = 2;

        private IPacketProvider _packetProvider;
        private StreamStats _stats;
        private Queue<float[][]> _bufferPool;

        private byte _channels;
        private int _sampleRate;
        private int _block0Size;
        private int _block1Size;
        private Mode[] _modes;
        private int _modeFieldBits;

        private byte[] _utf8Vendor;
        private byte[][] _utf8Comments;
        private ITagData? _tags;

        private long _currentPosition;
        private bool _hasClipped;
        private bool _hasPosition;
        private EndOfStreamFlags _eosFound;

        private float[][]? _nextPacketBuf;
        private float[][]? _prevPacketBuf;
        private int _prevPacketStart;
        private int _prevPacketEnd;
        private int _prevPacketStop;

        /// <summary>
        /// Creates a new instance of <see cref="StreamDecoder"/>.
        /// </summary>
        /// <param name="packetProvider">A <see cref="IPacketProvider"/> instance for the decoder to read from.</param>
        public StreamDecoder(IPacketProvider packetProvider)
        {
            _packetProvider = packetProvider ?? throw new ArgumentNullException(nameof(packetProvider));

            _stats = new StreamStats();
            _bufferPool = new Queue<float[][]>(MaxPooledBuffers);

            _utf8Vendor = Array.Empty<byte>();
            _utf8Comments = Array.Empty<byte[]>();
            _modes = Array.Empty<Mode>();

            _currentPosition = 0L;
            ClipSamples = true;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            VorbisPacket packet = _packetProvider.GetNextPacket();
            if (!packet.IsValid)
                throw new InvalidDataException();

            if (!ProcessHeaderPackets(ref packet))
            {
                packet.Reset();
                Dispose();

                throw GetInvalidStreamException(ref packet);
            }
        }

        private static ArgumentException GetInvalidStreamException(ref VorbisPacket packet)
        {
            try
            {
                // let's give our caller some helpful hints about what they've encountered...
                ulong header = packet.ReadBits(64);
                if (header == 0x646165487375704ful)
                {
                    return new ArgumentException("Found OPUS bitstream.");
                }
                else if ((header & 0xFF) == 0x7F)
                {
                    return new ArgumentException("Found FLAC bitstream.");
                }
                else if (header == 0x2020207865657053ul)
                {
                    return new ArgumentException("Found Speex bitstream.");
                }
                else if (header == 0x0064616568736966ul)
                {
                    // ugh...  we need to add support for this in the container reader
                    return new ArgumentException("Found Skeleton metadata bitstream.");
                }
                else if ((header & 0xFFFFFFFFFFFF00ul) == 0x61726f65687400ul)
                {
                    return new ArgumentException("Found Theora bitsream.");
                }
                return new ArgumentException("Could not find Vorbis data to decode.");
            }
            finally
            {
                packet.Finish();
            }
        }

        #region Init

        private bool ProcessHeaderPackets(ref VorbisPacket headerPacket)
        {
            try
            {
                if (!LoadStreamHeader(ref headerPacket))
                    return false;
            }
            finally
            {
                headerPacket.Finish();
            }

            VorbisPacket commentPacket = _packetProvider.GetNextPacket();
            if (!commentPacket.IsValid)
                return false;
            try
            {
                if (!LoadComments(ref commentPacket))
                    return false;
            }
            finally
            {
                commentPacket.Finish();
            }

            VorbisPacket bookPacket = _packetProvider.GetNextPacket();
            if (!bookPacket.IsValid)
                return false;
            try
            {
                if (!LoadBooks(ref bookPacket))
                    return false;
            }
            finally
            {
                bookPacket.Finish();
            }

            _currentPosition = 0;
            ResetDecoder();
            _hasPosition = true;

            return true;
        }

        private static ReadOnlySpan<byte> PacketSignatureStream => new byte[] { 0x01, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73, 0x00, 0x00, 0x00, 0x00 };
        private static ReadOnlySpan<byte> PacketSignatureComments => new byte[] { 0x03, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };
        private static ReadOnlySpan<byte> PacketSignatureBooks => new byte[] { 0x05, 0x76, 0x6f, 0x72, 0x62, 0x69, 0x73 };

        private static bool ValidateHeader(ref VorbisPacket packet, ReadOnlySpan<byte> expected)
        {
            for (int i = 0; i < expected.Length; i++)
            {
                ulong v = packet.ReadBits(8);
                if (expected[i] != v)
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] ReadString(ref VorbisPacket packet, bool skip)
        {
            int byteLength = (int)packet.ReadBits(32);
            if (byteLength != 0)
            {
                if (!skip)
                {
                    return packet.ReadBytes(byteLength);
                }
                packet.SkipBytes(byteLength);
            }
            return Array.Empty<byte>();
        }

        private bool LoadStreamHeader(ref VorbisPacket packet)
        {
            if (!ValidateHeader(ref packet, PacketSignatureStream))
            {
                return false;
            }

            _channels = (byte)packet.ReadBits(8);
            _sampleRate = (int)packet.ReadBits(32);
            UpperBitrate = (int)packet.ReadBits(32);
            NominalBitrate = (int)packet.ReadBits(32);
            LowerBitrate = (int)packet.ReadBits(32);

            _block0Size = 1 << (int)packet.ReadBits(4);
            _block1Size = 1 << (int)packet.ReadBits(4);

            if (NominalBitrate == 0 && UpperBitrate > 0 && LowerBitrate > 0)
            {
                NominalBitrate = (UpperBitrate + LowerBitrate) / 2;
            }

            _stats.SetSampleRate(_sampleRate);
            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private bool LoadComments(ref VorbisPacket packet)
        {
            if (!ValidateHeader(ref packet, PacketSignatureComments))
            {
                return false;
            }

            _utf8Vendor = ReadString(ref packet, SkipTags);

            _utf8Comments = new byte[packet.ReadBits(32)][];
            for (int i = 0; i < _utf8Comments.Length; i++)
            {
                _utf8Comments[i] = ReadString(ref packet, SkipTags);
            }

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private bool LoadBooks(ref VorbisPacket packet)
        {
            if (!ValidateHeader(ref packet, PacketSignatureBooks))
            {
                return false;
            }

            // read the books
            Codebook[] books = new Codebook[packet.ReadBits(8) + 1];
            for (int i = 0; i < books.Length; i++)
            {
                books[i] = new Codebook(ref packet);
            }

            // Vorbis never used this feature, so we just skip the appropriate number of bits
            int times = (int)packet.ReadBits(6) + 1;
            packet.SkipBits(16 * times);

            // read the floors
            IFloor[] floors = new IFloor[packet.ReadBits(6) + 1];
            for (int i = 0; i < floors.Length; i++)
            {
                floors[i] = CreateFloor(ref packet, _block0Size, _block1Size, books);
            }

            // read the residues
            Residue0[] residues = new Residue0[packet.ReadBits(6) + 1];
            for (int i = 0; i < residues.Length; i++)
            {
                residues[i] = CreateResidue(ref packet, _channels, books);
            }

            // read the mappings
            Mapping[] mappings = new Mapping[packet.ReadBits(6) + 1];
            for (int i = 0; i < mappings.Length; i++)
            {
                mappings[i] = CreateMapping(ref packet, _channels, floors, residues);
            }

            // read the modes
            _modes = new Mode[packet.ReadBits(6) + 1];
            for (int i = 0; i < _modes.Length; i++)
            {
                _modes[i] = new Mode(ref packet, _block0Size, _block1Size, mappings);
            }

            // verify the closing bit
            if (!packet.ReadBit())
                throw new InvalidDataException("Book packet did not end on correct bit!");

            // save off the number of bits to read to determine packet mode
            _modeFieldBits = Utils.ilog(_modes.Length - 1);

            _stats.AddPacket(-1, packet.BitsRead, packet.BitsRemaining, packet.ContainerOverheadBits);

            return true;
        }

        private static IFloor CreateFloor(ref VorbisPacket packet, int block0Size, int block1Size, Codebook[] codebooks)
        {
            int type = (int)packet.ReadBits(16);
            return type switch
            {
                0 => new Floor0(ref packet, block0Size, block1Size, codebooks),
                1 => new Floor1(ref packet, codebooks),
                _ => throw new InvalidDataException("Invalid floor type!"),
            };
        }

        private static Mapping CreateMapping(ref VorbisPacket packet, int channels, IFloor[] floors, Residue0[] residues)
        {
            if (packet.ReadBits(16) != 0)
            {
                throw new InvalidDataException("Invalid mapping type!");
            }
            return new Mapping(ref packet, channels, floors, residues);
        }

        private static Residue0 CreateResidue(ref VorbisPacket packet, int channels, Codebook[] codebooks)
        {
            int type = (int)packet.ReadBits(16);
            return type switch
            {
                0 => new Residue0(ref packet, channels, codebooks),
                1 => new Residue1(ref packet, channels, codebooks),
                2 => new Residue2(ref packet, channels, codebooks),
                _ => throw new InvalidDataException("Invalid residue type!"),
            };
        }

        #endregion

        private void ResetDecoder()
        {
            ReturnBuffer(_prevPacketBuf);
            _prevPacketBuf = null;
            _prevPacketStart = 0;
            _prevPacketEnd = 0;
            _prevPacketStop = 0;
            ReturnBuffer(_nextPacketBuf);
            _nextPacketBuf = null;
            _eosFound = EndOfStreamFlags.None;
            _hasClipped = false;
            _hasPosition = false;
        }

        private float[][] GetBuffer()
        {
            if (!_bufferPool.TryDequeue(out float[][]? buffer))
            {
                buffer = new float[_channels][];
                for (int i = 0; i < buffer.Length; i++)
                {
                    buffer[i] = new float[_block1Size];
                }
            }
            return buffer;
        }

        private void ReturnBuffer(float[][]? buffer)
        {
            if (buffer == null)
            {
                return;
            }

            Debug.Assert(buffer.Length == _channels);
            for (int i = 0; i < buffer.Length; i++)
            {
                Debug.Assert(buffer[i].Length == _block1Size);
            }

            if (_bufferPool.Count < MaxPooledBuffers)
            {
                _bufferPool.Enqueue(buffer);
            }
        }

        #region Decoding

        /// <inheritdoc/>
        public int Read(Span<float> buffer)
        {
            return Read(buffer, buffer.Length / _channels, 0, interleave: true);
        }

        /// <inheritdoc/>
        public int Read(Span<float> buffer, int samplesToRead, int channelStride)
        {
            return Read(buffer, samplesToRead, channelStride, interleave: false);
        }

        private int Read(Span<float> buffer, int samplesToRead, int channelStride, bool interleave)
        {
            // if the caller didn't ask for any data, bail early
            if (buffer.Length == 0)
            {
                return 0;
            }

            int channels = _channels;
            if (buffer.Length % channels != 0)
            {
                throw new ArgumentException("Length must be a multiple of Channels.", nameof(buffer));
            }
            if (buffer.Length < samplesToRead * channels)
            {
                throw new ArgumentException("The buffer is too small for the requested amount.");
            }
            if (_packetProvider == null)
            {
                throw new ObjectDisposedException(nameof(StreamDecoder));
            }

            // save off value to track when we're done with the request
            int idx = 0;
            int tgt = samplesToRead;

            // try to fill the buffer; drain the last buffer if EOS, resync, bad packet, or parameter change
            while (idx < tgt)
            {
                // if we don't have any more valid data in the current packet, read in the next packet
                if (_prevPacketStart == _prevPacketEnd)
                {
                    if (_eosFound != EndOfStreamFlags.None)
                    {
                        // no more samples, so just return
                        ReturnBuffer(_prevPacketBuf);
                        _prevPacketBuf = null;
                        break;
                    }

                    if (!ReadNextPacket(idx, out long samplePosition))
                    {
                        if ((_eosFound & EndOfStreamFlags.PacketFlag) != 0)
                        {
                            // drain the current packet (the windowing will fade it out)
                            _prevPacketEnd = _prevPacketStop;
                        }
                    }

                    // if we need to pick up a position, and the packet had one, apply the position now
                    if (samplePosition != -1 && !_hasPosition)
                    {
                        _hasPosition = true;
                        _currentPosition = samplePosition - (_prevPacketEnd - _prevPacketStart) - idx;
                    }
                }

                // we read out the valid samples from the previous packet
                int copyLen = Math.Min(tgt - idx, _prevPacketEnd - _prevPacketStart);
                Debug.Assert(copyLen >= 0);

                if (copyLen > 0)
                {
                    if (interleave)
                    {
                        Span<float> target = buffer.Slice(idx * channels, copyLen * channels);
                        if (ClipSamples)
                        {
                            StoreInterleaved<ClipEnable>(target, copyLen);
                        }
                        else
                        {
                            StoreInterleaved<ClipDisable>(target, copyLen);
                        }
                    }
                    else
                    {
                        Span<float> target = buffer.Slice(idx, channelStride + copyLen);
                        StoreContiguous(target, copyLen, channelStride, ClipSamples);
                    }

                    idx += copyLen;
                    _prevPacketStart += copyLen;
                }
            }

            // update the position
            _currentPosition += idx;

            // return count of floats written
            return idx;
        }

        private interface IClipValue
        {
            static abstract bool IsClip { get; }
        }

        private struct ClipEnable : IClipValue
        {
            public static bool IsClip => true;
        }

        private struct ClipDisable : IClipValue
        {
            public static bool IsClip => false;
        }

        private void StoreInterleaved<T>(Span<float> target, int count)
            where T : IClipValue
        {
            float[][]? prevPacketBuf = _prevPacketBuf;
            Debug.Assert(prevPacketBuf != null);

            int channels = _channels;
            int j = 0;
            ref float dst = ref MemoryMarshal.GetReference(target.Slice(0, count * channels));

            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<float> clipped0 = Vector128<float>.Zero;
                Vector128<float> clipped1 = Vector128<float>.Zero;

                if (channels == 2)
                {
                    ref float prev0 = ref MemoryMarshal.GetReference(prevPacketBuf[0].AsSpan(_prevPacketStart, count));
                    ref float prev1 = ref MemoryMarshal.GetReference(prevPacketBuf[1].AsSpan(_prevPacketStart, count));

                    for (; j + Vector128<float>.Count <= count; j += Vector128<float>.Count)
                    {
                        Vector128<float> p0 = Vector128.LoadUnsafe(ref prev0, (nuint)j);
                        Vector128<float> p1 = Vector128.LoadUnsafe(ref prev1, (nuint)j);

                        // Interleave channels
                        Vector128<float> ts0 = Vector128Helper.UnpackLow(p0, p1);  // [ 0, 0, 1, 1 ]
                        Vector128<float> ts1 = Vector128Helper.UnpackHigh(p0, p1); // [ 2, 2, 3, 3 ]

                        if (T.IsClip)
                        {
                            ts0 = Utils.ClipValue(ts0, ref clipped0);
                            ts1 = Utils.ClipValue(ts1, ref clipped1);
                        }

                        ts0.StoreUnsafe(ref dst, (nuint)j * 2);
                        ts1.StoreUnsafe(ref dst, (nuint)j * 2 + (nuint)Vector128<float>.Count);
                    }
                }
                else if (channels == 1)
                {
                    ref float prev0 = ref MemoryMarshal.GetReference(prevPacketBuf[0].AsSpan(_prevPacketStart, count));

                    for (; j + Vector128<float>.Count <= count; j += Vector128<float>.Count)
                    {
                        Vector128<float> p0 = Vector128.LoadUnsafe(ref prev0, (nuint)j);
                        if (T.IsClip)
                        {
                            p0 = Utils.ClipValue(p0, ref clipped0);
                        }
                        p0.StoreUnsafe(ref dst, (nuint)j);
                    }
                }

                _hasClipped |= !Vector128.EqualsAll(clipped0, Vector128<float>.Zero);
                _hasClipped |= !Vector128.EqualsAll(clipped1, Vector128<float>.Zero);
            }

            for (int ch = 0; ch < channels; ch++)
            {
                ref float prev = ref MemoryMarshal.GetReference(prevPacketBuf[ch].AsSpan(_prevPacketStart, count));
                ref float tar = ref Unsafe.Add(ref dst, ch);

                bool clipped = false;

                for (int i = j; i < count; i++)
                {
                    float p = Unsafe.Add(ref prev, i);
                    if (T.IsClip)
                    {
                        p = Utils.ClipValue(p, ref clipped);
                    }
                    Unsafe.Add(ref tar, i * channels) = p;
                }

                _hasClipped |= clipped;
            }
        }

        private void StoreContiguous(Span<float> buffer, int count, int channelStride, bool clip)
        {
            float[][]? prevPacketBuf = _prevPacketBuf;
            Debug.Assert(prevPacketBuf != null);

            for (int ch = 0; ch < _channels; ch++)
            {
                Span<float> destination = buffer.Slice(ch * channelStride, count);
                Span<float> source = prevPacketBuf[ch].AsSpan(_prevPacketStart, count);

                if (clip)
                {
                    ref float dst = ref MemoryMarshal.GetReference(destination);
                    ref float src = ref MemoryMarshal.GetReference(source);
                    int j = 0;

                    if (Vector.IsHardwareAccelerated)
                    {
                        Vector<float> clipped = Vector<float>.Zero;

                        for (; j + Vector<float>.Count <= count; j += Vector<float>.Count)
                        {
                            Vector<float> p0 = VectorHelper.LoadUnsafe(ref src, j);
                            Vector<float> c0 = Utils.ClipValue(p0, ref clipped);
                            c0.StoreUnsafe(ref dst, (nuint)j);
                        }

                        _hasClipped |= !Vector.EqualsAll(clipped, Vector<float>.Zero);
                    }

                    {
                        bool clipped = false;

                        for (; j < count; j++)
                        {
                            Unsafe.Add(ref dst, j) = Utils.ClipValue(Unsafe.Add(ref src, j), ref clipped);
                        }

                        _hasClipped |= clipped;
                    }
                }
                else
                {
                    source.CopyTo(destination);
                }
            }
        }

        private bool ReadNextPacket(nint bufferedSamples, out long samplePosition)
        {
            // decode the next packet now so we can start overlapping with it
            float[][]? curPacket = DecodeNextPacket(
                out int startIndex, out int validLen, out int totalLen, out EndOfStreamFlags isEndOfStream,
                out samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits);

            _eosFound |= isEndOfStream;
            if (curPacket == null)
            {
                _stats.AddPacket(0, bitsRead, bitsRemaining, containerOverheadBits);
                return false;
            }

            // if we get a max sample position, back off our valid length to match
            if (samplePosition != -1 && isEndOfStream != EndOfStreamFlags.None)
            {
                long actualEnd = _currentPosition + bufferedSamples + validLen - startIndex;
                int diff = (int)(samplePosition - actualEnd);
                if (diff < 0)
                {
                    validLen = Math.Max(validLen + diff, 0);
                }
            }

            // start overlapping (if we don't have an previous packet data,
            // just loop and the previous packet logic will handle things appropriately)
            if (_prevPacketEnd > 0)
            {
                Debug.Assert(_prevPacketBuf != null);

                // overlap the first samples in the packet with the previous packet, then loop
                OverlapBuffers(_prevPacketBuf, curPacket, _prevPacketStart, _prevPacketStop, startIndex, _channels);
                _prevPacketStart = startIndex;
            }
            else if (_prevPacketBuf == null)
            {
                // first packet, so it doesn't have any good data before the valid length
                _prevPacketStart = validLen;
            }

            // update stats
            _stats.AddPacket(validLen - _prevPacketStart, bitsRead, bitsRemaining, containerOverheadBits);

            // keep the old buffer so the GC doesn't have to reallocate every packet
            _nextPacketBuf = _prevPacketBuf;

            // save off our current packet's data for the next pass
            _prevPacketEnd = validLen;
            _prevPacketStop = totalLen;
            _prevPacketBuf = curPacket;
            return true;
        }

        private float[][]? DecodeNextPacket(
            out int packetStartIndex, out int packetValidLength, out int packetTotalLength, out EndOfStreamFlags isEndOfStream,
            out long samplePosition, out int bitsRead, out int bitsRemaining, out int containerOverheadBits)
        {
            VorbisPacket packet = _packetProvider.GetNextPacket();
            if (!packet.IsValid)
            {
                // no packet? we're at the end of the stream
                isEndOfStream = EndOfStreamFlags.InvalidPacket;
                bitsRead = 0;
                bitsRemaining = 0;
                containerOverheadBits = 0;
            }
            else
            {
                try
                {
                    // if the packet is flagged as the end of the stream, we can safely mark _eosFound
                    isEndOfStream = packet.IsEndOfStream ? EndOfStreamFlags.PacketFlag : EndOfStreamFlags.None;

                    // resync... that means we've probably lost some data; pick up a new position
                    if (packet.IsResync)
                    {
                        _hasPosition = false;
                    }

                    // grab the container overhead now, since the read won't affect it
                    containerOverheadBits = packet.ContainerOverheadBits;

                    // make sure the packet starts with a 0 bit as per the spec
                    if (packet.ReadBits(1) == 0)
                    {
                        // if we get here, we should have a good packet; decode it and add it to the buffer
                        ref Mode mode = ref _modes[(int)packet.ReadBits(_modeFieldBits)];
                        _nextPacketBuf ??= GetBuffer();
                        if (mode.Decode(ref packet, _nextPacketBuf, out packetStartIndex, out packetValidLength, out packetTotalLength))
                        {
                            // per the spec, do not decode more samples than the last granulePosition
                            samplePosition = packet.GranulePosition;
                            bitsRead = packet.BitsRead;
                            bitsRemaining = packet.BitsRemaining;
                            return _nextPacketBuf;
                        }
                    }
                    bitsRead = packet.BitsRead;
                    bitsRemaining = packet.BitsRead + packet.BitsRemaining;
                }
                finally
                {
                    packet.Finish();
                }
            }

            packetStartIndex = 0;
            packetValidLength = 0;
            packetTotalLength = 0;
            samplePosition = -1;
            return null;
        }

        private static void OverlapBuffers(
            float[][] previousBuffers, float[][] nextBuffers, int prevStart, int prevLen, int nextStart, int channels)
        {
            int length = prevLen - prevStart;
            for (int c = 0; c < channels; c++)
            {
                Span<float> prevSpan = previousBuffers[c].AsSpan(prevStart, length);
                Span<float> nextSpan = nextBuffers[c].AsSpan(nextStart, length);

                ref float prev = ref MemoryMarshal.GetReference(prevSpan);
                ref float next = ref MemoryMarshal.GetReference(nextSpan);

                int i = 0;
                if (Vector.IsHardwareAccelerated)
                {
                    for (; i + Vector<float>.Count <= length; i += Vector<float>.Count)
                    {
                        Vector<float> ni = VectorHelper.LoadUnsafe(ref next, i);
                        Vector<float> pi = VectorHelper.LoadUnsafe(ref prev, i);

                        Vector<float> result = ni + pi;
                        result.StoreUnsafe(ref next, (nuint)i);
                    }
                }
                for (; i < length; i++)
                {
                    Unsafe.Add(ref next, i) += Unsafe.Add(ref prev, i);
                }
            }
        }

        #endregion

        #region Seeking

        /// <summary>
        /// Seeks the stream by the specified duration.
        /// </summary>
        /// <param name="timePosition">The relative time to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        /// <inheritdoc cref="SeekTo(long, SeekOrigin)"/>
        public void SeekTo(TimeSpan timePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            SeekTo((long)(SampleRate * timePosition.TotalSeconds), seekOrigin);
        }

        /// <summary>
        /// Seeks the stream by the specified sample count.
        /// </summary>
        /// <param name="samplePosition">The relative sample position to seek to.</param>
        /// <param name="seekOrigin">The reference point used to obtain the new position.</param>
        /// <exception cref="PreRollPacketException">
        /// Could not read pre-roll packet. Try seeking again prior to reading more samples.
        /// </exception>
        /// <exception cref="SeekOutOfRangeException">The requested seek position extends beyond the stream.</exception>
        public void SeekTo(long samplePosition, SeekOrigin seekOrigin = SeekOrigin.Begin)
        {
            if (_packetProvider == null)
                throw new ObjectDisposedException(nameof(StreamDecoder));

            if (!_packetProvider.CanSeek)
                throw new InvalidOperationException("Seek is not supported by the underlying packet provider.");

            if (samplePosition < 0)
                throw new ArgumentOutOfRangeException(nameof(samplePosition));

            switch (seekOrigin)
            {
                case SeekOrigin.Begin:
                    // no-op
                    break;

                case SeekOrigin.Current:
                    samplePosition = SamplePosition - samplePosition;
                    break;

                case SeekOrigin.End:
                    samplePosition = TotalSamples - samplePosition;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(seekOrigin));
            }

            // seek the stream to the correct position
            long pos = _packetProvider.SeekTo(samplePosition, 1, this);
            int rollForward = (int)(samplePosition - pos);

            // clear out old data
            ResetDecoder();
            _hasPosition = true;

            // read the pre-roll packet
            if (!ReadNextPacket(0, out _))
            {
                // we'll use this to force ReadSamples to fail to read
                _eosFound |= EndOfStreamFlags.InvalidPreroll;
                long maxGranuleCount = _packetProvider.GetGranuleCount(this);
                if (samplePosition > maxGranuleCount)
                {
                    throw new SeekOutOfRangeException();
                }
                _prevPacketStart = _prevPacketStop;
                _currentPosition = samplePosition;
                return;
            }

            // read the actual packet
            if (!ReadNextPacket(0, out _))
            {
                ResetDecoder();
                // we'll use this to force ReadSamples to fail to read
                _eosFound |= EndOfStreamFlags.InvalidPacket;
                throw new PreRollPacketException();
            }

            // adjust our indexes to match what we want
            _prevPacketStart += rollForward;
            _currentPosition = samplePosition;
        }

        int IPacketGranuleCountProvider.GetPacketGranuleCount(ref VorbisPacket curPacket)
        {
            try
            {
                // if it's a resync, there's not any audio data to return
                if (curPacket.IsResync)
                    return 0;

                // if it's not an audio packet, there's no audio data (seems obvious, though...)
                if (curPacket.ReadBit())
                    return 0;

                // OK, let's ask the appropriate mode how long this packet actually is

                // first we need to know which mode...
                uint modeIdx = (uint)curPacket.ReadBits(_modeFieldBits);

                // if we got an invalid mode value, we can't decode any audio data anyway...
                if (modeIdx >= (uint)_modes.Length)
                    return 0;

                return _modes[modeIdx].GetPacketSampleCount(ref curPacket);
            }
            finally
            {
                curPacket.Finish();
            }
        }

        #endregion

        /// <summary>
        /// Cleans up this instance.
        /// </summary>
        public void Dispose()
        {
            (_packetProvider as IDisposable)?.Dispose();
            _packetProvider = null!;

            _nextPacketBuf = null;
            _prevPacketBuf = null;
            _bufferPool = null!;
        }

        #region Properties

        /// <inheritdoc />
        public int Channels => _channels;

        /// <inheritdoc />
        public int SampleRate => _sampleRate;

        /// <inheritdoc />
        public int UpperBitrate { get; private set; }

        /// <summary>
        /// Gets the nominal bitrate of the stream, if specified. 
        /// May be calculated from <see cref="LowerBitrate"/> and <see cref="UpperBitrate"/>.
        /// </summary>
        public int NominalBitrate { get; private set; }

        /// <inheritdoc />
        public int LowerBitrate { get; private set; }

        /// <inheritdoc />
        public ITagData Tags => _tags ??= new TagData(_utf8Vendor, _utf8Comments);

        /// <inheritdoc />
        public TimeSpan TotalTime => TimeSpan.FromSeconds((double)TotalSamples / _sampleRate);

        /// <inheritdoc />
        public long TotalSamples
        {
            get
            {
                if (_packetProvider == null)
                    throw new ObjectDisposedException(nameof(StreamDecoder));
                return _packetProvider.GetGranuleCount(this);
            }
        }

        /// <inheritdoc />
        public TimeSpan TimePosition
        {
            get => TimeSpan.FromSeconds((double)_currentPosition / _sampleRate);
            set => SeekTo(value);
        }

        /// <inheritdoc />
        public long SamplePosition
        {
            get => _currentPosition;
            set => SeekTo(value);
        }

        /// <summary>
        /// Gets or sets whether to clip samples returned by <see cref="Read(Span{float})"/>.
        /// </summary>
        public bool ClipSamples { get; set; }

        /// <inheritdoc />
        public bool SkipTags { get; set; }

        /// <summary>
        /// Gets whether <see cref="Read(Span{float})"/> has returned any clipped samples.
        /// </summary>
        public bool HasClipped => _hasClipped;

        /// <inheritdoc />
        public bool IsEndOfStream => _eosFound != EndOfStreamFlags.None && _prevPacketBuf == null;

        /// <inheritdoc />
        public IStreamStats Stats => _stats;

        #endregion
    }
}
