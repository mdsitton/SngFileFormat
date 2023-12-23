using System;
using NVorbis.Ogg;

namespace NVorbis.Contracts
{
    /// <summary>
    /// Describes an interface for a packet stream reader.
    /// </summary>
    public interface IPacketProvider : IDisposable
    {
        /// <summary>
        /// Gets whether the provider supports seeking.
        /// </summary>
        bool CanSeek { get; }

        /// <summary>
        /// Gets the serial number of this provider's data stream.
        /// </summary>
        int StreamSerial { get; }

        /// <summary>
        /// Gets the next packet in the stream and advances to the next packet position.
        /// </summary>
        /// <returns>The <see cref="VorbisPacket"/> for the next packet if available.</returns>
        VorbisPacket GetNextPacket();

        /// <summary>
        /// Seeks the stream to the packet that is prior to the requested granule position by the specified preroll number of packets.
        /// </summary>
        /// <param name="granulePos">The granule position to seek to.</param>
        /// <param name="preRoll">The number of packets to seek backward prior to the granule position.</param>
        /// <param name="packetGranuleCountProvider">A provider that calculates the number of granules in packets.</param>
        /// <returns>The granule position at the start of the packet containing the requested position.</returns>
        long SeekTo(long granulePos, uint preRoll, IPacketGranuleCountProvider packetGranuleCountProvider);

        /// <summary>
        /// Gets the total number of granule available in the stream.
        /// </summary>
        long GetGranuleCount(IPacketGranuleCountProvider packetGranuleCountProvider);

        /// <summary>
        /// Gets packet data for the requested location.
        /// </summary>
        /// <param name="location">The packet data location.</param>
        /// <returns>The packet data slice.</returns>
        PageSlice GetPacketData(PacketLocation location);

        /// <summary>
        /// Used to finish a packet. Using a finished packet is undefined behavior.
        /// </summary>
        /// <param name="packet">The packet to finish.</param>
        void FinishPacket(ref VorbisPacket packet);
    }
}
