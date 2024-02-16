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
            // Use a hashset when discovering song folders
            // so that we can easily ensure that even when a folder contains multiple chart files
            // that the folder will only be added once to the song encoding process
            HashSet<string> validSongFolders = new HashSet<string>();

            try
            {
                DirectoryInfo path = new DirectoryInfo(rootFolder);

                if (!path.Exists)
                {
                    Console.WriteLine($"SongPath {rootFolder} does not exist, or cannot be accessed.");
                    return new List<string>();
                }

                foreach (var entry in path.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
                {
                    if (entry.DirectoryName == null)
                    {
                        continue;
                    }
                    // if we find a .chart or .mid then we validate it for a song folder
                    if (entry.Extension == ".chart" || entry.Extension == ".mid")
                    {
                        if (IsValidSongFolder(entry.DirectoryName))
                        {
                            validSongFolders.Add(entry.DirectoryName);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error checking path: {rootFolder}\n{e}");
                // return without any folders so that the error is visible in the cli
                return new List<string>();
            }

            return validSongFolders.ToList();
        }

        private static bool IsValidSongFolder(string subfolder)
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

            IniFile iniFile = new IniFile();
            iniFile.Load(iniPath);
            if (!iniFile.TryGetSection("song", out var section))
                return false;

            KnownKeys.ValidateKeys(section);

            foreach (var (key, value) in section)
            {
                if (!KnownKeys.IsKnownKey(key) && Program.Verbose)
                {
                    ConMan.Out($"Unknown metadata. Key: {key} Value: {value}");
                }
                sngFile.SetString(key, value);
            }

            sngFile.metadataAvailable = true;
            return true;
        }

        private static readonly string videoPattern = @"(?i).*\.(mp4|avi|webm|vp8|ogv|mpeg)$";
        private static Regex videoRegex = new Regex(videoPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string imagePattern = @"(?i).*\.(png|jpg|jpeg)$";
        private static Regex imageRegex = new Regex(imagePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string audioPattern = @"(?i).*\.(wav|opus|ogg|mp3)$";
        private static Regex audioRegex = new Regex(audioPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly string sngPattern = @"(?i).*\.sng$";
        private static Regex sngRegex = new Regex(sngPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            if (opusEncode)
            {
                return await AudioEncoding.ToOpus(filePath, conf.OpusBitrate);
            }
            else
            {
                var format = AudioEncoding.DetermineAudioFormat(filePath);
                return (Path.ChangeExtension(fileName, AudioEncoding.GetAudioFormatExtention(format)), await LargeFile.ReadAllBytesAsync(filePath));
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

                // Skip any sng files found
                if (sngRegex.IsMatch(file))
                {
                    continue;
                }

                if (audioRegex.IsMatch(file) && !conf.SkipUnknown)
                {
                    bool encodeOpus = conf.OpusEncode || (conf.OpusEncode && conf.EncodeUnknown);
                    fileData = await EncodeAudio(MakeFileName(fileName), file, encodeOpus);
                }
                else if (imageRegex.IsMatch(file) && !conf.SkipUnknown)
                {
                    // don't re-encode a file if they are already jpegs
                    bool encodeJpg = (conf.JpegEncode || (conf.JpegEncode && conf.EncodeUnknown)) && (!file.EndsWith(".jpg") || file.EndsWith(".jpeg"));
                    fileData = await EncodeImage(MakeFileName(fileName), file, false, JpegEncoding.SizeTiers.None, encodeJpg);
                }
                else
                {
                    if (conf.SkipUnknown)
                    {
                        continue;
                    }
                    fileData = (MakeFileName(fileName), await LargeFile.ReadAllBytesAsync(file));
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

                // Skip any sng files found
                if (sngRegex.IsMatch(file))
                {
                    continue;
                }

                if (audioRegex.IsMatch(file))
                {
                    var knownAudio = MatchesNames(fileName, supportedAudioNames);
                    if (!conf.SkipUnknown || knownAudio)
                    {
                        bool encodeOpus = conf.OpusEncode || (conf.OpusEncode && conf.EncodeUnknown);
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
                    else if (!conf.SkipUnknown ||
                        fileName.StartsWith("background", StringComparison.OrdinalIgnoreCase) ||
                        fileName.StartsWith("highway", StringComparison.OrdinalIgnoreCase))
                    {
                        // don't re-encode a file if they are already jpegs
                        bool encodeJpg = (conf.JpegEncode || (conf.JpegEncode && conf.EncodeUnknown)) && (!file.EndsWith(".jpg") || file.EndsWith(".jpeg"));
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

                Stack<string> pathStack = new(Directory.GetDirectories(songFolder, "*", new EnumerationOptions() { RecurseSubdirectories = false }));

                while (pathStack.TryPop(out var path))
                {
                    // Skip any folders that are commonly ignored such as the __MACOSX folders
                    string folderName = Path.GetDirectoryName(path)!;
                    if (excludeFolders.Contains(folderName))
                    {
                        continue;
                    }
                    string folderRelativePath = Path.GetRelativePath(songFolder, path).Replace("\\", "/");

                    // only add a subfolder if it's not detected as a valid song folder
                    // This also prunes any sub-folders of this directory from being added to this sng file
                    if (!IsValidSongFolder(path))
                    {
                        var fileInfo = await EncodeSubFolder(sngFile, path, folderRelativePath);
                        startingSize += fileInfo.startingSize;
                        encodedSize += fileInfo.encodedSize;

                        // add any direct sub-folders of this directory to scan
                        foreach (var subFolder in Directory.EnumerateDirectories(path, "*", new EnumerationOptions() { RecurseSubdirectories = false }))
                        {
                            pathStack.Push(subFolder);
                        }
                    }
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