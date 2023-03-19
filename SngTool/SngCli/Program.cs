using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace SngCli
{
    class Program
    {

        private static readonly List<string> AllowedArguments = new List<string>
        {
            "h", "help",
            "v", "version",
            "o", "output",
            "i", "input",
            "excludeVideo",
            "encodeOpus",
            "opusBitrate",
            "encodeJpeg",
            "jpegQuality",
            "forceSize",
            "resize"
        };

        private static void DisplayHelp()
        {
            Console.WriteLine("Usage: SngCli [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -h, --help          Show help message");
            Console.WriteLine("  -v, --version       Display version information");
            Console.WriteLine("  -o, --out FOLDER    Specify output folder location");
            Console.WriteLine("  -i, --input FOLDER  Specify input folder to search for song folders");
            Console.WriteLine("      --excludeVideo  Exclude video files, CH doesn't support videos in sng files so they can be excluded to reduce size.");
            Console.WriteLine("      --encodeOpus    Encode all audio to opus");
            Console.WriteLine("      --opusBitrate   Set opus encoder bitrate, default: 80");
            Console.WriteLine("      --encodeJpeg    Encode all images to JPEG");
            Console.WriteLine("      --jpegQuality   JPEG encoding quality, default: 75");
            Console.WriteLine("      --forceSize     Enable forcing image size to the specified resolution, by default if the image is smaller than specified the image is not resized.");
            Console.WriteLine("      --resize        Resize images to set size, default: 512x512");
            Console.WriteLine("                          Supported Sizes:");
            Console.WriteLine("                              Nearest: This uses next size below the image size");
            Console.WriteLine("                              256x256");
            Console.WriteLine("                              384x384");
            Console.WriteLine("                              512x512");
            Console.WriteLine("                              768x768");
            Console.WriteLine("                              1024x1024");
            Console.WriteLine("                              1536x1536");
            Console.WriteLine("                              2048x2048");
        }

        private static Dictionary<string, string>? ProcessArguments(string[] args)
        {

            var cliArgs = ParseArguments(args);

            if (cliArgs == null || cliArgs.Count == 0)
            {
                DisplayHelp();
                return null;
            }

            foreach ((var key, var val) in cliArgs)
            {
                Console.WriteLine($"{key} - {val}");
            }

            // Process any specific action arguments that should be
            // actioned immediately
            foreach ((string key, string value) in cliArgs)
            {
                if (!AllowedArguments.Contains(key))
                {
                    Console.WriteLine($"Invalid command line flag used: {key}");
                    DisplayHelp();
                    return null;
                }
                switch (key)
                {
                    case "h":
                    case "help":
                        DisplayHelp();
                        return null;
                    case "v":
                    case "version":
                        DisplayHelp();
                        return null;
                    default:
                        continue;
                }
            }

            return cliArgs;

        }

        private static List<string> SearchForFolders(string rootFolder)
        {
            List<string> validSubfolders = new List<string>();
            string[] subfolders = Directory.GetDirectories(rootFolder);

            foreach (string subfolder in subfolders)
            {
                if (IsValidSubfolder(subfolder))
                {
                    validSubfolders.Add(subfolder);
                    continue;
                }

                validSubfolders.AddRange(SearchForFolders(subfolder));
            }

            return validSubfolders;
        }

        private static bool IsValidSubfolder(string subfolder)
        {
            string[] files = Directory.GetFiles(subfolder);
            bool hasMidiOrChart = files.Any(f => f.EndsWith(".midi") || f.EndsWith(".chart"));
            bool hasAudioFile = files.Any(f => f.EndsWith(".wav") || f.EndsWith(".ogg") || f.EndsWith(".opus") || f.EndsWith(".mp3"));
            bool hasSongIni = files.Any(f => f.EndsWith("song.ini"));

            return hasMidiOrChart && hasAudioFile && hasSongIni;
        }


        private static Dictionary<string, string>? ParseArguments(string[] args)
        {
            Dictionary<string, string> parsedArguments = new Dictionary<string, string>();
            Regex argPattern = new Regex("^-{1,2}");

            for (int i = 0; i < args.Length; i++)
            {
                if (argPattern.IsMatch(args[i]))
                {
                    string key = argPattern.Replace(args[i], ""); // Remove the '-' or '--' prefix
                    if (i + 1 < args.Length && !argPattern.IsMatch(args[i + 1]))
                    {
                        string value = args[++i];
                        parsedArguments[key] = value;
                    }
                    else
                    {
                        // Flag without value (boolean flag)
                        parsedArguments[key] = "true";
                    }
                }
                else
                {
                    Console.WriteLine("Unexpected argument format: " + args[i]);
                    return null;
                }
            }

            return parsedArguments;
        }

        private static async Task EncodeSong(string songFolder, bool opusEncode, int opusBitrate)
        {

            foreach (var file in Directory.GetFiles(songFolder))
            {
                (string name, byte[]? data) audioFile;
                if (file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    )
                {

                    if (opusEncode && !file.EndsWith(".opus", StringComparison.OrdinalIgnoreCase))
                    {
                        // opusenc doesn't support loading mp3 or ogg vorbis
                        if (file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                        {
                            audioFile = await AudioEncoding.EncodeMp3ToOpus(file, opusBitrate);
                        }
                        else if (file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                        {
                            audioFile = await AudioEncoding.EncodeVorbisToOpus(file, opusBitrate);
                        }
                        else
                        {
                            audioFile = await AudioEncoding.EncodeFileToOpus(file, opusBitrate);
                        }
                    }
                    else
                    {
                        audioFile = (Path.GetFileName(file), File.ReadAllBytes(file));
                    }
                }
            }
        }


        private static async Task ProcessSongs(Dictionary<string, string> args)
        {
            // Validate command line arguments
            if (!args.TryGetValue("input", out var input) || (input == null && !args.TryGetValue("i", out input)))
            {
                Console.WriteLine("Input folder argument not found:");
                DisplayHelp();
                return;
            }
            if (!args.TryGetValue("output", out string? output) || (output == null && !args.TryGetValue("o", out output)))
            {
                Console.WriteLine("Output folder argument not found:");
                DisplayHelp();
                return;
            }

            // Bool flags we just need to make sure the keys exist
            bool excludeVideo = args.TryGetValue("excludeVideo", out _);
            bool encodeOpus = args.TryGetValue("encodeOpus", out _);
            bool encodeJpeg = args.TryGetValue("encodeJpeg", out _);
            bool forceSize = args.TryGetValue("forceSize", out _);
            bool resize = args.TryGetValue("resize", out _);

            int opusBitrate = 80;
            if (args.TryGetValue("opusBitrate", out string? bitrateStr) && bitrateStr != null)
            {
                if (!int.TryParse(bitrateStr, out opusBitrate))
                {
                    Console.WriteLine($"Value for opusBitrate is not valid {bitrateStr}");
                    return;
                }
            }

            int jpegQuality = 75;
            if (args.TryGetValue("jpegQuality", out string? jpegQualityStr) && jpegQualityStr != null)
            {
                if (!int.TryParse(jpegQualityStr, out jpegQuality))
                {
                    Console.WriteLine($"Value for jpegQuality is not valid {jpegQualityStr}");
                    return;
                }
            }


            Console.WriteLine("SngCli scanning song folders");

            List<string> songFolders = SearchForFolders(input!);
            await Parallel.ForEachAsync(songFolders, async (songFolder, token) =>
            {
                await EncodeSong(songFolder, encodeOpus, opusBitrate);
            });
        }

        static async Task Main(string[] args)
        {
            var cliArgs = ProcessArguments(args);

            if (cliArgs == null)
                return;

            await ProcessSongs(cliArgs);
        }
    }
}