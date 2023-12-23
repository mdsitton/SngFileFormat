using System;

namespace NVorbis
{
    public class SeekOutOfRangeException : Exception
    {
        private const string DefaultMessage = "The requested seek position extends beyond the stream.";

        public SeekOutOfRangeException() : base(DefaultMessage)
        {
        }

        public SeekOutOfRangeException(string? message) : base(message ?? DefaultMessage)
        {
        }

        public SeekOutOfRangeException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException)
        {
        }
    }
}
