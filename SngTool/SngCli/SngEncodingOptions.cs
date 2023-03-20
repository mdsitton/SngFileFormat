using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SongLib;

namespace SngCli
{
    internal class SngEncodingConfig
    {
        public string? InputPath;
        public string? OutputPath;

        // Program options
        public bool NoThreads;
        public bool VideoExclude;
        public bool SkipUnknown;

        // JPEG options
        public bool JpegEncode;
        public bool AlbumUpscale;
        public JpegEncoding.SizeTiers AlbumSize = JpegEncoding.SizeTiers.Size512x512;
        public int JpegQuality = 75;

        // OPUS options
        public bool OpusEncode;
        public int OpusBitrate = 128;

        private static SngEncodingConfig? _instance;
        public static SngEncodingConfig Instance => _instance ?? throw new InvalidOperationException("Not initialized");

        private bool ValidSize(string sizeInput)
        {
            switch (sizeInput)
            {
                case "None":
                case "Nearest":
                case "256x256":
                case "384x384":
                case "512x512":
                case "768x768":
                case "1024x1024":
                case "1536x1536":
                case "2048x2048":
                    return true;
                default:
                    return false;
            }
        }

        private JpegEncoding.SizeTiers SizeStrToEnum(string sizeInput)
        {
            switch (sizeInput)
            {
                case "Nearest":
                    return JpegEncoding.SizeTiers.Nearest;
                case "256x256":
                    return JpegEncoding.SizeTiers.Size256x256;
                case "384x384":
                    return JpegEncoding.SizeTiers.Size384x384;
                case "512x512":
                    return JpegEncoding.SizeTiers.Size512x512;
                case "768x768":
                    return JpegEncoding.SizeTiers.Size768x768;
                case "1024x1024":
                    return JpegEncoding.SizeTiers.Size1024x1024;
                case "1536x1536":
                    return JpegEncoding.SizeTiers.Size1536x1536;
                case "2048x2048":
                    return JpegEncoding.SizeTiers.Size2048x2048;
                default:
                    return JpegEncoding.SizeTiers.None;
            }
        }

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
            VideoExclude = args.TryGetValue("videoExclude", out _);
            OpusEncode = args.TryGetValue("opusEncode", out _);
            JpegEncode = args.TryGetValue("jpegEncode", out _);
            AlbumUpscale = args.TryGetValue("albumUpscale", out _);
            NoThreads = args.TryGetValue("noThreads", out _);
            SkipUnknown = args.TryGetValue("skipUnknown", out _);

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

            if (args.TryGetValue("albumResize", out string? albumSize) && albumSize != null)
            {
                if (!ValidSize(albumSize))
                {
                    Console.WriteLine($"Value for albumResize is not valid {albumSize}");
                    return;
                }
                AlbumSize = SizeStrToEnum(albumSize);
            }
        }
    }
}