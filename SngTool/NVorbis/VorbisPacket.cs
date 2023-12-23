using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NVorbis.Contracts;
using NVorbis.Ogg;

namespace NVorbis
{
    /// <summary>
    /// Describes a packet of data from a data stream.
    /// </summary>
    public struct VorbisPacket
    {
        private ulong _bitBucket;
        private int _bitCount;
        private byte _overflowBits;
        private PacketFlags _packetFlags;
        private int _readBits;

        private int _totalBits;
        private byte[] _data;
        private int _dataPartIndex;
        private int _dataOfs;
        private int _dataEnd;

        /// <summary>
        /// Gets the packet provider from which the packet originated.
        /// </summary>
        public IPacketProvider PacketProvider { get; }

        /// <summary>
        /// Gets the packet data parts that represent the data in the stream.
        /// </summary>
        public ArraySegment<PacketData> DataParts { get; }

        /// <summary>
        /// Creates a new and readable <see cref="VorbisPacket"/>.
        /// </summary>
        /// <param name="packetProvider">The packet provider.</param>
        /// <param name="dataParts">The packet data parts.</param>
        public VorbisPacket(IPacketProvider packetProvider, ArraySegment<PacketData> dataParts) : this()
        {
            PacketProvider = packetProvider;
            DataParts = dataParts;

            _data = Array.Empty<byte>();
        }

        /// <summary>
        /// Gets whether this packet is readable.
        /// </summary>
        public bool IsValid => PacketProvider != null;

        /// <summary>
        /// Gets the number of container overhead bits associated with this packet.
        /// </summary>
        public int ContainerOverheadBits { get; set; }

        /// <summary>
        /// Gets the granule position of the packet, if known.
        /// </summary>
        public long GranulePosition { get; set; }

        /// <summary>
        /// Gets whether this packet occurs immediately following a loss of sync in the stream.
        /// </summary>
        public bool IsResync
        {
            get => GetFlag(PacketFlags.IsResync);
            set => SetFlag(PacketFlags.IsResync, value);
        }

        /// <summary>
        /// Gets whether this packet did not read its full data.
        /// </summary>
        public bool IsShort
        {
            get => GetFlag(PacketFlags.IsShort);
            private set => SetFlag(PacketFlags.IsShort, value);
        }

        /// <summary>
        /// Gets whether the packet is the last packet of the stream.
        /// </summary>
        public bool IsEndOfStream
        {
            get => GetFlag(PacketFlags.IsEndOfStream);
            set => SetFlag(PacketFlags.IsEndOfStream, value);
        }

        /// <summary>
        /// Gets the number of bits read from the packet.
        /// </summary>
        public int BitsRead => _readBits;

        /// <summary>
        /// Gets the number of bits left in the packet.
        /// </summary>
        public int BitsRemaining => _totalBits - _readBits;

        /// <summary>
        /// Gets the total number of bits in the packet.
        /// </summary>
        public int TotalBits => _totalBits;

        private bool GetFlag(PacketFlags flag)
        {
            return (_packetFlags & flag) == flag;
        }

        private void SetFlag(PacketFlags flag, bool value)
        {
            if (value)
            {
                _packetFlags |= flag;
            }
            else
            {
                _packetFlags &= ~flag;
            }
        }

        private void SetPagePart(int partIndex)
        {
            ref PacketData dataPart = ref DataParts.AsSpan()[partIndex];
            if (dataPart.Slice.Page == null)
            {
                dataPart.Slice = PacketProvider.GetPacketData(dataPart.Location);
            }

            ArraySegment<byte> segment = dataPart.Slice.AsSegment();
            SetData(segment);
            _totalBits += segment.Count * 8;
        }

        /// <summary>
        /// Resets the read buffers to the beginning of the packet.
        /// </summary>
        public void Reset()
        {
            _bitBucket = 0;
            _bitCount = 0;
            _overflowBits = 0;
            _readBits = 0;
            _dataPartIndex = 0;
            _totalBits = 0;

            SetData(Array.Empty<byte>());
        }

        /// <summary>
        /// Reads the specified number of bits from the packet and advances the read position.
        /// </summary>
        /// <param name="count">The number of bits to read.</param>
        /// <returns>The value read. If not enough bits remained, this will be a truncated value.</returns>
        public ulong ReadBits(int count)
        {
            ulong value = TryPeekBits(count, out int bitsRead);

            SkipBits(bitsRead);

            return value;
        }

        private ulong RefillBits(ref int count)
        {
            ulong buffer = 0;
            uint toRead = (71 - (uint)_bitCount) / 8;
            int bytesRead = ReadBytes(MemoryMarshal.CreateSpan(ref Unsafe.As<ulong, byte>(ref buffer), (int)toRead));

            _bitBucket |= buffer << _bitCount;
            _bitCount += bytesRead * 8;

            if (bytesRead > 0 && _bitCount > 64)
            {
                ulong lastByte = buffer >> ((bytesRead - 1) * 8);
                _overflowBits = (byte)(lastByte >> (72 - _bitCount));
            }

            if (count > _bitCount)
            {
                count = _bitCount;
            }

            ulong value = _bitBucket;
            ulong mask = ~(ulong.MaxValue << count);
            return value & mask;
        }

