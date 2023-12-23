
namespace NVorbis.Ogg
{
    /// <summary>
    /// Encapsulates a method that calculates the number of granules in a packet.
    /// </summary>
    public interface IPacketGranuleCountProvider
    {
        /// <summary>
        /// Calculates the number of granules decodable from the specified packet.
        /// </summary>
        /// <param name="packet">The <see cref="VorbisPacket"/> to calculate.</param>
        /// <returns>The calculated number of granules.</returns>
        int GetPacketGranuleCount(ref VorbisPacket packet);
    }
}
