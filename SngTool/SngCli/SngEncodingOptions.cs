using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SngCli
{
    public class SngEncodingConfig
    {
        public string? InputPath;
        public string? OutputPath;

        // Program options
        public bool NoThreads;
        public bool ExcludeVideo;
        public bool Verbose;

        // JPEG options
        public bool EncodeJpeg;
        public bool ForceSize;
        public bool Resize;
        public int JpegQuality = 75;

        // OPUS options
        public bool EncodeOpus;
        public int OpusBitrate = 80;

        private static SngEncodingConfig? _instance;
        public static SngEncodingConfig Instance => _instance ?? throw new InvalidOperationException("Not initialized");

        public SngEncodingConfig(Dictionary<string, string> args)
        {
            _instance = this;
            // Validate command line arguments
            if (!args.TryGetValue("input", out InputPath) || (InputPath == null && !args.TryGetValue("i", out InputPath)))
            {
                Console.WriteLine("Input folder argument not found:");
                Program.DisplayHelp();
                return;
            }
            if (!args.TryGetValue("output", out OutputPath) || (OutputPath == null && !args.TryGetValue("o", out OutputPath)))
            {
                Console.WriteLine("Output folder argument not found:");
                Program.DisplayHelp();
                return;
            }

            // Bool flags we just need to make sure the keys exist
            ExcludeVideo = args.TryGetValue("excludeVideo", out _);
            EncodeOpus = args.TryGetValue("encodeOpus", out _);
            EncodeJpeg = args.TryGetValue("encodeJpeg", out _);
            ForceSize = args.TryGetValue("forceSize", out _);
            Resize = args.TryGetValue("resize", out _);
            NoThreads = args.TryGetValue("noThreads", out _);

            AudioEncoding.verbose = Verbose = args.TryGetValue("verbose", out _);

            if (args.TryGetValue("opusBitrate", out string? bitrateStr) && bitrateStr != null)
            {
                if (!int.TryParse(bitrateStr, out OpusBitrate))
                {
                    Console.WriteLine($"Value for opusBitrate is not valid {bitrateStr}");
                    return;
                }
            }

            if (args.TryGetValue("jpegQuality", out string? jpegQualityStr) && jpegQualityStr != null)
            {
                if (!int.TryParse(jpegQualityStr, out JpegQuality))
                {
                    Console.WriteLine($"Value for jpegQuality is not valid {jpegQualityStr}");
                    return;
                }
            }
        }
    }
}