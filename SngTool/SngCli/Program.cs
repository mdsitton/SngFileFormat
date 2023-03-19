using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SngLib;
using SongLib;

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

        private static void DisplayHelp()
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

        private static bool ParseMetadata(SngFile sngFile, string iniPath)
        {
            IniFile iniFile = new IniFile();
            iniFile.Load(iniPath);
            if (iniFile.IsSection("song"))
            {
                var name = iniFile.GetString("song", "name", "");
                var artist = iniFile.GetString("song", "artist", "");
                var album = iniFile.GetString("song", "album", "");
                var genre = iniFile.GetString("song", "genre", "");
                var year = iniFile.GetString("song", "year", "");
                var bandDiff = iniFile.GetInt("song", "diff_band", -1);
                var guitarDiff = iniFile.GetInt("song", "diff_guitar", -1);
                var rhythmDiff = iniFile.GetInt("song", "diff_rhythm", -1);
                var guitarCoopDiff = iniFile.GetInt("song", "diff_guitar_coop", -1);
                var bassDiff = iniFile.GetInt("song", "diff_bass", -1);
                var drumsDiff = iniFile.GetInt("song", "diff_drums", -1);
                var proDrumsDiff = iniFile.GetInt("song", "diff_drums_real", -1);
                var keysDiff = iniFile.GetInt("song", "diff_keys", -1);
                var gHLGuitarDiff = iniFile.GetInt("song", "diff_guitarghl", -1);
                var gHLBassDiff = iniFile.GetInt("song", "diff_bassghl", -1);
                var gHLGuitarCoopDiff = iniFile.GetInt("song", "diff_guitar_coop_ghl", -1);
                var gHLRhythmDiff = iniFile.GetInt("song", "diff_rhythm_ghl", -1);
                var previewStart = iniFile.GetInt("song", "preview_start_time", -1);
                var iconName = iniFile.GetString("song", "icon", "").ToLowerInvariant();
                var playlistTrack = iniFile.GetInt("song", "playlist_track", 16000);
                var modchart = iniFile.GetBool("song", "modchart", false);
                var songLength = iniFile.GetInt("song", "song_length", 0);
                var forceProDrums = iniFile.GetBool("song", "pro_drums", false);
                var forceFiveLane = iniFile.GetBool("song", "five_lane_drums", false);
                var topLevelPlaylist = iniFile.GetString("song", "playlist", "").ToLowerInvariant();
                var subPlaylist = iniFile.GetString("song", "sub_playlist", "").ToLowerInvariant();


                int albumTrack;
                if (iniFile.IsKey("song", "album_track"))
                    albumTrack = iniFile.GetInt("song", "album_track", 16000);
                else
                    albumTrack = iniFile.GetInt("song", "track", 16000);

                var charter = iniFile.GetString("song", iniFile.IsKey("song", "charter") ? "charter" : "frets", "");

                var customHOPO = iniFile.GetInt("song", "hopo_frequency", 0);
                var isEighthHOPO = iniFile.GetBool("song", "eighthnote_hopo", false);
                var multiplierNote = iniFile.GetInt("song", "multiplier_note", 0);
                var offset = iniFile.GetInt("song", "delay", 0);
                var videoStart = iniFile.GetInt("song", "video_start_time", 0);
                var endEventsEnabled = iniFile.GetBool("song", "end_events", true);

                // Save metadata to sng file
                sngFile.SetString("name", name);
                sngFile.SetString("artist", artist);
                sngFile.SetString("album", album);
                sngFile.SetString("genre", genre);
                sngFile.SetString("year", year);
                sngFile.SetInt("diff_band", bandDiff);
                sngFile.SetInt("diff_guitar", guitarDiff);
                sngFile.SetInt("diff_rhythm", rhythmDiff);
                sngFile.SetInt("diff_guitar_coop", guitarCoopDiff);
                sngFile.SetInt("diff_bass", bassDiff);
                sngFile.SetInt("diff_drums", drumsDiff);
                sngFile.SetInt("diff_drums_real", proDrumsDiff);
                sngFile.SetInt("diff_keys", keysDiff);
                sngFile.SetInt("diff_guitarghl", gHLGuitarDiff);
                sngFile.SetInt("diff_bassghl", gHLBassDiff);
                sngFile.SetInt("diff_guitar_coop_ghl", gHLGuitarCoopDiff);
                sngFile.SetInt("diff_rhythm_ghl", gHLRhythmDiff);
                sngFile.SetInt("preview_start_time", previewStart);
                sngFile.SetString("icon", iconName);
                sngFile.SetInt("playlist_track", playlistTrack);
                sngFile.SetBool("modchart", modchart);
                sngFile.SetInt("song_length", songLength);
                sngFile.SetBool("pro_drums", forceProDrums);
                sngFile.SetBool("five_lane_drums", forceFiveLane);
                sngFile.SetString("playlist", topLevelPlaylist);
                sngFile.SetString("sub_playlist", subPlaylist);
                sngFile.SetInt("album_track", albumTrack);
                sngFile.SetString("album_track", charter);
                sngFile.SetInt("hopo_frequency", customHOPO);
                sngFile.SetBool("eighthnote_hopo", isEighthHOPO);
                sngFile.SetInt("multiplier_note", multiplierNote);
                sngFile.SetInt("delay", offset);
                sngFile.SetInt("video_start_time", videoStart);
                sngFile.SetBool("end_events", endEventsEnabled);

                // TODO - should we automatically parse any ch unrecognized tags and pass them in as-is?
                return true;
            }
            else
            {
                return false;
            }
        }

        private static async Task EncodeSong(string songFolder, bool opusEncode, int opusBitrate, string outputFolder)
        {
            SngFile sngFile = new SngFile();
            Random.Shared.NextBytes(sngFile.Seed);

            (string name, byte[]? data) fileData = ("", null);

            var fileList = Directory.GetFiles(songFolder);

            foreach (var file in fileList)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    )
                {
                    if (opusEncode && !fileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase))
                    {
                        // opusenc doesn't support loading mp3 or ogg vorbis
                        if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                        {
                            fileData = await AudioEncoding.EncodeMp3ToOpus(file, opusBitrate);
                        }
                        else if (fileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                        {
                            fileData = await AudioEncoding.EncodeVorbisToOpus(file, opusBitrate);
                        }
                        else
                        {
                            fileData = await AudioEncoding.EncodeFileToOpus(file, opusBitrate);
                        }
                    }
                    else
                    {
                        fileData = (Path.GetFileName(file), File.ReadAllBytes(file));
                    }
                }
                else if (string.Equals(fileName, "song.ini", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ParseMetadata(sngFile, file))
                    {
                        Console.WriteLine($"Error: Failed to parse metadata for chart {songFolder}");
                        return;
                    }
                    continue;
                }
                else if (string.Equals(fileName, "notes.mid", StringComparison.OrdinalIgnoreCase))
                {
                    fileData = ("notes.mid", File.ReadAllBytes(file));
                }
                else if (string.Equals(fileName, "notes.chart", StringComparison.OrdinalIgnoreCase))
                {
                    fileData = ("notes.chart", File.ReadAllBytes(file));
                }

                if (fileData.data != null)
                {
                    sngFile.AddFile(fileData.name, new SngFile.FileData { Masked = true, Contents = fileData.data });
                }
            }
            var saveFile = $"{Path.GetFileName(songFolder)}.sng";
            Console.WriteLine($"Saving file: {saveFile}");
            SngSerializer.SaveSngFile(sngFile, Path.Combine(outputFolder, saveFile));
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
            bool noThreads = args.TryGetValue("noThreads", out _);

            AudioEncoding.verbose = args.TryGetValue("verbose", out _);

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
            if (noThreads)
            {
                foreach (var songFolder in songFolders)
                {
                    await EncodeSong(songFolder, encodeOpus, opusBitrate, output);
                }
            }
            else
            {
                await Parallel.ForEachAsync(songFolders, async (songFolder, token) =>
                {
                    await EncodeSong(songFolder, encodeOpus, opusBitrate, output);
                });
            }
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