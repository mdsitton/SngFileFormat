using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SngLib;
using SongLib;

namespace SngCli
{
    public enum IniValueType
    {
        Integer,
        String,
        Boolean,
    }

    public static class KnownKeys
    {
        private static readonly List<(string key, IniValueType type)> knownMetadata = new()
        {
            ( "name",                 IniValueType.String ),
            ( "artist",               IniValueType.String ),
            ( "album",                IniValueType.String ),
            ( "genre",                IniValueType.String ),
            ( "year",                 IniValueType.String ),

            ( "charter",              IniValueType.String ),
            ( "frets",                IniValueType.String ),
            ( "icon",                 IniValueType.String ),
            ( "loading_phrase",       IniValueType.String ),

            ( "diff_band",            IniValueType.Integer ),
            ( "diff_guitar",          IniValueType.Integer ),
            ( "diff_rhythm",          IniValueType.Integer ),
            ( "diff_guitar_coop",     IniValueType.Integer ),
            ( "diff_bass",            IniValueType.Integer ),
            ( "diff_drums",           IniValueType.Integer ),
            ( "diff_drums_real",      IniValueType.Integer ),
            ( "diff_keys",            IniValueType.Integer ),
            ( "diff_guitarghl",       IniValueType.Integer ),
            ( "diff_bassghl",         IniValueType.Integer ),
            ( "diff_guitar_coop_ghl", IniValueType.Integer ),
            ( "diff_rhythm_ghl",      IniValueType.Integer ),

            ( "song_length",          IniValueType.Integer ),
            ( "preview_start_time",   IniValueType.Integer ),
            ( "video_start_time",     IniValueType.Integer ),
            ( "delay",                IniValueType.Integer ),

            ( "playlist_track",       IniValueType.Integer ),
            ( "album_track",          IniValueType.Integer ),
            ( "track",                IniValueType.Integer ),

            ( "playlist",             IniValueType.String ),
            ( "sub_playlist",         IniValueType.String ),

            ( "multiplier_note",      IniValueType.Integer ),
            ( "hopo_frequency",       IniValueType.Integer ),
            ( "eighthnote_hopo",      IniValueType.Boolean ),

            ( "pro_drums",            IniValueType.Boolean ),
            ( "five_lane_drums",      IniValueType.Boolean ),
            ( "end_events",           IniValueType.Boolean ),
            ( "modchart",             IniValueType.Boolean ),
        };

        private static readonly HashSet<string> knownMetadataKeys = knownMetadata.Select((v) => v.key).ToHashSet();
    
        private static readonly List<(string oldKey, string newKey)> legacyMetadataKeys = new()
        {
            ( "frets", "charter" ),
            ( "track", "album_track" ),
        };

        private static readonly List<(string key, Func<string, string?> fixup)> metadataFixups = new()
        {
            ( "icon", (value) => value == "0" ? null : value.ToLowerInvariant() ),
        };

        public static void ValidateKeys(Dictionary<string, string> keys)
        {
            // Validate known metadata
            foreach (var (key, type) in knownMetadata)
            {
                if (!keys.TryGetValue(key, out string? value))
                {
                    continue;
                }

                bool valid = type switch
                {
                    IniValueType.Integer => int.TryParse(value, out _),
                    IniValueType.String => !string.IsNullOrEmpty(value),
                    IniValueType.Boolean => bool.TryParse(value, out _) ||
                        (int.TryParse(value, out int intBool) && intBool is 0 or 1),                            
                    _ => throw new NotImplementedException($"Unrecognized IniValueType {type}")
                };

                if (!valid)
                {
                    if (Program.Verbose)
                        ConMan.Out($"Removing invalid metadata. Key: {key} Value: {value} Type: {type}");

                    keys.Remove(key);
                }
            }

            // Apply any necessary fixups
            foreach (var (key, fixup) in metadataFixups)
            {
                if (!keys.TryGetValue(key, out string? value))
                    continue;

                string? fixedValue = fixup(value);
                if (!string.IsNullOrEmpty(fixedValue))
                {
                    if (Program.Verbose)
                        ConMan.Out($"Applying fixup to metadata. Key: {key} Value: {value} Replacement: {fixedValue}");

                    keys[key] = value;
                }
                else
                {
                    if (Program.Verbose)
                        ConMan.Out($"Removing invalid metadata. Key: {key} Value: {value}");

                    keys.Remove(key);
                }
            }

            // Replace legacy metadata
            foreach (var (oldKey, newKey) in legacyMetadataKeys)
            {
                if (!keys.TryGetValue(oldKey, out string? value))
                    continue;

                keys.Remove(oldKey);
                if (!keys.ContainsKey(newKey))
                {
                    if (Program.Verbose)
                        ConMan.Out($"Replacing legacy metadata {oldKey} with {newKey}");

                    keys.Add(newKey, value);
                }
                else
                {
                    if (Program.Verbose)
                        ConMan.Out($"Removing legacy metadata {oldKey}");
                }
            }
        }

        public static bool IsKnownKey(string key)
        {
            return knownMetadataKeys.Contains(key);
        }
    }
}