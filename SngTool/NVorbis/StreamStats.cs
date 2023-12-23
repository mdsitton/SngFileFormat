using NVorbis.Contracts;

namespace NVorbis
{
    internal unsafe class StreamStats : IStreamStats
    {
        private int _sampleRate;

        private PacketInt2 _packetBits;
        private PacketInt2 _packetSamples;
        private int _packetIndex;

        private long _totalSamples;
        private long _audioBits;
        private long _headerBits;
        private long _containerBits;
        private long _wasteBits;

        private object _lock = new();
        private int _packetCount;

        public int EffectiveBitRate
        {
            get
            {
                long samples, bits;
                lock (_lock)
                {
                    samples = _totalSamples;
                    bits = _audioBits + _headerBits + _containerBits + _wasteBits;
                }
                if (samples > 0)
                {
                    return (int)(((double)bits / samples) * _sampleRate);
                }
                return 0;
            }
        }

        public int InstantBitRate
        {
            get
            {
                int samples, bits;
                lock (_lock)
                {
                    bits = _packetBits.Buffer[0] + _packetBits.Buffer[1];
                    samples = _packetSamples.Buffer[0] + _packetSamples.Buffer[1];
                }
                if (samples > 0)
                {
                    return (int)(((double)bits / samples) * _sampleRate);
                }
                return 0;
            }
        }

        public long ContainerBits => _containerBits;

        public long OverheadBits => _headerBits;

        public long AudioBits => _audioBits;

        public long WasteBits => _wasteBits;

        public int PacketCount => _packetCount;

        public void ResetStats()
        {
            lock (_lock)
            {
                _packetBits = default;
                _packetSamples = default;
                _packetIndex = 0;
                _packetCount = 0;
                _audioBits = 0;
                _totalSamples = 0;
                _headerBits = 0;
                _containerBits = 0;
                _wasteBits = 0;
            }
        }

        internal void SetSampleRate(int sampleRate)
        {
            lock (_lock)
            {
                _sampleRate = sampleRate;

                ResetStats();
            }
        }

        internal void AddPacket(int samples, int bits, int waste, int container)
        {
            lock (_lock)
            {
                if (samples >= 0)
                {
                    // audio packet
                    _audioBits += bits;
                    _wasteBits += waste;
                    _containerBits += container;
                    _totalSamples += samples;
                    _packetBits.Buffer[_packetIndex] = bits + waste;
                    _packetSamples.Buffer[_packetIndex] = samples;

                    if (++_packetIndex == 2)
                    {
                        _packetIndex = 0;
                    }
                }
                else
                {
                    // header packet
                    _headerBits += bits;
                    _wasteBits += waste;
                    _containerBits += container;
                }
            }
        }

        private struct PacketInt2
        {
            public fixed int Buffer[2];
        }
    }
}
