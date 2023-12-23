using System;
using System.Buffers;
using System.Collections.Generic;
using NVorbis.Ogg;

namespace NVorbis
{
    internal sealed class PageDataPool
    {
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Create();
        private Queue<PageData> _objectPool = new();

        public PageData Rent(int length, bool isResync)
        {
            PageData obj = RentObject();

            obj._refCount = 1;
            obj.IsResync = isResync;

            byte[] array = _arrayPool.Rent(length);
            ArraySegment<byte> segment = new(array, 0, length);
            obj._pageData = segment;

            return obj;
        }

        private PageData RentObject()
        {
            lock (_objectPool)
            {
                if (_objectPool.TryDequeue(out PageData? result))
                {
                    return result;
                }
            }
            return new PageData(this);
        }

        public void Return(PageData pageData)
        {
            if (pageData._refCount != 0)
            {
                throw new InvalidOperationException();
            }

            ArraySegment<byte> segment = pageData._pageData;
            if (segment.Array != null)
            {
                _arrayPool.Return(segment.Array);
            }

            pageData._pageData = Array.Empty<byte>();
            ReturnObject(pageData);
        }

        private void ReturnObject(PageData pageData)
        {
            lock (_objectPool)
            {
                if (_objectPool.Count <= 4)
                {
                    _objectPool.Enqueue(pageData);
                    return;
                }
            }
        }
    }
}
