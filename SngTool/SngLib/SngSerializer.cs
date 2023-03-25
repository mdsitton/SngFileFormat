using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Text;
using BinaryEx;

namespace SngLib
{

    public static class SngSerializer
    {
        private const string FileIdentifier = "SNGPKG";
        private static readonly byte[] FileIdentifierBytes = Encoding.ASCII.GetBytes(FileIdentifier);

        private static Vector<byte>[] dataIndexVectors;

        // values loop every 256 characters since 16 and 256 are aligned
        private static byte[] loopLookup = new byte[256];

        static SngSerializer()
        {
            dataIndexVectors = new Vector<byte>[Vector<byte>.Count];
        }

        private static void InitializeLookup(byte[] seed)
        {
            for (int i = 0; i < loopLookup.Length; i++)
            {
                loopLookup[i] = (byte)(i ^ seed[i & 0xF]);
            }
        }

        /// <summary>
        /// Precompute a lookup table for index values for masking algorithm
        /// as they will loop and always be the same for the same values of
        /// Vector<byte>.Count and seed
        /// </summary>
        private static void InitializeDataIndexVectors(byte[] seed)
        {
            InitializeLookup(seed);
            int vecSize = Vector<byte>.Count;
            int arraySize = loopLookup.Length / vecSize;
            dataIndexVectors = new Vector<byte>[arraySize];
            for (int i = 0; i < arraySize; i++)
            {
                dataIndexVectors[i] = new Vector<byte>(loopLookup, i * vecSize);
            }
        }

        /// <summary>
        /// An optimized Masking routine using Vector<byte> objects along with some pre-computation
        /// This is about 5-10x faster than the standard implementation on coreclr, in unity/mono it's slower
        /// </summary>
        private static void MaskData(byte[] dataIn, byte[] dataOut, byte[] seed)
        {
            int vecSize = Vector<byte>.Count;
            int vecCount = dataIn.Length / vecSize;
            int lastByteIndex = vecCount * vecSize;
            var lookupSizeMask = (loopLookup.Length / vecSize) - 1;

            for (int vectorIndex = 0; vectorIndex < vecCount; ++vectorIndex)
            {
                int byteIndex = vectorIndex * vecSize;
                Vector<byte> dataVector = new Vector<byte>(dataIn, byteIndex);

                Vector<byte> maskedData = dataVector ^ dataIndexVectors[vectorIndex & lookupSizeMask];
                maskedData.CopyTo(dataOut, byteIndex);
            }

            // Handle the remaining bytes
            for (int i = lastByteIndex; i < dataIn.Length; i++)
            {
                dataOut[i] = (byte)(dataIn[i] ^ loopLookup[i & 0xFF]);
            }
        }

        public static SngFile LoadSngFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);

            // Read header
            byte[] identifier = new byte[6];
            fs.Read(identifier);

            if (!FileIdentifierBytes.SequenceEqual(identifier))
                throw new FormatException("Invalid SNG file identifier");

            var sngFile = new SngFile
            {
                Version = fs.ReadUInt32LE(),
                XorMask = new byte[16]
            };
            fs.Read(sngFile.XorMask);

            if (sngFile.Version != SngFile.CurrentVersion)
                throw new NotSupportedException("Unsupported SNG file version");

            // Precompute the lookup table for xormask
            InitializeDataIndexVectors(sngFile.XorMask);

            // Read metadata
            ulong metadataLen = fs.ReadUInt64LE();
            ulong metadataCount = fs.ReadUInt64LE();
            for (ulong i = 0; i < metadataCount; i++)
            {
                int keyLen = fs.ReadInt32LE();
                if (keyLen < 0)
                {
                    throw new FormatException("Metadata Key length value cannot be negative");
                }
                var keyBytes = new byte[keyLen];
                fs.Read(keyBytes);
                string key = Encoding.UTF8.GetString(keyBytes);
                int valueLen = fs.ReadInt32LE();
                if (valueLen < 0)
                {
                    throw new FormatException("Metadata value length value cannot be negative");
                }
                var valueBytes = new byte[valueLen];
                fs.Read(valueBytes);
                string value = Encoding.UTF8.GetString(valueBytes);
                sngFile.SetString(key, value);
            }

            // Read file metadata
            ulong fileIndexLen = fs.ReadUInt64LE();
            ulong fileCount = fs.ReadUInt64LE();
            var fileInfo = new (ulong index, ulong size, string fileName)[fileCount];
            for (ulong i = 0; i < fileCount; i++)
            {
                var fileNameLength = fs.ReadByte();
                if (fileNameLength < 0)
                {
                    throw new FormatException("File name length value cannot be negative");
                }
                var fileNameBytes = new byte[16];
                fs.Read(fileNameBytes);

                string fileName = Encoding.UTF8.GetString(fileNameBytes);
                ulong contentsLen = fs.ReadUInt64LE();
                ulong contentsIndex = fs.ReadUInt64LE();
                fileInfo[i] = (contentsIndex, contentsLen, fileName);
            }

            foreach ((ulong pos, ulong size, string name) in fileInfo)
            {
                // Read file contents
                if (fs.Position != (long)pos)
                    fs.Seek((long)pos, SeekOrigin.Begin);

                byte[] contents = new byte[(int)size];
                fs.Read(contents);

                // Unmask data
                MaskData(contents, contents, sngFile.XorMask);

                sngFile.AddFile(name, contents);
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

        private static ulong CountNonNullMetadata(IDictionary<string, string> metadata)
        {
            ulong count = 0;
            foreach ((var key, var value) in metadata)
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                {
                    continue;
                }
                count++;
            }
            return count;
        }

        private static long GetHeaderLength(this SngFile sngFile)
        {
            // version + SngIdentify + XorMask
            return sizeof(uint) + FileIdentifierBytes.Length + sngFile.XorMask.Length;
        }

