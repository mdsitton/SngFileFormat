using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;

namespace SongLib
{
    public static class WavFileWriter
    {
        public static byte[] Get16BitWavData(float[] audioSamples, int sampleRate, int channels)
        {
            int bitsPerSample = 16;
            int blockAlign = channels * (bitsPerSample / 8);
            int numSamples = audioSamples.Length;
            int dataSize = numSamples * blockAlign;
            int totalSize = 44 + dataSize; // 44 bytes for header + dataSize

            byte[] wavData = new byte[totalSize];
            Span<byte> wavDataSpan = wavData;

            // Create WAV header
            CreateWavHeader(wavDataSpan, sampleRate, bitsPerSample, channels, dataSize);

            // Convert float samples to 16-bit samples and write them to the wavDataSpan
            for (int i = 0; i < numSamples; i++)
            {
                short intSample = (short)(audioSamples[i] * short.MaxValue);
                WriteInt16(wavDataSpan, 44 + i * 2, intSample);
            }

            return wavData;
        }

        private static void CreateWavHeader(Span<byte> buffer, int sampleRate, int bitsPerSample, int channels, int dataSize)
        {
            int byteRate = sampleRate * channels * (bitsPerSample / 8);

            // RIFF chunk
            WriteFourCC(buffer, 0, "RIFF");
            WriteInt32(buffer, 4, 36 + dataSize); // Chunk size
            WriteFourCC(buffer, 8, "WAVE");

            // fmt sub-chunk
            WriteFourCC(buffer, 12, "fmt ");
            WriteInt32(buffer, 16, 16); // Sub-chunk size (16 for PCM)
            WriteInt16(buffer, 20, 1); // Audio format (1 for PCM)
            WriteInt16(buffer, 22, (short)channels);
            WriteInt32(buffer, 24, sampleRate);
            WriteInt32(buffer, 28, byteRate);
            WriteInt16(buffer, 32, (short)(channels * (bitsPerSample / 8))); // Block align
            WriteInt16(buffer, 34, (short)bitsPerSample);

            // data sub-chunk
            WriteFourCC(buffer, 36, "data");
            WriteInt32(buffer, 40, dataSize); // Sub-chunk size
        }

        private static void WriteFourCC(Span<byte> buffer, int offset, string fourCC)
        {
            if (fourCC.Length != 4)
            {
                throw new ArgumentException("The length of the fourCC string must be exactly 4 characters.");
            }

            Encoding.ASCII.GetBytes(fourCC).CopyTo(buffer.Slice(offset, 4));
        }

        private static void WriteInt16(Span<byte> buffer, int offset, short value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(buffer.Slice(offset, 2), value);
        }

        private static void WriteInt32(Span<byte> buffer, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(offset, 4), value);
        }
    }
}