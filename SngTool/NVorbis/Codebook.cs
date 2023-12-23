using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NVorbis.Contracts;

namespace NVorbis
{
    internal sealed class Codebook
    {
        private int[] _lengths;
        private float[] _lookupTable;
        private HuffmanListNode[] _overflowList;
        private HuffmanListNode[] _prefixList;
        private int _prefixBitLength;
        private int _maxBits;

        public Codebook(ref VorbisPacket packet)
        {
            // first, check the sync pattern
            ulong chkVal = packet.ReadBits(24);
            if (chkVal != 0x564342UL) throw new InvalidDataException("Book header had invalid signature!");

            // get the counts
            Dimensions = (int)packet.ReadBits(16);
            Entries = (int)packet.ReadBits(24);

            // init the storage
            _lengths = new int[Entries];

            Huffman huffman = InitTree(ref packet, out _maxBits);
            _prefixList = huffman.PrefixTree;
            _prefixBitLength = huffman.TableBits;
            _overflowList = huffman.OverflowList;

            _lookupTable = InitLookupTable(ref packet);
        }

        private Huffman InitTree(ref VorbisPacket packet, out int maxBits)
        {
            bool sparse;
            int total = 0;

            int maxLen;
            if (packet.ReadBit())
            {
                // ordered
                int len = (int)packet.ReadBits(5) + 1;
                for (int i = 0; i < Entries;)
                {
                    int cnt = (int)packet.ReadBits(Utils.ilog(Entries - i));

                    while (--cnt >= 0)
                    {
                        _lengths[i++] = len;
                    }

                    ++len;
                }
                total = 0;
                sparse = false;
                maxLen = len;
            }
            else
            {
                // unordered
                maxLen = -1;
                sparse = packet.ReadBit();
                for (int i = 0; i < Entries; i++)
                {
                    if (!sparse || packet.ReadBit())
                    {
                        _lengths[i] = (int)packet.ReadBits(5) + 1;
                        ++total;
                    }
                    else
                    {
                        // mark the entry as unused
                        _lengths[i] = -1;
                    }
                    if (_lengths[i] > maxLen)
                    {
                        maxLen = _lengths[i];
                    }
                }
            }

            // figure out the maximum bit size; if all are unused, don't do anything else
            maxBits = maxLen;
            if (maxLen <= -1)
            {
                return Huffman.Empty;
            }

            int[]? codewordLengths = null;
            if (sparse && total >= Entries >> 2)
            {
                codewordLengths = new int[Entries];
                Array.Copy(_lengths, codewordLengths, Entries);

                sparse = false;
            }

            int sortedCount;
            // compute size of sorted tables
            if (sparse)
            {
                sortedCount = total;
            }
            else
            {
                sortedCount = 0;
            }

            int[]? values = null;
            int[]? codewords = null;
            if (!sparse)
            {
                codewords = new int[Entries];
            }
            else if (sortedCount != 0)
            {
                codewordLengths = new int[sortedCount];
                codewords = new int[sortedCount];
                values = new int[sortedCount];
            }

            if (!ComputeCodewords(sparse, codewords, codewordLengths, _lengths.AsSpan(0, Entries), values))
                throw new InvalidDataException();

            Debug.Assert(codewords != null);

            int[] lengthList = codewordLengths ?? _lengths;
            Huffman huffman = Huffman.GenerateTable(values, lengthList, codewords);

            return huffman;
        }

        [SkipLocalsInit]
        private bool ComputeCodewords(bool sparse, int[]? codewords, int[]? codewordLengths, ReadOnlySpan<int> len, int[]? values)
        {
            Span<uint> available = stackalloc uint[32];
            available.Clear();
            int i, k, m = 0;

            for (k = 0; k < len.Length; ++k) if (len[k] > 0) break;
            if (k == len.Length) return true;

            Debug.Assert(codewords != null);
            if (sparse)
            {
                Debug.Assert(codewordLengths != null);
                Debug.Assert(values != null);
            }

            AddEntry(0, k, m++, len[k]);

            for (i = 1; i <= len[k]; ++i) available[i] = 1U << (32 - i);

            for (i = k + 1; i < len.Length; ++i)
            {
                uint res;
                int z = len[i], y;
                if (z <= 0) continue;

                while (z > 0 && available[z] == 0) --z;
                if (z == 0) return false;
                res = available[z];
                available[z] = 0;
                AddEntry(Utils.BitReverse(res), i, m++, len[i]);

                if (z != len[i])
                {
                    for (y = len[i]; y > z; --y)
                    {
                        available[y] = res + (1U << (32 - y));
                    }
                }
            }

            return true;

            void AddEntry(uint huffCode, int symbol, int count, int len)
            {
                if (sparse)
                {
                    codewords[count] = (int)huffCode;
                    codewordLengths![count] = len;
                    values![count] = symbol;
                }
                else
                {
                    codewords[symbol] = (int)huffCode;
                }
            }
        }

