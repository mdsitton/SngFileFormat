using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace SngLib
{

    public static class SngSerializer
    {
        private const string FileIdentifier = "SNGPKG";
        private static readonly byte[] FileIdentifierBytes = Encoding.ASCII.GetBytes(FileIdentifier);

        private static void MaskData(byte[] data, byte[] seed)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= (byte)(seed[i % 16] ^ i);
            }
        }

        public static SngFile LoadSngFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs, Encoding.UTF8);

            // Read header
            var identifier = br.ReadBytes(FileIdentifierBytes.Length);
            if (!FileIdentifierBytes.SequenceEqual(identifier))
                throw new FormatException("Invalid SNG file identifier");

            var sngFile = new SngFile
            {
                Version = br.ReadUInt64(),
                Seed = br.ReadBytes(16),
                Metadata = new ConcurrentDictionary<string, string>(),
                Files = new ConcurrentDictionary<string, byte[]?>()
            };

            if (sngFile.Version != SngFile.CurrentVersion)
                throw new NotSupportedException("Unsupported SNG file version");

            // Read metadata
            ulong metadataCount = br.ReadUInt64();
            for (ulong i = 0; i < metadataCount; i++)
            {
                int keyLen = br.ReadInt32();
                if (keyLen < 0)
                {
                    throw new FormatException("Metadata Key length value cannot be negative");
                }
                string key = Encoding.UTF8.GetString(br.ReadBytes(keyLen));
                int valueLen = br.ReadInt32();
                if (valueLen < 0)
                {
                    throw new FormatException("Metadata value length value cannot be negative");
                }
                string value = Encoding.UTF8.GetString(br.ReadBytes(valueLen));
                sngFile.SetString(key, value);
            }

            // Read file metadata
            ulong fileCount = br.ReadUInt64();
            for (ulong i = 0; i < fileCount; i++)
            {
                var fileNameLength = br.ReadInt32();
                if (fileNameLength < 0)
                {
                    throw new FormatException("File name length value cannot be negative");
                }
                string fileName = Encoding.UTF8.GetString(br.ReadBytes(fileNameLength));
                ulong contentsLen = br.ReadUInt64();
                ulong contentsIndex = br.ReadUInt64();
                long currentPos = br.BaseStream.Position;

                // Read file contents
                br.BaseStream.Seek((long)contentsIndex, SeekOrigin.Begin);
                byte[] contents = br.ReadBytes((int)contentsLen);

                // Unmask data
                MaskData(contents, sngFile.Seed);

                br.BaseStream.Position = currentPos;

                sngFile.AddFile(fileName, contents);
            }

            return sngFile;
        }

        private static ulong CountNonNullFiles(IEnumerable<byte[]?> data)
        {
            ulong count = 0;
            foreach (var item in data)
            {
                if (item != null)
                {
                    count++;
                }
            }
            return count;
        }

        public static void SaveSngFile(SngFile sngFile, string path)
        {
            if (sngFile.Version != SngFile.CurrentVersion)
                throw new NotSupportedException("Unsupported SNG file version");

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // Write header
            bw.Write(FileIdentifierBytes);
            bw.Write(sngFile.Version);
            bw.Write(sngFile.Seed);

            // Write metadata
            bw.Write((ulong)sngFile.Metadata.Count);
            foreach (var metadata in sngFile.Metadata)
            {
                var bytesKey = Encoding.UTF8.GetBytes(metadata.Key);
                bw.Write(bytesKey.Length);
                bw.Write(bytesKey);

                var bytesValue = Encoding.UTF8.GetBytes(metadata.Value);
                bw.Write(bytesValue.Length);
                bw.Write(bytesValue);
            }

            // Calculate end of file metadata section
            // This information is used to calculate the file index positions
            long contentPosition = bw.BaseStream.Position;
            contentPosition += sizeof(ulong); // File count
            foreach (var fileEntry in sngFile.Files)
            {
                if (fileEntry.Value == null)
                    continue;

                contentPosition += sizeof(int) + Encoding.UTF8.GetByteCount(fileEntry.Key); // FileName length and FileName
                contentPosition += sizeof(ulong) * 2; // Contents length and Contents index
            }

            // Write file metadata and store file content positions
            bw.Write(CountNonNullFiles(sngFile.Files.Values));
            foreach (var fileEntry in sngFile.Files)
            {
                if (fileEntry.Value == null)
                    continue;

                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileEntry.Key);
                bw.Write(fileNameBytes.Length);
                bw.Write(fileNameBytes);
                bw.Write((ulong)fileEntry.Value.Length);
                bw.Write((ulong)contentPosition);
                contentPosition += fileEntry.Value.Length;
            }

            // Write file contents
            foreach (var fileEntry in sngFile.Files)
            {
                if (fileEntry.Value == null)
                    continue;
                byte[] contents = fileEntry.Value;

                // Copy original data for masking
                var contentsCopy = new byte[contents.Length];
                Array.Copy(contents, contentsCopy, contents.Length);

                MaskData(contentsCopy, sngFile.Seed);
                contents = contentsCopy;

                bw.Write(contents);
            }
            File.WriteAllBytes(path, ms.ToArray());
        }
    }
}