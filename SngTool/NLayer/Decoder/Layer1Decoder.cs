/*
 * NLayer - A C# MPEG1/2/2.5 audio decoder
 * 
 */

namespace NLayer.Decoder
{
    // Layer I is really just a special case of Layer II...  
    // 1 granule, 4 allocation bits per subband, 1 scalefactor per active subband, no grouping
    // That (of course) means we literally have no logic here
    internal class Layer1Decoder : Layer2DecoderBase
    {
        // this is simple: all 32 subbands have a 4-bit allocations, and positive allocation values are {bits per sample} - 1
        private static readonly int[] _rateTable = {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        private static readonly int[][] _allocLookupTable = {
            new int[] { 4, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }
        };

        public Layer1Decoder() : base(_allocLookupTable, 1) 
        {
        }

        public static bool GetCRC(MpegFrame frame, ref uint crc)
        {
            return GetCRC(frame, _rateTable, _allocLookupTable, false, ref crc);
        }

        protected override int[] GetRateTable(MpegFrame frame)
        {
            return _rateTable;
        }

        protected override void ReadScaleFactorSelection(MpegFrame frame, int[][] scfsi, int channels)
        {
            // this is a no-op since the base logic uses "2" as the "has energy" marker
        }
    }
}
