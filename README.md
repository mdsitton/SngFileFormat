# SNG Specification

`.sng` is a simple and generic binary container format that groups a list of files and metadata into a single file.

## Structure Overview

```jsx
[header]
[metadata_0] ... [metadata_M]
[file_0] ... [file_F]
```

### `[header]`

```jsx
[fileIdentifier]                // SNGPKG - 0x53 0x4E 0x47 0x50 0x4b 0x47 sequence to identify the file type
[version]						// The .sng format version (uint64)
[seed]							// Randomly selected bytes to use when wrangling files (16 bytes)
[M]								// The number of [metadata] sections (uint64)
[Mstart_0] ... [Mstart_M]		// The first byte index of each [metadata] section (uint64 each)
[F]								// The number of [file] and [fileMeta] sections (uint64)
[fileMeta_0] ... [fileMeta_F]	// See [fileMeta] below
```

### `[fileMeta]` (contains the metadata of each `[file]` section)
```jsx
[filenameLen]	// The number of bytes in [filename] (uint64)
[filename]		// The file's name (UTF-8 string)
[encryption]	// The type of encryption used on the file's [contents] (1 byte)
[contentsLen]	// The number of bytes in the file's [contents] (uint64)
[contentsIndex] // The first byte index of the corresponding [file] section (uint64)
/*
 * encryption == 0: [contents] is the raw file binary
 * encryption == 1: [contents] have been XOR-ed with [seed]
 * Example code to convert to the file's true contents:
 * for i = 0 to size(wrangled_audio_file_bytes):
 *  clear_audio_file_bytes[i] = wrangled_audio_file_bytes[i] xor seed[i modulo 16]
 * encryption == 2: [contents] is encrypted using CH's encryption
 */
```

### `[metadata]` (represents a single key/value pair)

```jsx
[keyLen]		// The number of bytes in [key] (uint64)
[key]			// The metadata's key (UTF-8 string)
[valueLen]		// The number of bytes in [value] (uint64)
[value]			// The metadata's value (UTF-8 string)
```


### `[file]` (contains the binary contents of a single file)

```jsx
[contents]		// The file's binary contents
```

## Design Decisions
- The metadata is arbitrary and not tied to any specific application. There can be any number of properties. Application-specific properties can be added. However, to avoid unintentional property conflicts between the data of different applications, the convention is for each application to encode all its metadata into a single key-value pair, where the key is the application name.
- `.sng` is designed to be able to contain the binary contents of any set of files.
- If no encryption is used, the raw binary of each file appears at its listed index, allowing for programs to efficiently access only certain files.
- File binaries are placed at the end of the format to allow programs to efficiently scan only the `.sng`'s `metadata` or `fileMeta` sections.
- Files can be encrypted. The default encryption will simply obfuscate the file's binary so that it can't be directly detected by programs that don't know how to parse the file, however other encryption settings are available if the author only wants certain programs to be able to parse the file.

