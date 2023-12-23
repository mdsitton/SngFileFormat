using System;
using System.Buffers.Binary;

namespace NVorbis.Ogg
{
    internal partial struct Crc
    {
        private uint _crc;
        private uint[] _table;

        public static Crc Create()
        {
            return new Crc
            {
                _crc = 0U,
                _table = s_crcTable,
            };
        }

        private static unsafe uint Update(uint* table, uint crc, byte* buffer, nint length)
        {
            byte* end = buffer + length;
            while (((nint)buffer & 7) != 0 && buffer < end)
            {
                crc = table[((byte)crc) ^ *buffer++] ^ (crc >> 8);
            }

            uint* table1 = table + 1 * TableLength;
            uint* table2 = table + 2 * TableLength;
            uint* table3 = table + 3 * TableLength;
            uint* table4 = table + 4 * TableLength;
            uint* table5 = table + 5 * TableLength;
            uint* table6 = table + 6 * TableLength;
            uint* table7 = table + 7 * TableLength;

            while (buffer < end - 7)
            {
                ulong value = !BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReverseEndianness(*(ulong*)buffer)
                    : *(ulong*)buffer;
                
                uint high = (uint)(value >> 32);

                crc ^= (uint)value;

                crc = table[(high >> 24) & 0xFF] ^
                      table1[(high >> 16) & 0xFF] ^
                      table2[(high >> 8) & 0xFF] ^
                      table3[high & 0xFF] ^
                      table4[((crc >> 24))] ^
                      table5[((crc >> 16) & 0xFF)] ^
                      table6[((crc >> 8) & 0xFF)] ^
                      table7[(crc & 0xFF)];

                buffer += 8;
            }

            while (buffer < end)
            {
                crc = table[((byte)crc) ^ *buffer++] ^ (crc >> 8);
            }
            return crc;
        }

        public unsafe void Update(ReadOnlySpan<byte> values)
        {
            fixed (uint* table = _table)
            fixed (byte* ptr = values)
            {
                _crc = Update(table, _crc, ptr, values.Length);
            }
        }

        public unsafe void Update(byte value)
        {
            _crc = _table[((byte)_crc) ^ value] ^ (_crc >> 8);
        }

        public bool Test(uint checkCrc)
        {
            return _crc == checkCrc;
        }
    }
}
