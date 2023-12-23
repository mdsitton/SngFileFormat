using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NVorbis.Contracts.Ogg;

namespace NVorbis.Ogg
{
    internal delegate bool NewStreamCallback(Contracts.IPacketProvider packetProvider);

    internal abstract class PageReaderBase : IPageReader
    {
        private readonly HashSet<int> _ignoredSerials = new();

        private List<PageSlice> _overflowPages = new();

        private Stream _stream;
        private bool _leaveOpen;
        private long _streamPosition;

        public VorbisConfig Config { get; }

        public bool IsDisposed { get; private set; }

        protected PageReaderBase(VorbisConfig config, Stream stream, bool leaveOpen)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _leaveOpen = leaveOpen;
        }

        protected long StreamPosition => _streamPosition;

        public bool CanSeek => _stream.CanSeek;

        public long ContainerBits { get; private set; }

        public long WasteBits { get; private set; }

        private bool VerifyPage(
            ReadOnlySpan<byte> headerBuf,
            bool isResync,
            [MaybeNullWhen(false)] out PageData pageData,
            out int bytesRead)
        {
            byte segCnt = headerBuf[26];
            if (headerBuf.Length < 27 + segCnt)
            {
                pageData = null;
                bytesRead = 0;
                return false;
            }

            int dataLen = 0;
            for (int i = 0; i < segCnt; i++)
            {
                dataLen += headerBuf[i + 27];
            }

            pageData = Config.PageDataPool.Rent(dataLen + segCnt + 27, isResync);
            Span<byte> pageSpan = pageData.AsSpan();

            headerBuf.Slice(0, segCnt + 27).CopyTo(pageSpan);
            bytesRead = EnsureRead(pageSpan.Slice(segCnt + 27, dataLen));
            if (bytesRead != dataLen)
            {
                pageData.DecrementRef();
                pageData = null;
                return false;
            }

            Crc crc = Crc.Create();
            Span<byte> pb0 = pageSpan.Slice(0, 22);
            crc.Update(pb0);
            crc.Update(0);
            crc.Update(0);
            crc.Update(0);
            crc.Update(0);

            Span<byte> pb1 = pageSpan.Slice(26, pageSpan.Length - 26);
            crc.Update(pb1);
            return crc.Test(BinaryPrimitives.ReadUInt32BigEndian(pageSpan.Slice(22, sizeof(uint))));
        }

        private bool TryAddPage(PageData pageData)
        {
            PageHeader header = pageData.Header;
            int streamSerial = header.StreamSerial;
            int pageOverhead = header.PageOverhead;

            if (!_ignoredSerials.Contains(streamSerial))
            {
                if (AddPage(pageData))
                {
                    ContainerBits += 8 * pageOverhead;
                    return true;
                }
                _ignoredSerials.Add(streamSerial);
            }
            return false;
        }

        private void EnqueueData(PageData pageData, int count)
        {
            pageData.IncrementRef();

            int start = pageData.Length - count;
            _overflowPages.Add(new PageSlice(pageData, start, count));
        }

        private void ConsumeOverflow(int count)
        {
            PageSlice page = _overflowPages[0];

            int newLength = page.Length - count;
            if (newLength == 0)
            {
                page.Page.DecrementRef();
                _overflowPages.RemoveAt(0);
            }
            else
            {
                int newStart = page.Start + count;
                _overflowPages[0] = new PageSlice(page.Page, newStart, newLength);
            }
        }

        private void ClearEnqueuedData(int count)
        {
            do
            {
                if (_overflowPages.Count <= 0)
                {
                    break;
                }
                PageSlice page = _overflowPages[0];

                int toSkip = Math.Min(count, page.Length);
                count -= toSkip;

                ConsumeOverflow(toSkip);
            }
            while (count > 0);
        }

        private int FillHeader(Span<byte> buffer)
        {
            int copyCount = 0;
            do
            {
                if (_overflowPages.Count <= 0)
                {
                    break;
                }
                PageSlice page = _overflowPages[0];

                int toCopy = Math.Min(buffer.Length, page.Length);
                ReadOnlySpan<byte> source = page.AsSpan().Slice(0, toCopy);

                source.CopyTo(buffer);
                buffer = buffer.Slice(toCopy);
                copyCount += toCopy;

                ConsumeOverflow(toCopy);
            }
            while (buffer.Length > 0);

            if (buffer.Length > 0)
            {
                copyCount += EnsureRead(buffer);
            }
            return copyCount;
        }

