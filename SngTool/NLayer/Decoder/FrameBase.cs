using System;
using System.Diagnostics;

namespace NLayer.Decoder
{
    public abstract class FrameBase
    {
        private static int _totalAllocation = 0;

        public static int TotalAllocation =>
            System.Threading.Interlocked.CompareExchange(ref _totalAllocation, 0, 0);

        public long Offset { get; private set; }
        public int Length { get; set; }

        private MpegStreamReader? _reader;
        private byte[]? _savedBuffer;

        protected FrameBase()
        {
        }

        /// <summary>
        /// Called to validate the frame header
        /// </summary>
        /// <returns>The length of the frame, or -1 if frame is invalid</returns>
        protected abstract int ValidateFrameHeader();

        public bool ValidateFrameHeader(long offset, MpegStreamReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Offset = offset;

            int length = ValidateFrameHeader();
            if (length > 0)
            {
                Length = length;
                return true;
            }
            return false;
        }

        protected int Read(int offset, Span<byte> destination)
        {
            if (_savedBuffer != null)
            {
                ReadOnlySpan<byte> source = _savedBuffer.AsSpan(offset);
                source = source.Slice(0, Math.Min(source.Length, destination.Length));
                source.CopyTo(destination);
                return source.Length;
            }
            else
            {
                Debug.Assert(_reader != null);
                return _reader.Read(Offset + offset, destination);
            }
        }

        protected int ReadByte(int offset)
        {
            if (_savedBuffer != null)
            {
                if (offset >= _savedBuffer.Length)
                    return -1;
                return _savedBuffer[offset];
            }
            else
            {
                Debug.Assert(_reader != null);
                return _reader.ReadByte(Offset + offset);
            }
        }

        public void SaveBuffer()
        {
            Debug.Assert(_reader != null);

            _savedBuffer = new byte[Length];
            _reader.Read(Offset, _savedBuffer);
            System.Threading.Interlocked.Add(ref _totalAllocation, Length);
        }

        public void ClearBuffer()
        {
            System.Threading.Interlocked.Add(ref _totalAllocation, -Length);
            _savedBuffer = null;
        }

        /// <summary>
        /// Called when the stream is not seekable.
        /// </summary>
        public virtual void Parse()
        {
        }
    }
}
