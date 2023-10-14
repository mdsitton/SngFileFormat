#nullable enable

#if !NETSTANDARD2_0 && !UNITY_2019_1_OR_NEWER

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cysharp.Collections
{
    public static class NativeMemoryArrayExtensions
    {
        public static async Task ReadFromAsync(this NativeMemoryArray<byte> buffer, Stream stream, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            var writer = buffer.CreateBufferWriter();

            long readTotal = 0;
            int read;
            int GetRemainingStreamChunkLength()
            {
                if (!stream.CanSeek)
                {
                    return 0x1000000; // just request a 16mb chunk if we cannot get a length
                }
                else
                {
                    var totalRemaining = stream.Length - readTotal;
                    if (totalRemaining > int.MaxValue)
                    {
                        return int.MaxValue;
                    }
                    else if (totalRemaining < 0)
                    {
                        return 0x1000000; // just request a 16mb chunk if we don't have any remaining
                    }
                    else
                    {
                        return (int)totalRemaining;
                    }
                }
            }

            while ((read = await stream.ReadAsync(writer.GetMemory(GetRemainingStreamChunkLength()), cancellationToken).ConfigureAwait(false)) != 0)
            {
                readTotal += read;
                progress?.Report(readTotal);
                writer.Advance(read);
            }
            // set size to the total number of bytes actually read
            // this won't reallocate the array, 
            buffer.Resize(readTotal);
        }

        public static async Task WriteToFileAsync(this NativeMemoryArray<byte> buffer, string path, FileMode mode = FileMode.Create, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            using (var fs = new FileStream(path, mode, FileAccess.Write, FileShare.ReadWrite, 1, useAsync: true))
            {
                await buffer.WriteToAsync(fs, progress: progress, cancellationToken: cancellationToken);
            }
        }

        public static async Task WriteToAsync(this NativeMemoryArray<byte> buffer, Stream stream, int chunkSize = int.MaxValue, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            foreach (var item in buffer.AsReadOnlyMemoryList(chunkSize))
            {
                await stream.WriteAsync(item, cancellationToken);
                progress?.Report(item.Length);
            }
        }
    }
}

#endif