        /// <summary>
        /// Attempts to read the specified number of bits from the packet. Does not advance the read position.
        /// </summary>
        /// <param name="count">The number of bits to read.</param>
        /// <param name="bitsRead">Outputs the actual number of bits read.</param>
        /// <returns>The value of the bits read.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong TryPeekBits(int count, out int bitsRead)
        {
            Debug.Assert((uint)count <= 64);

            bitsRead = count;
            if (_bitCount < count)
            {
                return RefillBits(ref bitsRead);
            }

            ulong value = _bitBucket;
            ulong mask = ~(ulong.MaxValue << count);
            return value & mask;
        }

        /// <summary>
        /// Advances the read position by the the specified number of bits.
        /// </summary>
        /// <param name="count">The number of bits to skip reading.</param>
        /// <returns>The amount of bits actually skipped.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int SkipBits(int count)
        {
            if (_bitCount >= count)
            {
                // we still have bits left over...
                _bitBucket >>= count;

                if (_bitCount > 64)
                {
                    SkipOverflow(count);
                }

                _bitCount -= count;
                _readBits += count;
                return count;
            }
            else //  _bitCount < count
            {
                // we have to move more bits than we have available...
                return SkipExtraBits(count);
            }
        }

        private void SkipOverflow(int count)
        {
            int overflowCount = _bitCount - 64;
            _bitBucket |= (ulong)_overflowBits << (_bitCount - count - overflowCount);

            if (overflowCount > count)
            {
                // ugh, we have to keep bits in overflow
                _overflowBits >>= count;
            }
        }

        private int SkipExtraBits(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            byte buffer = 0;
            Span<byte> span = new(ref buffer);
            int startReadBits = _readBits;

            count -= _bitCount;
            _readBits += _bitCount;
            _bitCount = 0;
            _bitBucket = 0;

            while (count >= 8)
            {
                if (ReadBytes(span) != span.Length)
                {
                    count = 0;
                    IsShort = true;
                    break;
                }
                count -= 8;
                _readBits += 8;
            }

            if (count > 0)
            {
                int r = ReadBytes(span);
                if (r != span.Length)
                {
                    IsShort = true;
                }
                else
                {
                    _bitBucket = (ulong)(buffer >> count);
                    _bitCount = 8 - count;
                    _readBits += count;
                }
            }

            return _readBits - startReadBits;
        }

        private void SetData(ArraySegment<byte> data)
        {
            _data = data.Array ?? Array.Empty<byte>();
            _dataOfs = data.Offset;
            _dataEnd = data.Offset + data.Count;
        }

        private bool GetNextPacketData()
        {
            int dataPartIndex = _dataPartIndex++;
            if (dataPartIndex < DataParts.Count)
            {
                SetPagePart(dataPartIndex);
                return true;
            }

            byte[] oldData = _data;
            SetData(Array.Empty<byte>());

            // Restore to previous index to not overflow ever
            _dataPartIndex--;

            // If data was already the special array,
            // there was an attempt to read past the end of the packet so invalidate the read.
            return oldData != Array.Empty<byte>();
        }

        /// <summary>
        /// Reads the next bytes in the packet.
        /// </summary>
        /// <returns>The amount of read bytes, or <c>0</c> if no more data is available.</returns>
        public int ReadBytes(Span<byte> destination)
        {
            int length = destination.Length;
            do
            {
                int left = _dataEnd - _dataOfs;
                int toRead = Math.Min(left, destination.Length);
                _data.AsSpan(_dataOfs, toRead).CopyTo(destination);
                destination = destination.Slice(toRead);

                _dataOfs += toRead;
                if (_dataOfs == _dataEnd)
                {
                    if (!GetNextPacketData())
                    {
                        // There is no further data.
                        break;
                    }
                }
            }
            while (destination.Length > 0);

            return length - destination.Length;
        }

        /// <summary>
        /// Frees the buffers and caching for the packet instance.
        /// </summary>
        public void Finish()
        {
            Span<PacketData> dataParts = DataParts;
            for (int i = 0; i < dataParts.Length; i++)
            {
                ref PacketData packetData = ref dataParts[i];
                if (packetData.Slice.Page != null)
                {
                    packetData.Slice.Page.DecrementRef();
                    packetData.Slice = default;
                }
            }

            PacketProvider.FinishPacket(ref this);
        }

        /// <summary>
        /// Defines flags to apply to the current packet
        /// </summary>
        [Flags]
        // for now, let's use a byte... if we find we need more space, we can always expand it...
        private enum PacketFlags : byte
        {
            /// <summary>
            /// Packet is first since reader had to resync with stream.
            /// </summary>
            IsResync = 1 << 0,

            /// <summary>
            /// Packet is the last in the logical stream.
            /// </summary>
            IsEndOfStream = 1 << 1,

            /// <summary>
            /// Packet does not have all its data available.
            /// </summary>
            IsShort = 1 << 2,
        }
    }
}
