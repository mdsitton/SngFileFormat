using System;
using NVorbis.Ogg;

namespace NVorbis.Contracts.Ogg
{
    internal interface IStreamPageReader : IDisposable
    {
        IPacketProvider PacketProvider { get; }

        void AddPage(PageData page, long pageOffset);

        PageData GetPage(long pageIndex);

        long FindPage(long granulePos);

        bool GetPage(
            long pageIndex,
            out long granulePos,
            out bool isResync,
            out bool isContinuation, 
            out bool isContinued, 
            out ushort packetCount, 
            out int pageOverhead);

        void SetEndOfStream();

        long PageCount { get; }

        bool HasAllPages { get; }

        long? MaxGranulePosition { get; }

        long FirstDataPageIndex { get; }
    }
}
