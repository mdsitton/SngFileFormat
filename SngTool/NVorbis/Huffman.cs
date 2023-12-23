using System;
using NVorbis.Contracts;

namespace NVorbis
{
    internal struct Huffman
    {
        public static Huffman Empty { get; } = new Huffman()
        {
            TableBits = 0,
            PrefixTree = Array.Empty<HuffmanListNode>(),
            OverflowList = Array.Empty<HuffmanListNode>(),
        };

        private const int MAX_TABLE_BITS = 10;

        public int TableBits { get; private set; }
        public HuffmanListNode[] PrefixTree { get; private set; }
        public HuffmanListNode[] OverflowList { get; private set; }

        public static Huffman GenerateTable(int[]? values, int[] lengthList, int[] codeList)
        {
            HuffmanListNode[] list = new HuffmanListNode[lengthList.Length];

            int maxLen = 0;
            for (int i = 0; i < list.Length; i++)
            {
                list[i] = new HuffmanListNode
                {
                    Value = values != null ? values[i] : i,
                    Length = lengthList[i] <= 0 ? 99999 : lengthList[i],
                    Bits = codeList[i],
                    Mask = (1 << lengthList[i]) - 1,
                };
                if (lengthList[i] > 0 && maxLen < lengthList[i])
                {
                    maxLen = lengthList[i];
                }
            }

            Array.Sort(list, 0, list.Length);

            int tableBits = maxLen > MAX_TABLE_BITS ? MAX_TABLE_BITS : maxLen;

            HuffmanListNode[] prefixList = new HuffmanListNode[1 << tableBits];

            HuffmanListNode[] overflowList = Array.Empty<HuffmanListNode>();
            int overflowIndex = 0;

            for (int i = 0; i < list.Length && list[i].Length < 99999; i++)
            {
                int itemBits = list[i].Length;
                if (itemBits > tableBits)
                {
                    int maxOverflowLength = list.Length - i;
                    if (overflowList.Length < maxOverflowLength)
                        overflowList = new HuffmanListNode[maxOverflowLength];

                    overflowIndex = 0;

                    for (; i < list.Length && list[i].Length < 99999; i++)
                    {
                        overflowList[overflowIndex++] = list[i];
                    }
                }
                else
                {
                    int maxVal = 1 << (tableBits - itemBits);
                    HuffmanListNode item = list[i];
                    for (int j = 0; j < maxVal; j++)
                    {
                        int idx = (j << itemBits) | item.Bits;
                        prefixList[idx] = item;
                    }
                }
            }

            if (overflowIndex < overflowList.Length)
            {
                Array.Resize(ref overflowList, overflowIndex);
            }

            return new Huffman
            {
                TableBits = tableBits,
                PrefixTree = prefixList,
                OverflowList = overflowList,
            };
        }
    }
}
