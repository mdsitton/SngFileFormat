using System;
using System.IO;

namespace NVorbis
{
    /// <summary>
    /// Provides extension methods for <see cref="VorbisPacket"/>.
    /// </summary>
    public static class PacketExtensions
    {
        /// <summary>
        /// Reads into the specified buffer.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <param name="buffer">The buffer to read into.</param>
        /// <returns>The number of bytes actually read into the buffer.</returns>
        public static int Read(ref this VorbisPacket packet, Span<byte> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                byte value = (byte)packet.TryPeekBits(8, out int bitsRead);
                if (bitsRead == 0)
                {
                    return i;
                }
                buffer[i] = value;
                packet.SkipBits(8);
            }
            return buffer.Length;
        }

        /// <summary>
        /// Reads the specified number of bytes from the packet and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A byte array holding the data read.</returns>
        /// <exception cref="EndOfStreamException">The packet did not contain enough data.</exception>
        public static byte[] ReadBytes(ref this VorbisPacket packet, int count)
        {
            byte[] buf = new byte[count];
            int cnt = Read(ref packet, buf.AsSpan(0, count));
            if (cnt < count)
            {
                throw new EndOfStreamException();
            }
            return buf;
        }

        /// <summary>
        /// Reads one bit from the packet and advances the read position.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns><see langword="true"/> if the bit was a one, otehrwise <see langword="false"/>.</returns>
        public static bool ReadBit(ref this VorbisPacket packet)
        {
            return packet.ReadBits(1) == 1;
        }

        /// <summary>
        /// Reads the next byte from the packet. Does not advance the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The byte read from the packet.</returns>
        public static byte PeekByte(ref this VorbisPacket packet)
        {
            return (byte)packet.TryPeekBits(8, out _);
        }

        /// <summary>
        /// Reads the next byte from the packet and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The byte read from the packet.</returns>
        public static byte ReadByte(ref this VorbisPacket packet)
        {
            return (byte)packet.ReadBits(8);
        }

        /// <summary>
        /// Reads the next 16 bits from the packet as a <see cref="short"/> and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The value of the next 16 bits.</returns>
        public static short ReadInt16(ref this VorbisPacket packet)
        {
            return (short)packet.ReadBits(16);
        }

        /// <summary>
        /// Reads the next 32 bits from the packet as a <see cref="int"/> and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The value of the next 32 bits.</returns>
        public static int ReadInt32(ref this VorbisPacket packet)
        {
            return (int)packet.ReadBits(32);
        }

        /// <summary>
        /// Reads the next 64 bits from the packet as a <see cref="long"/> and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The value of the next 64 bits.</returns>
        public static long ReadInt64(ref this VorbisPacket packet)
        {
            return (long)packet.ReadBits(64);
        }

        /// <summary>
        /// Reads the next 16 bits from the packet as a <see cref="ushort"/> and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The value of the next 16 bits.</returns>
        public static ushort ReadUInt16(ref this VorbisPacket packet)
        {
            return (ushort)packet.ReadBits(16);
        }

        /// <summary>
        /// Reads the next 32 bits from the packet as a <see cref="uint"/> and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The value of the next 32 bits.</returns>
        public static uint ReadUInt32(ref this VorbisPacket packet)
        {
            return (uint)packet.ReadBits(32);
        }

        /// <summary>
        /// Reads the next 64 bits from the packet as a <see cref="ulong"/> and advances the position counter.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <returns>The value of the next 64 bits.</returns>
        public static ulong ReadUInt64(ref this VorbisPacket packet)
        {
            return packet.ReadBits(64);
        }

        /// <summary>
        /// Advances the position counter by the specified number of bytes.
        /// </summary>
        /// <param name="packet">The packet to read from.</param>
        /// <param name="count">The number of bytes to advance.</param>
        /// <exception cref="EndOfStreamException">The packet did not contain enough data.</exception>
        public static void SkipBytes(ref this VorbisPacket packet, int count)
        {
            int bitsSkipped = packet.SkipBits(count * 8);
            if (bitsSkipped != count * 8)
            {
                throw new EndOfStreamException();
            }
        }
    }
}
