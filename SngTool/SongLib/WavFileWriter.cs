using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;
using BinaryEx;
using System.IO.MemoryMappedFiles;

namespace SongLib
{
    public unsafe class WavFileWriter : IDisposable
    {
        public const ushort BitsPerSample = 32;
        private const ushort ChannelSize = BitsPerSample / 8; // 8 bits per byte
        public readonly int SampleRate;
        public readonly ushort Channels;
        public readonly long TotalSamples;
        public readonly long TotalSize;
        public MemoryMappedFile mappedFile;
        private MemoryMappedViewAccessor accessor;
        public uint SamplesWritten = 0;

        private byte* ptrWrite;


        public static long CalculateSizeEstimate(long totalSamples)
        {
            return (totalSamples * ChannelSize) + 44;
        }

        public WavFileWriter(MemoryMappedFile file, int sampleRate, ushort channels, long totalSamples)
        {
            Channels = channels;
            TotalSamples = totalSamples;
            SampleRate = sampleRate;
            mappedFile = file;

            TotalSize = CalculateSizeEstimate(totalSamples);

            accessor = mappedFile.CreateViewAccessor();
            var mappedFileSize = accessor.SafeMemoryMappedViewHandle.ByteLength;

            if (mappedFileSize < (ulong)TotalSize)
            {
                accessor.Dispose();
                throw new ArgumentException("MemoryMappedFile too small");
            }

            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptrWrite);

            Span<byte> headerSpan = new Span<byte>(ptrWrite, 44);
            // write file header
            CreateWavHeader(headerSpan, sampleRate, BitsPerSample, channels, (int)TotalSize);
            writePos += 44;
        }

        private void CreateWavHeader(Span<byte> buffer, int sampleRate, int bitsPerSample, int channels, int dataSize)
        {
            int byteRate = sampleRate * channels * (bitsPerSample / 8);

            int offset = 0;

            // RIFF chunk
            WriteFourCC(buffer, ref offset, "RIFF");
            buffer.WriteInt32LE(ref offset, 36 + dataSize); // Chunk size
            WriteFourCC(buffer, ref offset, "WAVE");

            // fmt sub-chunk
            WriteFourCC(buffer, ref offset, "fmt "); // Sub-chunk ID
            buffer.WriteInt32LE(ref offset, 16); // Sub-chunk size
            buffer.WriteInt16LE(ref offset, 3); // Audio format (3 for float)
            buffer.WriteInt16LE(ref offset, (short)channels);
            buffer.WriteInt32LE(ref offset, sampleRate);
            buffer.WriteInt32LE(ref offset, byteRate);
            buffer.WriteInt16LE(ref offset, (short)(channels * (bitsPerSample / 8))); // Block align
            buffer.WriteInt16LE(ref offset, (short)bitsPerSample);

            // data sub-chunk
            WriteFourCC(buffer, ref offset, "data");
            buffer.WriteInt32LE(ref offset, dataSize); // Sub-chunk size
        }

        public void Dispose()
        {
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            accessor.Dispose();
        }

        /// <summary>
        /// This returns the maximum samples that we can ingest in one call 
        /// </summary>
        public int MaxChunkSamples => (Array.MaxLength / ChannelSize) & ~1;

        public bool Completed => SamplesWritten >= TotalSamples;

        long writePos = 0;

        public void IngestSamples(Span<float> audioSamples)
        {
            if (audioSamples.Length > MaxChunkSamples)
            {
                throw new ArgumentException("Too many samples, sample count should be lower than MaxChunkSamples");
            }
            int sampleCount = audioSamples.Length;
            int pcmDataSize = sampleCount * ChannelSize;

            var endPos = writePos + pcmDataSize;

            // If end pos too long clamp to max size
            if (endPos > TotalSize)
            {

                pcmDataSize = (int)(TotalSize - writePos) & ~1;
                sampleCount = pcmDataSize / ChannelSize;
                audioSamples = audioSamples.Slice(0, sampleCount);
                Console.WriteLine("Too long, clamping to max");
            }
            Span<byte> wavDataSpan = new Span<byte>(ptrWrite + writePos, pcmDataSize);

            int pos = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(wavDataSpan.Slice(pos, 4), audioSamples[i]);
                pos += 4;
            }
            writePos += pos;
            SamplesWritten += (uint)sampleCount;
        }

        private static void WriteFourCC(Span<byte> buffer, ref int offset, string fourCC)
        {
            if (fourCC.Length != 4)
            {
                throw new ArgumentException("The length of the fourCC string must be exactly 4 characters.");
            }

            Encoding.ASCII.GetBytes(fourCC).CopyTo(buffer.Slice(offset, 4));
            offset += 4;
        }
    }
}