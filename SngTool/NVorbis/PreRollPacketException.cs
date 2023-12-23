using System;

namespace NVorbis
{
    public class PreRollPacketException : Exception
    {
        private const string DefaultMessage = "Could not read pre-roll packet. Try seeking again prior to reading more samples.";

        public PreRollPacketException() : base(DefaultMessage)
        {
        }

        public PreRollPacketException(string? message) : base(message ?? DefaultMessage)
        {
        }

        public PreRollPacketException(string? message, Exception? innerException) : base(message ?? DefaultMessage, innerException)
        {
        }
    }
}