        private static long GetMetadataLength(this SngFile sngFile)
        {
            long metadataLength = 0;
            metadataLength += sizeof(ulong) * 2; // metadata count and section length
            foreach ((string key, string value) in sngFile.Metadata)
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                {
                    continue;
                }
                metadataLength += sizeof(int) * 2; // key/value length
                metadataLength += Encoding.UTF8.GetByteCount(key);
                metadataLength += Encoding.UTF8.GetByteCount(value);
            }
            return metadataLength;
        }

        private static (long fileIndexLength, long fileSectionLength) GetFileLengths(this SngFile sngFile)
        {
            long fileIndexLength = 0;
            long fileSectionLength = 0;
            fileIndexLength += sizeof(ulong) * 2; // File count and section length
            foreach ((string key, byte[]? value) in sngFile.Files)
            {
                if (value == null)
                    continue;

                fileIndexLength += sizeof(int); // fileName length
                fileIndexLength += sizeof(ulong) * 2; // length/index
                fileIndexLength += Encoding.UTF8.GetByteCount(key); // file name
                fileSectionLength += value.Length;
            }
            return (fileIndexLength, fileSectionLength);
        }

        private static void WriteHeader(this SngFile sngFile, byte[] bytesOut, ref int pos)
        {
            bytesOut.WriteBytes(ref pos, FileIdentifierBytes, (uint)FileIdentifierBytes.Length);
            bytesOut.WriteUInt32LE(ref pos, sngFile.Version);
            bytesOut.WriteBytes(ref pos, sngFile.XorMask, 16);
        }

        private static void WriteMetadata(this SngFile sngFile, long metadataSize, byte[] bytesOut, ref int pos)
        {
            bytesOut.WriteUInt64LE(ref pos, (ulong)metadataSize);
            bytesOut.WriteUInt64LE(ref pos, CountNonNullMetadata(sngFile.Metadata));
            foreach (var metadata in sngFile.Metadata)
            {
                if (string.IsNullOrEmpty(metadata.Key) || string.IsNullOrEmpty(metadata.Value))
                {
                    continue;
                }
                byte[] bytesKey = Encoding.UTF8.GetBytes(metadata.Key);

                bytesOut.WriteInt32LE(ref pos, bytesKey.Length);
                bytesOut.WriteBytes(ref pos, bytesKey, (uint)bytesKey.Length);

                var bytesValue = Encoding.UTF8.GetBytes(metadata.Value);
                bytesOut.WriteInt32LE(ref pos, bytesValue.Length);
                bytesOut.WriteBytes(ref pos, bytesValue, (uint)bytesValue.Length);
            }
        }

        private static void WriteFileIndex(this SngFile sngFile, long fileIndexSize, ulong startOfFileIndex, byte[] bytesOut, ref int pos)
        {

            bytesOut.WriteUInt64LE(ref pos, (ulong)fileIndexSize);
            bytesOut.WriteUInt64LE(ref pos, (ulong)CountNonNullFiles(sngFile.Files.Values));
            ulong fileOffset = startOfFileIndex;
            foreach ((string key, byte[]? value) in sngFile.Files)
            {
                if (value == null)
                    continue; ;

                byte[] fileNameBytes = Encoding.UTF8.GetBytes(key);
                byte filenameLength = (byte)fileNameBytes.Length;
                bytesOut.WriteByte(ref pos, filenameLength);
                bytesOut.WriteBytes(ref pos, fileNameBytes, filenameLength);
                bytesOut.WriteUInt64LE(ref pos, (ulong)value.Length);
                bytesOut.WriteUInt64LE(ref pos, fileOffset);
                fileOffset += (ulong)value.Length;
            }
        }

        public static void SaveSngFile(SngFile sngFile, string path)
        {
            if (sngFile.Version != SngFile.CurrentVersion)
                throw new NotSupportedException("Unsupported SNG file version");


            // Precompute the lookup table for xormask
            InitializeDataIndexVectors(sngFile.XorMask);

            // Calculate full file size for allocating a MemoryMappedFile
            // We need to keep the individual lengths around for writing out the header as well

            // header section
            long headerSize = sngFile.GetHeaderLength();

            // metadata section
            long metaDataPreLength = sizeof(ulong);
            long metaDataLength = sngFile.GetMetadataLength();

            // fileIndex and File sections
            long fileIndexPreLength = sizeof(ulong);
            long filePreLength = sizeof(ulong);
            (long fileIndexLength, long fileSectionLength) = sngFile.GetFileLengths();

            long totalLength = headerSize + metaDataPreLength + metaDataLength + fileIndexPreLength + fileIndexLength + filePreLength + fileSectionLength;
            long lengthMinusData = headerSize + metaDataPreLength + metaDataLength + fileIndexPreLength + fileIndexLength + filePreLength;

            int pos = 0;
            var headerData = new byte[lengthMinusData];
            sngFile.WriteHeader(headerData, ref pos);
            sngFile.WriteMetadata(metaDataLength, headerData, ref pos);
            sngFile.WriteFileIndex(fileIndexLength, (ulong)lengthMinusData, headerData, ref pos);
            headerData.WriteInt64LE(ref pos, fileSectionLength);

            using (var fs = File.OpenWrite(path))
            {
                fs.Write(headerData);
                // Write file contents
                foreach (var fileEntry in sngFile.Files)
                {
                    if (fileEntry.Value == null)
                        continue;
                    byte[] contents = fileEntry.Value;

                    // Copy original data for masking
                    var contentsCopy = new byte[contents.Length];

                    MaskData(contents, contentsCopy, sngFile.XorMask);
                    contents = contentsCopy;

                    fs.Write(contents);
                }
            }
        }
    }
}