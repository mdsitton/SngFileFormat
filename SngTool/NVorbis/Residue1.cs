using System;

namespace NVorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    internal sealed class Residue1 : Residue0
    {
        public Residue1(ref VorbisPacket packet, int channels, Codebook[] codebooks) : base(ref packet, channels, codebooks)
        {
        }

        protected override bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, ReadOnlySpan<float[]> residues, int channel, int offset, int partitionSize)
        {
            for (int i = 0; i < partitionSize;)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                ReadOnlySpan<float> lookup = codebook.GetLookup(entry);
                Span<float> res = residues[channel].AsSpan(offset + i, lookup.Length);

                for (int j = 0; j < lookup.Length; j++)
                {
                    res[j] += lookup[j];
                }

                i += lookup.Length;
            }

            return false;
        }
    }
}
