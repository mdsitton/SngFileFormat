using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Collections;
using SngLib;
using SongLib;

namespace SngCli
{
    public static class SngEncode
    {
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
            bool hasChart = files.Any(f => f.EndsWith(".chart"));
            bool hasMidi = files.Any(f => f.EndsWith(".mid"));
            bool hasAudioFile = files.Any(f => f.EndsWith(".wav") || f.EndsWith(".ogg") || f.EndsWith(".opus") || f.EndsWith(".mp3"));
            bool hasSongIni = files.Any(f => f.EndsWith("song.ini"));

            return (hasChart || hasMidi) && hasAudioFile && (hasSongIni || hasChart);
        }

        public static void ReadFeedbackChartMetadata(SngFile sngFile, string chartPath)
        {
            // Already parsed metadata
            if (sngFile.metadataAvailable)
            {
                return;
            }
            using (Stream stream = File.Open(chartPath, FileMode.Open))
            {
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        string? lineFull = reader.ReadLine();

                        if (lineFull == null)
                        {
                            break;
                        }
                        ReadOnlySpan<char> trimString = lineFull;
                        trimString = trimString.Trim();

                        // quit reading once we hit the end of the first .chart section
                        if (trimString.EndsWith("}"))
                            break;

                        var sepPos = trimString.IndexOf("=");

                        // Skip any lines that don't have a key value pair
                        if (sepPos == -1)
                        {
                            continue;
                        }

                        // parse key
                        var keySpan = trimString.Slice(0, sepPos).Trim();

                        // Parse value
                        var valueSpan = trimString.Slice(sepPos + 1).Trim();
                        int quotePosStart = valueSpan.IndexOf("\"");

                        // Remove any quotes around values
                        // CH removes any quotes within the value, however i've
                        // opted to only remove the outer set of quotes if they
                        // exist, any quotes within are not touched.
                        if (quotePosStart != -1)
                        {
                            int quotePosEnd = valueSpan.LastIndexOf("\"");

                            // We only have a single quote in this string
                            // some older malformed charts do have this unfortunately
                            if (quotePosStart == quotePosEnd)
                            {
                                if (quotePosStart == 0)
                                {
                                    // remove the first character
                                    valueSpan = valueSpan.Slice(1);
                                }
                                else if (quotePosStart == (valueSpan.Length - 1))
                                {
                                    // remove the last character
                                    valueSpan = valueSpan.Slice(0, valueSpan.Length - 1);
                                }
                            }
                            // This means we should have atleast 2 different quotes
                            else
                            {
                                // only remove the first and last quote don't touch others
                                if (quotePosStart == 0)
                                {
                                    // remove the first character
                                    valueSpan = valueSpan.Slice(1);
                                    quotePosEnd--; // offset end position
                                }

                                if (quotePosEnd == (valueSpan.Length - 1))
                                {
                                    // remove the last character
                                    valueSpan = valueSpan.Slice(0, valueSpan.Length - 1);
                                }
                            }
                        }

                        valueSpan = valueSpan.Trim();

                        // skip any empty keys
                        if (valueSpan.IsEmpty || valueSpan.IsWhiteSpace())
                        {
                            continue;
                        }

                        switch (keySpan)
                        {
                            case "Name":
                                sngFile.SetString("name", valueSpan.ToString());
                                break;
                            case "Artist":
                                sngFile.SetString("artist", valueSpan.ToString());
                                break;
                            case "Genre":
                                sngFile.SetString("genre", valueSpan.ToString());
                                break;
                            case "Charter":
                                sngFile.SetString("charter", valueSpan.ToString());
                                break;
                            case "Year":
                                // Some older charts have a comma in front of the year
                                // to make songs look nicer in GH3 but we want to remove
                                // this for most modern games
                                if (valueSpan.StartsWith(", "))
                                {
                                    valueSpan = valueSpan.Slice(2);
                                }
                                sngFile.SetString("year", valueSpan.ToString());
                                break;
                            case "Album":
                                sngFile.SetString("album", valueSpan.ToString());
                                break;
                            case "Offset":
                                sngFile.SetInt("delay", (int)Math.Ceiling(float.Parse(valueSpan) * 1000));
                                break;
                            case "PreviewStart":
                                sngFile.SetInt("preview_start_time", (int)Math.Ceiling(float.Parse(valueSpan) * 1000));
                                break;
                        }
                    }
                }
            }
            sngFile.metadataAvailable = true;
        }

        private static bool ParseMetadata(SngFile sngFile, string iniPath)
        {
            // Already parsed metadata
            if (sngFile.metadataAvailable)
            {
                return false;
            }
            // Manually parse the types of known keys so that we know they are correct
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
                var loadingPhrase = iniFile.GetString("song", "loading_phrase", "");

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
                sngFile.SetString("charter", charter);
                sngFile.SetInt("hopo_frequency", customHOPO);
                sngFile.SetBool("eighthnote_hopo", isEighthHOPO);
                sngFile.SetInt("multiplier_note", multiplierNote);
                sngFile.SetInt("delay", offset);
                sngFile.SetInt("video_start_time", videoStart);
                sngFile.SetBool("end_events", endEventsEnabled);
                sngFile.SetString("loading_phrase", loadingPhrase);

                foreach (var keyName in iniFile.GetKeyNames("song"))
                {
                    if (KnownKeys.IsKnownKey(keyName))
                    {
                        continue;
                    }

                    if (iniFile.TryGetBool("song", keyName, out var boolVal))
                    {
                        if (Program.Verbose)
                        {
                            ConMan.Out($"Unknown Key: {keyName} Value: {boolVal} BOOL");
                        }
                        sngFile.SetBool(keyName, boolVal);
                    }
                    else if (iniFile.TryGetInt("song", keyName, out var intVal))
                    {
                        if (Program.Verbose)
                        {
                            ConMan.Out($"Unknown Key: {keyName} Value: {intVal} INT");
                        }
                        sngFile.SetInt(keyName, intVal);
                    }
                    else if (iniFile.TryGetInt("song", keyName, out var floatVal))
                    {
                        if (Program.Verbose)
                        {
                            ConMan.Out($"Unknown Key: {keyName} Value: {floatVal} FLOAT");
                        }
                        sngFile.SetFloat(keyName, floatVal);
                    }
                    else if (iniFile.TryGetString("song", keyName, out var stringVal))
                    {
                        if (Program.Verbose)
                        {
                            ConMan.Out($"Unknown Key: {keyName} Value: {stringVal} STRING");
                        }
                        sngFile.SetString(keyName, stringVal);
                    }
                }
                sngFile.metadataAvailable = true;

                return true;
            }
            else
            {
                return false;
            }
        }

        private static readonly string videoPattern = @"(?i).*\.(mp4|avi|webm|vp8|ogv|mpeg)$";
        private static Regex videoRegex = new Regex(videoPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string imagePattern = @"(?i).*\.(png|jpg|jpeg)$";
        private static Regex imageRegex = new Regex(imagePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string audioPattern = @"(?i).*\.(wav|opus|ogg|mp3)$";
        private static Regex audioRegex = new Regex(audioPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string[] supportedImageNames = { "background", "highway", "album" };

        private static readonly string[] supportedAudioNames =
        {
            "guitar",
            "bass",
            "rhythm",
            "vocals",
            "vocals_1",
            "vocals_2",
            "drums",
            "drums_1",
            "drums_2",
            "drums_3",
            "drums_4",
            "keys",
            "song",
            "crowd",
            "preview"
        };

        private static readonly string[] excludeFiles =
        {
            "desktop.ini",
            ".DS_Store",
            "ps.dat",
            "ch.dat"
        };

        private static readonly string[] excludeFolders =
        {
            "__MACOSX"
        };

        private static bool MatchesNames(string fileName, string[] names)
        {
            ReadOnlySpan<char> spanFile = fileName;
            spanFile = spanFile.Slice(0, spanFile.LastIndexOf("."));

            Span<char> strSpan = stackalloc char[spanFile.Length];
            spanFile.ToLowerInvariant(strSpan);

            return names.Contains(strSpan.ToString());
        }

        private static bool PathsHasFileName(string fileName, string[] filePaths)
        {
            foreach (var path in filePaths)
            {
                if (path.EndsWith(fileName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<(string filename, NativeByteArray? data)> EncodeAudio(string fileName, string filePath, bool opusEncode = false)
        {
            var conf = SngEncodingConfig.Instance;
            if (opusEncode && !fileName.EndsWith(".opus", StringComparison.OrdinalIgnoreCase))
            {
                return await AudioEncoding.ToOpus(filePath, conf.OpusBitrate);
            }
            else
            {
                return (fileName, await LargeFile.ReadAllBytesAsync(filePath));
            }
        }

        private static async Task<(string filename, NativeByteArray? data)> EncodeImage(string fileName, string filePath, bool upscale = false, JpegEncoding.SizeTiers size = JpegEncoding.SizeTiers.Size512x512, bool encodeJpeg = false)
        {
            var conf = SngEncodingConfig.Instance;
            if (encodeJpeg)
            {
                return await JpegEncoding.EncodeImageToJpeg(filePath, conf.JpegQuality, upscale, size);
            }
            else
            {
                return (fileName, await LargeFile.ReadAllBytesAsync(filePath));
            }
        }

        private static async Task<(long startingSize, long encodedSize)> EncodeSubFolder(SngFile sngFile, string folder, string folderRelativePath)
        {

            string MakeFileName(string fileName)
            {
                return $"{folderRelativePath}/{fileName}";
            }

            var fileList = Directory.GetFiles(folder);

            var conf = SngEncodingConfig.Instance;
            (string name, NativeByteArray? data) fileData = ("", null);

            long startingSize = 0;
            long endSize = 0;
            foreach (var file in fileList)
            {
                FileInfo fileInfo = new FileInfo(file);
                startingSize += fileInfo.Length;
                var fileName = Path.GetFileName(file);
                if (excludeFiles.Contains(file))
                {
                    continue;
                }

                if (audioRegex.IsMatch(file) && !conf.SkipUnknown)
                {
                    bool encodeOpus = conf.OpusEncode || conf.EncodeUnknown;
                    fileData = await EncodeAudio(MakeFileName(fileName), file, encodeOpus);
                }
                else if (imageRegex.IsMatch(file) && !conf.SkipUnknown)
                {
                    bool encodeJpg = conf.JpegEncode || conf.EncodeUnknown;
                    fileData = await EncodeImage(MakeFileName(fileName), file, false, JpegEncoding.SizeTiers.None, encodeJpg);
                }
                else
                {
                    if (conf.SkipUnknown)
                    {
                        continue;
                    }
                    fileData = (fileName, await LargeFile.ReadAllBytesAsync(file));
                }
                if (fileData.data != null)
                {
                    endSize += fileData.data.Length;
                    sngFile.AddFile(fileData.name, fileData.data);
                }
            }
            return (startingSize, endSize);
        }

        private static async Task<(long startingSize, long encodedSize)> EncodeFolder(SngFile sngFile, string folder)
        {

            string MakeFileName(string fileName, bool knownFile)
            {
                return knownFile ? fileName.ToLowerInvariant() : fileName;
            }
            var conf = SngEncodingConfig.Instance;
            (string name, NativeByteArray? data) fileData = ("", null);


            var fileList = Directory.GetFiles(folder);
            bool hasIniFile = PathsHasFileName("song.ini", fileList);

            long startingSize = 0;
            long endSize = 0;
            foreach (var file in fileList)
            {
                FileInfo fileInfo = new FileInfo(file);
                startingSize += fileInfo.Length;
                var fileName = Path.GetFileName(file);
                if (audioRegex.IsMatch(file))
                {
                    var knownAudio = MatchesNames(fileName, supportedAudioNames);
                    if (!conf.SkipUnknown || knownAudio)
                    {
                        bool encodeOpus = conf.OpusEncode || conf.EncodeUnknown;
                        fileData = await EncodeAudio(MakeFileName(fileName, knownAudio), file, encodeOpus);
                    }
                }
                else if (string.Equals(fileName, "song.ini", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ParseMetadata(sngFile, file))
                    {
                        ConMan.Out($"Error: Failed to parse metadata for chart {folder}");
                        return (-1, -1);
                    }
                    continue;
                }
                else if (string.Equals(fileName, "notes.mid", StringComparison.OrdinalIgnoreCase))
                {
                    fileData = ("notes.mid", await LargeFile.ReadAllBytesAsync(file));
                }
                else if (string.Equals(fileName, "notes.chart", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasIniFile)
                    {
                        ReadFeedbackChartMetadata(sngFile, file);
                    }
                    fileData = ("notes.chart", await LargeFile.ReadAllBytesAsync(file));
                }
                else if (imageRegex.IsMatch(file))
                {
                    var knownImage = MatchesNames(fileName, supportedImageNames);
                    if (fileName.StartsWith("album", StringComparison.OrdinalIgnoreCase))
                    {
                        fileData = await EncodeImage(MakeFileName(fileName, true), file, conf.AlbumUpscale, conf.AlbumSize, conf.JpegEncode);
                    }
                    else if (!conf.SkipUnknown)
                    {
                        bool encodeJpg = conf.JpegEncode || conf.EncodeUnknown;
                        fileData = await EncodeImage(MakeFileName(fileName, knownImage), file, false, JpegEncoding.SizeTiers.None, encodeJpg);
                    }
                }
                else if (videoRegex.IsMatch(file) && fileName.StartsWith("video", StringComparison.OrdinalIgnoreCase))
                {
                    if (conf.VideoExclude)
                    {
                        continue;
                    }
                    fileData = (MakeFileName(fileName, true), await LargeFile.ReadAllBytesAsync(file));
                }
                else // Include other unknown files
                {
                    if (conf.SkipUnknown)
                    {
                        continue;
                    }
                    fileData = (fileName, await LargeFile.ReadAllBytesAsync(file));
                }

                if (fileData.data != null)
                {
                    endSize += fileData.data.Length;
                    sngFile.AddFile(fileData.name, fileData.data);
                }
            }
            return (startingSize, endSize);
        }

        private static async Task EncodeSong(string songFolder)
        {
            var conf = SngEncodingConfig.Instance;

            var folder = Path.GetDirectoryName(songFolder)!;
            var relative = Path.GetRelativePath(conf.InputPath!, folder);
            var outputFolder = Path.Combine(Path.GetFullPath(conf.OutputPath!), relative);

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            var saveFile = $"{Path.GetFileName(songFolder)}.sng";
            var fullPath = Path.Combine(outputFolder, saveFile);

            if (conf.SkipExisting && File.Exists(fullPath))
            {
                ConMan.Out($"{fullPath} already exists skipping!");
                skippedSongs++;
                return;
            }
            else
            {
                ConMan.Out($"Starting: {songFolder}");
            }

            using (SngFile sngFile = new SngFile())
            {

                Random.Shared.NextBytes(sngFile.XorMask);

                // encode the top level folder first
                (long startingSize, long encodedSize) = await EncodeFolder(sngFile, songFolder);

                // TODO - This does not work with nested sub-folders
                var subfolders = Directory.GetDirectories(songFolder, "*", new EnumerationOptions() { RecurseSubdirectories = true });


                foreach (var subfolder in subfolders)
                {
                    // Skip any excluded folders
                    string folderName = Path.GetDirectoryName(songFolder)!;
                    if (excludeFolders.Contains(folderName))
                    {
                        continue;
                    }
                    string folderRelativePath = Path.GetRelativePath(songFolder, subfolder).Replace("\\", "/");

                    var fileInfo = await EncodeSubFolder(sngFile, subfolder, folderRelativePath);
                    startingSize += fileInfo.startingSize;
                    encodedSize += fileInfo.encodedSize;
                }

                ConMan.Out($"{fullPath} Saving compression ratio: {startingSize / (double)encodedSize:0.00}x");
                SngSerializer.SaveSngFile(sngFile, fullPath);
                Interlocked.Increment(ref completedSongs);
            }
        }

        private static int completedSongs;
        private static int erroredSongs;
        private static int skippedSongs;
        private static int processedSongs;

        public static void OutputReport()
        {
            var conf = SngEncodingConfig.Instance;
            Console.WriteLine($"Packaged Songs: {completedSongs}");
            Console.WriteLine($"Errored Songs: {erroredSongs}");
            if (conf.SkipExisting)
            {
                Console.WriteLine($"Skipped Songs: {skippedSongs}");
            }
            Console.WriteLine($"Total Songs Processed: {processedSongs}");
        }

        public static async Task ProcessSongs()
        {
            var conf = SngEncodingConfig.Instance;

            if (!Directory.Exists(conf.InputPath))
            {
                ConMan.Out($"Input folder does not exist {conf.InputPath}");
                Program.DisplayHelp();
                Environment.Exit(1);
                return;
            }

            ConMan.Out("SngCli scanning song folders");
            // ConMan.ProgramCanceledAction += OutputReport;

            List<string> songFolders = SearchForFolders(conf.InputPath!);

            if (songFolders.Count == 0)
            {
                ConMan.Out($"No valid songs found at: {conf.InputPath!}");
                Environment.Exit(1);
                return;
            }

            if (conf.StatusBar)
            {
                ConMan.EnableProgress(1);
            }

            ConMan.ProgressItems = songFolders.Count;
            ConMan.Out($"Song count: {songFolders.Count}");
            await Utils.ForEachAsync(songFolders, async (songFolder, token) =>
            {
                try
                {
                    await EncodeSong(songFolder);
                }
                catch (Exception e)
                {
                    ConMan.Out($"{songFolder} ERROR! \n{e}");
                    Interlocked.Increment(ref erroredSongs);
                }
                ConMan.UpdateProgress(Interlocked.Increment(ref processedSongs));
            }, conf.Threads);

            if (conf.StatusBar)
            {
                ConMan.DisableProgress();
            }
            OutputReport();
        }
    }
}