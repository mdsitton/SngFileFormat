using System.Collections.Concurrent;

namespace SngLib
{
    public class SngFile
    {
        public const ulong CurrentVersion = 1;
        public ulong Version = CurrentVersion;
        public byte[] Seed = new byte[16];

        public ConcurrentDictionary<string, string> Metadata = new();
        public ConcurrentDictionary<string, byte[]?> Files = new();


        public void AddFile(string fileName, byte[]? data)
        {
            Files.AddOrUpdate(fileName, data, (_, _) => data);
        }

        public bool TryGetString(string key, out string value)
        {
            if (Metadata.TryGetValue(key, out var strVal))
            {
                value = strVal!;
                return true;
            }
            else
            {
                value = string.Empty;
                return false;
            }
        }

        public bool TryGetInt(string key, out int value)
        {
            if (Metadata.TryGetValue(key, out var stringValue) && int.TryParse(stringValue, out int intValue))
            {
                value = intValue;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetFloat(string key, out float value)
        {
            if (Metadata.TryGetValue(key, out var stringValue) && float.TryParse(stringValue, out float floatValue))
            {
                value = floatValue;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetBool(string key, out bool value)
        {
            if (Metadata.TryGetValue(key, out var stringValue) && bool.TryParse(stringValue, out bool boolValue))
            {
                value = boolValue;
                return true;
            }

            value = false;
            return false;
        }

        public string GetString(string key, string defaultValue)
        {
            if (TryGetString(key, out var value) && value != null)
            {
                return value;
            }
            else
            {
                return defaultValue;
            }
        }

        public int GetInt(string key, int defaultValue)
        {
            if (TryGetInt(key, out var value))
            {
                return value;
            }
            else
            {
                return defaultValue;
            }
        }

        public float GetFloat(string key, float defaultValue)
        {
            if (TryGetFloat(key, out var value))
            {
                return value;
            }
            else
            {
                return defaultValue;
            }
        }

        public bool GetBool(string key, bool defaultValue)
        {
            if (TryGetBool(key, out var value))
            {
                return value;
            }
            else
            {
                return defaultValue;
            }
        }

        public void SetString(string key, string value)
        {
            Metadata.AddOrUpdate(key, value, (_, _) => value);
        }

        public void SetInt(string key, int value)
        {
            Metadata.AddOrUpdate(key, value.ToString(), (_, _) => value.ToString());
        }

        public void SetFloat(string key, float value)
        {
            Metadata.AddOrUpdate(key, value.ToString(), (_, _) => value.ToString());
        }

        public void SetBool(string key, bool value)
        {
            Metadata.AddOrUpdate(key, value.ToString(), (_, _) => value.ToString());
        }

        public Dictionary<string, string> GetRawMetadata()
        {
            // Make a copy of the internal dictionary
            return new Dictionary<string, string>(Metadata);
        }

    }
}