using System;
using System.Collections.Generic;
using System.IO;
using NVorbis.Contracts;

namespace NVorbis
{
    // Packed LSP values on dB amplittude and Bark frequency scale.
    // Virtually unused (libvorbis did not use past beta 4). Probably untested.
    internal sealed class Floor0 : IFloor
    {
        private sealed class Data : FloorData
        {
            internal readonly float[] Coeff;
            internal float Amp;

            public Data(float[] coeff)
            {
                Coeff = coeff;
            }

            public override bool ExecuteChannel => (ForceEnergy || Amp > 0f) && !ForceNoEnergy;

            public override void Reset()
            {
                Array.Clear(Coeff);
                Amp = 0;
                ForceEnergy = false;
                ForceNoEnergy = false;
            }
        }

        private int _order, _rate, _bark_map_size, _ampBits, _ampOfs, _ampDiv;
        private Codebook[] _books;
        private int _bookBits;
        private Dictionary<int, float[]> _wMap;
        private Dictionary<int, int[]> _barkMaps;

        public Floor0(ref VorbisPacket packet, int block0Size, int block1Size, Codebook[] codebooks)
        {
            // this is pretty well stolen directly from libvorbis...  BSD license
            _order = (int)packet.ReadBits(8);
            _rate = (int)packet.ReadBits(16);
            _bark_map_size = (int)packet.ReadBits(16);
            _ampBits = (int)packet.ReadBits(6);
            _ampOfs = (int)packet.ReadBits(8);
            _books = new Codebook[(int)packet.ReadBits(4) + 1];

            if (_order < 1 || _rate < 1 || _bark_map_size < 1 || _books.Length == 0) throw new InvalidDataException();

            _ampDiv = (1 << _ampBits) - 1;

            for (int i = 0; i < _books.Length; i++)
            {
                int num = (int)packet.ReadBits(8);
                if (num < 0 || num >= codebooks.Length) throw new InvalidDataException();
                Codebook book = codebooks[num];

                if (book.MapType == 0 || book.Dimensions < 1) throw new InvalidDataException();

                _books[i] = book;
            }
            _bookBits = Utils.ilog(_books.Length);

            _barkMaps = new Dictionary<int, int[]>
            {
                [block0Size] = SynthesizeBarkCurve(block0Size / 2),
                [block1Size] = SynthesizeBarkCurve(block1Size / 2)
            };

            _wMap = new Dictionary<int, float[]>
            {
                [block0Size] = SynthesizeWDelMap(block0Size / 2),
                [block1Size] = SynthesizeWDelMap(block1Size / 2)
            };
        }

        public FloorData CreateFloorData()
        {
            return new Data(new float[_order + 1]);
        }

        private int[] SynthesizeBarkCurve(int n)
        {
            float scale = _bark_map_size / ToBARK(_rate / 2);

            int[] map = new int[n + 1];

            for (int i = 0; i < map.Length - 2; i++)
            {
                map[i] = Math.Min(_bark_map_size - 1, (int)Math.Floor(ToBARK((_rate / 2f) / n * i) * scale));
            }
            map[n] = -1;
            return map;
        }

        private static float ToBARK(double lsp)
        {
            return (float)(13.1 * Math.Atan(0.00074 * lsp) + 2.24 * Math.Atan(0.0000000185 * lsp * lsp) + .0001 * lsp);
        }

        private float[] SynthesizeWDelMap(int n)
        {
            float wdel = (float)(Math.PI / _bark_map_size);

            float[] map = new float[n];
            for (int i = 0; i < map.Length; i++)
            {
                map[i] = 2f * MathF.Cos(wdel * i);
            }
            return map;
        }

        public void Unpack(ref VorbisPacket packet, FloorData floorData, int blockSize, int channel)
        {
            Data data = (Data)floorData;

            data.Amp = packet.ReadBits(_ampBits);
            if (data.Amp > 0f)
            {
                // this is pretty well stolen directly from libvorbis...  BSD license
                Array.Clear(data.Coeff, 0, data.Coeff.Length);

                data.Amp = data.Amp / _ampDiv * _ampOfs;

                uint bookNum = (uint)packet.ReadBits(_bookBits);
                if (bookNum >= (uint)_books.Length)
                {
                    // we ran out of data or the packet is corrupt...  0 the floor and return
                    data.Amp = 0;
                    return;
                }
                Codebook book = _books[bookNum];

                // first, the book decode...
                for (int i = 0; i < _order;)
                {
                    int entry = book.DecodeScalar(ref packet);
                    if (entry == -1)
                    {
                        // we ran out of data or the packet is corrupt...  0 the floor and return
                        data.Amp = 0;
                        return;
                    }

                    ReadOnlySpan<float> lookup = book.GetLookup(entry);
                    for (int j = 0; i < _order && j < lookup.Length; j++, i++)
                    {
                        data.Coeff[i] = lookup[j];
                    }
                }

                // then, the "averaging"
                float last = 0f;
                for (int j = 0; j < _order;)
                {
                    for (int k = 0; j < _order && k < book.Dimensions; j++, k++)
                    {
                        data.Coeff[j] += last;
                    }
                    last = data.Coeff[j - 1];
                }
            }
        }

        public void Apply(FloorData floorData, int blockSize, float[] residue)
        {
            Data data = (Data)floorData;
            int n = blockSize / 2;

            if (data.Amp > 0f)
            {
                // this is pretty well stolen directly from libvorbis...  BSD license
                int[] barkMap = _barkMaps[blockSize];
                float[] wMap = _wMap[blockSize];

                Span<float> coeff = data.Coeff.AsSpan(0, _order);
                for (int j = 0; j < coeff.Length; j++)
                {
                    coeff[j] = 2f * MathF.Cos(coeff[j]);
                }

                int i = 0;
                while (i < n)
                {
                    int j;
                    int k = barkMap[i];
                    float p = .5f;
                    float q = .5f;
                    float w = wMap[k];
                    for (j = 1; j < _order; j += 2)
                    {
                        q *= w - data.Coeff[j - 1];
                        p *= w - data.Coeff[j];
                    }
                    if (j == _order)
                    {
                        // odd order filter; slightly assymetric
                        q *= w - data.Coeff[j - 1];
                        p *= p * (4f - w * w);
                        q *= q;
                    }
                    else
                    {
                        // even order filter; still symetric
                        p *= p * (2f - w);
                        q *= q * (2f + w);
                    }

                    // calc the dB of this bark section
                    q = data.Amp / MathF.Sqrt(p + q) - _ampOfs;

                    // now convert to a linear sample multiplier
                    q = MathF.Exp(q * 0.11512925f);

                    residue[i] *= q;

                    while (barkMap[++i] == k) residue[i] *= q;
                }
            }
            else
            {
                Array.Clear(residue, 0, n);
            }
        }
    }
}
