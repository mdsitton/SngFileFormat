using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NVorbis;

namespace SngCli
{
    public class AudioEncoding
    {
        public static bool verbose = false;

        public static (string filename, byte[]? data) DecodeVorbisToWav(string filePath)
        {
            using (var file = File.OpenRead(filePath))
            using (var vorbis = new VorbisReader(file))
            {
                float[] samples = new float[vorbis.TotalSamples];

                vorbis.ReadSamples(samples, 0, samples.Length);

                var data = WavFileWriter.Get16BitWavData(samples, vorbis.SampleRate, vorbis.Channels);

                Console.WriteLine($"{filePath}: Vorbis expansion Ratio: {(float)data.Length / file.Length:0.00}x larger");
                return (Path.GetFileName(filePath), data);
            }
        }

        public async static Task<(string filename, byte[]? data)> EncodeVorbisToOpus(string filePath, int bitRate)
        {
            var data = DecodeVorbisToWav(filePath);
            if (data.data == null)
                return data;
            // TODO = fix the file names to use opus extension on output
            return await EncodeFileToOpus(filePath, data.data, bitRate);
        }

        public async static Task<(string filename, byte[]? data)> EncodeMp3ToOpus(string filePath, int bitRate)
        {
            var data = await DecodeMp3ToWav(filePath);
            if (data.data == null)
                return data;
            // TODO = fix the file names to use opus extension on output
            return await EncodeFileToOpus(filePath, data.data, bitRate);
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
        /// Encode opus file from byte array, this implementation is a bit less reliable and uses more cpu
        /// </summary>
        public async static Task<(string filename, byte[]? data)> DecodeMp3ToWav(string filePath)
        {

            using (var file = File.OpenRead(filePath))
            {
                var args = $"--decode \"{filePath}\" - ";
                var encodeData = await RunAudioProcess("lame", args, null, verbose);

                var fileName = Path.GetFileName(filePath);

                if (encodeData == null)
                {
                    Console.WriteLine($"{filePath} encode failed");
                    return (fileName, null);
                }

                Console.WriteLine($"{filePath}: Mp3 -> Wav expansion Ratio: {encodeData.Length / (float)file.Length:0.00}x");
                return (fileName, encodeData);
            }

        }

        private async static Task<byte[]?> RunAudioProcess(string processName, string arguments, byte[]? inputBytes = null, bool debug = false)
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

            using Process process = new Process { StartInfo = info };
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

                    if (inputBytes != null)
                    {
                        // Write inputBytes to the process's StandardInput asynchronously
                        writeTask = Task.Run(async () =>
                        {
                            try
                            {
                                using (Stream stdin = process.StandardInput.BaseStream)
                                {
                                    await stdin.WriteAsync(inputBytes, 0, inputBytes.Length);
                                    await stdin.FlushAsync();
                                }
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
        /// Encode opus file from byte array
        /// </summary>
        public async static Task<(string filename, byte[]? data)> EncodeFileToOpus(string filePath, byte[] inputData, int bitRate)
        {
            var args = $"--vbr --framesize 60 --bitrate {bitRate} --discard-pictures --discard-comments - -";
            var encodeData = await RunAudioProcess("opusenc", args, inputData, verbose);

            var fileName = Path.GetFileName(filePath);

            if (encodeData == null)
            {
                Console.WriteLine($"{filePath} encode failed");
                return (fileName, null);
            }


            Console.WriteLine($"{filePath}: Opus Compression Ratio: {inputData.Length / (float)encodeData.Length:0.00}x");
            return (fileName, encodeData);

        }

        /// <summary>
        /// Encode opus file from path
        /// </summary>
        public async static Task<(string filename, byte[]? data)> EncodeFileToOpus(string filePath, int bitRate)
        {
            return await EncodeFileToOpus(filePath, File.ReadAllBytes(filePath), bitRate);
        }
    }
}