using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace NVorbis
{
    internal static class VectorHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector<T> LoadUnsafe<T>(ref T source, int elementOffset)
            where T : struct
        {
            ThrowForUnsupportedNumericsVectorBaseType<T>();
            ref byte address = ref Unsafe.As<T, byte>(ref Unsafe.Add(ref source, elementOffset));
            return Unsafe.ReadUnaligned<Vector<T>>(ref address);
        }

#if !NET8_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreUnsafe<T>(this Vector<T> source, ref T destination, nuint elementOffset)
            where T : struct
        {
            ThrowForUnsupportedNumericsVectorBaseType<T>();
            ref byte address = ref Unsafe.As<T, byte>(ref Unsafe.Add(ref destination, elementOffset));
            Unsafe.WriteUnaligned(ref address, source);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ThrowForUnsupportedNumericsVectorBaseType<T>()
            where T : struct
        {
            if (!Vector<T>.IsSupported)
            {
                ThrowNotSupportedException();

                [DoesNotReturn]
                static void ThrowNotSupportedException()
                {
                    throw new NotSupportedException();
                }
            }
        }
    }
}
