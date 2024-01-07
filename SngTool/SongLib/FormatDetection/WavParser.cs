
using System;
using System.IO;
using System.Text;
using BinaryEx;

public class WavParser
{
    private const int HeaderSize = 44 + 40; // header size plus extra for junk sections
    private const string RiffIdentifier = "RIFF";
    private const string WaveIdentifier = "WAVE";
    private const string FmtIdentifier = "fmt ";
    private const string JunkIdentifier = "JUNK";

    public static bool IsWav(Stream stream, string filePath)
    {
        try
        {
            byte[] header = new byte[HeaderSize];
            int bytesRead = stream.Read(header, 0, HeaderSize);

            int pos = 0;

            // Check if the file starts with the "RIFF" chunk identifier
            Span<byte> riffIdentifierBytes = stackalloc byte[RiffIdentifier.Length];
            pos += Encoding.ASCII.GetBytes(RiffIdentifier, riffIdentifierBytes);
            Span<byte> riffHeaderBytes = header.AsSpan(0, RiffIdentifier.Length);
            if (!riffIdentifierBytes.SequenceEqual(riffHeaderBytes))
            {
                return false;
            }

            // Parse the RIFF header
            int fileSize = header.ReadInt32LE(ref pos);

            // Check if the file format is "WAVE"
            Span<byte> waveIdentifierBytes = stackalloc byte[WaveIdentifier.Length];
            Encoding.ASCII.GetBytes(WaveIdentifier, waveIdentifierBytes);

            Span<byte> wavIdBytes = stackalloc byte[WaveIdentifier.Length];
            header.ReadCountLE(ref pos, wavIdBytes);

            if (!waveIdentifierBytes.SequenceEqual(wavIdBytes))
            {
                return false;
            }

            Span<byte> fmtIdentifierBytes = stackalloc byte[FmtIdentifier.Length];
            Encoding.ASCII.GetBytes(FmtIdentifier, fmtIdentifierBytes);

            Span<byte> junkIdentifierBytes = stackalloc byte[FmtIdentifier.Length];
            Encoding.ASCII.GetBytes(JunkIdentifier, junkIdentifierBytes);

            Span<byte> fmtIdBytes = stackalloc byte[FmtIdentifier.Length];
            header.ReadCountLE(ref pos, fmtIdBytes);

            if (fmtIdBytes.SequenceEqual(junkIdentifierBytes))
            {
                // Skip the junk chunk
                int junkChunkSize = header.ReadInt32LE(ref pos);
                pos += junkChunkSize;
                header.ReadCountLE(ref pos, fmtIdBytes);
            }

            // Parse the fmt chunk
            if (!fmtIdBytes.SequenceEqual(fmtIdentifierBytes))
            {
                return false;
            }

            int fmtChunkSize = header.ReadInt32LE(ref pos);

            // opusenc only supports 16 byte chunk sizes and larger
            // While there is technically a 14 byte chunk in older files, it is not supported by opusenc
            if (fmtChunkSize < 16)
            {
                return false;
            }

            // Parse the WAV header
            ushort audioFormat = header.ReadUInt16LE(ref pos);
            ushort numChannels = header.ReadUInt16LE(ref pos);
            uint sampleRate = header.ReadUInt32LE(ref pos);
            uint byteRate = header.ReadUInt32LE(ref pos);
            ushort blockAlign = header.ReadUInt16LE(ref pos);
            ushort bitsPerSample = header.ReadUInt16LE(ref pos);
            ushort cbSize = 0;

            if (fmtChunkSize >= 18)
            {
                cbSize = header.ReadUInt16LE(ref pos);
            }

            if (audioFormat == 0xFFFEu && fmtChunkSize >= 40) // WAVE_FORMAT_EXTENSIBLE
            {
                var samples = header.ReadUInt16LE(ref pos);
                var channelMask = header.ReadUInt32LE(ref pos);
                int a = header.ReadInt32LE(ref pos);
                short b = header.ReadInt16LE(ref pos);
                short c = header.ReadInt16LE(ref pos);

                Span<byte> guidDtoEBytes = stackalloc byte[8];
                header.ReadCountLE(ref pos, guidDtoEBytes);

                audioFormat = (ushort)a; // first segment of GUID is the audio format
            }

            if (audioFormat == 1 || audioFormat == 3)
            {
                return true;
            }

            return false;
        }
        finally
        {
            stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
