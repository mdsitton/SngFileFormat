
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NLayer;

namespace YourNamespace
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: mp3test <filename>");
                return;
            }

            string filePath = args[0];

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using (var fs = File.OpenRead(filePath))
            using (var bs = new BufferedStream(fs))
            using (MpegFile mp3 = new MpegFile(bs, true))
            using (var fw = File.OpenWrite(Path.ChangeExtension(filePath, ".raw")))
            {
                var totalSamples = mp3.Length ?? 0;

                if (totalSamples == 0)
                {
                    Console.WriteLine($"{filePath}: Mp3 file has no samples excluding from sng file");
                    return;
                }

                Console.WriteLine($"Decoding {filePath} to {Path.ChangeExtension(filePath, ".raw")}");
                Console.WriteLine($"Sample rate: {mp3.SampleRate} Hz, Channels: {mp3.Channels}");

                Span<float> writeBuffer = stackalloc float[65536];
                while (mp3.Length!.Value - mp3.Position > 0)
                {
                    var samples = mp3.ReadSamples(writeBuffer);
                    if (samples == 0)
                    {
                        break;
                    }
                    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(writeBuffer.Slice(0, samples));
                    fw.Write(bytes);
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"Execution time: {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}