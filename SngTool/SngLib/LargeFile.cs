using System.Buffers;
using Cysharp.Collections;

public static class LargeFile
{
    public static async Task<NativeByteArray> ReadAllBytesAsync(string path)
    {
        using (FileStream f = File.OpenRead(path))
        {
            var fileLength = f.Length;
            var arr = new NativeByteArray(fileLength, skipZeroClear: true);
            await arr.ReadFromAsync(f);
            return arr;
        }
    }

    public static void ReadToNativeArray(this Stream stream, NativeByteArray arr, long readCount)
    {

        var writer = arr.CreateBufferWriter();

        long readTotal = 0;
        int read;

        Span<byte> GetWriteSpan()
        {
            long remaining = readCount - readTotal;

            if (remaining > int.MaxValue)
            {
                return writer.GetSpan(int.MaxValue);
            }
            else if (remaining > 0)
            {
                return writer.GetSpan((int)remaining).Slice((int)remaining);
            }
            else
            {
                return Array.Empty<byte>();
            }
        }

        while ((read = stream.Read(GetWriteSpan())) != 0)
        {
            readTotal += read;
            writer.Advance(read);
        }
        // set size to the total number of bytes actually read
        // this won't reallocate the array, 
        arr.Resize(readCount);
    }

    public static void WriteFromNativeArray(this Stream stream, NativeByteArray arr, int chunkSize = int.MaxValue)
    {
        foreach (var item in arr.AsReadOnlyMemoryList(chunkSize))
        {
            stream.Write(item.Span);
        }
    }
}