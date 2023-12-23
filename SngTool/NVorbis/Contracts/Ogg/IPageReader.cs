using System;
using System.Diagnostics.CodeAnalysis;
using NVorbis.Ogg;

namespace NVorbis.Contracts.Ogg
{
    internal interface IPageReader : IDisposable
    {
        void Lock();
        bool Release();

        bool CanSeek { get; }
        long ContainerBits { get; }
        long WasteBits { get; }

        bool ReadNextPage([MaybeNullWhen(false)] out PageData pageData);

        bool ReadPageAt(long offset, [MaybeNullWhen(false)] out PageData pageData);

        bool ReadPageHeaderAt(long offset, Span<byte> headerBuffer);
    }
}
