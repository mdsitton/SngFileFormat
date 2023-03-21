using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SngCli
{
    public static class KnownKeys
    {
        private static readonly HashSet<string> knownMetdataKeys = new HashSet<string>() {
            "name",
            "artist",
            "album",
            "genre",
            "year",
            "diff_band",
            "diff_guitar",
            "diff_rhythm",
            "diff_guitar_coop",
            "diff_bass",
            "diff_drums",
            "diff_drums_real",
            "diff_keys",
            "diff_guitarghl",
            "diff_bassghl",
            "diff_guitar_coop_ghl",
            "diff_rhythm_ghl",
            "preview_start_time",
            "icon",
            "playlist_track",
            "modchart",
            "song_length",
            "pro_drums",
            "five_lane_drums",
            "playlist",
            "sub_playlist",
            "album_track",
            "track",
            "charter",
            "frets",
            "hopo_frequency",
            "eighthnote_hopo",
            "multiplier_note",
            "delay",
            "video_start_time",
            "end_events",
            "loading_phrase"
        };

        public static bool IsKnownKey(string key)
        {
            return knownMetdataKeys.TryGetValue(key, out _);
        }
    }
}