using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace NVorbis
{
    internal static class Vector256Helper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Vector256<float> Gather(
            float* baseAddress,
            Vector256<int> index,
            [ConstantExpected(Min = 1, Max = 8)] byte scale)
        {
            if (Avx2.IsSupported)
            {
                return Avx2.GatherVector256(baseAddress, index, scale);
            }
            else
            {
                return SoftwareFallback(baseAddress, index, scale);
            }

            static Vector256<float> SoftwareFallback(
                float* baseAddress,
                Vector256<int> index,
                [ConstantExpected(Min = 1, Max = 8)] byte scale)
            {
                return Vector256.Create(
                    Vector128Helper.Gather(baseAddress, index.GetLower(), scale),
                    Vector128Helper.Gather(baseAddress, index.GetUpper(), scale));
            }
        }
    }
}
