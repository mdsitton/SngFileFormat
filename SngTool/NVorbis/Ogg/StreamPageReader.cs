using System;
using System.Collections.Generic;
using System.Diagnostics;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class StreamPageReader : IStreamPageReader
    {
        private readonly IPageReader _reader;
        private readonly List<long> _pageOffsets = new();

        private bool _isDisposed;

        private int _lastSeqNbr;
        private long? _firstDataPageIndex;
        private long _maxGranulePos;

        private PageData? _lastPage;
        private long _lastPageIndex = -1;
        private long _lastPageGranulePos;
        private bool _lastPageIsResync;
        private bool _lastPageIsContinuation;
        private bool _lastPageIsContinued;
        private ushort _lastPagePacketCount;
        private int _lastPageOverhead;

        public Contracts.IPacketProvider PacketProvider { get; private set; }

        public StreamPageReader(IPageReader pageReader, int streamSerial)
        {
            _reader = pageReader;

            // The packet provider has a reference to us, and we have a reference to it.
            // The page reader has a reference to us.
            // The container reader has a _weak_ reference to the packet provider.
            // The user has a reference to the packet provider.
            // So long as the user doesn't drop their reference and the page reader doesn't drop us,
            //  the packet provider will stay alive.
            // This is important since the container reader only holds a week reference to it.
            PacketProvider = new PacketProvider(this, streamSerial);
        }

        public void AddPage(PageData pageData, long pageOffset)
        {
            // verify we haven't read all pages
            if (HasAllPages)
            {
                return;
            }

            // verify the new page's flags
            PageHeader header = pageData.Header;
            int seqNumber = header.SequenceNumber;
            PageFlags flags = header.PageFlags;
            long granulePosition = header.GranulePosition;

            header.GetPacketCount(out ushort packetCount, out _, out bool isContinued);

            // if the page's granule position is 0 or less it doesn't have any sample
            if (granulePosition != -1)
            {
                if (_firstDataPageIndex == null && granulePosition > 0)
                {
                    _firstDataPageIndex = _pageOffsets.Count;
                }
                else if (_maxGranulePos > granulePosition)
                {
                    // uuuuh, what?!
                    throw new System.IO.InvalidDataException("Granule Position regressed?!");
                }
                _maxGranulePos = granulePosition;
            }
            // granule position == -1, so this page doesn't complete any packets
            // we don't really care if it's a continuation itself, only that it is continued and has a single packet
            else if (_firstDataPageIndex.HasValue && (!isContinued || packetCount != 1))
            {
                throw new System.IO.InvalidDataException(
                    "Granule Position was -1 but page does not have exactly 1 continued packet.");
            }

            if ((flags & PageFlags.EndOfStream) != 0)
            {
                HasAllPages = true;
            }

            if (pageData.IsResync || (_lastSeqNbr != 0 && _lastSeqNbr + 1 != seqNumber))
            {
                // as a practical matter, if the sequence numbers are "wrong",
                // our logical stream is now out of sync so whether the page header sync was lost
                // or we just got an out of order page / sequence jump, we're counting it as a resync
                _pageOffsets.Add(-pageOffset);
            }
            else
            {
                _pageOffsets.Add(pageOffset);
            }

            _lastSeqNbr = seqNumber;

            pageData.IncrementRef();
            SetLastPage(pageData);

            _lastPageGranulePos = granulePosition;
            _lastPageIsContinuation = (flags & PageFlags.ContinuesPacket) != 0;
            _lastPageIsContinued = isContinued;
            _lastPagePacketCount = packetCount;
            _lastPageOverhead = header.PageOverhead;
            _lastPageIndex = _pageOffsets.Count - 1;
        }

        public PageData GetPage(long pageIndex)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (_lastPage != null && _lastPageIndex == pageIndex)
            {
                _lastPage.IncrementRef();
                return _lastPage;
            }

            long pageOffset = _pageOffsets[(int)pageIndex];
            if (pageOffset < 0)
            {
                pageOffset = -pageOffset;
            }

            _reader.Lock();
            try
            {
                if (_reader.ReadPageAt(pageOffset, out PageData? page))
                {
                    if (pageIndex == _lastPageIndex)
                    {
                        Debug.Assert(_lastPage == null);
                        page.IncrementRef();
                        SetLastPage(page);
                    }
                    return page;
                }
            }
            finally
            {
                _reader.Release();
            }
            throw new SeekOutOfRangeException();
        }

        public long FindPage(long granulePos)
        {
            // if we're being asked for the first granule, just grab the very first data page
            long pageIndex = -1;
            if (granulePos == 0)
            {
                pageIndex = FindFirstDataPage();
            }
            else
            {
                // start by looking at the last read page's position...
                int lastPageIndex = _pageOffsets.Count - 1;
                if (GetPageRaw(lastPageIndex, out long pageGP))
                {
                    // most likely, we can look at previous pages for the appropriate one...
                    if (granulePos < pageGP)
                    {
                        pageIndex = FindPageBisection(granulePos, FindFirstDataPage(), lastPageIndex, pageGP);
                    }
                    // unless we're seeking forward, which is merely an excercise in reading forward...
                    else if (granulePos > pageGP)
                    {
                        pageIndex = FindPageForward(lastPageIndex, pageGP, granulePos);
                    }
                    // but of course, it's possible (though highly unlikely) that
                    // the last read page ended on the granule we're looking for.
                    else
                    {
                        pageIndex = lastPageIndex + 1;
                    }
                }
            }
            if (pageIndex == -1)
            {
                throw new SeekOutOfRangeException();
            }
            return pageIndex;
        }

        private long FindFirstDataPage()
        {
            long pageIndex = _pageOffsets.Count - 1;
            if (pageIndex < 0)
            {
                pageIndex = 0;
            }

            while (!_firstDataPageIndex.HasValue)
            {
                if (!GetPage(pageIndex, out _, out _, out _, out _, out _, out _))
                {
                    return -1;
                }
                pageIndex++;
            }
            return _firstDataPageIndex.Value;
        }

        private int FindPageForward(int pageIndex, long pageGranulePos, long granulePos)
        {
            while (pageGranulePos <= granulePos)
            {
                if (++pageIndex == _pageOffsets.Count)
                {
                    if (!GetNextPageGranulePos(out pageGranulePos))
                    {
                        // if we couldn't get a page because we're EOS, allow finding the last granulePos
                        if (MaxGranulePosition < granulePos)
                        {
                            pageIndex = -1;
                        }
                        break;
                    }
                }
                else
                {
                    if (!GetPageRaw(pageIndex, out pageGranulePos))
                    {
                        pageIndex = -1;
                        break;
                    }
                }
            }
            return pageIndex;
        }

        private bool GetNextPageGranulePos(out long granulePos)
        {
            int pageCount = _pageOffsets.Count;
            while (pageCount == _pageOffsets.Count && !HasAllPages)
            {
                PageData? pageData = null;
                _reader.Lock();
                try
                {
                    if (!_reader.ReadNextPage(out pageData))
                    {
                        HasAllPages = true;
                        continue;
                    }

                    if (pageCount < _pageOffsets.Count)
                    {
                        granulePos = pageData.Header.GranulePosition;
                        return true;
                    }
                }
                finally
                {
                    pageData?.DecrementRef();
                    _reader.Release();
                }
            }
            granulePos = 0;
            return false;
        }

        private long FindPageBisection(long granulePos, long low, long high, long highGranulePos)
        {
            // we can treat low as always being before the first sample; later work will correct that if needed
            long lowGranulePos = 0L;
            long dist;
            while ((dist = high - low) > 0)
            {
                // try to find the right page by assumming they are all about the same size
                long index = low + (long)(dist * ((granulePos - lowGranulePos) / (double)(highGranulePos - lowGranulePos)));

                // go get the actual position of the selected page
                if (!GetPageRaw(index, out long idxGranulePos))
                {
                    return -1;
                }

                // figure out where to go from here
                if (idxGranulePos > granulePos)
                {
                    // we read a page after our target (could be the right one, but we don't know yet)
                    high = index;
                    highGranulePos = idxGranulePos;
                }
                else if (idxGranulePos < granulePos)
                {
                    // we read a page before our target
                    low = index + 1;
                    lowGranulePos = idxGranulePos + 1;
                }
                else
                {
                    // direct hit
                    return index + 1;
                }
            }
            return low;
        }

        private bool GetPageRaw(long pageIndex, out long pageGranulePos)
        {
            long offset = _pageOffsets[(int)pageIndex];
            if (offset < 0)
            {
                offset = -offset;
            }

            _reader.Lock();
            try
            {
                Span<byte> headerBuffer = stackalloc byte[PageHeader.MaxHeaderSize];
                if (_reader.ReadPageHeaderAt(offset, headerBuffer))
                {
                    PageHeader header = new(headerBuffer);

                    pageGranulePos = header.GranulePosition;
                    return true;
                }
                pageGranulePos = 0;
                return false;
            }
            finally
            {
                _reader.Release();
            }
        }

        public bool GetPage(
            long pageIndex,
            out long granulePos,
            out bool isResync,
            out bool isContinuation,
            out bool isContinued,
            out ushort packetCount,
            out int pageOverhead)
        {
            if (_lastPageIndex == pageIndex)
            {
                granulePos = _lastPageGranulePos;
                isResync = _lastPageIsResync;
                isContinuation = _lastPageIsContinuation;
                isContinued = _lastPageIsContinued;
                packetCount = _lastPagePacketCount;
                pageOverhead = _lastPageOverhead;
                return true;
            }

            _reader.Lock();
            try
            {
                while (pageIndex >= _pageOffsets.Count && !HasAllPages)
                {
                    if (!_reader.ReadNextPage(out PageData? pageData))
                    {
                        break;
                    }
                    // if we found our page, return it from here so we don't have to do further processing
                    if (pageIndex < _pageOffsets.Count)
                    {
                        isResync = pageData.IsResync;

                        ReadPageData(
                            pageData.Header, pageData, pageIndex,
                            out granulePos, out isContinuation, out isContinued, out packetCount, out pageOverhead);
                        return true;
                    }
                    pageData.DecrementRef();
                }
            }
            finally
            {
                _reader.Release();
            }

            if (pageIndex < _pageOffsets.Count)
            {
                long offset = _pageOffsets[(int)pageIndex];
                if (offset < 0)
                {
                    isResync = true;
                    offset = -offset;
                }
                else
                {
                    isResync = false;
                }

                _reader.Lock();
                try
                {
                    Span<byte> headerBuffer = stackalloc byte[PageHeader.MaxHeaderSize];
                    if (_reader.ReadPageHeaderAt(offset, headerBuffer))
                    {
                        PageHeader header = new(headerBuffer);

                        _lastPageIsResync = isResync;

                        ReadPageData(
                            header, null, pageIndex,
                            out granulePos, out isContinuation, out isContinued, out packetCount, out pageOverhead);
                        return true;
                    }
                }
                finally
                {
                    _reader.Release();
                }
            }

            granulePos = 0;
            isResync = false;
            isContinuation = false;
            isContinued = false;
            packetCount = 0;
            pageOverhead = 0;
            return false;
        }

        private void ReadPageData(
            PageHeader header, PageData? pageData, long pageIndex,
            out long granulePos, out bool isContinuation, out bool isContinued, out ushort packetCount, out int pageOverhead)
        {
            header.GetPacketCount(out packetCount, out _, out isContinued);

            SetLastPage(pageData);

            _lastPageGranulePos = granulePos = header.GranulePosition;
            _lastPageIsContinuation = isContinuation = (header.PageFlags & PageFlags.ContinuesPacket) != 0;
            _lastPageIsContinued = isContinued;
            _lastPagePacketCount = packetCount;
            _lastPageOverhead = pageOverhead = header.PageOverhead;
            _lastPageIndex = pageIndex;
        }

        public void SetEndOfStream()
        {
            HasAllPages = true;
            SetLastPage(null);
        }

        public long PageCount => _pageOffsets.Count;

        public bool HasAllPages { get; private set; }

        public long? MaxGranulePosition => HasAllPages ? _maxGranulePos : null;

        public long FirstDataPageIndex => FindFirstDataPage();

        private void SetLastPage(PageData? pageData)
        {
            Debug.Assert(pageData == null || !pageData.IsClosed);

            _lastPage?.DecrementRef();
            _lastPage = pageData;
        }

        private void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    SetLastPage(null);
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
