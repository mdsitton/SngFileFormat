using System;

namespace NVorbis
{
    [Flags]
    internal enum EndOfStreamFlags : byte
    {
        None,
        InvalidPacket = 1 << 0,
        PacketFlag = 1 << 2,
        InvalidPreroll = 1 << 3,
    }
}
