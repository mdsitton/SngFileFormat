using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NVorbis
{
    // all channels in one pass, interleaved
    internal class Residue2 : Residue0
    {
        private int _channels;

        public Residue2(ref VorbisPacket packet, int channels, Codebook[] codebooks) : base(ref packet, 1, codebooks)
        {
            _channels = channels;
        }

        public override void Decode(
            ref VorbisPacket packet, ReadOnlySpan<bool> doNotDecodeChannel, int blockSize, float[][] buffer)
        {
            // since we're doing all channels in a single pass, the block size has to be multiplied.
            // otherwise this is just a pass-through call
            base.Decode(ref packet, doNotDecodeChannel, blockSize * _channels, buffer);
        }

        protected override bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            uint dimensions = (uint) codebook.Dimensions;
            uint channels = (uint) _channels;
            uint o = (uint) offset / channels;

            ReadOnlySpan<float[]> residues = residue.AsSpan(0, (int) channels);
            ref float res0 = ref MemoryMarshal.GetArrayDataReference(residues[0]);
            ref float res1 = ref channels == 2 ? ref MemoryMarshal.GetArrayDataReference(residue[1]) : ref Unsafe.NullRef<float>();
            ref float lookupTable = ref MemoryMarshal.GetReference(codebook.GetLookupTable());

            for (uint c = 0; c < partitionSize; c += dimensions)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                ref float lookup = ref Unsafe.Add(ref lookupTable, (uint) entry * dimensions);
                
                if (dimensions != 1 && channels == 2)
                {
                    for (uint d = 0; d < dimensions; d += 2, o++)
                    {
                        Unsafe.Add(ref res0, o) += Unsafe.Add(ref lookup, d + 0);
                        Unsafe.Add(ref res1, o) += Unsafe.Add(ref lookup, d + 1);
                    }
                }
                else if (channels == 1)
                {
                    for (uint d = 0; d < dimensions; d += 1, o++)
                    {
                        Unsafe.Add(ref res0, o) += Unsafe.Add(ref lookup, d);
                    }
                }
                else
                {
                    for (uint d = 0; d < dimensions; d += channels, o++)
                    {
                        for (int ch = 0; ch < residues.Length; ch++)
                        {
                            residues[ch][o] += Unsafe.Add(ref lookup, d + (uint) ch);
                        }
                    }
                }
            }
            return false;
        }
    }
}
