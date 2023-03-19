using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UtfUnknown;

namespace SongLib
{

    public class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> sections;

        public IniFile()
        {
            this.sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        }

        public void Load(string filePath)
        {
            var buffer = ArrayPool<byte>.Shared.Rent((int)new FileInfo(filePath).Length);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Read(buffer, 0, buffer.Length);
                }

                var encoding = Encoding.UTF8;
                var detection = CharsetDetector.DetectFromBytes(buffer);
                var best = detection.Detected;

                Console.WriteLine($"{best.EncodingName} encoding found");

                encoding = best.Encoding;

                var fileContent = encoding.GetString(buffer, 0, buffer.Length).AsSpan();
                var currentSection = string.Empty;

                while (!fileContent.IsEmpty)
                {
                    var endOfLineIndex = fileContent.IndexOf('\n');

                    if (endOfLineIndex == -1)
                    {
                        endOfLineIndex = fileContent.Length;
                    }

                    var line = fileContent.Slice(0, endOfLineIndex).Trim();
                    fileContent = fileContent.Slice(endOfLineIndex + 1);

                    if (line.IsEmpty)
                    {
                        continue;
                    }

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Slice(1, line.Length - 2).ToString();
                    }
                    else
                    {
                        var separatorIndex = line.IndexOf('=');

                        if (separatorIndex == -1)
                        {
                            throw new FormatException($"Invalid INI file format: {line.ToString()}");
                        }

                        var key = line.Slice(0, separatorIndex).Trim();
                        var value = line.Slice(separatorIndex + 1).Trim();

                        if (!this.sections.TryGetValue(currentSection, out var values))
                        {
                            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            this.sections[currentSection] = values;
                        }

                        values[key.ToString()] = value.ToString();
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void Save(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                foreach (var section in this.sections)
                {
                    writer.WriteLine($"[{section.Key}]");

                    foreach (var keyValue in section.Value)
                    {
                        writer.WriteLine($"{keyValue.Key}={keyValue.Value}");
                    }
                }
            }
        }

        public string GetString(string section, string key, string defaultValue = "")
        {
            if (!this.sections.TryGetValue(section, out var values) ||
                !values.TryGetValue(key, out var value))
            {
                return defaultValue;
            }

            return value;
        }

        public int GetInt(string section, string key, int defaultValue = 0)
        {
            var stringValue = this.GetString(section, key);

            if (int.TryParse(stringValue, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public float GetFloat(string section, string key, float defaultValue = 0f)
        {
            var stringValue = this.GetString(section, key);

            if (float.TryParse(stringValue, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public bool GetBool(string section, string key, bool defaultValue = false)
        {
            var stringValue = this.GetString(section, key);

            if (bool.TryParse(stringValue, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public void SetString(string section, string key, string value)
        {
            if (!this.sections.TryGetValue(section, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                this.sections[section] = values;
            }

            values[key] = value;
        }

        public void SetInt(string section, string key, int value)
        {
            this.SetString(section, key, value.ToString());
        }

        public void SetFloat(string section, string key, float value)
        {
            this.SetString(section, key, value.ToString());
        }

        public void SetBool(string section, string key, bool value)
        {
            this.SetString(section, key, value.ToString());
        }
    }
}