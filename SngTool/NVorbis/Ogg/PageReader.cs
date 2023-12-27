using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal sealed class PageReader : PageReaderBase
    {
        private readonly Dictionary<int, IStreamPageReader> _streamReaders = new();
        private readonly List<IStreamPageReader> _readersToDispose = new();
        private readonly NewStreamCallback _newStreamCallback;
        private readonly object _readLock = new();

        private PageData? _page;
        private long _pageOffset;
        private long _nextPageOffset;

        public PageReader(VorbisConfig config, Stream stream, bool leaveOpen, NewStreamCallback newStreamCallback)
            : base(config, stream, leaveOpen)
        {
            _newStreamCallback = newStreamCallback;
        }

        public override void Lock()
        {
            Monitor.Enter(_readLock);
        }

        protected override bool CheckLock()
        {
            return Monitor.IsEntered(_readLock);
        }

        public override bool Release()
        {
            if (Monitor.IsEntered(_readLock))
            {
                Monitor.Exit(_readLock);
                return true;
            }
            return false;
        }

        protected override void SaveNextPageSearch()
        {
            _nextPageOffset = StreamPosition;
        }

        protected override void PrepareStreamForNextPage()
        {
            SeekStream(_nextPageOffset);
        }

        protected override bool AddPage(PageData pageData)
        {
            PageHeader header = pageData.Header;
            header.GetPacketCount(out ushort packetCount, out int dataLength, out _);

            // if the page doesn't have any packets, we can't use it
            if (packetCount == 0)
            {
                return false;
            }

            int streamSerial = header.StreamSerial;
            long pageOffset = StreamPosition - (header.PageOverhead + dataLength);
            PageFlags pageFlags = header.PageFlags;

            if (_streamReaders.TryGetValue(streamSerial, out IStreamPageReader? spr))
            {
                spr.AddPage(pageData, pageOffset);

                // if we've read the last page, remove from our list so cleanup can happen.
                // this is safe because the instance still has access to us for reading.
                if ((pageFlags & PageFlags.EndOfStream) == PageFlags.EndOfStream)
                {
                    if (_streamReaders.Remove(streamSerial, out IStreamPageReader? sprToDispose))
                    {
                        Debug.Assert(spr == sprToDispose);
                        _readersToDispose.Add(spr);
                    }
                }
            }
            else
            {
                StreamPageReader streamReader = new(this, streamSerial);
                streamReader.AddPage(pageData, pageOffset);

                _streamReaders.Add(streamSerial, streamReader);
                if (!_newStreamCallback.Invoke(streamReader.PacketProvider))
                {
                    streamReader.Dispose();
                    _streamReaders.Remove(streamSerial);
                    return false;
                }
            }
            return true;
        }

        public override bool ReadPageAt(long offset, [MaybeNullWhen(false)] out PageData pageData)
        {
            Span<byte> hdrBuf = stackalloc byte[PageHeader.MaxHeaderSize];

            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock())
                throw new InvalidOperationException("Must be locked prior to reading!");

            // this should be safe; we've already checked the page by now
            if (offset == _pageOffset)
            {
                pageData = _page;
                if (pageData != null)
                {
                    // short circuit for when we've already loaded the page
                    pageData.IncrementRef();
                    return true;
                }
            }

            SeekStream(offset);
            int cnt = EnsureRead(hdrBuf.Slice(0, 27));

            _pageOffset = offset;
            ClearLastPage();

            if (VerifyHeader(hdrBuf, ref cnt))
            {
                PageHeader header = new(hdrBuf);
                header.GetPacketCount(out _, out int dataLength, out _);

                int length = header.PageOverhead + dataLength;
                pageData = Config.PageDataPool.Rent(length, false);

                Span<byte> pageSpan = pageData.AsSpan();
                hdrBuf.Slice(0, cnt).CopyTo(pageSpan);

                Span<byte> dataSpan = pageSpan.Slice(cnt);
                int read = EnsureRead(dataSpan);
                if (read != dataSpan.Length)
                {
                    pageData.DecrementRef();
                    pageData = null;
                    return false;
                }

                pageData.IncrementRef();
                _page = pageData;
                return true;
            }

            pageData = null;
            return false;
        }

        public override bool ReadPageHeaderAt(long offset, Span<byte> headerBuffer)
        {
            if (headerBuffer.Length < PageHeader.MaxHeaderSize)
                throw new ArgumentException(null, nameof(headerBuffer));

            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock())
                throw new InvalidOperationException("Must be locked prior to reading!");

            // this should be safe; we've already checked the page by now
            if (offset == _pageOffset)
            {
                if (_page != null)
                {
                    // short circuit for when we've already loaded the page
                    ReadOnlySpan<byte> data = _page.AsSpan();
                    data.Slice(0, Math.Min(data.Length, headerBuffer.Length)).CopyTo(headerBuffer);
                    return true;
                }
            }

            SeekStream(offset);
            int cnt = EnsureRead(headerBuffer.Slice(0, 27));

            _pageOffset = offset;
            ClearLastPage();

            if (VerifyHeader(headerBuffer, ref cnt))
            {
                return true;
            }
            return false;
        }

        private void ClearLastPage()
        {
            if (_page != null)
            {
                _page.DecrementRef();
                _page = null;
            }
        }

        protected override void SetEndOfStreams()
        {
            foreach (KeyValuePair<int, IStreamPageReader> kvp in _streamReaders)
            {
                kvp.Value.SetEndOfStream();
            }
            _streamReaders.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                foreach (KeyValuePair<int, IStreamPageReader> kvp in _streamReaders)
                {
                    kvp.Value.Dispose();
                }
                _streamReaders.Clear();

                foreach (IStreamPageReader spr in _readersToDispose)
                {
                    spr.Dispose();
                }
                _readersToDispose.Clear();

                ClearLastPage();
            }
            base.Dispose(disposing);
        }
    }
}