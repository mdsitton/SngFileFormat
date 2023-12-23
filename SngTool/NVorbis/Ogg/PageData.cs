using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace NVorbis.Ogg
{
    internal class PageData
    {
        private readonly PageDataPool _pool;
        internal ArraySegment<byte> _pageData;
        internal int _refCount;

        public bool IsResync { get; internal set; }

        public PageHeader Header
        {
            get
            {
                if (_refCount <= 0)
                {
                    ThrowObjectDisposed();
                }
                return new PageHeader(_pageData);
            }
        }

        public int Length
        {
            get
            {
                if (_refCount <= 0)
                {
                    ThrowObjectDisposed();
                }
                return _pageData.Count;
            }
        }

        internal PageData(PageDataPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));

            _pageData = Array.Empty<byte>();
        }

        public ArraySegment<byte> AsSegment()
        {
            if (_refCount <= 0)
            {
                ThrowObjectDisposed();
            }
            return _pageData;
        }

        public Span<byte> AsSpan()
        {
            if (_refCount <= 0)
            {
                ThrowObjectDisposed();
            }
            return _pageData.AsSpan();
        }

        public PageSlice GetPacket(uint packetIndex)
        {
            ReadOnlySpan<byte> pageSpan = AsSpan();
            PageHeader header = new(pageSpan);

            byte segmentCount = header.SegmentCount;
            ReadOnlySpan<byte> segments = pageSpan.Slice(27, segmentCount);
            int packetIdx = 0;
            int dataIdx = 27 + segments.Length;
            int size = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                byte seg = segments[i];
                size += seg;
                if (seg < 255)
                {
                    if (packetIndex == packetIdx)
                    {
                        return new PageSlice(this, dataIdx, size);
                    }
                    packetIdx++;
                    dataIdx += size;
                    size = 0;
                }
            }
            if (packetIndex == packetIdx)
            {
                return new PageSlice(this, dataIdx, size);
            }
            return new PageSlice(this, 0, 0);
        }

        public void IncrementRef()
        {
            if (_refCount == 0)
            {
                ThrowObjectDisposed();
            }
            Interlocked.Increment(ref _refCount);
        }

        public int DecrementRef()
        {
            int count = Interlocked.Decrement(ref _refCount);
            if (count == 0)
            {
                Dispose();
            }
            return count;
        }

        private void Dispose()
        {
            _pool.Return(this);
        }

        [DoesNotReturn]
        private void ThrowObjectDisposed()
        {
            throw new ObjectDisposedException(GetType().Name);
        }

#if DEBUG
        ~PageData()
        {
            if (_refCount > 0)
            {
                Dispose();
            }
        }
#endif
    }
}