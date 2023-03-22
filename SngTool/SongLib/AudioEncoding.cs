using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NVorbis;
using NLayer;

namespace SongLib
{
    public class AudioEncoding
    {
        public static bool verbose = false;

        public static async Task<(string filename, byte[]? data)> ToOpus(string filePath, int bitRate)
        {
            (string filename, byte[]? data) outData;

            // opusenc doesn't support loading mp3 or ogg vorbis
            if (filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                outData = await EncodeMp3ToOpus(filePath, bitRate);
            }
            else if (filePath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                outData = await EncodeVorbisToOpus(filePath, bitRate);
            }
            else
            {
                outData = await EncodeFileToOpus(filePath, bitRate);
            }

            if (outData.data == null)
            {
                Console.WriteLine($"{filePath}: Opus Compression failed");
            }
            return outData;
        }


        /// <summary>
        /// Decode mp3 file and convert into a wav file
        /// </summary>
        private static async Task<(string filename, byte[]? data)> EncodeVorbisToOpus(string filePath, int bitRate)
        {
            using (var ms = new MemoryStream())
            {
                // Read file from disk async since VorbisReader isn't
                using (var file = File.OpenRead(filePath))
                {
                    await file.CopyToAsync(ms);
                }

                using (var vorbis = new VorbisReader(ms))
                {
                    long totalSamples = vorbis.TotalSamples * vorbis.Channels;
                    long fileSize = PcmFileWriter.CalculateSizeEstimate(totalSamples);

                    using (var mmf = MemoryMappedFile.CreateNew(null, fileSize))
                    using (var writer = new PcmFileWriter(mmf, (ushort)vorbis.SampleRate, (ushort)vorbis.Channels, totalSamples))
                    {
                        float[] samples = ArrayPool<float>.Shared.Rent(262144); // 256K floats = 1MiB 
                        var sampleLength = samples.Length;
                        long totalCount = 0;
                        while (!writer.Completed)
                        {
                            var remaining = totalSamples - totalCount;
                            var count = vorbis.ReadSamples(samples, 0, (int)Math.Min(remaining, sampleLength));
                            totalCount += count;
                            writer.IngestSamples(samples.AsSpan(0, count));
                        }
                        ArrayPool<float>.Shared.Return(samples);

                        var name = Path.GetFileName(filePath);
                        return (Path.ChangeExtension(name, ".opus"), await EncodePcmToOpus(writer, bitRate));
                    }
                }
            }
        }