        private bool VerifyHeader(Span<byte> buffer, ref int cnt, bool isFromReadNextPage)
        {
            if (buffer[0] == 0x4f && buffer[1] == 0x67 && buffer[2] == 0x67 && buffer[3] == 0x53)
            {
                if (cnt < 27)
                {
                    if (isFromReadNextPage)
                    {
                        cnt += FillHeader(buffer.Slice(27 - cnt, 27 - cnt));
                    }
                    else
                    {
                        cnt += EnsureRead(buffer.Slice(27 - cnt, 27 - cnt));
                    }
                }

                if (cnt >= 27)
                {
                    byte segCnt = buffer[26];
                    if (isFromReadNextPage)
                    {
                        cnt += FillHeader(buffer.Slice(27, segCnt));
                    }
                    else
                    {
                        cnt += EnsureRead(buffer.Slice(27, segCnt));
                    }
                    if (cnt == 27 + segCnt)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        protected int EnsureRead(Span<byte> buffer)
        {
            int totalRead = 0;
            do
            {
                int count = _stream.Read(buffer.Slice(totalRead));
                if (count == 0)
                {
                    break;
                }
                _streamPosition += count;
                totalRead += count;
            }
            while (totalRead < buffer.Length);
            return totalRead;
        }

        /// <summary>
        /// Verifies the sync sequence and loads the rest of the header.
        /// </summary>
        /// <returns><see langword="true"/> if successful, otherwise <see langword="false"/>.</returns>
        protected bool VerifyHeader(Span<byte> buffer, ref int cnt)
        {
            return VerifyHeader(buffer, ref cnt, false);
        }

        /// <summary>
        /// Seeks the underlying stream to the requested position.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <returns>The new position of the stream.</returns>
        /// <exception cref="InvalidOperationException">The stream is not seekable.</exception>
        protected long SeekStream(long offset)
        {
            // make sure we're locked; seeking won't matter if we aren't
            if (!CheckLock())
                throw new InvalidOperationException("Must be locked prior to reading!");

            if (_streamPosition == offset)
            {
                return offset;
            }

            long result = _stream.Seek(offset, SeekOrigin.Begin);
            _streamPosition = result;
            return result;
        }

        protected virtual void PrepareStreamForNextPage()
        {
        }

        protected virtual void SaveNextPageSearch()
        {
        }

        protected abstract bool AddPage(PageData pageData);

        protected abstract void SetEndOfStreams();

        public virtual void Lock()
        {
        }

        protected virtual bool CheckLock()
        {
            return true;
        }

        public virtual bool Release()
        {
            return false;
        }

        public bool ReadNextPage([MaybeNullWhen(false)] out PageData pageData)
        {
            // 27 - 4 + 27 + 255 (found sync at end of first buffer, and found page has full segment count)
            Span<byte> headerBuf = stackalloc byte[305];

            // make sure we're locked; no sense reading if we aren't
            if (!CheckLock())
                throw new InvalidOperationException("Must be locked prior to reading!");

            PrepareStreamForNextPage();

            bool isResync = false;
            int ofs = 0;
            int cnt;
            while ((cnt = FillHeader(headerBuf.Slice(ofs, 27 - ofs))) > 0)
            {
                cnt += ofs;
                for (int i = 0; i < cnt - 4; i++)
                {
                    if (VerifyHeader(headerBuf.Slice(i), ref cnt, true))
                    {
                        if (VerifyPage(headerBuf.Slice(i, cnt), isResync, out PageData? page, out int bytesRead))
                        {
                            // one way or the other, we have to clear out the page's bytes from the queue (if queued)
                            ClearEnqueuedData(bytesRead);

                            // also, we need to let our inheritors have a chance to save state for next time
                            SaveNextPageSearch();

                            int pageLength = page.Length;

                            // pass it to our inheritor
                            if (TryAddPage(page))
                            {
                                pageData = page;
                                return true;
                            }

                            page.DecrementRef();

                            // otherwise, the whole page is useless...

                            // save off that we've burned that many bits
                            WasteBits += pageLength * 8;

                            // set up to load the next page, then loop
                            ofs = 0;
                            cnt = 0;
                            break;
                        }
                        else if (page != null)
                        {
                            EnqueueData(page, bytesRead);
                        }
                    }
                    WasteBits += 8;
                    isResync = true;
                }

                if (cnt >= 3)
                {
                    headerBuf[0] = headerBuf[cnt - 3];
                    headerBuf[1] = headerBuf[cnt - 2];
                    headerBuf[2] = headerBuf[cnt - 1];
                    ofs = 3;
                }
            }

            if (cnt == 0)
            {
                SetEndOfStreams();
            }

            pageData = null;
            return false;
        }

        public abstract bool ReadPageAt(long offset, [MaybeNullWhen(false)] out PageData pageData);

        public abstract bool ReadPageHeaderAt(long offset, Span<byte> headerBuffer);

        protected virtual void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    foreach (PageSlice slice in _overflowPages)
                    {
                        slice.Page.DecrementRef();
                    }

                    SetEndOfStreams();

                    if (!_leaveOpen)
                    {
                        _stream?.Dispose();
                    }
                    _stream = null!;
                }
                IsDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
