using System;
using System.Buffers.Binary;

namespace NLayer.Decoder
{
    /// <summary>
    /// RIFF header reader
    /// </summary>
    internal sealed class RiffHeaderFrame : FrameBase
    {
        private RiffHeaderFrame()
        {
        }

        protected override int ValidateFrameHeader()
        {
            Span<byte> buf = stackalloc byte[4];

            // we expect this to be the "WAVE" chunk
            if (Read(8, buf) != 4)
                return -1;
            if (buf[0] != 'W' || buf[1] != 'A' || buf[2] != 'V' || buf[3] != 'E')
                return -1;

            // now the "fmt " chunk
            if (Read(12, buf) != 4)
                return -1;
            if (buf[0] != 'f' || buf[1] != 'm' || buf[2] != 't' || buf[3] != ' ')
                return -1;

            // we've found the fmt chunk, so look for the data chunk
            int offset = 16;
            while (true)
            {
                // read the length and seek forward
                if (Read(offset, buf) != 4)
                    return -1;
                offset += 4 + BinaryPrimitives.ReadInt32LittleEndian(buf);

                // get the chunk ID
                if (Read(offset, buf) != 4)
                    return -1;
                offset += 4;

                // if it's not the data chunk, try again
                if (buf[0] == 'd' && buf[1] == 'a' && buf[2] == 't' && buf[3] == 'a')
                    break;
            }

            // ... and now we know exactly where the frame ends
            return offset + 4;
        }

        public static RiffHeaderFrame? TrySync(uint syncMark)
        {
            if (syncMark == 0x52494646U)
                return new RiffHeaderFrame();

            return null;
        }
    }
}
