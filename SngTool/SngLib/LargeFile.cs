using System.Buffers;
using Cysharp.Collections;

public static class LargeFile
{
    public static async Task<NativeMemoryArray<byte>> ReadAllBytesAsync(string path)
    {
        using (FileStream f = File.OpenRead(path))
        {
            var fileLength = f.Length;
            var arr = new NativeMemoryArray<byte>(fileLength, skipZeroClear: true);
            await arr.ReadFromAsync(f);
            return arr;
        }
    }

    public static void ReadToNativeArray(this Stream stream, NativeMemoryArray<byte> arr)
    {
        var writer = arr.CreateBufferWriter();

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

        while ((read = stream.Read(writer.GetSpan(GetRemainingStreamChunkLength()))) != 0)
        {
            readTotal += read;
            writer.Advance(read);
        }
        // set size to the total number of bytes actually read
        // this won't reallocate the array, 
        arr.Resize(readTotal);
    }

    // public static async Task WriteToAsync(this NativeMemoryArray<byte> buffer, Stream stream, int chunkSize = int.MaxValue, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    // {
    //     foreach (var item in buffer.AsReadOnlyMemoryList(chunkSize))
    //     {
    //         await stream.WriteAsync(item, cancellationToken);
    //         progress?.Report(item.Length);
    //     }
    // }

    public static void WriteFromNativeArray(this Stream stream, NativeMemoryArray<byte> arr, int chunkSize = int.MaxValue)
    {
        foreach (var item in arr.AsReadOnlyMemoryList(chunkSize))
        {
            stream.Write(item.Span);
        }
    }
}