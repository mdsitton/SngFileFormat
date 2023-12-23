using System;

namespace NVorbis.Contracts
{
    internal struct HuffmanListNode : IComparable<HuffmanListNode>
    {
        public int Value;
        public int Length;
        public int Bits;
        public int Mask;

        public int CompareTo(HuffmanListNode y)
        {
            int len = Length - y.Length;
            if (len == 0)
            {
                return Bits - y.Bits;
            }
            return len;
        }
    }
}
