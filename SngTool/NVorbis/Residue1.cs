
namespace NVorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    internal class Residue1 : Residue0
    {
        public Residue1(ref VorbisPacket packet, int channels, Codebook[] codebooks) : base(ref packet, channels, codebooks)
        {
        }

        protected override bool WriteVectors(
            Codebook codebook, ref VorbisPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            float[] res = residue[channel];

            for (int i = 0; i < partitionSize;)
            {
                int entry = codebook.DecodeScalar(ref packet);
                if (entry == -1)
                {
                    return true;
                }

                System.ReadOnlySpan<float> lookup = codebook.GetLookup(entry);
                for (int j = 0; j < lookup.Length; i++, j++)
                {
                    res[offset + i] += lookup[j];
                }
            }

            return false;
        }
    }
}
