/*
 * NLayer - A C# MPEG1/2/2.5 audio decoder
 * 
 * Portions of this file are courtesy Fluendo, S.A.  They are dual licensed as Ms-PL
 * and under the following license:
 *
 *   Copyright <2005-2012> Fluendo S.A.
 *   
 *   Unless otherwise indicated, Source Code is licensed under MIT license.
 *   See further explanation attached in License Statement (distributed in the file
 *   LICENSE).
 *   
 *   Permission is hereby granted, free of charge, to any person obtaining a copy of
 *   this software and associated documentation files (the "Software"), to deal in
 *   the Software without restriction, including without limitation the rights to
 *   use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 *   of the Software, and to permit persons to whom the Software is furnished to do
 *   so, subject to the following conditions:
 *   
 *   The above copyright notice and this permission notice shall be included in all
 *   copies or substantial portions of the Software.
 *   
 *   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *   SOFTWARE.
 *
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NLayer.Decoder
{
    /// <summary>
    /// Class Implementing Layer III Decoder.
    /// </summary>
    [SkipLocalsInit]
    internal sealed unsafe class Layer3Decoder : LayerDecoderBase
    {
        private const int SSLIMIT = 18;

        private const float sqrt32 = 0.8660254037844385965883020617184229195117950439453125f;

        #region Child Classes

        // This class is based on the Fluendo hybrid logic.
        private class HybridMDCT
        {
            private static float[][] _swin;

            static HybridMDCT()
            {
                _swin = new float[][] { new float[36], new float[36], new float[36], new float[36] };

                int i;

                // type 0
                for (i = 0; i < 36; i++)
                    _swin[0][i] = MathF.Sin(MathF.PI / 36 * (i + 0.5f));

                // type 1
                for (i = 0; i < 18; i++)
                    _swin[1][i] = MathF.Sin(MathF.PI / 36 * (i + 0.5f));
                for (i = 18; i < 24; i++)
                    _swin[1][i] = 1.0f;
                for (i = 24; i < 30; i++)
                    _swin[1][i] = MathF.Sin(MathF.PI / 12 * (i + 0.5f - 18));
                for (i = 30; i < 36; i++)
                    _swin[1][i] = 0.0f;

                // type 3 
                for (i = 0; i < 6; i++)
                    _swin[3][i] = 0.0f;
                for (i = 6; i < 12; i++)
                    _swin[3][i] = MathF.Sin(MathF.PI / 12 * (i + 0.5f - 6));
                for (i = 12; i < 18; i++)
                    _swin[3][i] = 1.0f;
                for (i = 18; i < 36; i++)
                    _swin[3][i] = MathF.Sin(MathF.PI / 36 * (i + 0.5f));

                // type 2
                for (i = 0; i < 12; i++)
                    _swin[2][i] = MathF.Sin(MathF.PI / 12 * (i + 0.5f));
                for (i = 12; i < 36; i++)
                    _swin[2][i] = 0.0f;
            }

            #region Tables

            private static float[] ICos72Table { get; } = {
                5.004763425816599609063928255636710673570632934570312500000000e-01f,
                5.019099187716736798492433990759309381246566772460937500000000e-01f,
                5.043144802900764167574720886477734893560409545898437500000000e-01f,
                5.077133059428725614381505693017970770597457885742187500000000e-01f,
                5.121397571572545714957414020318537950515747070312500000000000e-01f,
                5.176380902050414789528076653368771076202392578125000000000000e-01f,
                5.242645625704053236049162478593643754720687866210937500000000e-01f,
                5.320888862379560269033618169487453997135162353515625000000000e-01f,
                5.411961001461970122150546558259520679712295532226562500000000e-01f,
                5.516889594812458552652856269560288637876510620117187500000000e-01f,
                5.636909734331712051869089918909594416618347167968750000000000e-01f,
                5.773502691896257310588680411456152796745300292968750000000000e-01f,
                5.928445237170802961657045671017840504646301269531250000000000e-01f,
                6.103872943807280293526673631276935338973999023437500000000000e-01f,
                6.302362070051321651931175438221544027328491210937500000000000e-01f,
                6.527036446661392821155800447741057723760604858398437500000000e-01f,
                6.781708524546284921896699415810871869325637817382812500000000e-01f,
                7.071067811865474617150084668537601828575134277343750000000000e-01f,
                7.400936164611303658134033867099788039922714233398437500000000e-01f,
                7.778619134302061643992942663317080587148666381835937500000000e-01f,
                8.213398158522907666068135768000502139329910278320312500000000e-01f,
                8.717233978105488612087015098950359970331192016601562500000000e-01f,
                9.305794983517888807611484480730723589658737182617187500000000e-01f,
                9.999999999999997779553950749686919152736663818359375000000000e-01f,
                1.082840285100100219395358180918265134096145629882812500000000e+00f,
                1.183100791576249255498964885191526263952255249023437500000000e+00f,
                1.306562964876376353728915091778617352247238159179687500000000e+00f,
                1.461902200081543146126250576344318687915802001953125000000000e+00f,
                1.662754761711521034328598034335300326347351074218750000000000e+00f,
                1.931851652578135070115195048856548964977264404296875000000000e+00f,
                2.310113157672649020213384574162773787975311279296875000000000e+00f,
                2.879385241571815523542454684502445161342620849609375000000000e+00f,
                3.830648787770197127855453800293616950511932373046875000000000e+00f,
                5.736856622834929808618653623852878808975219726562500000000000e+00f,
                1.146279281302667207853573927422985434532165527343750000000000e+01f
            };

            #endregion

            private List<float[]> _prevBlock;
            private List<float[]> _nextBlock;

            internal HybridMDCT()
            {
                _prevBlock = new List<float[]>();
                _nextBlock = new List<float[]>();
            }

            internal void Reset()
            {
                _prevBlock.Clear();
                _nextBlock.Clear();
            }

            private void GetPrevBlock(int channel, out float[] prevBlock, out float[] nextBlock)
            {
                while (_prevBlock.Count <= channel)
                    _prevBlock.Add(new float[SSLIMIT * SBLIMIT]);

                while (_nextBlock.Count <= channel)
                    _nextBlock.Add(new float[SSLIMIT * SBLIMIT]);

                prevBlock = _prevBlock[channel];
                nextBlock = _nextBlock[channel];

                // now swap them (see Apply(...) below)
                _nextBlock[channel] = prevBlock;
                _prevBlock[channel] = nextBlock;
            }

            internal void Apply(ref float fsIn, int channel, int blockType, bool doMixed)
            {
                // get the previous & next blocks so we can overlap correctly
                //  NB: we swap each pass so we can add the previous block in a single pass
                GetPrevBlock(channel, out float[] prevBlock, out float[] nextBlock);

                ref float rPrevBlock = ref MemoryMarshal.GetArrayDataReference(prevBlock);
                ref float rNextBlock = ref MemoryMarshal.GetArrayDataReference(nextBlock);

                // now we have a few options for processing blocks...
                int start = 0;
                if (doMixed)
                {
                    // a mixed block always has the first two subbands as blocktype 0
                    LongImpl(ref fsIn, 0, 2, ref rNextBlock, 0);
                    start = 2;
                }

                if (blockType == 2)
                    // this is the only place we care about short blocks
                    ShortImpl(ref fsIn, start, ref rNextBlock);
                else
                    LongImpl(ref fsIn, start, SBLIMIT, ref rNextBlock, blockType);

                // overlap

                for (nint i = 0; i < SSLIMIT * SBLIMIT; i++)
                    Unsafe.Add(ref fsIn, i) += Unsafe.Add(ref rPrevBlock, i);
            }

            private static void LongImpl(ref float fsIn, int sbStart, int sbLimit, ref float nextBlock, int blockType)
            {
                float* imdctResult = stackalloc float[SSLIMIT * 2];

                for (int sb = sbStart, ofs = sbStart * SSLIMIT; sb < sbLimit; sb++)
                {
                    // IMDCT
                    LongIMDCT(ref Unsafe.Add(ref fsIn, ofs), imdctResult);

                    // window
                    ref float win = ref MemoryMarshal.GetArrayDataReference(_swin[blockType]);

                    nint i = 0;
                    for (; i < SSLIMIT; i++)
                        Unsafe.Add(ref fsIn, ofs++) = imdctResult[i] * Unsafe.Add(ref win, i);

                    ofs -= SSLIMIT;

                    for (; i < SSLIMIT * 2; i++)
                        Unsafe.Add(ref nextBlock, ofs++) = imdctResult[i] * Unsafe.Add(ref win, i);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            private static void LongIMDCT(ref float invec, float* outvec)
            {
                float* H = stackalloc float[17];
                float* h = stackalloc float[18];
                float* even = stackalloc float[9];
                float* odd = stackalloc float[9];
                float* even_idct = stackalloc float[9];
                float* odd_idct = stackalloc float[9];

                for (nint i = 0; i < SSLIMIT - 1; i++)
                    H[i] = Unsafe.Add(ref invec, i) + Unsafe.Add(ref invec, i + 1);

                even[0] = invec;
                odd[0] = H[0];

                even[1] = H[0 + 1];
                odd[1] = H[0] + H[0 + 2];

                even[2] = H[2 + 1];
                odd[2] = H[2] + H[2 + 2];

                even[3] = H[4 + 1];
                odd[3] = H[4] + H[4 + 2];

                even[4] = H[6 + 1];
                odd[4] = H[6] + H[6 + 2];

                even[5] = H[8 + 1];
                odd[5] = H[8] + H[8 + 2];

                even[6] = H[10 + 1];
                odd[6] = H[10] + H[10 + 2];

                even[7] = H[12 + 1];
                odd[7] = H[12] + H[12 + 2];

                even[8] = H[14 + 1];
                odd[8] = H[14] + H[14 + 2];

                Imdct_9pt(even, even_idct);
                Imdct_9pt(odd, odd_idct);

                ref float icos72table = ref MemoryMarshal.GetArrayDataReference(ICos72Table);

                odd_idct[0] *= Unsafe.Add(ref icos72table, 4 * 0 + 1);
                odd_idct[1] *= Unsafe.Add(ref icos72table, 4 * 1 + 1);
                odd_idct[2] *= Unsafe.Add(ref icos72table, 4 * 2 + 1);
                odd_idct[3] *= Unsafe.Add(ref icos72table, 4 * 3 + 1);
                odd_idct[4] *= Unsafe.Add(ref icos72table, 4 * 4 + 1);
                odd_idct[5] *= Unsafe.Add(ref icos72table, 4 * 5 + 1);
                odd_idct[6] *= Unsafe.Add(ref icos72table, 4 * 6 + 1);
                odd_idct[7] *= Unsafe.Add(ref icos72table, 4 * 7 + 1);
                odd_idct[8] *= Unsafe.Add(ref icos72table, 4 * 8 + 1);

                h[0] = (even_idct[0] + odd_idct[0]) * Unsafe.Add(ref icos72table, 2 * 0);
                h[1] = (even_idct[1] + odd_idct[1]) * Unsafe.Add(ref icos72table, 2 * 1);
                h[2] = (even_idct[2] + odd_idct[2]) * Unsafe.Add(ref icos72table, 2 * 2);
                h[3] = (even_idct[3] + odd_idct[3]) * Unsafe.Add(ref icos72table, 2 * 3);
                h[4] = (even_idct[4] + odd_idct[4]) * Unsafe.Add(ref icos72table, 2 * 4);
                h[5] = (even_idct[5] + odd_idct[5]) * Unsafe.Add(ref icos72table, 2 * 5);
                h[6] = (even_idct[6] + odd_idct[6]) * Unsafe.Add(ref icos72table, 2 * 6);
                h[7] = (even_idct[7] + odd_idct[7]) * Unsafe.Add(ref icos72table, 2 * 7);
                h[8] = (even_idct[8] + odd_idct[8]) * Unsafe.Add(ref icos72table, 2 * 8);

                h[09] = (even_idct[8] - odd_idct[8]) * Unsafe.Add(ref icos72table, 2 * 09);
                h[10] = (even_idct[7] - odd_idct[7]) * Unsafe.Add(ref icos72table, 2 * 10);
                h[11] = (even_idct[6] - odd_idct[6]) * Unsafe.Add(ref icos72table, 2 * 11);
                h[12] = (even_idct[5] - odd_idct[5]) * Unsafe.Add(ref icos72table, 2 * 12);
                h[13] = (even_idct[4] - odd_idct[4]) * Unsafe.Add(ref icos72table, 2 * 13);
                h[14] = (even_idct[3] - odd_idct[3]) * Unsafe.Add(ref icos72table, 2 * 14);
                h[15] = (even_idct[2] - odd_idct[2]) * Unsafe.Add(ref icos72table, 2 * 15);
                h[16] = (even_idct[1] - odd_idct[1]) * Unsafe.Add(ref icos72table, 2 * 16);
                h[17] = (even_idct[0] - odd_idct[0]) * Unsafe.Add(ref icos72table, 2 * 17);

                // Rearrange the 18 values from the IDCT to the output vector
                outvec[0] = h[9];
                outvec[1] = h[10];
                outvec[2] = h[11];
                outvec[3] = h[12];
                outvec[4] = h[13];
                outvec[5] = h[14];
                outvec[6] = h[15];
                outvec[7] = h[16];
                outvec[8] = h[17];

                outvec[9] = -h[17];
                outvec[10] = -h[16];
                outvec[11] = -h[15];
                outvec[12] = -h[14];
                outvec[13] = -h[13];
                outvec[14] = -h[12];
                outvec[15] = -h[11];
                outvec[16] = -h[10];
                outvec[17] = -h[9];

                outvec[35] = outvec[18] = -h[8];
                outvec[34] = outvec[19] = -h[7];
                outvec[33] = outvec[20] = -h[6];
                outvec[32] = outvec[21] = -h[5];
                outvec[31] = outvec[22] = -h[4];
                outvec[30] = outvec[23] = -h[3];
                outvec[29] = outvec[24] = -h[2];
                outvec[28] = outvec[25] = -h[1];
                outvec[27] = outvec[26] = -h[0];
            }

            private static void Imdct_9pt(float* invec, float* outvec)
            {
                float even_idct0;
                float even_idct1;
                float even_idct2;
                float even_idct3;
                float even_idct4;

                float odd_idct0;
                float odd_idct1;
                float odd_idct2;
                float odd_idct3;

                // BEGIN 5 Point IMDCT 
                float t0 = invec[6] / 2.0f + invec[0];
                float t1 = invec[0] - invec[6];
                float t2 = invec[2] - invec[4] - invec[8];

                even_idct0 = t0 + invec[2] * 0.939692621f
                    + invec[4] * 0.766044443f + invec[8] * 0.173648178f;

                even_idct1 = t2 / 2.0f + t1;
                even_idct2 = t0 - invec[2] * 0.173648178f
                    - invec[4] * 0.939692621f + invec[8] * 0.766044443f;

                even_idct3 = t0 - invec[2] * 0.766044443f
                    + invec[4] * 0.173648178f - invec[8] * 0.939692621f;

                even_idct4 = t1 - t2;
                // END 5 Point IMDCT 

                // BEGIN 4 Point IMDCT 
                {
                    float odd1 = invec[1] + invec[3];
                    float odd2 = invec[3] + invec[5];
                    t0 = (invec[5] + invec[7]) * 0.5f + invec[1];

                    odd_idct0 = t0 + odd1 * 0.939692621f + odd2 * 0.766044443f;
                    odd_idct1 = (invec[1] - invec[5]) * 1.5f - invec[7];
                    odd_idct2 = t0 - odd1 * 0.173648178f - odd2 * 0.939692621f;
                    odd_idct3 = t0 - odd1 * 0.766044443f + odd2 * 0.173648178f;
                }
                // END 4 Point IMDCT 

                // Adjust for non power of 2 IDCT 
                odd_idct0 += invec[7] * 0.173648178f;
                odd_idct1 -= invec[7] * 0.5f;
                odd_idct2 += invec[7] * 0.766044443f;
                odd_idct3 -= invec[7] * 0.939692621f;

                // Post-Twiddle 
                odd_idct0 *= 0.5f / 0.984807753f;
                odd_idct1 *= 0.5f / 0.866025404f;
                odd_idct2 *= 0.5f / 0.64278761f;
                odd_idct3 *= 0.5f / 0.342020143f;

                outvec[0] = even_idct0 + odd_idct0;
                outvec[1] = even_idct1 + odd_idct1;
                outvec[2] = even_idct2 + odd_idct2;
                outvec[3] = even_idct3 + odd_idct3;
                outvec[4] = even_idct4;

                // Mirror into the other half of the vector 
                outvec[5] = even_idct3 - odd_idct3;
                outvec[6] = even_idct2 - odd_idct2;
                outvec[7] = even_idct1 - odd_idct1;
                outvec[8] = even_idct0 - odd_idct0;
            }

            private static void ShortImpl(ref float fsIn, nint sbStart, ref float nextBlock)
            {
                float* imdctTmp = stackalloc float[SSLIMIT];
                float* imdctResult = stackalloc float[SSLIMIT * 2];

                for (nint sb = sbStart, ofs = sbStart * SSLIMIT; sb < SBLIMIT; sb++, ofs += SSLIMIT)
                {
                    // rearrange vectors
                    for (nint i = 0, tmpptr = 0; i < 3; i++)
                    {
                        nint v = ofs + i;
                        for (nint j = 0; j < 6; j++)
                        {
                            imdctTmp[tmpptr + j] = Unsafe.Add(ref fsIn, v);
                            v += 3;
                        }
                        tmpptr += 6;
                    }

                    // short blocks are fun...  3 separate IMDCT's with overlap in two different buffers

                    Unsafe.InitBlockUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref fsIn, ofs)),
                        0,
                        6 * sizeof(float));

                    // do the first 6 samples
                    ShortIMDCT(imdctTmp, 0, imdctResult);

                    Unsafe.CopyBlockUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref fsIn, ofs + 6)),
                        ref Unsafe.AsRef<byte>(imdctResult),
                        12 * sizeof(float));

                    // now the next 6
                    ShortIMDCT(imdctTmp, 6, imdctResult);
                    for (nint i = 0; i < 6; i++)
                    {
                        // add the first half to tsOut
                        Unsafe.Add(ref fsIn, ofs + i + 12) += imdctResult[i];
                    }

                    Unsafe.CopyBlockUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref nextBlock, ofs)),
                        ref Unsafe.AsRef<byte>(imdctResult + 6),
                        6 * sizeof(float));

                    // now the final 6
                    ShortIMDCT(imdctTmp, 12, imdctResult);
                    for (nint i = 0; i < 6; i++)
                    {
                        // add the first half to nextBlock
                        Unsafe.Add(ref nextBlock, ofs + i) += imdctResult[i];
                    }

                    Unsafe.CopyBlockUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref nextBlock, ofs + 6)),
                        ref Unsafe.AsRef<byte>(imdctResult + 6),
                        6 * sizeof(float));

                    Unsafe.InitBlockUnaligned(
                        ref Unsafe.As<float, byte>(ref Unsafe.Add(ref nextBlock, ofs + 12)),
                        0,
                        6 * sizeof(float));
                }
            }

            private static void ShortIMDCT(float* invec, int inIdx, float* outvec)
            {
                float* H = stackalloc float[6];
                float* h = stackalloc float[6];
                float* even_idct = stackalloc float[3];
                float* odd_idct = stackalloc float[3];

                // Preprocess the input to the two 3-point IDCT's 
                int idx = inIdx;
                int i;
                for (i = 1; i < 6; i++)
                {
                    H[i] = invec[idx];
                    H[i] += invec[++idx];
                }

                float t0, t1, t2;

                // 3-point IMDCT 
                t0 = H[4] / 2.0f + invec[inIdx];
                t1 = H[2] * sqrt32;
                even_idct[0] = t0 + t1;
                even_idct[1] = invec[inIdx] - H[4];
                even_idct[2] = t0 - t1;
                // END 3-point IMDCT 

                // 3-point IMDCT 
                t2 = H[3] + H[5];

                t0 = (t2) / 2.0f + H[1];
                t1 = (H[1] + H[3]) * sqrt32;
                odd_idct[0] = t0 + t1;
                odd_idct[1] = H[1] - t2;
                odd_idct[2] = t0 - t1;
                // END 3-point IMDCT 

                // Post-Twiddle 
                odd_idct[0] *= 0.51763809f;
                odd_idct[1] *= 0.707106781f;
                odd_idct[2] *= 1.931851653f;

                h[0] = (even_idct[0] + odd_idct[0]) * 0.50431448f;
                h[1] = (even_idct[1] + odd_idct[1]) * 0.5411961f;
                h[2] = (even_idct[2] + odd_idct[2]) * 0.630236207f;

                h[3] = (even_idct[2] - odd_idct[2]) * 0.821339816f;
                h[4] = (even_idct[1] - odd_idct[1]) * 1.306562965f;
                h[5] = (even_idct[0] - odd_idct[0]) * 3.830648788f;

                // Rearrange the 6 values from the IDCT to the output vector 
                float[] swin = _swin[2];
                outvec[0] = h[3] * swin[0];
                outvec[1] = h[4] * swin[1];
                outvec[2] = h[5] * swin[2];
                outvec[3] = -h[5] * swin[3];
                outvec[4] = -h[4] * swin[4];
                outvec[5] = -h[3] * swin[5];
                outvec[6] = -h[2] * swin[6];
                outvec[7] = -h[1] * swin[7];
                outvec[8] = -h[0] * swin[8];
                outvec[9] = -h[0] * swin[9];
                outvec[10] = -h[1] * swin[10];
                outvec[11] = -h[2] * swin[11];
            }
        }

        #endregion

        static internal bool GetCRC(MpegFrame frame, ref uint crc)
        {
            int count = frame.GetSideDataSize();
            while (--count >= 0)
            {
                MpegFrame.UpdateCRC(frame.ReadBits(8), 8, ref crc);
            }
            return true;
        }

        private HybridMDCT _hybrid = new HybridMDCT();
        private BitReservoir _bitRes = new BitReservoir();

        internal Layer3Decoder()
        {
            _tableSelect = new int[][][]
            {
                new int[][] { new int[3], new int[3] },
                new int[][] { new int[3], new int[3] },
            };

            _subblockGain = new float[][][]
            {
                new float[][] { new float[3], new float[3] },
                new float[][] { new float[3], new float[3] },
            };
        }

        private float[] GetSamplesArray(int channelIndex)
        {
            return channelIndex == 0 ? _samples0 : _samples1;
        }

        public override int DecodeFrame(MpegFrame frame, Span<float> ch0, Span<float> ch1)
        {
            // load the frame information
            ReadSideInfo(frame);

            // load the frame's main data
            if (!_bitRes.AddBits(frame, _mainDataBegin))
                return 0;

            // prep the reusable tables
            PrepTables(frame);

            int* channelMapping = stackalloc int[2];

            // do our stereo mode setup
            var startChannel = 0;
            var endChannel = _channels - 1;
            if (_channels == 1 || StereoMode == StereoMode.LeftOnly || StereoMode == StereoMode.DownmixToMono)
            {
                channelMapping[0] = 0;
                channelMapping[1] = -1;
                endChannel = 0;
            }
            else if (StereoMode == StereoMode.RightOnly)
            {
                // this is correct... if there's only a single channel output, it goes in channel 0's buffer
                channelMapping[0] = -1;
                channelMapping[1] = 0;
                startChannel = 1;
            }
            else    // MpegStereoMode.Both
            {
                channelMapping[0] = 0;
                channelMapping[1] = 1;
            }

            // get the granule count
            int granules;
            if (frame.Version == MpegVersion.Version1)
            {
                granules = 2;
            }
            else
            {
                granules = 1;
            }

            // decode the audio data
            int offset = 0;
            for (var gr = 0; gr < granules; gr++)
            {
                for (var ch = 0; ch < _channels; ch++)
                {
                    // read scale factors
                    int sfbits;
                    if (frame.Version == MpegVersion.Version1)
                    {
                        sfbits = ReadScalefactors(gr, ch);
                    }
                    else
                    {
                        sfbits = ReadLsfScalefactors(gr, ch, frame.ChannelModeExtension);
                    }

                    // huffman & dequant
                    ReadSamples(sfbits, gr, ch);
                }

                // stereo processing
                Stereo(frame.ChannelMode, frame.ChannelModeExtension, gr, frame.Version != MpegVersion.Version1);

                for (int channelIndex = startChannel; channelIndex <= endChannel; channelIndex++)
                {
                    // pull some values so we don't have to index them again later
                    Span<float> buf = GetSamplesArray(channelIndex).AsSpan();
                    ref float rBuf = ref MemoryMarshal.GetReference(buf);

                    int blockType = _blockType[gr][channelIndex];
                    bool blockSplit = _blockSplitFlag[gr][channelIndex];
                    bool mixedBlock = _mixedBlockFlag[gr][channelIndex];

                    // do the short/long/mixed logic here so it's only done once per channel per granule
                    if (blockSplit && blockType == 2)
                    {
                        if (mixedBlock)
                        {
                            // reorder & antialias mixed blocks
                            Reorder(buf, true);
                            AntiAlias(buf, true);
                        }
                        else
                        {
                            // reorder short blocks
                            Reorder(buf, false);
                        }
                    }
                    else
                    {
                        // antialias long blocks
                        AntiAlias(buf, false);
                    }

                    // hybrid processing
                    _hybrid.Apply(ref rBuf, channelIndex, blockType, blockSplit && mixedBlock);

                    // frequency inversion
                    FrequencyInversion(buf);

                    // inverse polyphase

                    Span<float> dst = MapOutput(channelIndex, channelMapping, ch0, ch1);
                    InversePolyphase(ref rBuf, channelIndex, offset, dst);
                }

                offset += SBLIMIT * SSLIMIT;
            }

            return offset;
        }

        public override void ResetForSeek()
        {
            base.ResetForSeek();

            _hybrid.Reset();

            _bitRes.Reset();
        }

        #region Side Info

        #region Variables

        private int _channels, _privBits, _mainDataBegin;
        private int[][] _scfsi = { new int[4], new int[4] };                //     ch, scfsi_band
        private int[][] _part23Length = { new int[2], new int[2] };         // gr, ch
        private int[][] _bigValues = { new int[2], new int[2] };            // gr, ch
        private float[][] _globalGain = { new float[2], new float[2] };     // gr, ch
        private int[][] _scalefacCompress = { new int[2], new int[2] };     // gr, ch
        private bool[][] _blockSplitFlag = { new bool[2], new bool[2] };    // gr, ch
        private bool[][] _mixedBlockFlag = { new bool[2], new bool[2] };    // gr, ch
        private int[][] _blockType = { new int[2], new int[2] };            // gr, ch
        private int[][][] _tableSelect;                                     // gr, ch, region
        private float[][][] _subblockGain;                                  // gr, ch, window
        private int[][] _regionAddress1 = { new int[2], new int[2] };       // gr, ch
        private int[][] _regionAddress2 = { new int[2], new int[2] };       // gr, ch
        private byte[][] _preflag = { new byte[2], new byte[2] };              // gr, ch
        private float[][] _scalefacScale = { new float[2], new float[2] };  // gr, ch
        private int[][] _count1TableSelect = { new int[2], new int[2] };    // gr, ch

        private static float[] GAIN_TAB =
        {
            1.57009245868378E-16f, 1.86716512307887E-16f, 2.22044604925031E-16f, 2.64057024024816E-16f,
            3.14018491736756E-16f, 3.73433024615774E-16f, 4.44089209850063E-16f, 5.28114048049630E-16f,
            6.28036983473509E-16f, 7.46866049231544E-16f, 8.88178419700125E-16f, 1.05622809609926E-15f,
            1.25607396694702E-15f, 1.49373209846309E-15f, 1.77635683940025E-15f, 2.11245619219853E-15f,
            2.51214793389404E-15f, 2.98746419692619E-15f, 3.55271367880050E-15f, 4.22491238439706E-15f,
            5.02429586778810E-15f, 5.97492839385238E-15f, 7.10542735760100E-15f, 8.44982476879408E-15f,
            1.00485917355761E-14f, 1.19498567877047E-14f, 1.42108547152020E-14f, 1.68996495375882E-14f,
            2.00971834711523E-14f, 2.38997135754094E-14f, 2.84217094304040E-14f, 3.37992990751764E-14f,
            4.01943669423047E-14f, 4.77994271508190E-14f, 5.68434188608080E-14f, 6.75985981503528E-14f,
            8.03887338846093E-14f, 9.55988543016378E-14f, 1.13686837721616E-13f, 1.35197196300706E-13f,
            1.60777467769219E-13f, 1.91197708603275E-13f, 2.27373675443232E-13f, 2.70394392601411E-13f,
            3.21554935538437E-13f, 3.82395417206551E-13f, 4.54747350886464E-13f, 5.40788785202823E-13f,
            6.43109871076876E-13f, 7.64790834413101E-13f, 9.09494701772928E-13f, 1.08157757040564E-12f,
            1.28621974215375E-12f, 1.52958166882621E-12f, 1.81898940354586E-12f, 2.16315514081129E-12f,
            2.57243948430750E-12f, 3.05916333765241E-12f, 3.63797880709171E-12f, 4.32631028162258E-12f,
            5.14487896861500E-12f, 6.11832667530482E-12f, 7.27595761418343E-12f, 8.65262056324518E-12f,
            1.02897579372300E-11f, 1.22366533506096E-11f, 1.45519152283669E-11f, 1.73052411264903E-11f,
            2.05795158744600E-11f, 2.44733067012193E-11f, 2.91038304567337E-11f, 3.46104822529806E-11f,
            4.11590317489199E-11f, 4.89466134024385E-11f, 5.82076609134674E-11f, 6.92209645059613E-11f,
            8.23180634978400E-11f, 9.78932268048772E-11f, 1.16415321826935E-10f, 1.38441929011922E-10f,
            1.64636126995680E-10f, 1.95786453609754E-10f, 2.32830643653870E-10f, 2.76883858023845E-10f,
            3.29272253991360E-10f, 3.91572907219509E-10f, 4.65661287307739E-10f, 5.53767716047690E-10f,
            6.58544507982719E-10f, 7.83145814439016E-10f, 9.31322574615479E-10f, 1.10753543209538E-09f,
            1.31708901596544E-09f, 1.56629162887804E-09f, 1.86264514923096E-09f, 2.21507086419076E-09f,
            2.63417803193088E-09f, 3.13258325775607E-09f, 3.72529029846191E-09f, 4.43014172838152E-09f,
            5.26835606386176E-09f, 6.26516651551212E-09f, 7.45058059692383E-09f, 8.86028345676304E-09f,
            1.05367121277235E-08f, 1.25303330310243E-08f, 1.49011611938477E-08f, 1.77205669135261E-08f,
            2.10734242554471E-08f, 2.50606660620485E-08f, 2.98023223876953E-08f, 3.54411338270521E-08f,
            4.21468485108941E-08f, 5.01213321240971E-08f, 5.96046447753906E-08f, 7.08822676541044E-08f,
            8.42936970217880E-08f, 1.00242664248194E-07f, 1.19209289550781E-07f, 1.41764535308209E-07f,
            1.68587394043576E-07f, 2.00485328496388E-07f, 2.38418579101562E-07f, 2.83529070616417E-07f,
            3.37174788087152E-07f, 4.00970656992777E-07f, 4.76837158203125E-07f, 5.67058141232835E-07f,
            6.74349576174305E-07f, 8.01941313985553E-07f, 9.53674316406250E-07f, 1.13411628246567E-06f,
            1.34869915234861E-06f, 1.60388262797110E-06f, 1.90734863281250E-06f, 2.26823256493134E-06f,
            2.69739830469722E-06f, 3.20776525594221E-06f, 3.81469726562500E-06f, 4.53646512986268E-06f,
            5.39479660939444E-06f, 6.41553051188442E-06f, 7.62939453125000E-06f, 9.07293025972536E-06f,
            1.07895932187889E-05f, 1.28310610237688E-05f, 1.52587890625000E-05f, 1.81458605194507E-05f,
            2.15791864375777E-05f, 2.56621220475377E-05f, 3.05175781250000E-05f, 3.62917210389014E-05f,
            4.31583728751555E-05f, 5.13242440950754E-05f, 6.10351562500000E-05f, 7.25834420778029E-05f,
            8.63167457503110E-05f, 1.02648488190151E-04f, 1.22070312500000E-04f, 1.45166884155606E-04f,
            1.72633491500622E-04f, 2.05296976380301E-04f, 2.44140625000000E-04f, 2.90333768311211E-04f,
            3.45266983001244E-04f, 4.10593952760603E-04f, 4.88281250000000E-04f, 5.80667536622423E-04f,
            6.90533966002488E-04f, 8.21187905521206E-04f, 9.76562500000000E-04f, 1.16133507324485E-03f,
            1.38106793200498E-03f, 1.64237581104241E-03f, 1.95312500000000E-03f, 2.32267014648969E-03f,
            2.76213586400995E-03f, 3.28475162208482E-03f, 3.90625000000000E-03f, 4.64534029297938E-03f,
            5.52427172801990E-03f, 6.56950324416964E-03f, 7.81250000000000E-03f, 9.29068058595876E-03f,
            1.10485434560398E-02f, 1.31390064883393E-02f, 1.56250000000000E-02f, 1.85813611719175E-02f,
            2.20970869120796E-02f, 2.62780129766786E-02f, 3.12500000000000E-02f, 3.71627223438350E-02f,
            4.41941738241592E-02f, 5.25560259533572E-02f, 6.25000000000000E-02f, 7.43254446876701E-02f,
            8.83883476483184E-02f, 1.05112051906714E-01f, 1.25000000000000E-01f, 1.48650889375340E-01f,
            1.76776695296637E-01f, 2.10224103813429E-01f, 2.50000000000000E-01f, 2.97301778750680E-01f,
            3.53553390593274E-01f, 4.20448207626857E-01f, 5.00000000000000E-01f, 5.94603557501361E-01f,
            7.07106781186547E-01f, 8.40896415253715E-01f, 1.00000000000000E+00f, 1.18920711500272E+00f,
            1.41421356237310E+00f, 1.68179283050743E+00f, 2.00000000000000E+00f, 2.37841423000544E+00f,
            2.82842712474619E+00f, 3.36358566101486E+00f, 4.00000000000000E+00f, 4.75682846001088E+00f,
            5.65685424949238E+00f, 6.72717132202972E+00f, 8.00000000000000E+00f, 9.51365692002177E+00f,
            1.13137084989848E+01f, 1.34543426440594E+01f, 1.60000000000000E+01f, 1.90273138400435E+01f,
            2.26274169979695E+01f, 2.69086852881189E+01f, 3.20000000000000E+01f, 3.80546276800871E+01f,
            4.52548339959390E+01f, 5.38173705762377E+01f, 6.40000000000000E+01f, 7.61092553601742E+01f,
            9.05096679918781E+01f, 1.07634741152475E+02f, 1.28000000000000E+02f, 1.52218510720348E+02f,
            1.81019335983756E+02f, 2.15269482304951E+02f, 2.56000000000000E+02f, 3.04437021440696E+02f,
            3.62038671967512E+02f, 4.30538964609902E+02f, 5.12000000000000E+02f, 6.08874042881393E+02f,
            7.24077343935025E+02f, 8.61077929219803E+02f, 1.02400000000000E+03f, 1.21774808576279E+03f,
            1.44815468787005E+03f, 1.72215585843961E+03f, 2.04800000000000E+03f, 2.43549617152557E+03f,
        };

        #endregion

        private void ReadSideInfo(MpegFrame frame)
        {
            if (frame.Version == MpegVersion.Version1)
            {
                // main_data_begin      9
                _mainDataBegin = frame.ReadBits(9);

                // private_bits         3 or 5
                if (frame.ChannelMode == MpegChannelMode.Mono)
                {
                    _privBits = frame.ReadBits(5);
                    _channels = 1;
                }
                else
                {
                    _privBits = frame.ReadBits(3);
                    _channels = 2;
                }

                for (var ch = 0; ch < _channels; ch++)
                {
                    // scfsi[ch][0...3]     1 x4
                    _scfsi[ch][0] = frame.ReadBits(1);
                    _scfsi[ch][1] = frame.ReadBits(1);
                    _scfsi[ch][2] = frame.ReadBits(1);
                    _scfsi[ch][3] = frame.ReadBits(1);
                }

                for (var gr = 0; gr < 2; gr++)
                {
                    for (var ch = 0; ch < _channels; ch++)
                    {
                        // part2_3_length[gr][ch]        12
                        _part23Length[gr][ch] = frame.ReadBits(12);
                        // big_values[gr][ch]            9
                        _bigValues[gr][ch] = frame.ReadBits(9);
                        // global_gain[gr][ch]           8
                        _globalGain[gr][ch] = GAIN_TAB[frame.ReadBits(8)];
                        // scalefac_compress[gr][ch]     4
                        _scalefacCompress[gr][ch] = frame.ReadBits(4);
                        // blocksplit_flag[gr][ch]       1
                        _blockSplitFlag[gr][ch] = frame.ReadBits(1) == 1;
                        if (_blockSplitFlag[gr][ch])
                        {
                            _blockType[gr][ch] = frame.ReadBits(2);
                            _mixedBlockFlag[gr][ch] = frame.ReadBits(1) == 1;
                            _tableSelect[gr][ch][0] = frame.ReadBits(5);
                            _tableSelect[gr][ch][1] = frame.ReadBits(5);
                            _tableSelect[gr][ch][2] = 0;

                            // set the region information
                            if (_blockType[gr][ch] == 2 && !_mixedBlockFlag[gr][ch])
                                _regionAddress1[gr][ch] = 8;
                            else
                                _regionAddress1[gr][ch] = 7;

                            _regionAddress2[gr][ch] = 20 - _regionAddress1[gr][ch];
                            _subblockGain[gr][ch][0] = frame.ReadBits(3) * -2f;
                            _subblockGain[gr][ch][1] = frame.ReadBits(3) * -2f;
                            _subblockGain[gr][ch][2] = frame.ReadBits(3) * -2f;
                        }
                        else
                        {
                            _tableSelect[gr][ch][0] = frame.ReadBits(5);
                            _tableSelect[gr][ch][1] = frame.ReadBits(5);
                            _tableSelect[gr][ch][2] = frame.ReadBits(5);
                            _regionAddress1[gr][ch] = frame.ReadBits(4);
                            _regionAddress2[gr][ch] = frame.ReadBits(3);
                            // set the block type so it doesn't accidentally carry
                            _blockType[gr][ch] = 0;

                            // make subblock gain equal unity
                            _subblockGain[gr][ch][0] = 0;
                            _subblockGain[gr][ch][1] = 0;
                            _subblockGain[gr][ch][2] = 0;
                        }
                        _preflag[gr][ch] = (byte)frame.ReadBits(1);
                        _scalefacScale[gr][ch] = .5f * (1f + frame.ReadBits(1));
                        _count1TableSelect[gr][ch] = frame.ReadBits(1);
                    }
                }
            }
            else    // MPEG 2+
            {
                // main_data_begin      8
                _mainDataBegin = frame.ReadBits(8);

                // private_bits         1 or 2
                if (frame.ChannelMode == MpegChannelMode.Mono)
                {
                    _privBits = frame.ReadBits(1);
                    _channels = 1;
                }
                else
                {
                    _privBits = frame.ReadBits(2);
                    _channels = 2;
                }

                var gr = 0;
                for (var ch = 0; ch < _channels; ch++)
                {
                    _part23Length[gr][ch] = frame.ReadBits(12);
                    _bigValues[gr][ch] = frame.ReadBits(9);
                    _globalGain[gr][ch] = GAIN_TAB[frame.ReadBits(8)];
                    _scalefacCompress[gr][ch] = frame.ReadBits(9);
                    _blockSplitFlag[gr][ch] = frame.ReadBits(1) == 1;
                    if (_blockSplitFlag[gr][ch])
                    {
                        _blockType[gr][ch] = frame.ReadBits(2);
                        _mixedBlockFlag[gr][ch] = frame.ReadBits(1) == 1;
                        _tableSelect[gr][ch][0] = frame.ReadBits(5);
                        _tableSelect[gr][ch][1] = frame.ReadBits(5);
                        _tableSelect[gr][ch][2] = 0;

                        // set the region information
                        if (_blockType[gr][ch] == 2 && !_mixedBlockFlag[gr][ch])
                            _regionAddress1[gr][ch] = 8;
                        else
                            _regionAddress1[gr][ch] = 7;

                        _regionAddress2[gr][ch] = 20 - _regionAddress1[gr][ch];
                        _subblockGain[gr][ch][0] = frame.ReadBits(3) * -2f;
                        _subblockGain[gr][ch][1] = frame.ReadBits(3) * -2f;
                        _subblockGain[gr][ch][2] = frame.ReadBits(3) * -2f;
                    }
                    else
                    {
                        _tableSelect[gr][ch][0] = frame.ReadBits(5);
                        _tableSelect[gr][ch][1] = frame.ReadBits(5);
                        _tableSelect[gr][ch][2] = frame.ReadBits(5);
                        _regionAddress1[gr][ch] = frame.ReadBits(4);
                        _regionAddress2[gr][ch] = frame.ReadBits(3);
                        // set the block type so it doesn't accidentally carry
                        _blockType[gr][ch] = 0;

                        // make subblock gain equal unity
                        _subblockGain[gr][ch][0] = 0;
                        _subblockGain[gr][ch][1] = 0;
                        _subblockGain[gr][ch][2] = 0;
                    }
                    _scalefacScale[gr][ch] = .5f * (1f + frame.ReadBits(1));
                    _count1TableSelect[gr][ch] = frame.ReadBits(1);
                }
            }
        }

        #endregion

        #region Precalc Table Prep

        #region Variables

        private int[]? _sfBandIndexL;
        private int[]? _sfBandIndexS;

        // these are byte[] to save memory
        private byte[] _cbLookupL = new byte[SSLIMIT * SBLIMIT];
        private byte[] _cbLookupS = new byte[SSLIMIT * SBLIMIT];
        private byte[] _cbwLookupS = new byte[SSLIMIT * SBLIMIT];
        private int _cbLookupSR;

        private static readonly int[][] _sfBandIndexLTable = {
            // MPEG 1
            // 44.1 kHz
            new int[] { 0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 52, 62, 74, 90, 110, 134, 162, 196, 238, 288, 342, 418, 576 },
            // 48 kHz
            new int[] { 0, 4, 8, 12, 16, 20, 24, 30, 36, 42, 50, 60, 72, 88, 106, 128, 156, 190, 230, 276, 330, 384, 576 },
            // 32 kHz
            new int[] { 0, 4, 8, 12, 16, 20, 24, 30, 36, 44, 54, 66, 82, 102, 126, 156, 194, 240, 296, 364, 448, 550, 576 },

            // MPEG 2
            // 22.05 kHz
            new int[] { 0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576 },
            // 24 kHz
            new int[] { 0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 114, 136, 162, 194, 232, 278, 330, 394, 464, 540, 576 },
            // 16 kHz
            new int[] { 0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576 },

            // MPEG 2.5
            // 11.025 kHz
            new int[] { 0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576 },
            // 12 kHz
            new int[] { 0, 6, 12, 18, 24, 30, 36, 44, 54, 66, 80, 96, 116, 140, 168, 200, 238, 284, 336, 396, 464, 522, 576 },
            // 8 kHz
            new int[] {
                0, 12, 24, 36, 48, 60, 72, 88, 108, 132, 160, 192, 232, 280, 336, 400, 476, 566, 568, 570, 572, 574, 576
            },
        };

        private static readonly int[][] _sfBandIndexSTable = {
            // MPEG 1
            // 44.1 kHz
            new int[] { 0, 4, 8, 12, 16, 22, 30, 40, 52, 66, 84, 106, 136, 192 },
            // 48 kHz
            new int[] { 0, 4, 8, 12, 16, 22, 28, 38, 50, 64, 80, 100, 126, 192 },
            // 32 kHz
            new int[] { 0, 4, 8, 12, 16, 22, 30, 42, 58, 78, 104, 138, 180, 192 },

            // MPEG 2
            // 22.05 kHz
            new int[] { 0, 4, 8, 12, 18, 24, 32, 42, 56, 74, 100, 132, 174, 192 },
            // 24 kHz
            new int[] { 0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 136, 180, 192 },
            // 16 kHz
            new int[] { 0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192 },

            // MPEG 2.5
            // 11.025 kHz
            new int[] { 0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192 },
            // 12 kHz
            new int[] { 0, 4, 8, 12, 18, 26, 36, 48, 62, 80, 104, 134, 174, 192 },
            // 8 kHz
            new int[] { 0, 8, 16, 24, 36, 52, 72, 96, 124, 160, 162, 164, 166, 192 },
        };

        #endregion

        private void PrepTables(MpegFrame frame)
        {
            if (_cbLookupSR != frame.SampleRate)
            {
                switch (frame.SampleRate)
                {
                    case 44100:
                        _sfBandIndexL = _sfBandIndexLTable[0];
                        _sfBandIndexS = _sfBandIndexSTable[0];
                        break;
                    case 48000:
                        _sfBandIndexL = _sfBandIndexLTable[1];
                        _sfBandIndexS = _sfBandIndexSTable[1];
                        break;
                    case 32000:
                        _sfBandIndexL = _sfBandIndexLTable[2];
                        _sfBandIndexS = _sfBandIndexSTable[2];
                        break;

                    case 22050:
                        _sfBandIndexL = _sfBandIndexLTable[3];
                        _sfBandIndexS = _sfBandIndexSTable[3];
                        break;
                    case 24000:
                        _sfBandIndexL = _sfBandIndexLTable[4];
                        _sfBandIndexS = _sfBandIndexSTable[4];
                        break;
                    case 16000:
                        _sfBandIndexL = _sfBandIndexLTable[5];
                        _sfBandIndexS = _sfBandIndexSTable[5];
                        break;

                    case 11025:
                        _sfBandIndexL = _sfBandIndexLTable[6];
                        _sfBandIndexS = _sfBandIndexSTable[6];
                        break;
                    case 12000:
                        _sfBandIndexL = _sfBandIndexLTable[7];
                        _sfBandIndexS = _sfBandIndexSTable[7];
                        break;
                    case 8000:
                        _sfBandIndexL = _sfBandIndexLTable[8];
                        _sfBandIndexS = _sfBandIndexSTable[8];
                        break;
                }

                Debug.Assert(_sfBandIndexL != null);
                Debug.Assert(_sfBandIndexS != null);

                // precalculate the critical bands per bucket
                int cbL = 0, cbS = 0;
                int next_cbL = _sfBandIndexL[1], next_cbS = _sfBandIndexS[1] * 3;
                for (int i = 0; i < 576; i++)
                {
                    if (i == next_cbL)
                    {
                        cbL++;
                        next_cbL = _sfBandIndexL[cbL + 1];
                    }

                    if (i == next_cbS)
                    {
                        cbS++;
                        next_cbS = _sfBandIndexS[cbS + 1] * 3;
                    }

                    _cbLookupL[i] = (byte)cbL;
                    _cbLookupS[i] = (byte)cbS;
                }

                // set up the short block windows
                int idx = 0;
                for (cbS = 0; cbS < 12; cbS++)
                {
                    int width = _sfBandIndexS[cbS + 1] - _sfBandIndexS[cbS];
                    for (byte i = 0; i < 3; i++)
                    {
                        for (int j = 0; j < width; j++, idx++)
                            _cbwLookupS[idx] = i;
                    }
                }

                _cbLookupSR = frame.SampleRate;
            }
        }

        #endregion

        #region Scale Factors

        #region Variables

        private int[][][] _scalefac = {   // ch, window, cb
            new int[][] { new int[13], new int[13], new int[13], new int[23] },
            new int[][] { new int[13], new int[13], new int[13], new int[23] }
        };

        private static ReadOnlySpan<byte> Slen0 => new byte[]
        {
            0, 0, 0, 0, 3, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4
        };

        private static ReadOnlySpan<byte> Slen1 => new byte[]
        {
            0, 1, 2, 3, 0, 1, 2, 3, 1, 2, 3, 1, 2, 3, 2, 3
        };

        private static readonly int[][][] _sfbBlockCntTab = {
            new int[][] { new int[] { 6, 5, 5, 5 },   new int[] { 9, 9, 9, 9 },    new int[] { 6, 9, 9, 9 }   },
            new int[][] { new int[] { 6, 5, 7, 3 },   new int[] { 9, 9, 12, 6 },   new int[] { 6, 9, 12, 6 }  },
            new int[][] { new int[] { 11, 10, 0, 0 }, new int[] { 18, 18, 0, 0 },  new int[] { 15, 18, 0, 0 } },
            new int[][] { new int[] { 7, 7, 7, 0 },   new int[] { 12, 12, 12, 0 }, new int[] { 6, 15, 12, 0 } },
            new int[][] { new int[] { 6, 6, 6, 3 },   new int[] { 12, 9, 9, 6 },   new int[] { 6, 12, 9, 6 }  },
            new int[][] { new int[] { 8, 8, 5, 0 },   new int[] { 15, 12, 9, 0 },  new int[] { 6, 18, 9, 0 }  },
        };

        #endregion

        private int ReadScalefactors(int gr, int ch)
        {
            int slen0 = Slen0[_scalefacCompress[gr][ch]];
            int slen1 = Slen1[_scalefacCompress[gr][ch]];
            int bits;

            int cb = 0;
            if (_blockSplitFlag[gr][ch] && _blockType[gr][ch] == 2)
            {
                if (slen0 > 0)
                {
                    bits = slen0 * 18;

                    if (_mixedBlockFlag[gr][ch])
                    {
                        // mixed has bands 0..7 of long, then 3..11 of short
                        for (; cb < 8; cb++)
                            _scalefac[ch][3][cb] = _bitRes.GetBits(slen0);

                        cb = 3;
                        bits -= slen0;  // mixed blocks need slen0 fewer bits
                    }

                    // short / mixed: just read from wherever cb happens to be through 11
                    for (; cb < 6; cb++)
                    {
                        _scalefac[ch][0][cb] = _bitRes.GetBits(slen0);
                        _scalefac[ch][1][cb] = _bitRes.GetBits(slen0);
                        _scalefac[ch][2][cb] = _bitRes.GetBits(slen0);
                    }
                }
                else
                {
                    Array.Clear(_scalefac[ch][0], 0, 6);
                    Array.Clear(_scalefac[ch][1], 0, 6);
                    Array.Clear(_scalefac[ch][2], 0, 6);
                    Array.Clear(_scalefac[ch][3], 0, 8);
                    bits = 0;
                }

                if (slen1 > 0)
                {
                    bits += slen1 * 18;

                    for (cb = 6; cb < 12; cb++)
                    {
                        _scalefac[ch][0][cb] = _bitRes.GetBits(slen1);
                        _scalefac[ch][1][cb] = _bitRes.GetBits(slen1);
                        _scalefac[ch][2][cb] = _bitRes.GetBits(slen1);
                    }
                }
                else
                {
                    Array.Clear(_scalefac[ch][0], 6, 6);
                    Array.Clear(_scalefac[ch][1], 6, 6);
                    Array.Clear(_scalefac[ch][2], 6, 6);
                }
            }
            else
            {
                // long: read if gr == 0, otherwise honor scfsi for the channel
                bits = 0;
                if (gr == 0 || _scfsi[ch][0] == 0)
                {
                    if (slen0 > 0)
                    {
                        bits += slen0 * 6;
                        _scalefac[ch][3][0] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][1] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][2] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][3] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][4] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][5] = _bitRes.GetBits(slen0);
                    }
                    else
                    {
                        Array.Clear(_scalefac[ch][3], 0, 6);
                    }
                }
                if (gr == 0 || _scfsi[ch][1] == 0)
                {
                    if (slen0 > 0)
                    {
                        bits += slen0 * 5;
                        _scalefac[ch][3][6] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][7] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][8] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][9] = _bitRes.GetBits(slen0);
                        _scalefac[ch][3][10] = _bitRes.GetBits(slen0);
                    }
                    else
                    {
                        Array.Clear(_scalefac[ch][3], 6, 5);
                    }
                }
                if (gr == 0 || _scfsi[ch][2] == 0)
                {
                    if (slen1 > 0)
                    {
                        bits += slen1 * 5;
                        _scalefac[ch][3][11] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][12] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][13] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][14] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][15] = _bitRes.GetBits(slen1);
                    }
                    else
                    {
                        Array.Clear(_scalefac[ch][3], 11, 5);
                    }
                }
                if (gr == 0 || _scfsi[ch][3] == 0)
                {
                    if (slen1 > 0)
                    {
                        bits += slen1 * 5;
                        _scalefac[ch][3][16] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][17] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][18] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][19] = _bitRes.GetBits(slen1);
                        _scalefac[ch][3][20] = _bitRes.GetBits(slen1);
                    }
                    else
                    {
                        Array.Clear(_scalefac[ch][3], 16, 5);
                    }
                }
            }

            return bits;
        }

        private int ReadLsfScalefactors(int gr, int ch, int chanModeExt)
        {
            var sfc = _scalefacCompress[gr][ch];

            int blockTypeNumber;
            // block type number = 2 if mixed short, 1 if pure short, otherwise 0
            if (_blockType[gr][ch] == 2)
            {
                if (_mixedBlockFlag[gr][ch])
                {
                    blockTypeNumber = 2;
                }
                else
                {
                    blockTypeNumber = 1;
                }
            }
            else
            {
                blockTypeNumber = 0;
            }

            Span<int> slen = stackalloc int[4];
            int blockNumber;
            if ((chanModeExt & 1) == 1 && ch == 1)
            {
                var tsfc = sfc >> 1;
                if (tsfc < 180)
                {
                    slen[0] = tsfc / 36;                    // <= 4, 15
                    slen[1] = (tsfc % 36) / 6;              // <= 5, 31
                    slen[2] = tsfc % 6;                     // <= 5, 31
                    slen[3] = 0;
                    _preflag[gr][ch] = 0;
                    blockNumber = 3;
                }
                else if (tsfc < 244)
                {
                    slen[0] = ((tsfc - 180) % 64) >> 4;     // <= 3, 7
                    slen[1] = ((tsfc - 180) % 16) >> 2;     // <= 3, 7
                    slen[2] = ((tsfc - 180) % 4);           // <= 3, 7
                    slen[3] = 0;
                    _preflag[gr][ch] = 0;
                    blockNumber = 4;
                }
                else if (tsfc < 255)
                {
                    slen[0] = (tsfc - 244) / 3;             // <= 3, 7
                    slen[1] = (tsfc - 244) % 3;             // <= 1, 1
                    slen[2] = 0;
                    slen[3] = 0;
                    _preflag[gr][ch] = 0;
                    blockNumber = 5;
                }
                else
                {
                    blockNumber = 0;
                }
            }
            else
            {
                //   if scalefac_comp < 400
                if (sfc < 400)
                {
                    slen[0] = (sfc >> 4) / 5;               // <= 4, 15
                    slen[1] = (sfc >> 4) % 5;               // <= 4, 15
                    slen[2] = (sfc & 15) >> 2;              // <= 3, 7
                    slen[3] = sfc & 3;                      // <= 3, 7
                    _preflag[gr][ch] = 0;
                    blockNumber = 0;
                }
                else if (sfc < 500)
                {
                    slen[0] = ((sfc - 400) >> 2) / 5;       // <= 4, 15
                    slen[1] = ((sfc - 400) >> 2) % 5;       // <= 4, 15
                    slen[2] = (sfc - 400) & 3;              // <= 3, 7
                    slen[3] = 0;
                    _preflag[gr][ch] = 0;
                    blockNumber = 1;
                }
                else if (sfc < 512)
                {
                    slen[0] = (sfc - 500) / 3;              // <= 3, 7
                    slen[1] = (sfc - 500) % 3;              // <= 2, 3
                    slen[2] = 0;
                    slen[3] = 0;
                    _preflag[gr][ch] = 1;
                    blockNumber = 2;
                }
                else
                {
                    blockNumber = 0;
                }
            }

            // now we populate our buffer...
            Span<int> buffer = stackalloc int[54];
            buffer.Clear();

            var k = 0;
            var blkCnt = _sfbBlockCntTab[blockNumber][blockTypeNumber];
            for (int i = 0; i < slen.Length; i++)
            {
                if (slen[i] != 0)
                {
                    for (int j = 0; j < blkCnt[i]; j++, k++)
                    {
                        buffer[k] = _bitRes.GetBits(slen[i]);
                    }
                }
                else
                {
                    k += blkCnt[i];
                }
            }

            // now that we have that done, let's assign our scalefactors
            k = 0;
            int sfb = 0;
            if (_blockSplitFlag[gr][ch] && _blockType[gr][ch] == 2)
            {
                if (_mixedBlockFlag[gr][ch])
                {
                    for (; sfb < 8; sfb++)
                    {
                        _scalefac[ch][3][sfb] = buffer[k++];
                    }
                    sfb = 3;
                }

                for (; sfb < 12; sfb++)
                {
                    for (int window = 0; window < 3; window++)
                    {
                        _scalefac[ch][window][sfb] = buffer[k++];
                    }
                }
                _scalefac[ch][0][12] = 0;
                _scalefac[ch][1][12] = 0;
                _scalefac[ch][2][12] = 0;
            }
            else
            {
                for (; sfb < 21; sfb++)
                {
                    _scalefac[ch][3][sfb] = buffer[k++];
                }
                _scalefac[ch][3][22] = 0;
            }

            return slen[0] * blkCnt[0] + slen[1] * blkCnt[1] + slen[2] * blkCnt[2] + slen[3] * blkCnt[3];
        }

        #endregion

        #region Huffman & Dequantize

        ref struct DequantizeState
        {
            public bool IsShort;
            public bool IsMixed;
            public nint SfBandIndexL;
            public float GlobalGain;
            public Span<float> _SubBlockGain;
            public float ScaleFacScale;
            public Span<int[]> _ScaleFac;
            public Span<int> _ScaleFac3;
            public int Preflag;
            public Span<byte> _CbLookupS;
            public Span<byte> _CbwLookupS;
            public Span<byte> _CbLookupL;
            public Span<float> _Pow2Tab;

            public ref float SubBlockGain => ref MemoryMarshal.GetReference(_SubBlockGain);
            public ref int[] ScaleFac => ref MemoryMarshal.GetReference(_ScaleFac);
            public ref int ScaleFac3 => ref MemoryMarshal.GetReference(_ScaleFac3);
            public ref byte CbLookupS => ref MemoryMarshal.GetReference(_CbLookupS);
            public ref byte CbwLookupS => ref MemoryMarshal.GetReference(_CbwLookupS);
            public ref byte CbLookupL => ref MemoryMarshal.GetReference(_CbLookupL);
            public ref float Pow2Tab => ref MemoryMarshal.GetReference(_Pow2Tab);
        }

        private float[] _samples0 = new float[SSLIMIT * SBLIMIT + 3];
        private float[] _samples1 = new float[SSLIMIT * SBLIMIT + 3];

        private static ReadOnlySpan<byte> PRETAB => new byte[]
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 3, 2, 0
        };

        private static float[] POW2_TAB { get; } =
        {
            1.000000000000000E-00f, 7.071067811865470E-01f, 5.000000000000000E-01f, 3.535533905932740E-01f,
            2.500000000000000E-01f, 1.767766952966370E-01f, 1.250000000000000E-01f, 8.838834764831840E-02f,
            6.250000000000000E-02f, 4.419417382415920E-02f, 3.125000000000000E-02f, 2.209708691207960E-02f,
            1.562500000000000E-02f, 1.104854345603980E-02f, 7.812500000000000E-03f, 5.524271728019900E-03f,
            3.906250000000000E-03f, 2.762135864009950E-03f, 1.953125000000000E-03f, 1.381067932004980E-03f,
            9.765625000000000E-04f, 6.905339660024880E-04f, 4.882812500000000E-04f, 3.452669830012440E-04f,
            2.441406250000000E-04f, 1.726334915006220E-04f, 1.220703125000000E-04f, 8.631674575031100E-05f,
            6.103515625000000E-05f, 4.315837287515550E-05f, 3.051757812500000E-05f, 2.157918643757770E-05f,
            1.525878906250000E-05f, 1.078959321878890E-05f, 7.629394531250000E-06f, 5.394796609394440E-06f,
            3.814697265625000E-06f, 2.697398304697220E-06f, 1.907348632812500E-06f, 1.348699152348610E-06f,
            9.536743164062500E-07f, 6.743495761743050E-07f, 4.768371582031250E-07f, 3.371747880871520E-07f,
            2.384185791015620E-07f, 1.685873940435760E-07f, 1.192092895507810E-07f, 8.429369702178800E-08f,
            5.960464477539060E-08f, 4.214684851089410E-08f, 2.980232238769530E-08f, 2.107342425544710E-08f,
            1.490116119384770E-08f, 1.053671212772350E-08f, 7.450580596923830E-09f, 5.268356063861760E-09f,
            3.725290298461910E-09f, 2.634178031930880E-09f, 1.862645149230960E-09f, 1.317089015965440E-09f,
            9.313225746154790E-10f, 6.585445079827190E-10f, 4.656612873077390E-10f, 3.292722539913600E-10f,
        };

        private void ReadSamples(int sfBits, int gr, int ch)
        {
            int region1Start;
            int region2Start;
            int sfBandIndexL = 0;

            if (_blockSplitFlag[gr][ch] && _blockType[gr][ch] == 2)
            {
                region1Start = 36;
                region2Start = 576;
            }
            else
            {
                Debug.Assert(_sfBandIndexL != null);

                region1Start = _sfBandIndexL[_regionAddress1[gr][ch] + 1];
                region2Start = _sfBandIndexL[Math.Min(_regionAddress1[gr][ch] + _regionAddress2[gr][ch] + 2, 22)];
                sfBandIndexL = _sfBandIndexL[8];
            }

            long part3end = _bitRes.BitsRead - sfBits + _part23Length[gr][ch];

            ref float samples = ref MemoryMarshal.GetArrayDataReference(GetSamplesArray(ch));
            int[] tableSelect = _tableSelect[gr][ch];

            bool isShort =
                _blockSplitFlag[gr][ch] &&
                _blockType[gr][ch] == 2;

            DequantizeState dqState = new()
            {
                IsShort = isShort,
                IsMixed = !_mixedBlockFlag[gr][ch],
                SfBandIndexL = sfBandIndexL,
                GlobalGain = _globalGain[gr][ch],
                _SubBlockGain = _subblockGain[gr][ch],
                ScaleFacScale = _scalefacScale[gr][ch],
                _ScaleFac = _scalefac[ch],
                _ScaleFac3 = _scalefac[ch][3],
                Preflag = _preflag[gr][ch],
                _CbLookupL = _cbLookupL,
                _CbLookupS = _cbLookupS,
                _CbwLookupS = _cbwLookupS,
                _Pow2Tab = POW2_TAB,
            };

            nint idx = 0;
            int h = tableSelect[0];

            // bigvalues section
            int bigValueCount = _bigValues[gr][ch] * 2;

            if (h == 0 || h == 4 || h == 14)
            {
                while (idx < bigValueCount && idx < region1Start)
                {
                    Unsafe.Add(ref samples, idx++) = 0f;
                    Unsafe.Add(ref samples, idx++) = 0f;
                }
            }
            else
            {
                while (idx < bigValueCount && idx < region1Start)
                {
                    Huffman.Decode(_bitRes, h, out float x, out float y);
                    Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, x);
                    ++idx;
                    Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, y);
                    ++idx;
                }
            }
            h = tableSelect[1];

            if (h == 0 || h == 4 || h == 14)
            {
                while (idx < bigValueCount && idx < region2Start)
                {
                    Unsafe.Add(ref samples, idx++) = 0f;
                    Unsafe.Add(ref samples, idx++) = 0f;
                }
            }
            else
            {
                while (idx < bigValueCount && idx < region2Start)
                {
                    Huffman.Decode(_bitRes, h, out float x, out float y);
                    Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, x);
                    ++idx;
                    Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, y);
                    ++idx;
                }
            }
            h = tableSelect[2];

            if (h == 0 || h == 4 || h == 14)
            {
                while (idx < bigValueCount && idx < SBLIMIT * SSLIMIT - 1)
                {
                    Unsafe.Add(ref samples, idx++) = 0f;
                    Unsafe.Add(ref samples, idx++) = 0f;
                }
            }
            else
            {
                while (idx < bigValueCount && idx < SBLIMIT * SSLIMIT - 1)
                {
                    Huffman.Decode(_bitRes, h, out float x, out float y);
                    Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, x);
                    ++idx;
                    Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, y);
                    ++idx;
                }
            }

            // count1 section
            h = _count1TableSelect[gr][ch] + 32;

            // - 3 to ensure that we never get an out of range exception
            while (part3end > _bitRes.BitsRead && idx < SBLIMIT * SSLIMIT - 3)
            {
                Huffman.Decode(_bitRes, h, out float x, out float y, out float v, out float w);
                Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, v);
                ++idx;
                Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, w);
                ++idx;
                Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, x);
                ++idx;
                Unsafe.Add(ref samples, idx) = Dequantize(ref dqState, idx, y);
                ++idx;
            }

            // adjust the bit stream if we're off somehow
            if (_bitRes.BitsRead > part3end)
            {
                _bitRes.RewindBits((int)(_bitRes.BitsRead - part3end));

                idx -= 4;
                if (idx < 0)
                    idx = 0;
            }

            if (_bitRes.BitsRead < part3end)
                _bitRes.SkipBits((int)(part3end - _bitRes.BitsRead));

            // zero out the highest samples (defined as 0 in the standard)
            if (idx < SBLIMIT * SSLIMIT)
            {
                Unsafe.InitBlockUnaligned(
                    ref Unsafe.As<float, byte>(ref Unsafe.Add(ref samples, idx)),
                    0,
                    (uint)(SBLIMIT * SSLIMIT + 3 - idx) * sizeof(float));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Dequantize(ref DequantizeState s, nint idx, float val)
        {
            if (val == 0f)
                return 0f;

            if (s.IsShort && !(s.IsMixed && idx < s.SfBandIndexL))
            {
                return val * DequantizeShort(ref s, idx);
            }
            else
            {
                return val * DequantizeLong(ref s, idx);
            }
        }

        private static float DequantizeShort(ref DequantizeState s, nint idx)
        {
            // short / mixed short section
            nint cb = Unsafe.Add(ref s.CbLookupS, idx);
            nint window = Unsafe.Add(ref s.CbwLookupS, idx);
            float subBlockGain = Unsafe.Add(ref s.SubBlockGain, window);
            int[] scaleFacA = Unsafe.Add(ref s.ScaleFac, window);
            float scaleFac = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(scaleFacA), cb);

            return
                s.GlobalGain *
                Unsafe.Add(ref s.Pow2Tab, (nint)(-2 * (subBlockGain - (s.ScaleFacScale * scaleFac))));
        }

        private static float DequantizeLong(ref DequantizeState s, nint idx)
        {
            // long / mixed long section
            nint cb = Unsafe.Add(ref s.CbLookupL, idx);
            int scaleFac3 = Unsafe.Add(ref s.ScaleFac3, cb);
            int pretab = Unsafe.Add(ref MemoryMarshal.GetReference(PRETAB), cb);

            return
                s.GlobalGain *
                Unsafe.Add(ref s.Pow2Tab, (nint)(2 * s.ScaleFacScale * (scaleFac3 + s.Preflag * pretab)));
        }

        #endregion

        #region Stereo

        private static readonly float[] _isRatio0 = {
            0f, 0.211324865405187f, 0.366025403784439f, 0.5f, 0.633974596215561f, 0.788675134594813f, 1f
        };

        private static readonly float[] _isRatio1 = {
            1f, 0.788675134594813f, 0.633974596215561f, 0.5f, 0.366025403784439f, 0.211324865405187f, 0f
        };

        private static readonly float[][][] _lsfRatio = {   // sfc%2, ch, isPos
            new float[][]
            {
                new float[] {
                    1f, 0.840896415256f, 1f, 0.707106781190391f, 1f, 0.594603557506209f, 1f, 0.500000000005436f,
                    1f, 0.420448207632571f, 1f, 0.353553390599039f, 1f, 0.297301778756337f, 1f, 0.250000000005436f,
                    1f, 0.210224103818571f, 1f, 0.176776695301441f, 1f, 0.148650889379784f, 1f, 0.125000000004077f,
                    1f, 0.105112051910428f, 1f, 0.0883883476516816f, 1f, 0.0743254446907002f, 1f, 0.0625000000027179f },
                new float[] {
                    1f, 1f, 0.840896415256f, 1f, 0.707106781190391f, 1f, 0.594603557506209f, 1f,
                    0.500000000005436f, 1f, 0.420448207632571f, 1f, 0.353553390599039f, 1f, 0.297301778756337f, 1f,
                    0.250000000005436f, 1f, 0.210224103818571f, 1f, 0.176776695301441f, 1f, 0.148650889379784f, 1f,
                    0.125000000004077f, 1f, 0.105112051910428f, 1f, 0.0883883476516816f, 1f, 0.0743254446907002f, 1f },
            },
            new float[][]
            {
                new float[] {
                    1f, 0.707106781188f, 1f, 0.500000000002054f, 1f, 0.353553390595452f, 1f, 0.250000000002054f,
                    1f, 0.176776695298452f, 1f, 0.125000000001541f, 1f, 0.0883883476495893f, 1f, 0.062500000001027f,
                    1f, 0.0441941738249762f, 1f, 0.0312500000006419f, 1f, 0.0220970869125789f, 1f, 0.0156250000003851f,
                    1f, 0.0110485434563348f, 1f, 0.00781250000022466f, 1f, 0.00552427172819011f, 1f, 0.00390625000012838f },
                new float[] {
                    1f, 1f, 0.707106781188f, 1f, 0.500000000002054f, 1f, 0.353553390595452f, 1f,
                    0.250000000002054f, 1f, 0.176776695298452f, 1f, 0.125000000001541f, 1f, 0.0883883476495893f, 1f,
                    0.062500000001027f, 1f, 0.0441941738249762f, 1f, 0.0312500000006419f, 1f, 0.0220970869125789f, 1f,
                    0.0156250000003851f, 1f, 0.0110485434563348f, 1f, 0.00781250000022466f, 1f, 0.00552427172819011f, 1f },
            },
        };

        private void Stereo(MpegChannelMode channelMode, int chanModeExt, int gr, bool lsf)
        {
            // do the stereo decoding as needed...  This really only applies in two cases:
            //  1) Joint Stereo and one (or both) of the extensions are enabled, or
            //  2) We're doing a downmix to mono

            float[] samples1 = _samples1;

            if (channelMode != MpegChannelMode.JointStereo || chanModeExt == 0)
            {
                if (_channels != 1)
                {
                    // this is a no-op most of the time
                    ApplyFullStereo(0, SSLIMIT * SBLIMIT);
                }
            }
            else
            {
                bool midSide = (chanModeExt & 0x2) == 2;

                if ((chanModeExt & 0x1) == 1)
                {
                    // do the intensity stereo processing

                    #region Common Processing

                    // find the highest sample index with a value in channel 1
                    //   - now each step only has to start from there
                    int lastValueIdx = -1;
                    for (int i = SBLIMIT * SSLIMIT - (SBLIMIT + 1); i >= 0; i--)
                    {
                        if (samples1[i] != 0f)
                        {
                            lastValueIdx = i;
                            break;
                        }
                    }

                    Debug.Assert(_sfBandIndexL != null);
                    Debug.Assert(_sfBandIndexS != null);

                    // figure up which passes we'll need and for which ranges
                    int lEnd = -1, sStart = -1;
                    if (_blockSplitFlag[gr][0] && _blockType[gr][0] == 2)
                    {
                        if (_mixedBlockFlag[gr][0])
                        {
                            // 0 through 8 of long, then 3 through 12 of short
                            if (lastValueIdx < _sfBandIndexL[8])
                                lEnd = 8;
                            sStart = 3;
                        }
                        else
                        {
                            // 0 through 12 of short
                            sStart = 0;
                        }
                    }
                    else
                    {
                        // 0 through 21 of long
                        lEnd = 21;
                    }

                    #endregion

                    #region Long Processing

                    // long processing is far simpler than short...  
                    // just process from the start of the scalefactor band after the last non-zero sample
                    // we also don't have to mess with "finding" again; it was done above
                    int sfb = 0;
                    if (lastValueIdx > -1)
                        sfb = _cbLookupL[lastValueIdx] + 1;

                    // make sure we do the mid/side processing on the lower bands (if needed)
                    if (sfb > 0 && sStart == -1)
                    {
                        if (midSide)
                            ApplyMidSide(0, _sfBandIndexL[sfb]);
                        else
                            ApplyFullStereo(0, _sfBandIndexL[sfb]);
                    }

                    // now process the intensity bands
                    for (; sfb < lEnd; sfb++)
                    {
                        int i = _sfBandIndexL[sfb];
                        int width = _sfBandIndexL[sfb + 1] - _sfBandIndexL[sfb];
                        int isPos = _scalefac[1][3][sfb];
                        if (isPos == 7)
                        {
                            if (midSide)
                                ApplyMidSide(i, width);
                            else
                                ApplyFullStereo(i, width);
                        }
                        else if (lsf)
                        {
                            ApplyLsfIStereo(i, width, isPos, _scalefacCompress[gr][0]);
                        }
                        else
                        {
                            ApplyIStereo(i, width, isPos);
                        }
                    }

                    if (sStart <= -1)
                    {
                        // do final long processing
                        int isPos = _scalefac[1][3][20];
                        if (isPos == 7)
                        {
                            if (midSide)
                                ApplyMidSide(_sfBandIndexL[21], 576 - _sfBandIndexL[21]);
                            else
                                ApplyFullStereo(_sfBandIndexL[21], 576 - _sfBandIndexL[21]);
                        }
                        else if (lsf)
                        {
                            ApplyLsfIStereo(
                                _sfBandIndexL[21], 576 - _sfBandIndexL[21], isPos, _scalefacCompress[gr][0]);
                        }
                        else
                        {
                            ApplyIStereo(_sfBandIndexL[21], 576 - _sfBandIndexL[21], isPos);
                        }
                    }

                    #endregion

                    #region Short Processing

                    // short processing requires that each window be looked at separately... 
                    // they are interleaved, so it gets really interesting...
                    // on the plus side, whichever window the {lastValueIdx} is in is already found... :)
                    else
                    {
                        // find where each window starts intensity processing
                        int* sSfb = stackalloc int[] { -1, -1, -1 };
                        int window;
                        if (lastValueIdx > -1)
                        {
                            sfb = _cbLookupS[lastValueIdx];
                            window = _cbwLookupS[lastValueIdx];
                            sSfb[window] = sfb;
                        }
                        else
                        {
                            sfb = 12;
                            window = 3; // NB: 3 is correct!
                        }

                        window = (window - 1) % 3;
                        for (; sfb >= sStart && window >= 0; window = (window - 1) % 3)
                        {
                            if (sSfb[window] != -1)
                            {
                                if (sSfb[0] != -1 && sSfb[1] != -1 && sSfb[2] != -1)
                                    break;
                                continue;
                            }

                            int width = _sfBandIndexS[sfb + 1] - _sfBandIndexS[sfb];
                            int i = _sfBandIndexS[sfb] * 3 + width * (window + 1);

                            while (--width >= -1)
                            {
                                if (samples1[--i] != 0f)
                                {
                                    sSfb[window] = sfb;
                                    break;
                                }
                            }

                            if (window == 0)
                                sfb--;
                        }

                        // now apply the intensity processing for each window & scalefactor band
                        sfb = sStart;
                        for (; sfb < 12; sfb++)
                        {
                            int width = _sfBandIndexS[sfb + 1] - _sfBandIndexS[sfb];
                            int i = _sfBandIndexS[sfb] * 3;

                            for (window = 0; window < 3; window++)
                            {
                                if (sfb > sSfb[window])
                                {
                                    var isPos = _scalefac[1][window][sfb];
                                    if (isPos == 7)
                                    {
                                        if (midSide)
                                            ApplyMidSide(i, width);
                                        else
                                            ApplyFullStereo(i, width);
                                    }
                                    else if (lsf)
                                    {
                                        ApplyLsfIStereo(i, width, isPos, _scalefacCompress[gr][0]);
                                    }
                                    else
                                    {
                                        ApplyIStereo(i, width, isPos);
                                    }
                                }
                                else if (midSide)
                                {
                                    ApplyMidSide(i, width);
                                }
                                else
                                {
                                    ApplyFullStereo(i, width);
                                }

                                i += width;
                            }
                        }

                        // do final short processing
                        var finalWidth = _sfBandIndexS[13] - _sfBandIndexS[12];
                        for (window = 0; window < 3; window++)
                        {
                            var isPos = _scalefac[1][window][11];
                            if (isPos == 7)
                            {
                                if (midSide)
                                    ApplyMidSide(_sfBandIndexS[11] * 3 + finalWidth * window, finalWidth);
                                else
                                    ApplyFullStereo(_sfBandIndexS[11] * 3 + finalWidth * window, finalWidth);
                            }
                            else if (lsf)
                            {
                                ApplyLsfIStereo(
                                    _sfBandIndexS[11] * 3 + finalWidth * window, finalWidth, isPos, _scalefacCompress[gr][0]);
                            }
                            else
                            {
                                ApplyIStereo(_sfBandIndexS[11] * 3 + finalWidth * window, finalWidth, isPos);
                            }
                        }
                    }

                    #endregion
                }
                else if (midSide)
                {
                    // just do mid/side processing for everything
                    ApplyMidSide(0, SBLIMIT * SSLIMIT);
                }
                else
                {
                    // this is a no-op most of the time
                    ApplyFullStereo(0, SSLIMIT * SBLIMIT);
                }
            }
        }

        private void ApplyIStereo(int i, int sb, int isPos)
        {
            float[] samples0 = _samples0;
            float[] samples1 = _samples1;

            if (StereoMode == StereoMode.DownmixToMono)
            {
                for (; sb > 0; sb--, i++)
                {
                    samples0[i] /= 2f; // scale appropriately
                }
            }
            else
            {
                float ratio0 = _isRatio0[isPos];
                float ratio1 = _isRatio1[isPos];
                for (; sb > 0; sb--, i++)
                {
                    samples1[i] = samples0[i] * ratio1;
                    samples0[i] *= ratio0;
                }
            }
        }

        private void ApplyLsfIStereo(int i, int sb, int isPos, int scalefacCompress)
        {
            float[] lsfRatio = _lsfRatio[scalefacCompress % 1][isPos];
            float k0 = lsfRatio[0];
            float k1 = lsfRatio[1];

            float[] samples0 = _samples0;
            float[] samples1 = _samples1;

            if (StereoMode == StereoMode.DownmixToMono)
            {
                var ratio = 1 / (k0 + k1);
                for (; sb > 0; sb--, i++)
                {
                    samples0[i] *= ratio;
                }
            }
            else
            {
                for (; sb > 0; sb--, i++)
                {
                    samples1[i] = samples0[i] * k1;
                    samples0[i] *= k0;
                }
            }
        }

        private void ApplyMidSide(int i, int sb)
        {
            float[] samples0 = _samples0;
            float[] samples1 = _samples1;

            if (StereoMode == StereoMode.DownmixToMono)
            {
                for (; sb > 0; sb--, i++)
                {
                    samples0[i] *= 0.707106781f; // scale appropriately
                }
            }
            else
            {
                for (; sb > 0; sb--, i++)
                {
                    // apply the mid/side
                    float a = samples0[i];
                    float b = samples1[i];
                    samples0[i] = (a + b) * 0.707106781f;
                    samples1[i] = (a - b) * 0.707106781f;
                }
            }
        }

        private void ApplyFullStereo(int i, int sb)
        {
            float[] samples0 = _samples0;
            float[] samples1 = _samples1;

            if (StereoMode == StereoMode.DownmixToMono)
            {
                for (; sb > 0; sb--, i++)
                {
                    samples0[i] = (samples0[i] + samples1[i]) / 2f;
                }
            }
        }

        #endregion

        #region Reorder

        private void Reorder(Span<float> buf, bool mixedBlock)
        {
            Debug.Assert(_sfBandIndexL != null);
            Debug.Assert(_sfBandIndexS != null);

            Span<float> reorderBuf = stackalloc float[SBLIMIT * SSLIMIT];

            // reorder into reorderBuf, then copy back
            int sfb = 0;

            if (mixedBlock)
            {
                // mixed... copy the first two bands and reorder the rest
                buf.Slice(0, SSLIMIT * 2).CopyTo(reorderBuf);

                sfb = 3;
            }

            while (sfb < 13)
            {
                int sfb_start = _sfBandIndexS[sfb];
                int sfb_lines = _sfBandIndexS[sfb + 1] - sfb_start;

                for (int window = 0; window < 3; window++)
                {
                    for (int freq = 0; freq < sfb_lines; freq++)
                    {
                        int src_line = sfb_start * 3 + window * sfb_lines + freq;
                        int des_line = (sfb_start * 3) + window + (freq * 3);
                        reorderBuf[des_line] = buf[src_line];
                    }
                }

                sfb++;
            }

            reorderBuf.CopyTo(buf);
        }

        #endregion

        #region Anti-Alias

        private static readonly float[] _scs = {
            0.85749292571254400f,  0.88174199731770500f,  0.94962864910273300f,  0.98331459249179000f,
            0.99551781606758600f,  0.99916055817814800f,  0.99989919524444700f,  0.99999315507028000f,
        };

        private static readonly float[] _sca = {
            -0.51449575542752700f, -0.47173196856497200f, -0.31337745420390200f, -0.18191319961098100f,
            -0.09457419252642070f, -0.04096558288530410f, -0.01419856857247120f, -0.00369997467376004f,
        };

        private static void AntiAlias(Span<float> buf, bool mixedBlock)
        {
            int sblim;
            if (mixedBlock)
            {
                sblim = 1;
            }
            else
            {
                sblim = SBLIMIT - 1;
            }

            float[] scs = _scs;
            float[] sca = _sca;

            for (int sb = 0, offset = 0; sb < sblim; sb++, offset += SSLIMIT)
            {
                for (int ss = 0, buOfs = offset + SSLIMIT - 1, bdOfs = offset + SSLIMIT; ss < 8; ss++, buOfs--, bdOfs++)
                {
                    float bu = buf[buOfs];
                    float bd = buf[bdOfs];
                    buf[buOfs] = (bu * scs[ss]) - (bd * sca[ss]);
                    buf[bdOfs] = (bd * scs[ss]) + (bu * sca[ss]);
                }
            }
        }

        #endregion

        #region Frequency Inversion

        private static void FrequencyInversion(Span<float> buf)
        {
            for (int ss = 1; ss < SSLIMIT; ss += 2)
            {
                for (int sb = 1; sb < SBLIMIT; sb += 2)
                {
                    buf[sb * SSLIMIT + ss] = -buf[sb * SSLIMIT + ss];
                }
            }
        }

        #endregion

        #region Inverse Polyphase

        // Layer III interleaves the samples, so we have to make them linear again
        private void InversePolyphase(ref float source, int ch, int ofs, Span<float> dst)
        {
            for (nint ss = 0; ss < SSLIMIT; ss++, ofs += SBLIMIT)
            {
                Span<float> polyPhase = dst.Slice(ofs, SBLIMIT);
                for (int sb = 0; sb < SBLIMIT; sb++)
                    polyPhase[sb] = Unsafe.Add(ref source, (sb * SSLIMIT) + ss);

                InversePolyPhase(ch, polyPhase);
            }
        }

        #endregion
    }
}