        private float[] InitLookupTable(ref VorbisPacket packet)
        {
            MapType = (int)packet.ReadBits(4);
            if (MapType == 0)
                return Array.Empty<float>();

            float minValue = Utils.ConvertFromVorbisFloat32((uint)packet.ReadBits(32));
            float deltaValue = Utils.ConvertFromVorbisFloat32((uint)packet.ReadBits(32));
            int valueBits = (int)packet.ReadBits(4) + 1;
            bool sequence_p = packet.ReadBit();

            int entries = Entries;
            int dimensions = Dimensions;
            int lookupValueCount = entries * dimensions;
            float[] lookupTable = new float[lookupValueCount];

            if (MapType == 1)
            {
                lookupValueCount = lookup1_values();
            }

            ushort[] multiplicands = new ushort[lookupValueCount];
            for (int i = 0; i < multiplicands.Length; i++)
            {
                multiplicands[i] = (ushort)packet.ReadBits(valueBits);
            }
            
            // now that we have the initial data read in, calculate the entry tree
            if (MapType == 1)
            {
                for (int idx = 0; idx < entries; idx++)
                {
                    float last = 0f;
                    uint idxDiv = 1;
                    Span<float> dimLookup = lookupTable.AsSpan(idx * dimensions, dimensions);

                    for (int i = 0; i < dimensions; i++)
                    {
                        uint moff = (uint)idx / idxDiv % (uint)multiplicands.Length;
                        float value = multiplicands[moff] * deltaValue + minValue + last;
                        dimLookup[i] = value;

                        if (sequence_p) last = value;

                        idxDiv *= (uint)multiplicands.Length;
                    }
                }
            }
            else
            {
                for (int idx = 0; idx < entries; idx++)
                {
                    float last = 0f;
                    ReadOnlySpan<ushort> muls = multiplicands.AsSpan(idx * dimensions, dimensions);
                    Span<float> dimLookup = lookupTable.AsSpan(idx * dimensions, dimensions);

                    for (int i = 0; i < dimensions; i++)
                    {
                        float value = muls[i] * deltaValue + minValue + last;
                        dimLookup[i] = value;

                        if (sequence_p) last = value;
                    }
                }
            }

            return lookupTable;
        }

        private int lookup1_values()
        {
            int r = (int)Math.Floor(Math.Exp(Math.Log(Entries) / Dimensions));

            if (Math.Floor(Math.Pow(r + 1, Dimensions)) <= Entries) ++r;

            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int DecodeScalar(ref VorbisPacket packet)
        {
            ulong data = packet.TryPeekBits(_prefixBitLength, out int bitsRead);
            if (bitsRead != 0)
            {
                // try to get the value from the prefix list...
                ref HuffmanListNode node = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_prefixList), (nuint)data);
                if (node.Length != 0)
                {
                    packet.SkipBits(node.Length);
                    return node.Value;
                }
            }
            // nope, not possible... run through the overflow nodes
            return DecodeOverflowScalar(ref packet);
        }

        private int DecodeOverflowScalar(ref VorbisPacket packet)
        {
            int data = (int)packet.TryPeekBits(_maxBits, out int bitsRead);
            if (bitsRead != 0)
            {
                HuffmanListNode[] overflowList = _overflowList;
                for (int i = 0; i < overflowList.Length; i++)
                {
                    ref HuffmanListNode node = ref overflowList[i];
                    if (node.Bits == (data & node.Mask))
                    {
                        packet.SkipBits(node.Length);
                        return node.Value;
                    }
                }
            }
            return -1;
        }

        public ReadOnlySpan<float> GetLookup(int entry)
        {
            return _lookupTable.AsSpan(entry * Dimensions, Dimensions);
        }

        public ReadOnlySpan<float> GetLookupTable()
        {
            return _lookupTable.AsSpan();
        }

        public int Dimensions { get; private set; }

        public int Entries { get; private set; }

        public int MapType { get; private set; }
    }
}
