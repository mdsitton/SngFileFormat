using System;
using System.Collections.Generic;
using System.Text;
using NVorbis.Contracts;

namespace NVorbis
{
    internal class TagData : ITagData
    {
        private Dictionary<string, IReadOnlyList<string>> _tags;

        public TagData(byte[] utf8Vendor, byte[][] utf8Comments)
        {
            EncoderVendor = Encoding.UTF8.GetString(utf8Vendor);

            Dictionary<string, IReadOnlyList<string>> tags = new();
            for (int i = 0; i < utf8Comments.Length; i++)
            {
                string[] parts = Encoding.UTF8.GetString(utf8Comments[i]).Split('=');
                if (parts.Length == 1)
                {
                    parts = new[] { parts[0], string.Empty };
                }

                int bktIdx = parts[0].IndexOf('[');
                if (bktIdx > -1)
                {
                    parts[1] = parts[0]
                        .Substring(bktIdx + 1, parts[0].Length - bktIdx - 2)
                        .ToUpper(System.Globalization.CultureInfo.CurrentCulture)
                        + ": "
                        + parts[1];
                    parts[0] = parts[0].Substring(0, bktIdx);
                }

                if (tags.TryGetValue(parts[0].ToUpperInvariant(), out IReadOnlyList<string>? list))
                {
                    ((List<string>)list).Add(parts[1]);
                }
                else
                {
                    tags.Add(parts[0].ToUpperInvariant(), new List<string> { parts[1] });
                }
            }
            _tags = tags;
        }

        public string GetTagSingle(string key, bool concatenate = false)
        {
            IReadOnlyList<string> values = GetTagMulti(key);
            if (values.Count > 0)
            {
                if (concatenate)
                {
                    return string.Join(Environment.NewLine, values);
                }
                return values[values.Count - 1];
            }
            return string.Empty;
        }

        public IReadOnlyList<string> GetTagMulti(string key)
        {
            if (_tags.TryGetValue(key.ToUpperInvariant(), out IReadOnlyList<string>? values))
            {
                return values;
            }
            return Array.Empty<string>();
        }

        public IReadOnlyDictionary<string, IReadOnlyList<string>> All => _tags;

        public string EncoderVendor { get; }

        public string Title => GetTagSingle("TITLE");

        public string Version => GetTagSingle("VERSION");

        public string Album => GetTagSingle("ALBUM");

        public string TrackNumber => GetTagSingle("TRACKNUMBER");

        public string Artist => GetTagSingle("ARTIST");

        public IReadOnlyList<string> Performers => GetTagMulti("PERFORMER");

        public string Copyright => GetTagSingle("COPYRIGHT");

        public string License => GetTagSingle("LICENSE");

        public string Organization => GetTagSingle("ORGANIZATION");

        public string Description => GetTagSingle("DESCRIPTION");

        public IReadOnlyList<string> Genres => GetTagMulti("GENRE");

        public IReadOnlyList<string> Dates => GetTagMulti("DATE");

        public IReadOnlyList<string> Locations => GetTagMulti("LOCATION");

        public string Contact => GetTagSingle("CONTACT");

        public string Isrc => GetTagSingle("ISRC");
    }
}