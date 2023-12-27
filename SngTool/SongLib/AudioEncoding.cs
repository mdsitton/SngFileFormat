using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NVorbis;
using NLayer;
using Cysharp.Collections;
using System.Net;
using System.Runtime;

namespace SongLib
{
    public class AudioEncoding
    {
        public static bool verbose = false;

        public enum FileType
        {
            Unknown,
            Wav,
            OggVorbis,
            OggOpus,
            OggFlac
        }

        public static FileType DetermineAudioFormat(string filePath)
        {
            using (var fs = File.OpenRead(filePath))
            {
                if (WavParser.IsWav(fs, filePath))
                {
                    return FileType.Wav;
                }
                else if (OggParser.IsOggFile(fs))
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    if (OggParser.IsOggEncoding(fs, OggEncoding.Vorbis, filePath))
                    {
                        return FileType.OggVorbis;
                    }
                    else if (OggParser.IsOggEncoding(fs, OggEncoding.Opus, filePath))
                    {
                        return FileType.OggOpus;
                    }
                    else if (OggParser.IsOggEncoding(fs, OggEncoding.Flac, filePath))
                    {
                        return FileType.OggFlac;
                    }
                }
            }
            return FileType.Unknown;
        }

        public static string GetAudioFormatExtention(FileType fileType)
        {
            switch (fileType)
            {
                case FileType.Wav:
                    return ".wav";
                case FileType.OggFlac:
                case FileType.OggVorbis:
                    return ".ogg";
                case FileType.OggOpus:
                    return ".opus";
                case FileType.Unknown:
                default:
                    return ".mp3";
            }
        }

        public static async Task<(string filename, NativeByteArray? data)> ToOpus(string filePath, int bitRate)
        {
            (string filename, NativeByteArray? data) outData;

            Console.WriteLine($"Encoding {filePath}");

            var fileType = DetermineAudioFormat(filePath);

            switch (fileType)
            {
                case FileType.Wav: // opus enc directly supports wav files
                    outData = await EncodeFileToOpus(filePath, bitRate);
                    break;
                case FileType.OggVorbis:
                    outData = await EncodeVorbisToOpus(filePath, bitRate);
                    break;
                case FileType.OggOpus: // Don't re-encode opus files
                    outData = await ReadFileDataAsOpus(filePath);
                    break;
                case FileType.OggFlac: // opus enc directly supports ogg/flac files
                    outData = await EncodeFileToOpus(filePath, bitRate);
                    break;
                case FileType.Unknown: // fallback to mp3 since we don't have a type checker for it currently
                default:
                    outData = await EncodeMp3ToOpus(filePath, bitRate);
                    break;
            }

            if (outData.data == null)
            {
                Console.WriteLine($"{filePath}: Opus Compression failed");
            }
            return outData;
        }

        private static async Task<(string filename, NativeByteArray? data)> ReadFileDataAsOpus(string filePath)
        {
            var name = Path.GetFileName(filePath);
            return (Path.ChangeExtension(name, ".opus"), await LargeFile.ReadAllBytesAsync(filePath));
        }

        /// <summary>
        /// Decode OGG/Vorbis file and convert into an opus file
        /// </summary>
        private static async Task<(string filename, NativeByteArray? data)> EncodeVorbisToOpus(string filePath, int bitRate)
        {
            using (var fs = File.OpenRead(filePath))
            using (var bs = new BufferedStream(fs))
            using (var vorbis = new VorbisReader(bs, true))
            {
                vorbis.Initialize();
                if (vorbis.TotalSamples == 0)
                {
                    Console.WriteLine($"{filePath}: Vorbis file has no samples excluding from sng file");
                    return (Path.GetFileName(filePath), null);
                }
                long totalSamples = vorbis.TotalSamples * vorbis.Channels;
                long fileSize = PcmFileWriter.CalculateSizeEstimate(totalSamples);

                using (var mmf = MemoryMappedFile.CreateNew(null, fileSize))
                using (var writer = new PcmFileWriter(mmf, vorbis.SampleRate, (ushort)vorbis.Channels, totalSamples))
                {
                    int chunkSize = (int)Math.Min(totalSamples, writer.MaxChunkSamples);
                    float[] samples = new float[chunkSize];
                    var sampleLength = samples.Length;
                    while (true)
                    {
                        var remaining = (vorbis.TotalSamples - vorbis.SamplePosition) * vorbis.Channels;
                        var count = 128;
                        count = vorbis.ReadSamples(samples) * vorbis.Channels;
                        if (count == 0)
                        {
                            break;
                        }
                        writer.IngestSamples(samples.AsSpan(0, count));
                    }

                    var name = Path.GetFileName(filePath);
                    return (Path.ChangeExtension(name, ".opus"), await EncodePcmToOpus(writer, bitRate));
                }
            }
        }

