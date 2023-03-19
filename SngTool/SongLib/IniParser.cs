using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
            int currentLineNumber = 0;
            try
            {
                string text = File.ReadAllText(filePath);

                var fileContent = text.AsSpan();
                var currentSection = string.Empty;

                while (!fileContent.IsEmpty)
                {
                    var endOfLineIndex = fileContent.IndexOf('\n');
                    currentLineNumber++;

                    if (endOfLineIndex == -1)
                    {
                        endOfLineIndex = fileContent.Length;
                    }

                    var line = fileContent.Slice(0, endOfLineIndex).Trim();

                    if (endOfLineIndex == fileContent.Length)
                    {
                        fileContent = new Span<char>();
                    }
                    else
                    {
                        fileContent = fileContent.Slice(endOfLineIndex + 1);
                    }

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

                        // Skip lines without proper = character
                        if (separatorIndex == -1)
                        {
                            continue;
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
            catch (Exception e)
            {
                Console.WriteLine($"ERROR on line: # {currentLineNumber}: {e}");
                Environment.Exit(1);
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

        // handle making section names case-insensitive as some charts use cased section names
        public bool TryGetSection(string sectionName, out Dictionary<string, string>? section)
        {
            if (!sections.TryGetValue(sectionName, out var value))
            {

                if (!sections.TryGetValue(sectionName.ToLowerInvariant(), out value))
                {
                    section = null;
                    return false;
                }
                else
                {
                    section = value;
                    return true;
                }
            }
            else
            {
                section = value;
                return true;
            }
        }

        public bool IsSection(string sectionName)
        {
            if (!sections.ContainsKey(sectionName))
            {
                if (!sections.ContainsKey(sectionName.ToLowerInvariant()))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }

        public bool IsKey(string sectionName, string keyName)
        {
            return TryGetSection(sectionName, out var section) && section!.ContainsKey(keyName);
        }

        public string GetString(string section, string key, string defaultValue = "")
        {
            if (!TryGetSection(section, out var sectionValue) || !sectionValue!.TryGetValue(key, out var value))
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