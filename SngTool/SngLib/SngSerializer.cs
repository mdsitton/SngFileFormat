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
                Files = new ConcurrentDictionary<string, SngFile.FileData>()
            };

            if (sngFile.Version != SngFile.CurrentVersion)
                throw new NotSupportedException("Unsupported SNG file version");

            // Read metadata
            ulong metadataCount = br.ReadUInt64();
            for (ulong i = 0; i < metadataCount; i++)
            {
                int keyLen = br.ReadInt32();
                string key = Encoding.UTF8.GetString(br.ReadBytes(keyLen));
                int valueLen = br.ReadInt32();
                string value = Encoding.UTF8.GetString(br.ReadBytes(valueLen));
                sngFile.SetString(key, value);
            }

            // Read file metadata
            ulong fileCount = br.ReadUInt64();
            for (ulong i = 0; i < fileCount; i++)
            {
                string fileName = Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32()));
                var fileData = new SngFile.FileData()
                {
                    Masked = br.ReadByte() != 0,
                    Contents = null
                };
                ulong contentsLen = br.ReadUInt64();
                ulong contentsIndex = br.ReadUInt64();
                long currentPos = br.BaseStream.Position;

                // Read file contents
                br.BaseStream.Seek((long)contentsIndex, SeekOrigin.Begin);
                byte[] contents = br.ReadBytes((int)contentsLen);
                if (fileData.Masked)
                {
                    for (int j = 0; j < contents.Length; j++)
                    {
                        contents[j] ^= (byte)(sngFile.Seed[j % 16] ^ j);
                    }
                }
                fileData.Contents = contents;
                br.BaseStream.Position = currentPos;

                sngFile.AddFile(fileName, fileData);
            }

            return sngFile;
        }

        private static ulong CountNonNullFiles(IEnumerable<SngFile.FileData> data)
        {
            ulong count = 0;
            foreach (var item in data)
            {
                if (item.Contents != null)
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

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs, Encoding.UTF8);

            // Write header
            bw.Write(FileIdentifierBytes);
            bw.Write(sngFile.Version);
            bw.Write(sngFile.Seed);

            // Write metadata
            bw.Write((ulong)sngFile.Metadata.Count);
            foreach (var metadata in sngFile.Metadata)
            {
                bw.Write(metadata.Key.Length);
                bw.Write(Encoding.UTF8.GetBytes(metadata.Key));
                bw.Write(metadata.Value.Length);
                bw.Write(Encoding.UTF8.GetBytes(metadata.Value));
            }

            // Calculate and store file content positions
            long contentPosition = bw.BaseStream.Position;
            contentPosition += sizeof(ulong); // File count
            foreach (var fileEntry in sngFile.Files)
            {
                contentPosition += sizeof(ulong) + Encoding.UTF8.GetByteCount(fileEntry.Key); // FileName length and FileName
                contentPosition += 1; // Masked
                contentPosition += sizeof(ulong) * 2; // Contents length and Contents index
            }

            // Write file metadata and store file content positions
            bw.Write(CountNonNullFiles(sngFile.Files.Values));
            foreach (var fileEntry in sngFile.Files)
            {
                if (fileEntry.Value.Contents == null)
                    continue;

                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileEntry.Key);
                bw.Write(fileNameBytes.Length);
                bw.Write(fileNameBytes);
                bw.Write(fileEntry.Value.Masked ? (byte)1 : (byte)0);
                bw.Write((ulong)fileEntry.Value.Contents.Length);
                bw.Write((ulong)contentPosition);
                contentPosition += fileEntry.Value.Contents.Length;
            }

            // Write file contents
            foreach (var fileEntry in sngFile.Files)
            {
                if (fileEntry.Value.Contents == null)
                    continue;
                byte[] contents = fileEntry.Value.Contents;
                if (fileEntry.Value.Masked)
                {
                    contents = (byte[])contents.Clone();
                    for (int i = 0; i < contents.Length; i++)
                    {
                        contents[i] ^= (byte)(sngFile.Seed[i % 16] ^ i);
                    }
                }
                bw.Write(contents);
            }
        }
    }
}