        private static string GetExecutable(string name)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return $"{name}.exe";
            }
            return name;
        }

        /// <summary>
        /// Decode mp3 file and convert into a wav file
        /// </summary>
        private static async Task<(string filename, NativeByteArray? data)> EncodeMp3ToOpus(string filePath, int bitRate)
        {
            using (var fs = File.OpenRead(filePath))
            using (var bs = new BufferedStream(fs))
            using (MpegFile mp3 = new MpegFile(bs, true))
            {
                var totalSamples = mp3.Length!.Value;

                var fileSize = PcmFileWriter.CalculateSizeEstimate(totalSamples);

                if (totalSamples == 0)
                {
                    Console.WriteLine($"{filePath}: Mp3 file has no samples excluding from sng file");
                    return (Path.GetFileName(filePath), null);
                }

                using (var mmf = MemoryMappedFile.CreateNew(null, fileSize))
                using (var writer = new PcmFileWriter(mmf, (ushort)mp3.SampleRate, (ushort)mp3.Channels, totalSamples))
                {
                    int chunkSize = (int)Math.Min(totalSamples, writer.MaxChunkSamples);
                    float[] samples = new float[chunkSize];
                    var sampleLength = samples.Length;
                    while (true)
                    {
                        var remaining = (mp3.Length - mp3.Position).Value;
                        var count = mp3.ReadSamples(samples.AsSpan(0, (int)Math.Min(remaining, sampleLength)));
                        if (count == 0)
                        {
                            break;
                        }
                        writer.IngestSamples(samples.AsSpan(0, count));
                    }
                    var name = Path.GetFileName(filePath);
                    return (Path.ChangeExtension(name, ".opus"), await EncodePcmToOpus(writer, bitRate));
                }

            }
        }

        private async static Task<NativeByteArray?> RunAudioProcess(string processName, string arguments, MemoryMappedFile? file = null, bool debug = false)
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

                // Start with a 16mb buffer which should be able to handle the vast majority of cases
                // if it needs to be resized it will be automatically grown as-needed
                NativeByteArray outputData = new NativeByteArray(skipZeroClear: true);

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
                            await outputData.ReadFromAsync(stdout);
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

                await process.WaitForExitAsync();

                if (process.ExitCode == 1)
                {
                    Console.WriteLine($"{processName} encoding error!");
                    if (process.ExitCode == 1)
                    {
                        Console.WriteLine(await process.StandardError.ReadToEndAsync());
                    }
                    return null;
                }

                if (copyError)
                {
                    Console.WriteLine($"WARNING: {processName} stopped before full input data was sent, this isn't always bad but double check this audio file you can run verbose mode to get the output of the opus encoder to verify!");
                }

                return outputData;
            }
        }

        /// <summary>
        /// Encode opus file from <see cref="PcmFileWriter">
        /// </summary>
        private async static Task<NativeByteArray?> EncodePcmToOpus(PcmFileWriter file, int bitRate)
        {
            var args = $"--vbr --framesize 60 --bitrate {bitRate} --discard-pictures --discard-comments --raw-float --raw-bits {PcmFileWriter.BitsPerSample} --raw-rate {file.SampleRate} --raw-chan {file.Channels} - -";
            return await RunAudioProcess("opusenc", args, file.mappedFile, verbose);
        }

        /// <summary>
        /// Encode opus file from byte array
        /// </summary>
        private async static Task<(string filename, NativeByteArray? data)> EncodeFileToOpus(string filePath, MemoryMappedFile inputData, int bitRate)
        {
            var args = $"--vbr --framesize 60 --bitrate {bitRate} --discard-pictures --discard-comments - -";
            var encodeData = await RunAudioProcess("opusenc", args, inputData, verbose);

            var fileName = Path.GetFileName(filePath);

            if (encodeData == null)
            {
                Console.WriteLine($"{filePath} encode failed");
                return (fileName, null);
            }

            return (Path.ChangeExtension(fileName, ".opus"), encodeData);
        }

        /// <summary>
        /// Encode opus file from path
        /// </summary>
        private async static Task<(string filename, NativeByteArray? data)> EncodeFileToOpus(string filePath, int bitRate)
        {
            using (var mmf = MemoryMappedFile.CreateFromFile(filePath))
            {
                return await EncodeFileToOpus(filePath, mmf, bitRate);
            }
        }
    }
}