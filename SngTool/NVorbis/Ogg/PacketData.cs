
using System;

namespace NVorbis.Ogg
{
    /// <summary>
    /// Represents a packet location and potentially a data slice.
    /// </summary>
    public struct PacketData : IEquatable<PacketData>
    {
        /// <summary>
        /// Gets the packet location for this packet data.
        /// </summary>
        public PacketLocation Location { get; }

        /// <summary>
        /// Gets or sets the page slice for this packet data. Can be <see langword="default"/>.
        /// </summary>
        public PageSlice Slice { get; set; }

        /// <summary>
        /// Constructs the <see cref="PacketData"/> with the given location and data.
        /// </summary>
        /// <param name="location">The location of the packet within the logical stream.</param>
        /// <param name="slice">The data of the packet from a page. Can be <see langword="default"/>.</param>
        public PacketData(PacketLocation location, PageSlice slice)
        {
            Location = location;
            Slice = slice;
        }
        
        /// <summary>
        /// Constructs the <see cref="PacketData"/> with the given location.
        /// </summary>
        /// <inheritdoc cref="PacketData(PacketLocation, PageSlice)"/>
        public PacketData(PacketLocation location) : this(location, default)
        {
        }

        /// <inheritdoc/>
        public bool Equals(PacketData other)
        {
            return Location.Equals(other.Location);
        }
        
        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is PacketData other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Location.GetHashCode();
        }
    }
}
