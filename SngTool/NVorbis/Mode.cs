using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis
{
    internal struct Mode
    {
        private struct OverlapInfo
        {
            public int PacketStartIndex;
            public int PacketTotalLength;
            public int PacketValidLength;
        }

        private int _channels;
        private bool _blockFlag;
        private int _blockSize;
        private Mapping _mapping;
        private float[][] _windows;
        private OverlapInfo[] _overlapInfo;

        public Mode(ref VorbisPacket packet, int channels, int block0Size, int block1Size, Mapping[] mappings)
        {
            _channels = channels;

            _blockFlag = packet.ReadBit();
            if (0 != packet.ReadBits(32))
            {
                throw new System.IO.InvalidDataException("Mode header had invalid window or transform type!");
            }

            int mappingIdx = (int)packet.ReadBits(8);
            if (mappingIdx >= mappings.Length)
            {
                throw new System.IO.InvalidDataException("Mode header had invalid mapping index!");
            }
            _mapping = mappings[mappingIdx];

            if (_blockFlag)
            {
                _blockSize = block1Size;
                _windows = new float[][]
                {
                    CalcWindow(block0Size, block1Size, block0Size),
                    CalcWindow(block1Size, block1Size, block0Size),
                    CalcWindow(block0Size, block1Size, block1Size),
                    CalcWindow(block1Size, block1Size, block1Size),
                };
                _overlapInfo = new OverlapInfo[]
                {
                    CalcOverlap(block0Size, block1Size, block0Size),
                    CalcOverlap(block1Size, block1Size, block0Size),
                    CalcOverlap(block0Size, block1Size, block1Size),
                    CalcOverlap(block1Size, block1Size, block1Size),
                };
            }
            else
            {
                _blockSize = block0Size;
                _windows = new float[][]
                {
                    CalcWindow(block0Size, block0Size, block0Size),
                };
                _overlapInfo = Array.Empty<OverlapInfo>();
            }
        }

        private static float[] CalcWindow(int prevBlockSize, int blockSize, int nextBlockSize)
        {
            float[] array = new float[blockSize];
            Span<float> span = array;

            int left = prevBlockSize / 2;
            int wnd = blockSize;
            int right = nextBlockSize / 2;

            int leftbegin = wnd / 4 - left / 2;
            int rightbegin = wnd - wnd / 4 - right / 2;

            Span<float> leftSpan = span.Slice(leftbegin, left);
            for (int i = 0; i < leftSpan.Length; i++)
            {
                double x = Math.Sin((i + .5) / left * Math.PI / 2);
                x *= x;
                leftSpan[i] = (float)Math.Sin(x * Math.PI / 2);
            }

            span[(leftbegin + left)..rightbegin].Fill(1.0f);

            Span<float> rightSpan = span.Slice(rightbegin, right);
            for (int i = 0; i < rightSpan.Length; i++)
            {
                double x = Math.Sin((right - i - .5) / right * Math.PI / 2);
                x *= x;
                rightSpan[i] = (float)Math.Sin(x * Math.PI / 2);
            }

            return array;
        }

        private static OverlapInfo CalcOverlap(int prevBlockSize, int blockSize, int nextBlockSize)
        {
            int leftOverlapHalfSize = prevBlockSize / 4;
            int rightOverlapHalfSize = nextBlockSize / 4;

            int packetStartIndex = blockSize / 4 - leftOverlapHalfSize;
            int packetTotalLength = blockSize / 4 * 3 + rightOverlapHalfSize;
            int packetValidLength = packetTotalLength - rightOverlapHalfSize * 2;

            return new OverlapInfo
            {
                PacketStartIndex = packetStartIndex,
                PacketValidLength = packetValidLength,
                PacketTotalLength = packetTotalLength,
            };
        }

        private bool GetPacketInfo(ref VorbisPacket packet, out int windowIndex, out int packetStartIndex, out int packetValidLength, out int packetTotalLength)
        {
            if (packet.IsShort)
            {
                windowIndex = 0;
                packetStartIndex = 0;
                packetValidLength = 0;
                packetTotalLength = 0;
                return false;
            }

            if (_blockFlag)
            {
                bool prevFlag = packet.ReadBit();
                bool nextFlag = packet.ReadBit();

                windowIndex = (prevFlag ? 1 : 0) + (nextFlag ? 2 : 0);

                ref OverlapInfo overlapInfo = ref _overlapInfo[windowIndex];
                packetStartIndex = overlapInfo.PacketStartIndex;
                packetValidLength = overlapInfo.PacketValidLength;
                packetTotalLength = overlapInfo.PacketTotalLength;
            }
            else
            {
                windowIndex = 0;
                packetStartIndex = 0;
                packetValidLength = _blockSize / 2;
                packetTotalLength = _blockSize;
            }

            return true;
        }

        public unsafe bool Decode(
            ref VorbisPacket packet,
            float[][] buffers,
            out int packetStartIndex,
            out int packetValidLength,
            out int packetTotalLength)
        {
            if (GetPacketInfo(
                ref packet,
                out int windowIndex,
                out packetStartIndex,
                out packetValidLength,
                out packetTotalLength))
            {
                _mapping.DecodePacket(ref packet, _blockSize, _channels, buffers);

                int length = _blockSize;
                Span<float> windowSpan = _windows[windowIndex].AsSpan(0, length);

                for (int ch = 0; ch < _channels; ch++)
                {
                    Span<float> bufferSpan = buffers[ch].AsSpan(0, length);

                    ref float buffer = ref MemoryMarshal.GetReference(bufferSpan);
                    ref float window = ref MemoryMarshal.GetReference(windowSpan);
                    int i = 0;

                    if (Vector.IsHardwareAccelerated)
                    {
                        for (; i + Vector<float>.Count <= length; i += Vector<float>.Count)
                        {
                            Vector<float> v_buffer = VectorHelper.LoadUnsafe(ref buffer, i);
                            Vector<float> v_window = VectorHelper.LoadUnsafe(ref window, i);

                            Vector<float> result = v_buffer * v_window;
                            result.StoreUnsafe(ref buffer, i);
                        }
                    }

                    for (; i < length; i++)
                    {
                        Unsafe.Add(ref buffer, i) *= Unsafe.Add(ref window, i);
                    }
                }
                return true;
            }
            return false;
        }

        public int GetPacketSampleCount(ref VorbisPacket packet)
        {
            GetPacketInfo(ref packet, out _, out int packetStartIndex, out int packetValidLength, out _);
            return packetValidLength - packetStartIndex;
        }
    }
}
