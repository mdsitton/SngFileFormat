using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SngCli
{


    public class SngDecodingOptions
    {
        private static SngDecodingOptions? _instance;
        public static SngDecodingOptions Instance => _instance ?? throw new InvalidOperationException("Not initialized");

        public string? InputPath;
        public string? OutputPath;

        public short Threads;
        public bool StatusBar;

        public SngDecodingOptions(Dictionary<string, string> args)
        {
            _instance = this;
            // Validate command line arguments
            if (!args.TryGetValue("in", out InputPath) || (InputPath == null && !args.TryGetValue("i", out InputPath)))
            {
                Console.WriteLine("Input folder argument not found:");
                Program.DisplayHelp();
                return;
            }
            if (!args.TryGetValue("out", out OutputPath) || (OutputPath == null && !args.TryGetValue("o", out OutputPath)))
            {
                Console.WriteLine("Output folder argument not found:");
                Program.DisplayHelp();
                return;
            }

            string? count;
            if (!((args.TryGetValue("threads", out count) || args.TryGetValue("t", out count)) && short.TryParse(count, out Threads)))
            {
                Threads = -1;
            }

            StatusBar = !args.TryGetValue("noStatusBar", out _);
        }
    }
}