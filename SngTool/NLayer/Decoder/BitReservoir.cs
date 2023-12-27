using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NLayer.Decoder
{
    internal class BitReservoir
    {
        public const int BufferSize = 8192;

        // Per the spec, the maximum buffer size for layer III is 7680 bits, which is 960 bytes.
        // The only catch is if we're decoding a "free" frame, which could be a lot more (since
        //  some encoders allow higher bitrates to maintain audio transparency).
        private byte[] _buf = new byte[BufferSize];
        private int _start = 0, _end = -1, _bitsLeft = 0;
        private long _bitsRead = 0L;

        private static int GetSlots(MpegFrame frame)
        {
            int cnt = frame.FrameLength - 4;
            if (frame.HasCrc)
                cnt -= 2;

            if (frame.Version == MpegVersion.Version1 && frame.ChannelMode != MpegChannelMode.Mono)
                return cnt - 32;
            if (frame.Version > MpegVersion.Version1 && frame.ChannelMode == MpegChannelMode.Mono)
                return cnt - 9;
            return cnt - 17;
        }

        public bool AddBits(MpegFrame frame, int overlap)
        {
            int originalEnd = _end;

            int slots = GetSlots(frame);
            while (--slots >= 0)
            {
                int tmp = frame.ReadBits(8);
                if (tmp == -1)
                    ThrowFrameNotEnoughBytes();

                _buf[++_end] = (byte)tmp;
                if (_end == BufferSize - 1)
                    _end = -1;
            }

            _bitsLeft = 8;
            if (originalEnd == -1)
            {
                // it's either the start of the stream or we've reset...  
                // only return true if overlap says this frame is enough
                return overlap == 0;
            }
            else
            {
                // it's not the start of the stream so calculate _start based on whether we have enough bytes left

                // if we have enough bytes, reset start to match overlap
                if ((originalEnd + 1 - _start + BufferSize) % BufferSize >= overlap)
                {
                    _start = (originalEnd + 1 - overlap + BufferSize) % BufferSize;
                    return true;
                }
                // otherwise, just set start to match the start of the frame (we probably skipped a frame)
                else
                {
                    _start = originalEnd + overlap;
                    return false;
                }
            }
        }

        public int GetBits(int count)
        {
            int bits = TryPeekBits(count, out int bitsRead);
            if (bitsRead < count)
                ThrowReservoirNotEnoughBits();

            SkipBits(count);
            return bits;
        }

        // this is an optimized single-bit read
        public int Get1Bit()
        {
            if (_bitsLeft == 0)
                ThrowReservoirNotEnoughBits();

            _bitsLeft--;
            _bitsRead++;
            int val = (_buf[_start] >> _bitsLeft) & 1;

            if (_bitsLeft == 0)
            {
                if (++_start >= BufferSize)
                    _start = 0;

                if (_start != _end + 1)
                    _bitsLeft = 8;
            }

            return val;
        }

        public int TryPeekBits(int count, out int readCount)
        {
            Debug.Assert(count >= 0 && count < 32, "Count must be between 0 and 32 bits.");

            int bitsLeft = _bitsLeft;

            // if we don't have any bits left, just return no bits read
            if (bitsLeft == 0 || count == 0)
            {
                readCount = 0;
                return 0;
            }

            byte[] buf = _buf;

            // get bits from the current start of the reservoir
            int bits = buf[_start];
            if (count < bitsLeft)
            {
                // just grab the bits, adjust the "left" count, and return
                bits >>= bitsLeft - count;
                bits &= (1 << count) - 1;
                readCount = count;
                return bits;
            }

            // we have to do it the hard way...
            bits &= (1 << bitsLeft) - 1;
            count -= bitsLeft;
            readCount = bitsLeft;

            int resStart = _start;

            // arg... gotta grab some more bits...
            // advance the start marker, and if we just advanced it past the end of the buffer, bail
            while (count > 0)
            {
                if (++resStart >= BufferSize)
                    resStart = 0;
                else if (resStart == _end + 1)
                    break;

                // figure out how many bits to pull from it
                int bitsToRead = Math.Min(count, 8);

                // move the existing bits over
                bits <<= bitsToRead;
                bits |= buf[resStart] >> (8 - bitsToRead);

                // update our count
                count -= bitsToRead;

                // update our remaining bits
                readCount += bitsToRead;
            }

            return bits;
        }

        public int BitsAvailable
        {
            get
            {
                if (_bitsLeft > 0)
                    return ((_end - _start + BufferSize) % BufferSize * 8) + _bitsLeft;

                return 0;
            }
        }

        public long BitsRead => _bitsRead;

        public void SkipBits(int count)
        {
            if (count > 0)
            {
                // make sure we have enough bits to skip
                Debug.Assert(count <= BitsAvailable);

                // now calculate the new positions
                int offset = (8 - _bitsLeft) + count;
                _start = ((offset / 8) + _start) % BufferSize;
                _bitsLeft = 8 - (offset % 8);

                _bitsRead += count;
            }
        }

        public void RewindBits(int count)
        {
            _bitsLeft += count;
            _bitsRead -= count;

            while (_bitsLeft > 8)
            {
                _start--;
                _bitsLeft -= 8;
            }

            while (_start < 0)
            {
                _start += BufferSize;
            }
        }

        public void FlushBits()
        {
            if (_bitsLeft < 8)
            {
                SkipBits(_bitsLeft);
            }
        }

        public void Reset()
        {
            _start = 0;
            _end = -1;
            _bitsLeft = 0;
        }

        private static void ThrowReservoirNotEnoughBits()
        {
            throw new System.IO.InvalidDataException("Reservoir did not have enough bytes!");
        }

        private static void ThrowFrameNotEnoughBytes()
        {
            throw new System.IO.InvalidDataException("Frame did not have enough bytes!");
        }
    }
}
