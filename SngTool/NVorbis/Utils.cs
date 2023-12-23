using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace NVorbis
{
    internal static class Utils
    {
        private const float LowerClip = -0.99999994f;
        private const float UpperClip = 0.99999994f;

        internal static int ilog(int x)
        {
            int cnt = 0;
            while (x > 0)
            {
                ++cnt;
                x >>= 1;    // this is safe because we'll never get here if the sign bit is set
            }
            return cnt;
        }

        internal static uint BitReverse(uint n)
        {
            return BitReverse(n, 32);
        }

        internal static uint BitReverse(uint n, int bits)
        {
            n = ((n & 0xAAAAAAAA) >> 1) | ((n & 0x55555555) << 1);
            n = ((n & 0xCCCCCCCC) >> 2) | ((n & 0x33333333) << 2);
            n = ((n & 0xF0F0F0F0) >> 4) | ((n & 0x0F0F0F0F) << 4);
            n = ((n & 0xFF00FF00) >> 8) | ((n & 0x00FF00FF) << 8);
            return ((n >> 16) | (n << 16)) >> (32 - bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float ClipValue(float value, ref bool clipped)
        {
            if (value > UpperClip)
            {
                clipped = true;
                return UpperClip;
            }
            if (value < LowerClip)
            {
                clipped = true;
                return LowerClip;
            }
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector<float> ClipValue(Vector<float> value, ref Vector<float> clipped)
        {
            Vector<float> upper = new(UpperClip);
            Vector<float> lower = new(LowerClip);

            Vector<float> gt = Vector.GreaterThan<float>(value, upper);
            Vector<float> lt = Vector.LessThan<float>(value, lower);
            clipped = Vector.BitwiseOr(clipped, Vector.BitwiseOr(gt, lt));

            value = Vector.ConditionalSelect(gt, upper, value);
            value = Vector.ConditionalSelect(lt, lower, value);

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<float> ClipValue(Vector128<float> value, ref Vector128<float> clipped)
        {
            Vector128<float> upper = Vector128.Create(UpperClip);
            Vector128<float> lower = Vector128.Create(LowerClip);

            Vector128<float> gt = Vector128.GreaterThan(value, upper);
            Vector128<float> lt = Vector128.LessThan(value, lower);
            clipped = Vector128.BitwiseOr(clipped, Vector128.BitwiseOr(gt, lt));

            value = Vector128.ConditionalSelect(gt, upper, value);
            value = Vector128.ConditionalSelect(lt, lower, value);

            return value;
        }

        internal static float ConvertFromVorbisFloat32(uint bits)
        {
            // do as much as possible with bit tricks in integer math
            int sign = (int) bits >> 31;   // sign-extend to the full 32-bits
            int exponent = (int) ((bits & 0x7fe00000) >> 21) - 788;  // grab the exponent, remove the bias.
            float mantissa = ((int) (bits & 0x1fffff) ^ sign) + (sign & 1);  // grab the mantissa and apply the sign bit.

            // NB: We could use bit tricks to calc the exponent, but it can't be more than 63 in either direction.
            //     This creates an issue, since the exponent field allows for a *lot* more than that.
            //     On the flip side, larger exponent values don't seem to be used by the Vorbis codebooks...
            //     Either way, we'll play it safe and let the BCL calculate it.

            return System.MathF.ScaleB(mantissa, exponent);
        }
    }
}
