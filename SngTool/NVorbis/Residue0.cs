using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NVorbis
{
    // each channel gets its own pass, one dimension at a time
    internal class Residue0
    {
        private int _channels;
        private int _begin;
        private int _end;
        private int _partitionSize;
        private int _classifications;
        private int _maxStages;

        private Codebook[][] _books;
        private Codebook _classBook;

        private byte[] _cascade;
        private int[] _decodeMap;
        private int[]? _partWordCache;

        [SkipLocalsInit]
        public Residue0(ref VorbisPacket packet, int channels, Codebook[] codebooks)
        {
            Span<byte> bookNums = stackalloc byte[1024];

            // this is pretty well stolen directly from libvorbis...  BSD license
            _begin = (int)packet.ReadBits(24);
            _end = (int)packet.ReadBits(24);
            _partitionSize = (int)packet.ReadBits(24) + 1;
            _classifications = (int)packet.ReadBits(6) + 1;
            _classBook = codebooks[(int)packet.ReadBits(8)];

            byte[] cascade = new byte[_classifications];
            _cascade = cascade;

            int acc = 0;
            for (int i = 0; i < cascade.Length; i++)
            {
                byte low_bits = (byte)packet.ReadBits(3);
                if (packet.ReadBit())
                {
                    cascade[i] = (byte)(packet.ReadBits(5) << 3 | low_bits);
                }
                else
                {
                    cascade[i] = low_bits;
                }
                acc += BitOperations.PopCount(cascade[i]);
            }

            if (acc > bookNums.Length)
                bookNums = new byte[acc];
            else
                bookNums = bookNums.Slice(0, acc);

            for (int i = 0; i < bookNums.Length; i++)
            {
                bookNums[i] = (byte)packet.ReadBits(8);
                if (codebooks[bookNums[i]].MapType == 0) throw new InvalidDataException();
            }

            int entries = _classBook.Entries;
            int dim = _classBook.Dimensions;
            int partvals = 1;
            while (dim > 0)
            {
                partvals *= _classifications;
                if (partvals > entries) throw new InvalidDataException();
                --dim;
            }

            // now the lookups
            _books = new Codebook[_classifications][];

            acc = 0;
            int maxstage = 0;
            int stages;
            for (int j = 0; j < cascade.Length; j++)
            {
                stages = Utils.ilog(cascade[j]);
                _books[j] = new Codebook[stages];
                if (stages > 0)
                {
                    maxstage = Math.Max(maxstage, stages);
                    for (int k = 0; k < stages; k++)
                    {
                        if ((cascade[j] & (1 << k)) > 0)
                        {
                            _books[j][k] = codebooks[bookNums[acc++]];
                        }
                    }
                }
            }
            _maxStages = maxstage;

            _decodeMap = new int[partvals * _classBook.Dimensions];
            for (int j = 0; j < partvals; j++)
            {
                int val = j;
                int mult = partvals / _classifications;
                for (int k = 0; k < _classBook.Dimensions; k++)
                {
                    int deco = val / mult;
                    val -= deco * mult;
                    mult /= _classifications;
                    _decodeMap[j * _classBook.Dimensions + k] = deco;
                }
            }

            _channels = channels;
        }

        public virtual void Decode(
            ref VorbisPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, ReadOnlySpan<float[]> buffers)
        {
            // this is pretty well stolen directly from libvorbis...  BSD license
            int end = _end < blockSize / 2 ? _end : blockSize / 2;
            int n = end - _begin;

            if (n <= 0 || !doNotDecodeChannel.Contains(false))
            {
                return;
            }

            int channels = _channels;
            int[] decodeMap = _decodeMap;
            byte[] cascade = _cascade;
            int partitionCount = n / _partitionSize;

            int partitionWords = (partitionCount + _classBook.Dimensions - 1) / _classBook.Dimensions;
            int cacheLength = channels * partitionWords;

            if (_partWordCache == null || _partWordCache.Length < cacheLength)
                Array.Resize(ref _partWordCache, cacheLength);
            Span<int> partWordCache = _partWordCache.AsSpan(0, cacheLength);

            for (int stage = 0; stage < _maxStages; stage++)
            {
                for (int partitionIdx = 0, entryIdx = 0; partitionIdx < partitionCount; entryIdx++)
                {
                    if (stage == 0)
                    {
                        for (int ch = 0; ch < channels; ch++)
                        {
                            int idx = _classBook.DecodeScalar(ref packet);
                            if (idx >= 0 && idx < decodeMap.Length)
                            {
                                partWordCache[ch * partitionWords + entryIdx] = idx;
                            }
                            else
                            {
                                partitionIdx = partitionCount;
                                stage = _maxStages;
                                break;
                            }
                        }
                    }

                    for (int dimIdx = 0; partitionIdx < partitionCount && dimIdx < _classBook.Dimensions; dimIdx++, partitionIdx++)
                    {
                        int offset = _begin + partitionIdx * _partitionSize;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            int mapIndex = partWordCache[ch * partitionWords + entryIdx] * _classBook.Dimensions;
                            int idx = decodeMap[mapIndex + dimIdx];
                            if ((cascade[idx] & (1 << stage)) != 0)
                            {
                                Codebook book = _books[idx][stage];
                                if (book != null)
                                {
                                    if (WriteVectors(book, ref packet, buffers, ch, offset, _partitionSize))
                                    {
                                        // bad packet...  exit now and try to use what we already have
                                        partitionIdx = partitionCount;
                                        stage = _maxStages;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected virtual bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, ReadOnlySpan<float[]> residues, int channel, int offset, int partitionSize)
        {
            int steps = partitionSize / codebook.Dimensions;
            Span<float> res = residues[channel].AsSpan(offset, steps);
            
            for (int step = 0; step < steps; step++)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                float r = 0;
                ReadOnlySpan<float> lookup = codebook.GetLookup(entry);
                for (int dim = 0; dim < lookup.Length; dim++)
                {
                    r += lookup[dim];
                }
                res[step] += r;
            }
            return false;
        }
    }
}
