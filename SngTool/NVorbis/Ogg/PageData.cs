using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VoxelPizza.Memory;

namespace NVorbis.Ogg
{
    internal sealed class PageData : RefCounted
    {
        private readonly PageDataPool _pool;
        private ArraySegment<byte> _pageData;

        public bool IsResync { get; private set; }

        public PageHeader Header => new(AsSpan());

        public int Length => AsSegment().Count;

        internal PageData(PageDataPool pool)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));

            _pageData = Array.Empty<byte>();
        }

        public void Reset(ArraySegment<byte> pageData, bool isResync)
        {
            ResetState();

            _pageData = pageData;
            IsResync = isResync;
        }

        public ArraySegment<byte> AsSegment()
        {
            if (IsClosed)
            {
                ThrowObjectDisposed();
            }
            return _pageData;
        }

        public Span<byte> AsSpan()
        {
            return AsSegment().AsSpan();
        }

        internal ArraySegment<byte> ReplaceSegment(ArraySegment<byte> newSegment)
        {
            ArraySegment<byte> previousSegment = _pageData;
            _pageData = newSegment;
            return previousSegment;
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected override void Release()
        {
            _pool.Return(this);

            base.Release();
        }

#if DEBUG
        ~PageData()
        {
            if (Count > 0)
            {
                _pool.Return(this);
            }
        }
#endif
    }
}