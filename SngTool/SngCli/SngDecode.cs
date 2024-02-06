using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Collections;
using SngLib;
using SongLib;

namespace SngCli
{
    public static class SngDecode
    {
        private static string[] FindAllSngFiles(string rootFolder)
        {
            return Directory.GetFiles(rootFolder, "*.sng", SearchOption.AllDirectories);
        }

        private static void SerializeMetadata(SngFile sngFile, string savePath)
        {
            KnownKeys.ValidateKeys(sngFile.Metadata);

            IniFile iniFile = new IniFile();
            foreach (var (key, value) in sngFile.Metadata)
            {
                if (!KnownKeys.IsKnownKey(key) && Program.Verbose)
                {
                    ConMan.Out($"Unknown metadata. Key: {key} Value: {value}");
                }
                iniFile.SetString("song", key, value);
            }

            iniFile.Save(savePath);
        }

        private static async Task DecodeSong(string sngPath)
        {
            var conf = SngDecodingOptions.Instance;
            var folderName = Path.GetFileNameWithoutExtension(sngPath);
            var parentFolder = Path.GetDirectoryName(sngPath);

            var relative = Path.GetRelativePath(conf.InputPath!, parentFolder!);
            var outputFolder = Path.Combine(Path.GetFullPath(conf.OutputPath!), relative, folderName);
            ConMan.Out(outputFolder);
            SngFile sngFile = SngSerializer.LoadSngFile(sngPath);

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            // create ini file from metadata
            SerializeMetadata(sngFile, Path.Combine(outputFolder, "song.ini"));

            // iterate through files and save them to disk
            foreach ((var name, var data) in sngFile.Files)
            {
                var filePath = Path.Combine(outputFolder, Path.Combine(name.Split("/")));
                var folder = Path.GetDirectoryName(filePath)!;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                await data!.WriteToFileAsync(filePath);
            }
            ConMan.UpdateProgress(Interlocked.Increment(ref completedSongs));
        }

        private static int completedSongs;
        private static int erroredSongs;


        public async static Task ProcessSongs()
        {
            var conf = SngDecodingOptions.Instance;

            if (!Directory.Exists(conf.InputPath))
            {
                ConMan.Out("Input folder does not exist");
                Program.DisplayHelp();
                Environment.Exit(1);
                return;
            }

            var songs = FindAllSngFiles(conf.InputPath!);
            if (songs.Length == 0)
            {
                ConMan.Out($"No valid songs found at: {conf.InputPath!}");
                Environment.Exit(1);
                return;
            }

            if (conf.StatusBar)
            {
                ConMan.EnableProgress(1);
            }

            ConMan.ProgressItems = songs.Length;
            ConMan.Out($"Song count: {songs.Length}");
            await Utils.ForEachAsync(songs, async (sngFile, token) =>
            {
                try
                {
                    await DecodeSong(sngFile);
                }
                catch (Exception e)
                {
                    ConMan.Out($"{sngFile} ERROR! \n{e}");
                    erroredSongs++;
                }
            }, conf.Threads);

            if (conf.StatusBar)
            {
                ConMan.DisableProgress();
            }

            Console.WriteLine($"Extracted Songs: {completedSongs}");
            Console.WriteLine($"Errored Songs: {erroredSongs}");
            Console.WriteLine($"Total Songs Processed: {songs.Length}");
        }
    }
}