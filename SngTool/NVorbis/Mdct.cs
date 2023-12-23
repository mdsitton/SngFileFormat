// Source: https://github.com/nothings/stb/blob/master/stb_vorbis.c

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace NVorbis
{
    internal static unsafe class Mdct
    {
        private static ConcurrentDictionary<int, MdctImpl> _setupCache = new();

        public static void Reverse(float[] samples, float[] buf2, int sampleCount)
        {
            MdctImpl impl = _setupCache.GetOrAdd(sampleCount, static (c) => new MdctImpl(c));
            impl.CalcReverse(samples, buf2);
        }

        private class MdctImpl
        {
            private readonly int _n;
            private readonly int _ld;

            private readonly float[] _a, _b, _c;
            private readonly ushort[] _bitrev;

            public MdctImpl(int n)
            {
                _n = n;
                _ld = Utils.ilog(n) - 1;

                int _n2 = n >> 1;
                int _n4 = _n2 >> 1;
                int _n8 = _n4 >> 1;

                // first, calc the "twiddle factors"
                _a = new float[_n2];
                _b = new float[_n2];
                _c = new float[_n4];
                int k, k2;
                for (k = k2 = 0; k < _n4; ++k, k2 += 2)
                {
                    (float sina, float cosa) = MathF.SinCos(4 * k * MathF.PI / n);
                    _a[k2] = cosa;
                    _a[k2 + 1] = -sina;

                    (float sinb, float cosb) = MathF.SinCos((k2 + 1) * MathF.PI / n / 2);
                    _b[k2] = cosb * .5f;
                    _b[k2 + 1] = sinb * .5f;
                }
                for (k = k2 = 0; k < _n8; ++k, k2 += 2)
                {
                    (float sinc, float cosc) = MathF.SinCos(2 * (k2 + 1) * MathF.PI / n);
                    _c[k2] = cosc;
                    _c[k2 + 1] = -sinc;
                }

                // now, calc the bit reverse table
                _bitrev = new ushort[_n8];
                for (int i = 0; i < _n8; ++i)
                {
                    _bitrev[i] = (ushort)(Utils.BitReverse((uint)i, _ld - 3) << 2);
                }
            }

            public void CalcReverse(float[] buffer, float[] buf2)
            {
                fixed (float* bufferPtr = buffer)
                fixed (float* buf2Ptr = buf2)
                fixed (float* aa = _a)
                {
                    CalcReverse(bufferPtr, buf2Ptr, aa);
                }
            }

            private void CalcReverse(float* buffer, float* buf2, float* A)
            {
                int n = _n;
                int n2 = n >> 1;
                int n4 = n2 >> 1;
                int n8 = n4 >> 1;

                // IMDCT algorithm from "The use of multirate filter banks for coding of high quality digital audio"
                // See notes about bugs in that paper in less-optimal implementation 'inverse_mdct_old' after this function.

                // kernel from paper


                // merged:
                //   copy and reflect spectral data
                //   step 0

                // note that it turns out that the items added together during
                // this step are, in fact, being added to themselves (as reflected
                // by step 0). inexplicable inefficiency! this became obvious
                // once I combined the passes.

                // so there's a missing 'times 2' here (for adding X to itself).
                // this propagates through linearly to the end, where the numbers
                // are 1/2 too small, and need to be compensated for.

                {
                    float* d = &buf2[n2 - 2];   // buf2
                    float* AA = A;              // A
                    float* e = &buffer[0];      // buffer
                    float* e_stop = &buffer[n2];// buffer

                    while (e != e_stop)
                    {
                        d[1] = e[0] * AA[0] - e[2] * AA[1];
                        d[0] = e[0] * AA[1] + e[2] * AA[0];
                        d -= 2;
                        AA += 2;
                        e += 4;
                    }

                    e = &buffer[n2 - 3];
                    while (d >= buf2)
                    {
                        d[1] = -e[2] * AA[0] - -e[0] * AA[1];
                        d[0] = -e[2] * AA[1] + -e[0] * AA[0];
                        d -= 2;
                        AA += 2;
                        e -= 4;
                    }
                }

                // now we use symbolic names for these, so that we can
                // possibly swap their meaning as we change which operations
                // are in place
                float* u = buffer;
                float* v = buf2;

                // step 2    (paper output is w, now u)
                // this could be in place, but the data ends up in the wrong
                // place... _somebody_'s got to swap it, so this is nominated
                {

                    float* AA = &A[n2 - 8];   // A

                    float* e0 = &v[n4];       // v
                    float* e1 = &v[0];        // v

                    float* d0 = &u[n4];       // u
                    float* d1 = &u[0];        // u

                    while (AA >= A)
                    {
                        float v40_20, v41_21;

                        v41_21 = e0[1] - e1[1];
                        v40_20 = e0[0] - e1[0];
                        d0[1] = e0[1] + e1[1];
                        d0[0] = e0[0] + e1[0];
                        d1[1] = v41_21 * AA[4] - v40_20 * AA[5];
                        d1[0] = v40_20 * AA[4] + v41_21 * AA[5];

                        v41_21 = e0[3] - e1[3];
                        v40_20 = e0[2] - e1[2];
                        d0[3] = e0[3] + e1[3];
                        d0[2] = e0[2] + e1[2];
                        d1[3] = v41_21 * AA[0] - v40_20 * AA[1];
                        d1[2] = v40_20 * AA[0] + v41_21 * AA[1];

                        AA -= 8;

                        d0 += 4;
                        d1 += 4;
                        e0 += 4;
                        e1 += 4;
                    }
                }

                // step 3
                int ld = _ld;

                // optimized step 3:

                // the original step3 loop can be nested r inside s or s inside r;
                // it's written originally as s inside r, but this is dumb when r
                // iterates many times, and s few. So I have two copies of it and
                // switch between them halfway.

                // this is iteration 0 of step 3
                step3_iter0_loop(n >> 4, u, n2 - 1 - n4 * 0, -(n >> 3), A);
                step3_iter0_loop(n >> 4, u, n2 - 1 - n4 * 1, -(n >> 3), A);

                // this is iteration 1 of step 3
                step3_inner_r_loop(n >> 5, u, n2 - 1 - n8 * 0, -(n >> 4), A, 16);
                step3_inner_r_loop(n >> 5, u, n2 - 1 - n8 * 1, -(n >> 4), A, 16);
                step3_inner_r_loop(n >> 5, u, n2 - 1 - n8 * 2, -(n >> 4), A, 16);
                step3_inner_r_loop(n >> 5, u, n2 - 1 - n8 * 3, -(n >> 4), A, 16);

                int l = 2;
                for (; l < (ld - 3) >> 1; ++l)
                {
                    int k0 = n >> (l + 2);
                    int k0_2 = k0 >> 1;
                    int lim = 1 << (l + 1);
                    for (int i = 0; i < lim; ++i)
                    {
                        step3_inner_r_loop(n >> (l + 4), u, n2 - 1 - k0 * i, -k0_2, A, 1 << (l + 3));
                    }
                }

                for (; l < ld - 6; ++l)
                {
                    int k0 = n >> (l + 2);
                    int k1 = 1 << (l + 3);
                    int k0_2 = k0 >> 1;
                    int rlim = n >> (l + 6), r;
                    int lim = 1 << (l + 1);
                    float* A0 = A;
                    int i_off = n2 - 1;
                    for (r = rlim; r > 0; --r)
                    {
                        step3_inner_s_loop(lim, u, i_off, -k0_2, A0, k1, k0);
                        A0 += k1 * 4;
                        i_off -= 8;
                    }
                }

                // iterations with count:
                //   ld-6,-5,-4 all interleaved together
                //       the big win comes from getting rid of needless flops
                //         due to the constants on pass 5 & 4 being all 1 and 0;
                //       combining them to be simultaneous to improve cache made little difference
                step3_inner_s_loop_ld654(n >> 5, u, n2 - 1, A, n);

                // output is u

                // step 4, 5, and 6
                // cannot be in-place because of step 5
                fixed (ushort* bit_reverse = _bitrev)
                {
                    ushort* bitrev = bit_reverse;
                    // weirdly, I'd have thought reading sequentially and writing
                    // erratically would have been better than vice-versa, but in
                    // fact that's not what my testing showed. (That is, with
                    // j = bitreverse(i), do you read i and write j, or read j and write i.)

                    float* d0 = &v[n4 - 4];
                    float* d1 = &v[n2 - 4];
                    while (d0 >= v)
                    {
                        int k4;

                        k4 = bitrev[0];
                        d1[3] = u[k4 + 0];
                        d1[2] = u[k4 + 1];
                        d0[3] = u[k4 + 2];
                        d0[2] = u[k4 + 3];

                        k4 = bitrev[1];
                        d1[1] = u[k4 + 0];
                        d1[0] = u[k4 + 1];
                        d0[1] = u[k4 + 2];
                        d0[0] = u[k4 + 3];

                        d0 -= 4;
                        d1 -= 4;
                        bitrev += 2;
                    }
                }
                // (paper output is u, now v)


                // data must be in buf2
                Debug.Assert(v == buf2);

                // step 7   (paper output is v, now v)
                // this is now in place
                fixed (float* cc = _c)
                {
                    float* C = cc;

                    float* d = v;
                    float* e = v + n2 - 4;

                    while (d < e)
                    {
                        float a02, a11, b0, b1, b2, b3;

                        a02 = d[0] - e[2];
                        a11 = d[1] + e[3];

                        b0 = C[1] * a02 + C[0] * a11;
                        b1 = C[1] * a11 - C[0] * a02;

                        b2 = d[0] + e[2];
                        b3 = d[1] - e[3];

                        d[0] = b2 + b0;
                        d[1] = b3 + b1;
                        e[2] = b2 - b0;
                        e[3] = b1 - b3;

                        a02 = d[2] - e[0];
                        a11 = d[3] + e[1];

                        b0 = C[3] * a02 + C[2] * a11;
                        b1 = C[3] * a11 - C[2] * a02;

                        b2 = d[2] + e[0];
                        b3 = d[3] - e[1];

                        d[2] = b2 + b0;
                        d[3] = b3 + b1;
                        e[0] = b2 - b0;
                        e[1] = b1 - b3;

                        C += 4;
                        d += 4;
                        e -= 4;
                    }
                }

                // data must be in buf2


                // step 8+decode   (paper output is X, now buffer)
                // this generates pairs of data a la 8 and pushes them directly through
                // the decode kernel (pushing rather than pulling) to avoid having
                // to make another pass later

                // this cannot POSSIBLY be in place, so we refer to the buffers directly
                fixed (float* bb = _b)
                {
                    float* B = bb + n2 - 8;
                    float* e = buf2 + n2 - 8;
                    float* d0 = &buffer[0];
                    float* d1 = &buffer[n2 - 4];
                    float* d2 = &buffer[n2];
                    float* d3 = &buffer[n - 4];
                    while (e >= v)
                    {
                        float p0, p1, p2, p3;

                        p3 = e[6] * B[7] - e[7] * B[6];
                        p2 = -e[6] * B[6] - e[7] * B[7];

                        d0[0] = p3;
                        d1[3] = -p3;
                        d2[0] = p2;
                        d3[3] = p2;

                        p1 = e[4] * B[5] - e[5] * B[4];
                        p0 = -e[4] * B[4] - e[5] * B[5];

                        d0[1] = p1;
                        d1[2] = -p1;
                        d2[1] = p0;
                        d3[2] = p0;

                        p3 = e[2] * B[3] - e[3] * B[2];
                        p2 = -e[2] * B[2] - e[3] * B[3];

                        d0[2] = p3;
                        d1[1] = -p3;
                        d2[2] = p2;
                        d3[1] = p2;

                        p1 = e[0] * B[1] - e[1] * B[0];
                        p0 = -e[0] * B[0] - e[1] * B[1];

                        d0[3] = p1;
                        d1[0] = -p1;
                        d2[3] = p0;
                        d3[0] = p0;

                        B -= 8;
                        e -= 8;
                        d0 += 4;
                        d2 += 4;
                        d1 -= 4;
                        d3 -= 4;
                    }
                }
            }

            // the following were split out into separate functions while optimizing;
            // they could be pushed back up but eh. __forceinline showed no change;
            // they're probably already being inlined.
            private static void step3_iter0_loop(int n, float* e, int i_off, int k_off, float* A)
            {
                float* ee0 = e + i_off;
                float* ee2 = ee0 + k_off;
                int i;

                Debug.Assert((n & 3) == 0);

                for (i = n >> 2; i > 0; --i)
                {
                    float k00_20, k01_21;
                    k00_20 = ee0[0] - ee2[0];
                    k01_21 = ee0[-1] - ee2[-1];
                    ee0[0] += ee2[0];
                    ee0[-1] += ee2[-1];
                    ee2[0] = k00_20 * A[0] - k01_21 * A[1];
                    ee2[-1] = k01_21 * A[0] + k00_20 * A[1];
                    A += 8;

                    k00_20 = ee0[-2] - ee2[-2];
                    k01_21 = ee0[-3] - ee2[-3];
                    ee0[-2] += ee2[-2];
                    ee0[-3] += ee2[-3];
                    ee2[-2] = k00_20 * A[0] - k01_21 * A[1];
                    ee2[-3] = k01_21 * A[0] + k00_20 * A[1];
                    A += 8;

                    k00_20 = ee0[-4] - ee2[-4];
                    k01_21 = ee0[-5] - ee2[-5];
                    ee0[-4] += ee2[-4];
                    ee0[-5] += ee2[-5];
                    ee2[-4] = k00_20 * A[0] - k01_21 * A[1];
                    ee2[-5] = k01_21 * A[0] + k00_20 * A[1];
                    A += 8;

                    k00_20 = ee0[-6] - ee2[-6];
                    k01_21 = ee0[-7] - ee2[-7];
                    ee0[-6] += ee2[-6];
                    ee0[-7] += ee2[-7];
                    ee2[-6] = k00_20 * A[0] - k01_21 * A[1];
                    ee2[-7] = k01_21 * A[0] + k00_20 * A[1];
                    A += 8;

                    ee0 -= 8;
                    ee2 -= 8;
                }
            }

            private static void step3_inner_r_loop(int lim, float* e, int d0, int k_off, float* A, int k1)
            {
                float* e0 = e + d0;
                float* e2 = e0 + k_off;

                int i = lim >> 2;
                bool nonOverlapped = (e0 - e2) >= i * 8;
                Debug.Assert(nonOverlapped, "Overlapped addresses.");

                if (nonOverlapped && Vector256.IsHardwareAccelerated)
                {
                    Vector256<int> v_index =
                        Vector256.Create(0, 1, 0, 1, 0, 1, 0, 1) +
                        Vector256.Create(k1) * Vector256.Create(0, 0, 1, 1, 2, 2, 3, 3);

                    for (; i > 0; --i)
                    {
                        var v_e0 = Vector256.Load(e0 - 7);
                        var v_e2 = Vector256.Load(e2 - 7);

                        var v_res0 = v_e0 + v_e2;
                        v_res0.Store(e0 - 7);

                        var v_k0 = v_e0 - v_e2;

                        var v_a = Vector256Helper.Gather(A, v_index, 4);
                        A += k1 * 4;

                        var v_a0 = Vector256.Shuffle(v_a, Vector256.Create(6, 6, 4, 4, 2, 2, 0, 0));
                        var v_a1 = Vector256.Shuffle(v_a, Vector256.Create(7, 7, 5, 5, 3, 3, 1, 1));
                        var v_k1 = Vector256.Shuffle(v_k0, Vector256.Create(1, 0, 3, 2, 5, 4, 7, 6));

                        var v_res1 = v_k0 * v_a0 + v_k1 * v_a1 * Vector256.Create(1, -1, 1, -1, 1, -1, 1, -1f);
                        v_res1.Store(e2 - 7);

                        e0 -= 8;
                        e2 -= 8;
                    }
                }
                else if (nonOverlapped && Vector128.IsHardwareAccelerated)
                {
                    Vector128<int> v_index =
                        Vector128.Create(0, 1, 0, 1) +
                        Vector128.Create(k1) * Vector128.Create(0, 0, 1, 1);

                    for (; i > 0; --i)
                    {
                        var l_e0 = Vector128.Load(e0 - 7);
                        var h_e0 = Vector128.Load(e0 - 3);
                        var l_e2 = Vector128.Load(e2 - 7);
                        var h_e2 = Vector128.Load(e2 - 3);

                        var l_res0 = l_e0 + l_e2;
                        var h_res0 = h_e0 + h_e2;
                        l_res0.Store(e0 - 7);
                        h_res0.Store(e0 - 3);

                        var l_k0 = l_e0 - l_e2;
                        var h_k0 = h_e0 - h_e2;

                        var l_a = Vector128Helper.Gather(A, v_index, 4);
                        A += k1 * 2;

                        var h_a = Vector128Helper.Gather(A, v_index, 4);
                        A += k1 * 2;

                        var l_a0 = Vector128.Shuffle(h_a, Vector128.Create(2, 2, 0, 0));
                        var h_a0 = Vector128.Shuffle(l_a, Vector128.Create(2, 2, 0, 0));
                        var l_a1 = Vector128.Shuffle(h_a, Vector128.Create(3, 3, 1, 1));
                        var h_a1 = Vector128.Shuffle(l_a, Vector128.Create(3, 3, 1, 1));
                        var l_k1 = Vector128.Shuffle(l_k0, Vector128.Create(1, 0, 3, 2));
                        var h_k1 = Vector128.Shuffle(h_k0, Vector128.Create(1, 0, 3, 2));

                        var l_res1 = l_k0 * l_a0 + l_k1 * l_a1 * Vector128.Create(1, -1, 1, -1f);
                        var h_res1 = h_k0 * h_a0 + h_k1 * h_a1 * Vector128.Create(1, -1, 1, -1f);
                        l_res1.Store(e2 - 7);
                        h_res1.Store(e2 - 3);

                        e0 -= 8;
                        e2 -= 8;
                    }
                }
                else
                {
                    for (; i > 0; --i)
                    {
                        float k00_20 = e0[-0] - e2[-0];
                        float k01_21 = e0[-1] - e2[-1];
                        e0[-0] += e2[-0];
                        e0[-1] += e2[-1];
                        e2[-0] = k00_20 * A[0] - k01_21 * A[1];
                        e2[-1] = k01_21 * A[0] + k00_20 * A[1];

                        A += k1;

                        k00_20 = e0[-2] - e2[-2];
                        k01_21 = e0[-3] - e2[-3];
                        e0[-2] += e2[-2];
                        e0[-3] += e2[-3];
                        e2[-2] = k00_20 * A[0] - k01_21 * A[1];
                        e2[-3] = k01_21 * A[0] + k00_20 * A[1];

                        A += k1;

                        k00_20 = e0[-4] - e2[-4];
                        k01_21 = e0[-5] - e2[-5];
                        e0[-4] += e2[-4];
                        e0[-5] += e2[-5];
                        e2[-4] = k00_20 * A[0] - k01_21 * A[1];
                        e2[-5] = k01_21 * A[0] + k00_20 * A[1];

                        A += k1;

                        k00_20 = e0[-6] - e2[-6];
                        k01_21 = e0[-7] - e2[-7];
                        e0[-6] += e2[-6];
                        e0[-7] += e2[-7];
                        e2[-6] = k00_20 * A[0] - k01_21 * A[1];
                        e2[-7] = k01_21 * A[0] + k00_20 * A[1];

                        e0 -= 8;
                        e2 -= 8;

                        A += k1;
                    }
                }
            }

            private static void step3_inner_s_loop(int n, float* e, int i_off, int k_off, float* A, int a_off, int k0)
            {
                float A0 = A[0];
                float A1 = A[0 + 1];
                float A2 = A[0 + a_off];
                float A3 = A[0 + a_off + 1];
                float A4 = A[0 + a_off * 2 + 0];
                float A5 = A[0 + a_off * 2 + 1];
                float A6 = A[0 + a_off * 3 + 0];
                float A7 = A[0 + a_off * 3 + 1];

                float k00, k11;

                float* ee0 = e + i_off;
                float* ee2 = ee0 + k_off;

                for (int i = n; i > 0; --i)
                {
                    k00 = ee0[0] - ee2[0];
                    k11 = ee0[-1] - ee2[-1];
                    ee0[0] = ee0[0] + ee2[0];
                    ee0[-1] = ee0[-1] + ee2[-1];
                    ee2[0] = k00 * A0 - k11 * A1;
                    ee2[-1] = k11 * A0 + k00 * A1;

                    k00 = ee0[-2] - ee2[-2];
                    k11 = ee0[-3] - ee2[-3];
                    ee0[-2] = ee0[-2] + ee2[-2];
                    ee0[-3] = ee0[-3] + ee2[-3];
                    ee2[-2] = k00 * A2 - k11 * A3;
                    ee2[-3] = k11 * A2 + k00 * A3;

                    k00 = ee0[-4] - ee2[-4];
                    k11 = ee0[-5] - ee2[-5];
                    ee0[-4] = ee0[-4] + ee2[-4];
                    ee0[-5] = ee0[-5] + ee2[-5];
                    ee2[-4] = k00 * A4 - k11 * A5;
                    ee2[-5] = k11 * A4 + k00 * A5;

                    k00 = ee0[-6] - ee2[-6];
                    k11 = ee0[-7] - ee2[-7];
                    ee0[-6] = ee0[-6] + ee2[-6];
                    ee0[-7] = ee0[-7] + ee2[-7];
                    ee2[-6] = k00 * A6 - k11 * A7;
                    ee2[-7] = k11 * A6 + k00 * A7;

                    ee0 -= k0;
                    ee2 -= k0;
                }
            }

            private static void step3_inner_s_loop_ld654(int n, float* e, int i_off, float* A, int base_n)
            {
                int a_off = base_n >> 3;
                float A2 = A[0 + a_off];
                float* z = e + i_off;
                float* @base = z - 16 * n;

                while (z > @base)
                {
                    float k00, k11;
                    float l00, l11;

                    k00 = z[-0] - z[-8];
                    k11 = z[-1] - z[-9];
                    l00 = z[-2] - z[-10];
                    l11 = z[-3] - z[-11];
                    z[-0] = z[-0] + z[-8];
                    z[-1] = z[-1] + z[-9];
                    z[-2] = z[-2] + z[-10];
                    z[-3] = z[-3] + z[-11];
                    z[-8] = k00;
                    z[-9] = k11;
                    z[-10] = (l00 + l11) * A2;
                    z[-11] = (l11 - l00) * A2;

                    k00 = z[-4] - z[-12];
                    k11 = z[-5] - z[-13];
                    l00 = z[-6] - z[-14];
                    l11 = z[-7] - z[-15];
                    z[-4] = z[-4] + z[-12];
                    z[-5] = z[-5] + z[-13];
                    z[-6] = z[-6] + z[-14];
                    z[-7] = z[-7] + z[-15];
                    z[-12] = k11;
                    z[-13] = -k00;
                    z[-14] = (l11 - l00) * A2;
                    z[-15] = (l00 + l11) * -A2;

                    iter_54(z);
                    iter_54(z - 8);
                    z -= 16;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void iter_54(float* z)
            {
                float k00, k11, k22, k33;
                float y0, y1, y2, y3;

                k00 = z[0] - z[-4];
                y0 = z[0] + z[-4];
                y2 = z[-2] + z[-6];
                k22 = z[-2] - z[-6];

                z[-0] = y0 + y2;      // z0 + z4 + z2 + z6
                z[-2] = y0 - y2;      // z0 + z4 - z2 - z6

                // done with y0,y2

                k33 = z[-3] - z[-7];

                z[-4] = k00 + k33;    // z0 - z4 + z3 - z7
                z[-6] = k00 - k33;    // z0 - z4 - z3 + z7

                // done with k33

                k11 = z[-1] - z[-5];
                y1 = z[-1] + z[-5];
                y3 = z[-3] + z[-7];

                z[-1] = y1 + y3;      // z1 + z5 + z3 + z7
                z[-3] = y1 - y3;      // z1 + z5 - z3 - z7
                z[-5] = k11 - k22;    // z1 - z5 + z2 - z6
                z[-7] = k11 + k22;    // z1 - z5 - z2 + z6
            }
        }
    }
}
