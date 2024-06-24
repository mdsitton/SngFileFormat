using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NVorbis.Contracts;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class PacketProvider : IPacketProvider
    {
        private static ConcurrentQueue<PacketData[]> _dataPartPool = new();
        private const int DataPartInitialArraySize = 2;

        private readonly List<long> _pageEndGranules = new();

        private IStreamPageReader _reader;
        private bool _isDisposed;

        private long _pageIndex;
        private int _packetIndex;

        public bool CanSeek => true;

        public int StreamSerial { get; }

        internal PacketProvider(IStreamPageReader reader, int streamSerial)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));

            StreamSerial = streamSerial;
        }

        public long GetGranuleCount(IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            if (_reader == null)
                ThrowObjectDisposedException();

            _ = GetPageRange(long.MaxValue, packetGranuleCountProvider, out long pageStart, out _);

            long? maxGranule = _reader.MaxGranulePosition;
            if (maxGranule.HasValue && pageStart > maxGranule.GetValueOrDefault())
            {
                pageStart = maxGranule.GetValueOrDefault();
            }

            return pageStart;
        }

        public VorbisPacket GetNextPacket()
        {
            return GetNextPacket(ref _pageIndex, ref _packetIndex);
        }

        public long SeekTo(long granulePos, uint preRoll, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            if (_reader == null)
                ThrowObjectDisposedException();

            if (granulePos < 0)
                throw new ArgumentOutOfRangeException(nameof(granulePos));

            long approxPageIndex = _reader.FindPage(granulePos);

            (long pageIndex, int packetIndex, long actualPos) = GetTargetPageInfo(approxPageIndex, granulePos, packetGranuleCountProvider);

            if (pageIndex == -1)
            {
                // We're at the last page.
                return actualPos;
            }

            // Then apply the preRoll (but only if we're not seeking into the first packet, which is its own preRoll).
            if (pageIndex > _reader.FirstDataPageIndex || packetIndex > 0)
            {
                packetIndex -= (int)preRoll;
            }

            if (!NormalizePacketIndex(ref pageIndex, ref packetIndex))
            {
                throw new SeekOutOfRangeException();
            }

            _pageIndex = pageIndex;
            _packetIndex = (byte)packetIndex;
            return actualPos;
        }

        private (long pageIndex, int packetIndex, long granulePos) GetTargetPageInfo(
            long approxPageIndex,
            long granulePos,
            IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            long pageIndex = approxPageIndex;

            long pageEnd;
            do
            {
                if (!GetPageRange(pageIndex, packetGranuleCountProvider, out long pageStart, out pageEnd))
                {
                    return (-1, 0, pageStart);
                }

                if (granulePos >= pageStart && granulePos <= pageEnd)
                {
                    break;
                }

                if (granulePos - pageEnd > 0)
                {
                    pageIndex++;
                }
                else
                {
                    pageIndex--;
                }
            }
            while (true);

            if (!_reader.GetPage(
                pageIndex, out _, out bool isResync, out bool isContinuation, out bool isContinued,
                out ushort packetCount, out _))
            {
                ThrowMissingPageException();
            }

            int firstRealPacket = isContinuation ? 1 : 0;
            long currentGranulePos = pageEnd;

            int packetIndex = packetCount - 1;
            if (isContinued)
                packetIndex--;

            for (; packetIndex >= firstRealPacket; packetIndex--)
            {
                VorbisPacket packet = CreateValidPacket(
                    ref pageIndex, ref packetIndex, packetIndex == 0 && isResync, isContinued, packetCount);

                int count = packetGranuleCountProvider.GetPacketGranuleCount(ref packet);
                currentGranulePos -= count;

                if (granulePos >= currentGranulePos)
                {
                    break;
                }
            }

            // if we're continued, the continued packet ends on our calculated endGP
            if (packetIndex == 0 && firstRealPacket == 1)
            {
                long prevPageIndex = pageIndex - 1;
                if (!_reader.GetPage(
                    prevPageIndex, out _, out _, out _, out _, out ushort prevPacketCount, out _))
                {
                    ThrowMissingPrecedingPageException();
                }

                int lastPacketIndex = prevPacketCount - 1;
                VorbisPacket lastPacket = CreateValidPacket(
                    ref prevPageIndex, ref lastPacketIndex, packetIndex == 0 && isResync, isContinued, packetCount);

                int count = packetGranuleCountProvider.GetPacketGranuleCount(ref lastPacket);
                currentGranulePos -= count;

                return (prevPageIndex, lastPacketIndex, currentGranulePos);
            }
            return (pageIndex, packetIndex, currentGranulePos);
        }

        private bool GetPageRange(
            long pageIndex, IPacketGranuleCountProvider packetGranuleCountProvider, out long start, out long end)
        {
            if ((ulong)pageIndex >= (ulong)_pageEndGranules.Count)
            {
                FillPageEndGranuleCache(pageIndex, packetGranuleCountProvider);

                if ((ulong)pageIndex > (ulong)_pageEndGranules.Count)
                {
                    pageIndex = _pageEndGranules.Count;
                }
            }

            if ((ulong)(pageIndex - 1) < (ulong)_pageEndGranules.Count)
            {
                start = _pageEndGranules[(int)(pageIndex - 1)];
            }
            else
            {
                start = 0;
            }

            if ((ulong)pageIndex < (ulong)_pageEndGranules.Count)
            {
                end = _pageEndGranules[(int)pageIndex];
                return true;
            }

            end = start;
            return false;
        }

        private void FillPageEndGranuleCache(
            long targetPageIndex, IPacketGranuleCountProvider packetGranuleCountProvider)
        {
            long pIndex = _pageEndGranules.Count;
            long firstDataPage = Math.Max(_reader.FirstDataPageIndex, 0);

            while (pIndex < firstDataPage)
            {
                _pageEndGranules.Add(0);
                pIndex++;
            }

            while (pIndex <= targetPageIndex)
            {
                if (_reader.HasAllPages)
                {
                    if (pIndex >= _reader.PageCount)
                    {
                        break;
                    }
                }

                long pageLength = 0;
                int firstRealPacket = 0;

                long prevPageIndex = pIndex - 1;
                if (prevPageIndex >= 0)
                {
                    if (!_reader.GetPage(
                        prevPageIndex, out _, out _, out _, out bool prevIsContinued, out ushort prevPacketCount, out _))
                    {
                        ThrowMissingPrecedingPageException();
                    }

                    if (prevIsContinued)
                    {
                        int lastPacketIndex = prevPacketCount - 1;

                        // This will either be a continued packet OR the last packet of the last page,
                        // in both cases that's precisely the value we need.

                        VorbisPacket lastPacket = CreatePacket(
                            ref prevPageIndex, ref lastPacketIndex, false, 0, false, prevIsContinued, prevPacketCount, 0);

                        if (!lastPacket.IsValid)
                        {
                            // Avoid throwing for streams that do not have an end-of-stream flag.
                            if (!_reader.HasAllPages)
                            {
                                ThrowInvalidContinuationPacketException();
                            }
                            break;
                        }

                        int count = packetGranuleCountProvider.GetPacketGranuleCount(ref lastPacket);
                        pageLength += count;

                        firstRealPacket = 1;
                    }
                }

                if (!_reader.GetPage(
                    pIndex, out _, out bool isResync, out _, out bool isContinued, out ushort packetCount, out _))
                {
                    // GetPage can change whether a reader HasAllPages,
                    // so avoid throwing for streams that do not have an end-of-stream flag.
                    if (!_reader.HasAllPages)
                    {
                        ThrowMissingPageException();
                    }
                    break;
                }

                int packetIndex = firstRealPacket;
                if (pIndex == firstDataPage)
                {
                    packetIndex = 1;
                }

                int pCount = packetCount;
                if (isContinued)
                {
                    pCount--;
                }

                for (; packetIndex < pCount; packetIndex++)
                {
                    VorbisPacket packet = CreateValidPacket(
                        ref pIndex, ref packetIndex, packetIndex == 0 && isResync, isContinued, packetCount);

                    int packetLength = packetGranuleCountProvider.GetPacketGranuleCount(ref packet);
                    pageLength += packetLength;
                }

                long pageGranule = pageLength;
                if (pIndex > 0)
                {
                    pageGranule += _pageEndGranules[(int)(pIndex - 1)];
                }

                _pageEndGranules.Add(pageGranule);

                pIndex++;
            }
        }

        // this method calc's the appropriate page and packet prior to the one specified,
        // honoring continuations and handling negative packetIndex values
        // if packet index is larger than the current page allows, we just return it as-is
        private bool NormalizePacketIndex(ref long pageIndex, ref int packetIndex)
        {
            if (!_reader.GetPage(
                pageIndex, out _, out bool isResync, out bool isContinuation, out _, out _, out _))
            {
                return false;
            }

            long pgIdx = pageIndex;
            int pktIdx = packetIndex;

            while (pktIdx < (isContinuation ? 1 : 0))
            {
                // can't merge across resync
                if (isContinuation && isResync)
                    return false;

                // get the previous packet
                bool wasContinuation = isContinuation;
                if (!_reader.GetPage(
                    --pgIdx, out _, out isResync, out isContinuation, out bool isContinued, out ushort packetCount, out _))
                {
                    return false;
                }

                // can't merge if continuation flags don't match
                if (wasContinuation && !isContinued)
                    return false;

                // add the previous packet's packetCount
                pktIdx += packetCount - (wasContinuation ? 1 : 0);
            }

            pageIndex = pgIdx;
            packetIndex = pktIdx;
            return true;
        }

        private VorbisPacket GetNextPacket(ref long pageIndex, ref int packetIndex)
        {
            if (_reader == null)
                ThrowObjectDisposedException();

            if (_reader.GetPage(
                pageIndex, out long granulePos, out bool isResync, out _, out bool isContinued,
                out ushort packetCount, out int pageOverhead))
            {
                VorbisPacket packet = CreatePacket(
                    ref pageIndex, ref packetIndex, true, granulePos, isResync, isContinued, packetCount, pageOverhead);

                return packet;
            }

            return default;
        }

        private static PacketData[] GetDataPartArray(int minimumLength)
        {
            if (minimumLength == DataPartInitialArraySize)
            {
                if (_dataPartPool.TryDequeue(out PacketData[]? array))
                {
                    return array;
                }
            }
            return new PacketData[minimumLength];
        }

        private static void ReturnDataPartArray(PacketData[] array)
        {
            if (array.Length == DataPartInitialArraySize)
            {
                _dataPartPool.Enqueue(array);
            }
        }

        [DoesNotReturn]
        private void ThrowObjectDisposedException()
        {
            throw new ObjectDisposedException(GetType().FullName);
        }

        [DoesNotReturn]
        private static void ThrowMissingPrecedingPageException()
        {
            throw new InvalidDataException("Could not get found preceding page.");
        }

        [DoesNotReturn]
        private static void ThrowMissingPageException()
        {
            throw new InvalidDataException("Could not get found page.");
        }

        [DoesNotReturn]
        private static void ThrowInvalidContinuationPacketException()
        {
            throw new InvalidDataException("Could not find end of continuation.");
        }

        private VorbisPacket CreateValidPacket(
            ref long pageIndex, ref int packetIndex, bool isResync, bool isContinued, ushort packetCount)
        {
            VorbisPacket packet = CreatePacket(
                ref pageIndex, ref packetIndex, false, 0, packetIndex == 0 && isResync,
                isContinued, packetCount, 0);

            if (!packet.IsValid)
            {
                ThrowInvalidContinuationPacketException();
            }

            return packet;
        }

        private VorbisPacket CreatePacket(
            ref long pageIndex, ref int packetIndex,
            bool advance, long granulePos, bool isResync, bool isContinued, ushort packetCount, int pageOverhead)
        {
            // create the packet list and add the item to it
            PacketData[] dataParts = GetDataPartArray(DataPartInitialArraySize);
            int partCount = 0;

            PacketLocation firstLocation = new((ulong)pageIndex, (uint)packetIndex);
            PageSlice firstSlice = GetPacketData(firstLocation);
            dataParts[partCount++] = new PacketData(firstLocation, firstSlice);

            // make sure we handle continuations
            bool isLastPacket;
            bool isFirstPacket;
            long finalPage = pageIndex;
            if (isContinued && packetIndex == packetCount - 1)
            {
                // by definition, it's the first packet in the page it ends on
                isFirstPacket = true;

                // but we don't want to include the current page's overhead if we didn't start the page
                if (packetIndex > 0)
                {
                    pageOverhead = 0;
                }

                // go read the next page(s) that include this packet
                long contPageIdx = pageIndex;
                while (isContinued)
                {
                    if (!_reader.GetPage(
                        ++contPageIdx, out granulePos, out isResync, out bool isContinuation, out isContinued,
                        out packetCount, out int contPageOverhead))
                    {
                        // no more pages?  In any case, we can't satify the request
                        return default;
                    }
                    pageOverhead += contPageOverhead;

                    // if the next page isn't a continuation or is a resync,
                    // the stream is broken so we'll just return what we could get
                    if (!isContinuation || isResync)
                    {
                        break;
                    }

                    // if the next page is continued, only keep reading if there are no more packets in the page
                    if (isContinued && packetCount > 1)
                    {
                        isContinued = false;
                    }

                    // add the packet to the list
                    if (dataParts.Length <= partCount)
                    {
                        Array.Resize(ref dataParts, dataParts.Length + 2);
                    }

                    PacketLocation location = new(contPageIdx, 0);
                    PageSlice slice = GetPacketData(location);
                    dataParts[partCount++] = new PacketData(location, slice);
                }

                // we're now the first packet in the final page, so we'll act like it...
                isLastPacket = packetCount == 1;

                // track the final page read
                finalPage = contPageIdx;
            }
            else
            {
                isFirstPacket = packetIndex == 0;
                isLastPacket = packetIndex == packetCount - 1;
            }

            // create the packet instance and populate it with the appropriate initial data
            VorbisPacket packet = new(this, new ArraySegment<PacketData>(dataParts, 0, partCount))
            {
                IsResync = isResync
            };

            // if it's the first packet, associate the container overhead with it
            if (isFirstPacket)
            {
                packet.ContainerOverheadBits = pageOverhead * 8;
            }

            // if we're the last packet completed in the page, set the .GranulePosition
            if (isLastPacket)
            {
                packet.GranulePosition = granulePos;

                // if we're the last packet completed in the page, no more pages are available,
                // and _hasAllPages is set, set .IsEndOfStream
                if (_reader.HasAllPages && finalPage == _reader.PageCount - 1)
                {
                    packet.IsEndOfStream = true;
                }
            }
            else
            {
                packet.GranulePosition = -1;
            }

            if (advance)
            {
                // if we've advanced a page, we continued a packet and should pick up with the next page
                if (finalPage != pageIndex)
                {
                    // we're on the final page now
                    pageIndex = finalPage;

                    // the packet index will be modified below, so set it to the end of the continued packet
                    packetIndex = 0;
                }

                // if we're on the last packet in the page, move to the next page
                // we can't use isLast here because the logic is different; last in page granule vs. last in physical page
                if (packetIndex == packetCount - 1)
                {
                    ++pageIndex;
                    packetIndex = 0;
                }
                // otherwise, just move to the next packet
                else
                {
                    ++packetIndex;
                }
            }

            // done!
            return packet;
        }

        public PageSlice GetPacketData(PacketLocation location)
        {
            ulong pageIndex = location.PageIndex;
            uint packetIndex = location.PacketIndex;

            PageData? pageData = _reader.GetPage((long)pageIndex);
            return pageData.GetPacket(packetIndex);
        }

        public void FinishPacket(ref VorbisPacket packet)
        {
            if (packet.DataParts.Array != null)
            {
                ReturnDataPartArray(packet.DataParts.Array);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }
                _isDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
