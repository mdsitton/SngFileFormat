using System;
using System.IO;
using BinaryEx;
using System.Text;

public enum OggEncoding
{
    Vorbis,
    Opus,
    Flac
}

public static class OggParser
{
    private const string OggStr = "OggS";
    private const string VorbisStr = "vorbis";
    private const string OpusHeadStr = "OpusHead";
    private const string FlacStr = "FLAC";

    /// <summary>
    /// Determines whether the given stream is an Ogg file by comparing the file header with the Ogg magic number.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>true if the stream is an Ogg file; otherwise, false.</returns>
    public static bool IsOggFile(Stream stream)
    {
        var position = stream.Position;
        Span<byte> headerBytes = stackalloc byte[4];
        stream.Read(headerBytes);

        Span<byte> oggSBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(OggStr, oggSBytes);

        return headerBytes.SequenceEqual(oggSBytes);
    }

    /// <summary>
    /// Determines whether the given stream is encoded in the specified Ogg format.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <param name="format">The Ogg encoding format to compare with.</param>
    /// <returns>true if the stream is encoded in the specified Ogg format; otherwise, false.</returns>
    public static bool IsOggEncoding(Stream stream, OggEncoding format, string fileName)
    {
        var position = stream.Position;
        try
        {
            var isOgg = IsOggFile(stream);
            Console.WriteLine($"Is ogg: {isOgg} {format} {fileName}");
            if (isOgg)
            {
                var version = stream.ReadByte();
                var headerType = stream.ReadByte();
                var granulePosition = stream.ReadInt64LE();
                var bitStreamSerial = stream.ReadInt32LE();
                var pageSequenceNumber = stream.ReadInt32LE();
                var checksum = stream.ReadUInt32LE();
                var pageSegments = stream.ReadByte();

                Span<byte> segmentLengthTable = stackalloc byte[pageSegments];
                stream.ReadCountLE(segmentLengthTable);

                var totalPageLength = 0;

                for (int i = 0; i < pageSegments; i++)
                {
                    totalPageLength += segmentLengthTable[i];
                }


                if (format == OggEncoding.Vorbis)
                {
                    var packType = stream.ReadByte();
                    Span<byte> vorbisBytes = stackalloc byte[6];
                    Encoding.ASCII.GetBytes(VorbisStr, vorbisBytes);

                    Span<byte> vorbisIdBytes = stackalloc byte[6];
                    stream.ReadCountLE(vorbisIdBytes);
                    Console.WriteLine($"Is vorbis: {vorbisIdBytes.SequenceEqual(vorbisBytes)} {Encoding.ASCII.GetString(vorbisIdBytes)} {fileName}");

                    return vorbisIdBytes.SequenceEqual(vorbisBytes);
                }
                else if (format == OggEncoding.Opus)
                {
                    Span<byte> opusHeadBytes = stackalloc byte[8];
                    Encoding.ASCII.GetBytes(OpusHeadStr, opusHeadBytes);

                    Span<byte> opusIdBytes = stackalloc byte[8];
                    stream.ReadCountLE(opusIdBytes);

                    return opusIdBytes.SequenceEqual(opusHeadBytes);
                }
                else if (format == OggEncoding.Flac)
                {
                    var typeId = stream.ReadByte();
                    Span<byte> flacBytes = stackalloc byte[4];
                    Encoding.ASCII.GetBytes(FlacStr, flacBytes);

                    Span<byte> flacIdBytes = stackalloc byte[4];
                    stream.ReadCountLE(flacIdBytes);

                    return typeId == 0x7F && flacIdBytes.SequenceEqual(flacBytes);
                }
                else
                {
                    return false;
                }

            }
            else
            {
                return false;
            }
        }
        finally
        {
            stream.Position = position;
        }
    }
}