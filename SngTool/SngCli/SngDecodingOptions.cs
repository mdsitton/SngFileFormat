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

        public bool NoThreads;

        public SngDecodingOptions(Dictionary<string, string> args)
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
            NoThreads = args.TryGetValue("noThreads", out _);
        }
    }
}