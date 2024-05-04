using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text;
using BinaryEx;

namespace SongLib
{
    public class WavFileWriter : Stream
    {
        public const ushort BitsPerSample = 32;
        private const ushort ChannelSize = BitsPerSample / 8; // 8 bits per byte
        public readonly int SampleRate;
        public readonly ushort Channels;
        public readonly long TotalSamples;
        public readonly long TotalSize;
        public uint SamplesWritten = 0;

        public static long CalculateSizeEstimate(long totalSamples)
        {
            return (totalSamples * ChannelSize) + 44;
        }

        public delegate int IngestSamplesDelegate(Span<float> audioSamples);

        private IngestSamplesDelegate ingestCallback;
        private Func<long> remainingCallback;

        private byte[] header = new byte[44];
        private long streamPos = 0;

        public WavFileWriter(IngestSamplesDelegate ingestCallback, Func<long> remainingCallback, int sampleRate, ushort channels, long totalSamples)
        {
            Channels = channels;
            TotalSamples = totalSamples;
            SampleRate = sampleRate;
            this.ingestCallback = ingestCallback;
            this.remainingCallback = remainingCallback;

            TotalSize = CalculateSizeEstimate(totalSamples);

            Span<byte> headerSpan = new Span<byte>(header);

            // write file header
            CreateWavHeader(headerSpan, sampleRate, BitsPerSample, channels, (int)TotalSize);
        }

        private static void WriteFourCC(Span<byte> buffer, ref int offset, string fourCC)
        {
            if (fourCC.Length != 4)
            {
                throw new ArgumentException("The length of the fourCC string must be exactly 4 characters.");
            }

            if (buffer.Length < offset + 4)
            {
                throw new ArgumentException("The buffer is too small to write the fourCC string.");
            }

            offset += Encoding.ASCII.GetBytes(fourCC, buffer.Slice(offset, 4));
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

        public bool Completed => SamplesWritten >= TotalSamples;
        public int RemainingSamples => (int)(TotalSamples - SamplesWritten);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => TotalSize;

        public override long Position { get => streamPos; set => throw new NotImplementedException(); }

        private float[] sampleBuffer = new float[8192];

        private bool HasExaustedSouce => remainingCallback() == 0;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (streamPos < 44)
            {
                int headerBytes = Math.Min(44, count);
                Array.Copy(header, streamPos, buffer, offset, headerBytes);
                streamPos += headerBytes;
                return headerBytes;
            }
            else if (!HasExaustedSouce && SamplesWritten < TotalSamples)
            {
                int samplesToRead = Math.Min(RemainingSamples, count / ChannelSize);

                int maxBufferSamples = sampleBuffer.Length;

                int injestCount = samplesToRead / maxBufferSamples;

                int samplesRemaining = samplesToRead;
                while (samplesRemaining > 0 && !HasExaustedSouce)
                {
                    int sampleCount = Math.Min(maxBufferSamples, samplesRemaining);
                    int samplesRead = ingestCallback.Invoke(sampleBuffer.AsSpan(0, sampleCount));

                    if (samplesRead == 0)
                    {
                        // Console.WriteLine($"No samples read {samplesRemaining} samples but {remainingCallback()} samples remaining in the source.");
                        break;
                    }

                    var endPos = streamPos + samplesRead;

                    // If end pos too long clamp to max size
                    // typically the last samples will be empty for most music anyways
                    if (endPos > TotalSize)
                    {
                        Console.WriteLine($"End pos {endPos} is greater than total size {TotalSize} clamping to total size.");
                        var validSampleCount = (TotalSize - streamPos) / ChannelSize;
                        samplesRead = (int)validSampleCount;
                    }
                    samplesRemaining -= samplesRead;
                    SamplesWritten += (uint)samplesRead;
                    // Console.WriteLine($"Writing {samplesRead} samples of total {count / ChannelSize} to output");

                    buffer.WriteCountLE<float>(ref offset, sampleBuffer.AsSpan(0, samplesRead));

                    if (endPos > TotalSize)
                    {
                        break;
                    }
                }

                if (HasExaustedSouce && RemainingSamples > 0)
                {
                    Console.WriteLine($"Exausted source but still have {samplesRemaining} samples remaining in this read but {RemainingSamples} samples overall. Filling with 0s.");
                    for (int i = 0; i < samplesRemaining; i++)
                    {
                        buffer.WriteFloatLE(ref offset, 0);
                    }
                }
                else if (HasExaustedSouce)
                {
                    return 0;
                }

                int byteCount = (samplesToRead - samplesRemaining) * ChannelSize;
                streamPos += byteCount;

                return byteCount;
            }
            return 0;
        }

        // Required stream methods that are not implemented
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}