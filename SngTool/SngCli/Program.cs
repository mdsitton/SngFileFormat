using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SngLib;
using SongLib;
using static SngCli.JpegEncoding;

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
            "verbose",
            "noThreads",
            "excludeVideo",
            "encodeOpus",
            "opusBitrate",
            "encodeJpeg",
            "jpegQuality",
            "forceSize",
            "resize"
        };

        public static void DisplayHelp()
        {
            Console.WriteLine("Usage: SngCli [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  -h, --help          Show help message");
            Console.WriteLine("  -v, --version       Display version information");
            Console.WriteLine("      --verbose       Display more information such as audio encoder output.");
            Console.WriteLine("  -o, --out FOLDER    Specify output folder location");
            Console.WriteLine("  -i, --input FOLDER  Specify input folder to search for song folders");
            Console.WriteLine("      --noThreads     Disable threading only process one song at a time. Can also be useful when a song has an error along with --verbose.");
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

        static async Task Main(string[] args)
        {
            var cliArgs = ProcessArguments(args);

            if (cliArgs == null)
                return;

            // TODO - Implement encode vs decode vs inspect commands
            var encodeConfig = new SngEncodingConfig(cliArgs);

            await SngEncode.ProcessSongs();
        }
    }
}