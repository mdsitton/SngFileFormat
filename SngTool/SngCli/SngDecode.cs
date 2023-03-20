using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SngLib;
using SongLib;

namespace SngCli
{
    public static class SngDecode
    {
        private static IEnumerable<string> FindAllSngFiles(string rootFolder)
        {
            foreach (string subfolder in Directory.EnumerateFiles(rootFolder, "*.sng", SearchOption.AllDirectories))
            {
                yield return subfolder;
            }
        }

        private static void SerializeMetadata(SngFile sngFile, string savePath)
        {
            // read from metadata
            var name = sngFile.GetString("name", "");
            var artist = sngFile.GetString("artist", "");
            var album = sngFile.GetString("album", "");
            var genre = sngFile.GetString("genre", "");
            var year = sngFile.GetString("year", "");
            var bandDiff = sngFile.GetInt("diff_band", -1);
            var guitarDiff = sngFile.GetInt("diff_guitar", -1);
            var rhythmDiff = sngFile.GetInt("diff_rhythm", -1);
            var guitarCoopDiff = sngFile.GetInt("diff_guitar_coop", -1);
            var bassDiff = sngFile.GetInt("diff_bass", -1);
            var drumsDiff = sngFile.GetInt("diff_drums", -1);
            var proDrumsDiff = sngFile.GetInt("diff_drums_real", -1);
            var keysDiff = sngFile.GetInt("diff_keys", -1);
            var gHLGuitarDiff = sngFile.GetInt("diff_guitarghl", -1);
            var gHLBassDiff = sngFile.GetInt("diff_bassghl", -1);
            var gHLGuitarCoopDiff = sngFile.GetInt("diff_guitar_coop_ghl", -1);
            var gHLRhythmDiff = sngFile.GetInt("diff_rhythm_ghl", -1);
            var previewStart = sngFile.GetInt("preview_start_time", -1);
            var iconName = sngFile.GetString("icon", "").ToLowerInvariant();
            var playlistTrack = sngFile.GetInt("playlist_track", 16000);
            var modchart = sngFile.GetBool("modchart", false);
            var songLength = sngFile.GetInt("song_length", 0);
            var forceProDrums = sngFile.GetBool("pro_drums", false);
            var forceFiveLane = sngFile.GetBool("five_lane_drums", false);
            var topLevelPlaylist = sngFile.GetString("playlist", "").ToLowerInvariant();
            var subPlaylist = sngFile.GetString("sub_playlist", "").ToLowerInvariant();
            var albumTrack = sngFile.GetInt("album_track", 16000);
            var charter = sngFile.GetString("charter", "");
            var customHOPO = sngFile.GetInt("hopo_frequency", 0);
            var isEighthHOPO = sngFile.GetBool("eighthnote_hopo", false);
            var multiplierNote = sngFile.GetInt("multiplier_note", 0);
            var offset = sngFile.GetInt("delay", 0);
            var videoStart = sngFile.GetInt("video_start_time", 0);
            var endEventsEnabled = sngFile.GetBool("end_events", true);

            // write to ini

            IniFile iniFile = new IniFile();

            iniFile.SetString("song", "name", name);
            iniFile.SetString("song", "artist", artist);
            iniFile.SetString("song", "album", album);
            iniFile.SetString("song", "genre", genre);
            iniFile.SetString("song", "year", year);
            iniFile.SetInt("song", "diff_band", bandDiff);
            iniFile.SetInt("song", "diff_guitar", guitarDiff);
            iniFile.SetInt("song", "diff_rhythm", rhythmDiff);
            iniFile.SetInt("song", "diff_guitar_coop", guitarCoopDiff);
            iniFile.SetInt("song", "diff_bass", bassDiff);
            iniFile.SetInt("song", "diff_drums", drumsDiff);
            iniFile.SetInt("song", "diff_drums_real", proDrumsDiff);
            iniFile.SetInt("song", "diff_keys", keysDiff);
            iniFile.SetInt("song", "diff_guitarghl", gHLGuitarDiff);
            iniFile.SetInt("song", "diff_bassghl", gHLBassDiff);
            iniFile.SetInt("song", "diff_guitar_coop_ghl", gHLGuitarCoopDiff);
            iniFile.SetInt("song", "diff_rhythm_ghl", gHLRhythmDiff);
            iniFile.SetInt("song", "preview_start_time", previewStart);
            iniFile.SetString("song", "icon", iconName);
            iniFile.SetInt("song", "playlist_track", playlistTrack);
            iniFile.SetBool("song", "modchart", modchart);
            iniFile.SetInt("song", "song_length", songLength);
            iniFile.SetBool("song", "pro_drums", forceProDrums);
            iniFile.SetBool("song", "five_lane_drums", forceFiveLane);
            iniFile.SetString("song", "playlist", topLevelPlaylist);
            iniFile.SetString("song", "sub_playlist", subPlaylist);
            iniFile.SetInt("song", "album_track", albumTrack);
            iniFile.SetString("song", "charter", charter);
            iniFile.SetInt("song", "hopo_frequency", customHOPO);
            iniFile.SetBool("song", "eighthnote_hopo", isEighthHOPO);
            iniFile.SetInt("song", "multiplier_note", multiplierNote);
            iniFile.SetInt("song", "delay", offset);
            iniFile.SetInt("song", "video_start_time", videoStart);
            iniFile.SetBool("song", "end_events", endEventsEnabled);
            iniFile.Save(savePath);
        }

        private static async Task DecodeSong(string sngPath)
        {
            var conf = SngDecodingOptions.Instance;
            var folderName = Path.GetFileNameWithoutExtension(sngPath);
            var parentFolder = Path.GetDirectoryName(sngPath);

            var relative = Path.GetRelativePath(conf.InputPath!, parentFolder!);
            var outputFolder = Path.Combine(Path.GetFullPath(conf.OutputPath!), relative, folderName);
            Console.WriteLine(outputFolder);
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
                await File.WriteAllBytesAsync(Path.Combine(outputFolder, name), data.Contents!);
            }
        }

        public async static Task ProcessSongs()
        {
            var conf = SngDecodingOptions.Instance;

            if (conf.NoThreads)
            {
                foreach (var sngFile in FindAllSngFiles(conf.InputPath!))
                {
                    await DecodeSong(sngFile);
                }
            }
            else
            {
                await Parallel.ForEachAsync(FindAllSngFiles(conf.InputPath!), async (sngFile, token) =>
                {
                    await DecodeSong(sngFile);
                });
            }
        }

    }
}