using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SongLib;

namespace SngCli
{
    class Program
    {

        public static bool Verbose = false;

        private static readonly List<string> AllowedArguments = new List<string>
        {
            "h", "help",
            "v", "version",
            "o", "out",
            "i", "in",
            "verbose",
            "noThreads",
            "videoExclude",
            "opusEncode",
            "opusBitrate",
            "jpegEncode",
            "jpegQuality",
            "albumUpscale",
            "albumResize",
            "skipUnknown",
            "skipExisting"
        };

        public static void DisplayHelp()
        {
            Console.WriteLine("Usage: SngCli [command] [options]");

            Console.WriteLine("Options:");
            Console.WriteLine("  -h, --help            Show help message");
            Console.WriteLine("  -v, --version         Display version information");
            Console.WriteLine("      --verbose         Display more information such as audio encoder output.");
            Console.WriteLine("\nencode: ");
            Console.WriteLine("  -o, --out FOLDER      Specify output folder location for SNG files");
            Console.WriteLine("  -i, --in FOLDER       Specify input folder to search for song folders");
            Console.WriteLine("      --skipExisting    If the song to be encoded already exists as an sng in the output folder skip it");
            Console.WriteLine("      --skipUnknown     Skip unknown files.By default unknown files are included (All audio and images of supported formats are transcoded)");
            Console.WriteLine("      --noThreads       Disable threading only process one song at a time. Can also be useful when a song has an error along with --verbose.");
            Console.WriteLine("      --videoExclude    Exclude video files");
            Console.WriteLine("      --opusEncode      Encode all audio to opus");
            Console.WriteLine("      --opusBitrate     Set opus encoder bitrate, default: 128");
            Console.WriteLine("      --jpegEncode      Encode all images to JPEG");
            Console.WriteLine("      --jpegQuality     JPEG encoding quality, default: 75");
            Console.WriteLine("      --albumUpscale    Enable upscaling album art, by default images are only shrunk.");
            Console.WriteLine("      --albumResize     Resize album art to set size. Smaller resolutions load faster in-game, Default size: 512x512");
            Console.WriteLine("                            Supported Sizes:");
            Console.WriteLine("                                Nearest - This uses next size below the image size");
            Console.WriteLine("                                256x256");
            Console.WriteLine("                                384x384");
            Console.WriteLine("                                512x512");
            Console.WriteLine("                                768x768");
            Console.WriteLine("                                1024x1024");
            Console.WriteLine("                                1536x1536");
            Console.WriteLine("                                2048x2048");
            Console.WriteLine("\ndecode: ");
            Console.WriteLine("  -o, --out FOLDER      Specify output folder location for extracted song folders");
            Console.WriteLine("  -i, --input FOLDER    Specify input folder to search for SNG files");
            Console.WriteLine("      --noThreads       Disable threading only process one song at a time.");
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
                    case "verbose":
                        AudioEncoding.verbose = Verbose = true;
                        continue;
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
                Console.WriteLine(args[i]);
                if (argPattern.IsMatch(args[i]))
                {
                    string key = argPattern.Replace(args[i], ""); // Remove the '-' or '--' prefix
                    if (i + 1 < args.Length && !argPattern.IsMatch(args[i + 1]))
                    {
                        string value = args[++i];
                        parsedArguments[key] = value;
                        Console.WriteLine(value);
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


        static (string, string[]) GetCommandArg(string[] args)
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("No arguments provided.");
            }

            string firstArg = args[0];
            string[] remainingArgs = args.Skip(1).ToArray();
            return (firstArg, remainingArgs);
        }

        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("No command entered");
                DisplayHelp();
                return;
            }
            (var command, args) = GetCommandArg(args);

            // Proccess commands
            switch (command)
            {
                case "encode":
                    {

                        var cliArgs = ProcessArguments(args);

                        if (cliArgs == null)
                            return;
                        _ = new SngEncodingConfig(cliArgs);
                        await SngEncode.ProcessSongs();
                        break;
                    }
                case "decode":
                    {
                        var cliArgs = ProcessArguments(args);

                        if (cliArgs == null)
                            return;
                        _ = new SngDecodingOptions(cliArgs);
                        await SngDecode.ProcessSongs();
                        break;
                    }
                default:
                    Console.WriteLine("Please specify a command: encode, decode");
                    DisplayHelp();
                    break;
            }
        }
    }
}