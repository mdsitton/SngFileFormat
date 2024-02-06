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
        // Primitive
        Integer,
        String,
        Boolean,

        // Complex
        ColorCode,
        IntegerPair,
        ProGuitarTuning,
    }

    public static class KnownKeys
    {
        private static readonly List<(string key, IniValueType type)> knownMetadata = new()
        {
            ( "name",                 IniValueType.String ),
            ( "artist",               IniValueType.String ),
            ( "album",                IniValueType.String ),
            ( "genre",                IniValueType.String ),
            ( "sub_genre",            IniValueType.String ),
            ( "year",                 IniValueType.String ),

            ( "charter",              IniValueType.String ),
            ( "frets",                IniValueType.String ),
            ( "version",              IniValueType.Integer ),

            ( "diff_band",            IniValueType.Integer ),

            ( "diff_guitar",          IniValueType.Integer ),
            ( "diff_guitarghl",       IniValueType.Integer ),
            ( "diff_guitar_coop",     IniValueType.Integer ),
            ( "diff_guitar_coop_ghl", IniValueType.Integer ),
            ( "diff_guitar_real",     IniValueType.Integer ),
            ( "diff_guitar_real_22",  IniValueType.Integer ),

            ( "diff_rhythm",          IniValueType.Integer ),
            ( "diff_rhythm_ghl",      IniValueType.Integer ),

            ( "diff_bass",            IniValueType.Integer ),
            ( "diff_bassghl",         IniValueType.Integer ),
            ( "diff_bass_real",       IniValueType.Integer ),
            ( "diff_bass_real_22",    IniValueType.Integer ),

            ( "diff_drums",           IniValueType.Integer ),
            ( "diff_drums_real",      IniValueType.Integer ),
            ( "diff_drums_real_ps",   IniValueType.Integer ),

            ( "diff_keys",            IniValueType.Integer ),
            ( "diff_keys_real",       IniValueType.Integer ),
            ( "diff_keys_real_ps",    IniValueType.Integer ),

            ( "diff_vocals",          IniValueType.Integer ),
            ( "diff_vocals_harm",     IniValueType.Integer ),

            ( "diff_dance",           IniValueType.Integer ),

            ( "song_length",          IniValueType.Integer ),
            ( "delay",                IniValueType.Integer ),
            ( "preview_start_time",   IniValueType.Integer ),
            ( "preview_end_time",     IniValueType.Integer ),
            ( "preview",              IniValueType.IntegerPair ),
            ( "video_start_time",     IniValueType.Integer ),
            ( "video_end_time",       IniValueType.Integer ),

            ( "playlist_track",       IniValueType.Integer ),
            ( "album_track",          IniValueType.Integer ),
            ( "track",                IniValueType.Integer ),

            ( "playlist",             IniValueType.String ),
            ( "sub_playlist",         IniValueType.String ),

            ( "cover",                IniValueType.String ),
            ( "icon",                 IniValueType.String ),
            ( "background",           IniValueType.String ),
            ( "video",                IniValueType.String ),
            ( "video_loop",           IniValueType.Boolean ),

            ( "link_name_a",          IniValueType.String ),
            ( "link_name_b",          IniValueType.String ),
            ( "banner_link_a",        IniValueType.String ),
            ( "banner_link_b",        IniValueType.String ),

            ( "cassettecolor",        IniValueType.ColorCode ),
            ( "tags",                 IniValueType.String ),
            ( "loading_phrase",       IniValueType.String ),

            ( "tutorial",             IniValueType.Boolean ),
            ( "boss_battle",          IniValueType.Boolean ),

            ( "unlock_id",            IniValueType.String ),
            ( "unlock_require",       IniValueType.String ),
            ( "unlock_text",          IniValueType.String ),
            ( "unlock_completed",     IniValueType.Integer ),

            ( "sustain_cutoff_threshold", IniValueType.Integer ),
            ( "multiplier_note",          IniValueType.Integer ),
            ( "star_power_note",          IniValueType.Integer ),
            ( "hopo_frequency",           IniValueType.Integer ),
            ( "eighthnote_hopo",          IniValueType.Boolean ),

            ( "pro_drums",            IniValueType.Boolean ),
            ( "five_lane_drums",      IniValueType.Boolean ),
            ( "drum_fallback_blue",   IniValueType.Boolean ),
            ( "vocal_gender",         IniValueType.Boolean ),

            ( "sysex_slider",         IniValueType.Boolean ),
            ( "sysex_high_hat_ctrl",  IniValueType.Boolean ),
            ( "sysex_rimshot",        IniValueType.Boolean ),
            ( "sysex_open_bass",      IniValueType.Boolean ),
            ( "sysex_pro_slide",      IniValueType.Boolean ),

            ( "guitar_type",          IniValueType.Integer ),
            ( "bass_type",            IniValueType.Integer ),
            ( "kit_type",             IniValueType.Integer ),
            ( "keys_type",            IniValueType.Integer ),
            ( "dance_type",           IniValueType.Integer ),

            ( "real_guitar_tuning",    IniValueType.ProGuitarTuning ),
            ( "real_guitar_22_tuning", IniValueType.ProGuitarTuning ),
            ( "real_bass_tuning",      IniValueType.ProGuitarTuning ),
            ( "real_bass_22_tuning",   IniValueType.ProGuitarTuning ),

            ( "real_keys_lane_count_right", IniValueType.Integer ),
            ( "real_keys_lane_count_left",  IniValueType.Integer ),

            ( "eof_midi_import_drum_accent_velocity", IniValueType.Integer ),
            ( "eof_midi_import_drum_ghost_velocity",  IniValueType.Integer ),

            ( "end_events",           IniValueType.Boolean ),
            ( "modchart",             IniValueType.Boolean ),
            ( "lyrics",               IniValueType.Boolean ),
        };

        private static readonly HashSet<string> knownMetadataKeys = knownMetadata.Select((v) => v.key).ToHashSet();
    
        private static readonly List<(string oldKey, string newKey)> legacyMetadataKeys = new()
        {
            ( "frets", "charter" ),
            ( "track", "album_track" ),
            ( "star_power_note", "multiplier_note" ),
            ( "pro_drum", "pro_drums" ),
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

                    IniValueType.ColorCode => value.StartsWith('#') &&
                        value.AsSpan().IndexOfAny("0123456789ABCDEFabcdef") < 0,
                    IniValueType.IntegerPair => ValidateIntegerPair(value),
                    IniValueType.ProGuitarTuning => ValidateProGuitarTuning(value),

                    _ => throw new NotImplementedException($"Unrecognized IniValueType {type}")
                };

                // preview = 35000 65000
                static bool ValidateIntegerPair(ReadOnlySpan<char> text)
                {
                    int splitIndex = text.IndexOf(' ');
                    if (splitIndex < 0)
                        return false;

                    var first = text.Slice(0, splitIndex);
                    var second = text.Slice(splitIndex + 1);

                    return int.TryParse(first, out _) && int.TryParse(second, out _);
                }

                // real_guitar_tuning = 0 0 0 0 0 0 "Standard tuning"
                // real_guitar_22_tuning = 0 2 5 7 10 10
                // real_bass_tuning = -2 0 0 0 "Drop D tuning"
                // real_bass_22_tuning = 0 0 0 0 0
                static bool ValidateProGuitarTuning(ReadOnlySpan<char> text)
                {
                    // Go through each number
                    int numbers = 0;
                    int splitIndex;
                    while ((splitIndex = text.IndexOf(' ')) >= 0)
                    {
                        var split = text.Slice(0, splitIndex);
                        if (!int.TryParse(split, out _))
                            break;

                        numbers++;
                        text = text.Slice(splitIndex + 1);
                    }

                    // Handle remaining text
                    if (int.TryParse(text, out _))
                    {
                        numbers++;
                    }
                    else
                    {
                        // Check for quoted segment
                        int quoteStartIndex = text.IndexOf('"');
                        if (quoteStartIndex >= 0)
                        {
                            var quoteEndSearch = text.Slice(quoteStartIndex + 1);
                            if (quoteEndSearch.IndexOf('"') < 0)
                                return false;
                        }
                    }

                    return numbers is >= 4 and <= 6;
                }

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