        private static string GetExecutable(string name)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return $"{name}.exe";
            }
            return "lame";
        }

        /// <summary>
        /// Decode mp3 file and convert into a wav file
        /// </summary>
        private static async Task<(string filename, byte[]? data)> EncodeMp3ToOpus(string filePath, int bitRate)
        {
            using (var ms = new MemoryStream())
            {
                // Read file from disk async since VorbisReader isn't
                using (var file = File.OpenRead(filePath))
                {
                    await file.CopyToAsync(ms);
                }

                using (var mp3 = new MpegFile(ms))
                {
                    var totalSamples = mp3.Length;
                    var fileSize = PcmFileWriter.CalculateSizeEstimate(totalSamples);

                    using (var mmf = MemoryMappedFile.CreateNew(null, fileSize))
                    using (var writer = new PcmFileWriter(mmf, (ushort)mp3.SampleRate, (ushort)mp3.Channels, totalSamples))
                    {
                        float[] samples = ArrayPool<float>.Shared.Rent(262144); // 256K floats = 1MiB 
                        var sampleLength = samples.Length;
                        long totalCount = 0;
                        while (!writer.Completed)
                        {
                            var remaining = totalSamples - totalCount;
                            var count = mp3.ReadSamples(samples, 0, (int)Math.Min(remaining, sampleLength));
                            totalCount += count;
                            writer.IngestSamples(samples.AsSpan(0, count));
                        }
                        ArrayPool<float>.Shared.Return(samples);

                        var name = Path.GetFileName(filePath);
                        return (Path.ChangeExtension(name, ".opus"), await EncodePcmToOpus(writer, bitRate));
                    }
                }
            }
        }

        private async static Task<byte[]?> RunAudioProcess(string processName, string arguments, MemoryMappedFile? file = null, bool debug = false)
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = GetExecutable(processName),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = arguments
            };

            using (Process process = new Process { StartInfo = info })
            {
                if (debug)
                {
                    process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        Console.WriteLine(e.Data);
                    };
                }
                process.Start();

                process.BeginErrorReadLine();

                using (MemoryStream outputStream = new MemoryStream())
                {
                    bool copyError = false;

                    Task? writeTask = null;

                    if (file != null)
                    {
                        // Write inputBytes to the process's StandardInput asynchronously
                        writeTask = Task.Run(async () =>
                        {
                            try
                            {
                                using Stream memFile = file.CreateViewStream();
                                using Stream stdin = process.StandardInput.BaseStream;

                                await memFile.CopyToAsync(stdin);
                            }
                            catch (Exception e)
                            {
                                copyError = true;
                                Console.WriteLine($"Error sending data to {processName}");
                                Console.WriteLine(e);
                            }
                        });
                    }

                    // Read outputData from the process's StandardOutput asynchronously
                    Task readTask = Task.Run(async () =>
                    {
                        try
                        {
                            using (Stream stdout = process.StandardOutput.BaseStream)
                            {
                                await stdout.CopyToAsync(outputStream);
                            }
                        }
                        catch (Exception e)
                        {
                            copyError = true;
                            Console.WriteLine($"Error reading data from {processName}");
                            Console.WriteLine(e);
                        }
                    });

                    if (writeTask != null)
                    {
                        // Wait for both tasks to complete
                        await Task.WhenAll(writeTask, readTask);
                    }
                    else
                    {
                        await readTask;
                    }

                    process.WaitForExit();

                    if (process.ExitCode == 1)
                    {
                        Console.WriteLine($"{processName} encoding error!");
                        if (process.ExitCode == 1)
                        {
                            Console.WriteLine(process.StandardError.ReadToEnd());
                        }
                        return null;
                    }

                    if (copyError)
                    {
                        Console.WriteLine($"WARNING: {processName} stopped before full input data was sent, this isn't always bad but double check this audio file you can run verbose mode to get the output of the opus encoder to verify!");
                    }

                    return outputStream.ToArray();
                }
            }
        }

        /// <summary>
        /// Encode opus file from <see cref="PcmFileWriter">
        /// </summary>
        private async static Task<byte[]?> EncodePcmToOpus(PcmFileWriter file, int bitRate)
        {
            var args = $"--vbr --framesize 60 --bitrate {bitRate} --discard-pictures --discard-comments --raw --raw-bits {PcmFileWriter.BitsPerSample} --raw-rate {file.SampleRate} --raw-chan {file.Channels} - -";
            return await RunAudioProcess("opusenc", args, file.mappedFile, verbose);
        }

        /// <summary>
        /// Encode opus file from byte array
        /// </summary>
        private async static Task<(string filename, byte[]? data)> EncodeFileToOpus(string filePath, MemoryMappedFile inputData, int bitRate)
        {
            var args = $"--vbr --framesize 60 --bitrate {bitRate} --discard-pictures --discard-comments - -";
            var encodeData = await RunAudioProcess("opusenc", args, inputData, verbose);

            var fileName = Path.GetFileName(filePath);

            if (encodeData == null)
            {
                Console.WriteLine($"{filePath} encode failed");
                return (fileName, null);
            }

            var name = Path.GetFileNameWithoutExtension(filePath);
            return ($"{name}.opus", encodeData);

        }

        /// <summary>
        /// Encode opus file from path
        /// </summary>
        private async static Task<(string filename, byte[]? data)> EncodeFileToOpus(string filePath, int bitRate)
        {
            using (var mmf = MemoryMappedFile.CreateFromFile(filePath))
            {
                return await EncodeFileToOpus(filePath, mmf, bitRate);
            }
        }
    }
}