using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NVorbis.Ogg;

namespace NVorbis
{
    internal sealed class PageDataPool
    {
        private ArrayPool<byte> _arrayPool = ArrayPool<byte>.Create();
        private Queue<PageData> _objectPool = new();

        public PageData Rent(int length, bool isResync)
        {
            byte[] array = _arrayPool.Rent(length);
            ArraySegment<byte> segment = new(array, 0, length);

            PageData obj = RentObject();
            obj.Reset(segment, isResync);
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
            if (!pageData.IsClosed)
            {
                ThrowNotClosedPage();
            }

            ArraySegment<byte> segment = pageData.ReplaceSegment(default);
            if (segment.Array != null)
            {
                _arrayPool.Return(segment.Array);
            }

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

        [DoesNotReturn]
        private static void ThrowNotClosedPage()
        {
            throw new InvalidOperationException("Attempt at returning PageData that is not closed.");
        }
    }
}
