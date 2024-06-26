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

                Func<long> remainingCallback = () => (vorbis.TotalSamples - vorbis.SamplePosition) * vorbis.Channels;
                WavFileWriter.IngestSamplesDelegate readCallback = (Span<float> buffer) =>
                {
                    var read = vorbis.ReadSamples(buffer);
                    return read * vorbis.Channels;
                };

                using (var writer = new WavFileWriter(readCallback, remainingCallback, vorbis.SampleRate, (ushort)vorbis.Channels, totalSamples))
                {
                    var name = Path.GetFileName(filePath);
                    return (Path.ChangeExtension(name, ".opus"), await EncodeStreamToOpus(writer, bitRate));
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
                var totalSamples = mp3.Length ?? 0;

                if (totalSamples == 0)
                {
                    Console.WriteLine($"{filePath}: Mp3 file has no samples excluding from sng file");
                    return (Path.GetFileName(filePath), null);
                }
                Func<long> remainingCallback = () => mp3.Length!.Value - mp3.Position;
                using (var writer = new WavFileWriter(mp3.ReadSamples, remainingCallback, (ushort)mp3.SampleRate, (ushort)mp3.Channels, totalSamples))
                {
                    var name = Path.GetFileName(filePath);
                    return (Path.ChangeExtension(name, ".opus"), await EncodeStreamToOpus(writer, bitRate));
                }

            }
        }

        private async static Task<NativeByteArray?> RunAudioProcess(string processName, string arguments, Stream? inputStream = null, bool debug = false)
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
            List<string> errorData = new List<string>();
            using (Process process = new Process { StartInfo = info })
            {
                process.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    errorData.Add(e.Data);
                    if (debug)
                    {
                        Console.WriteLine(e.Data);
                    }
                };

                process.Start();

                process.BeginErrorReadLine();

                // Start with a 16mb buffer which should be able to handle the vast majority of cases
                // if it needs to be resized it will be automatically grown as-needed
                NativeByteArray outputData = new NativeByteArray(skipZeroClear: true);

                bool copyError = false;

                Task? writeTask = null;

                // Write inputStream if it exists to the process's StandardInput asynchronously
                if (inputStream != null)
                {
                    writeTask = Task.Run(async () =>
                    {
                        try
                        {
                            using Stream stdin = process.StandardInput.BaseStream;

                            await inputStream.CopyToAsync(stdin);
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
                        Console.WriteLine(string.Join('\n', errorData));
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
        /// Encode opus file from <see cref="MemoryMappedFile" data>
        /// </summary>
        private async static Task<NativeByteArray?> EncodeStreamToOpus(Stream inputData, int bitRate)
        {
            var args = $"--vbr --framesize 60 --bitrate {bitRate} --discard-pictures --discard-comments - -";
            return await RunAudioProcess("opusenc", args, inputData, verbose);
        }

        /// <summary>
        /// Encode opus file from byte array
        /// </summary>
        private async static Task<(string filename, NativeByteArray? data)> EncodeFileToOpus(string filePath, Stream inputData, int bitRate)
        {
            var fileName = Path.GetFileName(filePath);

            // using Stream memFile = file.CreateViewStream();
            var encodeData = await EncodeStreamToOpus(inputData, bitRate);

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
            using Stream fs = File.OpenRead(filePath);
            return await EncodeFileToOpus(filePath, fs, bitRate);
        }
    }
}