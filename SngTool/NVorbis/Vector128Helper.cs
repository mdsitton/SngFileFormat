using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace NVorbis
{
    internal static class Vector128Helper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> UnpackLow(Vector128<float> left, Vector128<float> right)
        {
            if (Sse.IsSupported)
            {
                return Sse.UnpackLow(left, right);
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                return AdvSimd.Arm64.ZipLow(left, right);
            }
            else
            {
                return SoftwareFallback(left, right);
            }

            static Vector128<float> SoftwareFallback(Vector128<float> left, Vector128<float> right)
            {
                Unsafe.SkipInit(out Vector128<float> result);
                result = result.WithElement(0, left.GetElement(0));
                result = result.WithElement(1, right.GetElement(0));
                result = result.WithElement(2, left.GetElement(1));
                result = result.WithElement(3, right.GetElement(1));
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector128<float> UnpackHigh(Vector128<float> left, Vector128<float> right)
        {
            if (Sse.IsSupported)
            {
                return Sse.UnpackHigh(left, right);
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                return AdvSimd.Arm64.ZipHigh(left, right);
            }
            else
            {
                return SoftwareFallback(left, right);
            }

            static Vector128<float> SoftwareFallback(Vector128<float> left, Vector128<float> right)
            {
                Unsafe.SkipInit(out Vector128<float> result);
                result = result.WithElement(0, left.GetElement(2));
                result = result.WithElement(1, right.GetElement(2));
                result = result.WithElement(2, left.GetElement(3));
                result = result.WithElement(3, right.GetElement(3));
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Vector128<float> Gather(
            float* baseAddress,
            Vector128<int> index,
            [ConstantExpected(Min = 1, Max = 8)] byte scale)
        {
            if (Avx2.IsSupported)
            {
                return Avx2.GatherVector128(baseAddress, index, scale);
            }
            else
            {
                return SoftwareFallback(baseAddress, index, scale);
            }

            static Vector128<float> SoftwareFallback(
                float* baseAddress,
                Vector128<int> index,
                byte scale)
            {
                Unsafe.SkipInit(out Vector128<float> result);
                result = result.WithElement(0, baseAddress[(long) index.GetElement(0) * scale]);
                result = result.WithElement(1, baseAddress[(long) index.GetElement(1) * scale]);
                result = result.WithElement(2, baseAddress[(long) index.GetElement(2) * scale]);
                result = result.WithElement(3, baseAddress[(long) index.GetElement(3) * scale]);
                return result;
            }
        }
    }
}
