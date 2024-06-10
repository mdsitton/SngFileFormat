# SNG File Format

`.sng` is a simple and generic binary container format that groups a list of files and metadata into a single file.

This repository includes several components: a reference tool for converting SNG files to and from the designated format, the file specification, a registry of frequently used metadata keys, and a registry of file names.

# SngCli
The SngCli tool is a basic command line tool that has a couple of operating commands each with their own set of command line flags:

```
Usage: SngCli [command] [options]
Options:
  -h, --help            Show help message
  -v, --version         Display version information
      --verbose         Display more information such as audio encoder output.

encode:
  -o, --out FOLDER      Specify output folder location for SNG files
  -i, --in FOLDER       Specify input folder to search for song folders
  -t, --threads         Set how many songs will be encoded in parallel. Can also be useful to set to 1 when a song has an error along with --verbose.
      --noStatusBar     Disable rendering of the status bar. Can reduce cpu usage.
      --skipExisting    If the song to be encoded already exists as an sng in the output folder skip it
      --skipUnknown     Skip unknown files.By default unknown files are included (All audio and images of supported formats are transcoded)
      --videoExclude    Exclude video files
      --opusEncode      Encode all audio to opus
      --opusBitrate     Set opus encoder bitrate, default: 80
      --jpegEncode      Encode all images to JPEG
      --jpegQuality     JPEG encoding quality, default: 75
      --albumUpscale    Enable upscaling album art, by default images are only shrunk.
      --albumResize     Resize album art to set size. Smaller resolutions load faster in-game, Default size: 512x512
                            Supported Sizes:
                                Nearest - This uses next size below the image size
                                256x256
                                384x384
                                512x512
                                768x768
                                1024x1024
                                1536x1536
                                2048x2048

decode:
  -o, --out FOLDER      Specify output folder location for extracted song folders
  -i, --input FOLDER    Specify input folder to search for SNG files
  -t, --threads         Set how many songs will be encoded in parallel.
      --noStatusBar     Disable rendering of the status bar. Can reduce cpu usage.
```

When encoding this tool can also do audio transcoding to opus, and image transcoding to JPEG. Opus encoding takes a significant amount of time for larger song libraries. The more cpu cores you have, the tool can take advantage of this, and encode multiple songs in parallel speeding up the process significantly.

# SNG File Specification


The following are definitions of the data types used in this file specification

| Data Type      | Description                                                                                                                         |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| byte           | 8-bit unsigned integer                                                                                                              |
| uint32         | 32-bit unsigned integer                                                                                                             |
| uint64         | 64-bit unsigned integer                                                                                                             |
| int32          | 32-bit signed integer                                                                                                               |
| string         | A sequence of utf-8 characters a byte[] array excluding NULL 0x00 characters as many languages use these as end of string sequences |
| SngIdentify    | The SNGPKG file identifier. `0x53 0x4E 0x47 0x50 0x4b 0x47` as bytes. Used to identify the file format                              |
| byte[]         | An array of bytes                                                                                                                   |
| MetadataPair[] | An array of metadataPair objects                                                                                                    |
| FileMeta[]     | An array of fileMeta objects                                                                                                        |
| File[]         | An array of file objects                                                                                                            |
| maskedByte[]   | An array of bytes representing masked file data                                                                                     |

## Structure Overview

Before delving into the full SNG specification, the following is a brief overview of the structure. The format consists of a header followed by three sections, each adhering to this format:

| Field         | Data Type | Size          | Description                                 |
| ------------- | --------- | ------------- | ------------------------------------------- |
| sectionLength | uint64    | 8             | Length of section in bytes after this field |
| sectionData   | byte[]    | sectionLength | Bytes that make up the section              |

These is the required ordering of each of these components:

| Section Type | Description                                                                        |
| ------------ | ---------------------------------------------------------------------------------- |
| Header       | The file header contains important details needed to parse the format              |
| Metadata     | Metadata information for applications that cover the specific song data stored     |
| FileIndex    | Defines information about what files are within the container and how to read them |
| FileData     | Contains the actual file data                                                      |


## Sections

### `Header`
| Field          | Data Type   | Size | Description                               |
| -------------- | ----------- | ---- | ----------------------------------------- |
| fileIdentifier | SngIdentify | 6    | SNGPKG sequence to identify the file type |
| version        | uint32      | 4    | The .sng format version                   |
| xorMask        | byte[]      | 16   | Random bytes for masking files            |


### `Metadata`
| Field             | Data Type      | Size            | Description                                     |
| ----------------- | -------------- | --------------- | ----------------------------------------------- |
| metadataLen       | uint64         | 8               | Number of bytes in the section after this field |
| metadataCount     | uint64         | 8               | Number of MetadataPair sections                 |
| metadataPairArray | MetadataPair[] | metadataLen - 8 | Array of MetadataPair sections                  |

### `MetadataPair` (represents a single metadata string key/value pair)
| Field    | Data Type | Size     | Description                              |
| -------- | --------- | -------- | ---------------------------------------- |
| keyLen   | int32     | 4        | The number of bytes in the key           |
| key      | string    | keyLen   | utf-8 byte string for the metadata's key |
| valueLen | int32     | 4        | The number of bytes in the value         |
| value    | string    | valueLen | The metadata's value                     |

### `FileIndex`
| Field         | Data Type  | Size            | Description                                     |
| ------------- | ---------- | --------------- | ----------------------------------------------- |
| fileMetaLen   | uint64     | 8               | Number of bytes in the section after this field |
| fileCount     | uint64     | 8               | Number of FileMeta (and FileData) sections      |
| fileMetaArray | FileMeta[] | fileMetaLen - 8 | Array of FileMeta sections                      |

### `FileMeta` (contains the file index metadata for each `File` section)
| Field         | Data Type | Size        | Description                                                                                 |
| ------------- | --------- | ----------- | ------------------------------------------------------------------------------------------- |
| filenameLen   | byte      | 1           | The number of bytes in the filename                                                         |
| filename      | string    | filenameLen | Relative file path within song folder. Folders are denoted by the '/' character following each folder. The filename follows all folders. |
| contentsLen   | uint64    | 8           | The number of bytes in the file's contents                                                  |
| contentsIndex | uint64    | 8           | The first byte index from the start of the file of the corresponding file section           |

### `FileData`
| Field         | Data Type | Size        | Description                            |
| ------------- | --------- | ----------- | -------------------------------------- |
| fileDataLen   | uint64    | 8           | Total length in bytes of all file data |
| fileDataArray | File[]    | fileDataLen | Concatenated file sections             |

### `File` (contains the binary contents of a single file)
| Field           | Data Type    | Size        | Description                       |
| --------------- | ------------ | ----------- | --------------------------------- |
| maskedFileBytes | maskedByte[] | contentsLen | The file's binary contents masked |

## Masking
File Data is masked using a fairly simple algorithm which utilizes the xorMask field from the file header and the byte pos within that file. The following is example pseudo code to convert to the file's true contents.
```js
 // Iterate through the indices of the maskedFileBytes array
 for i = 0 to size(maskedFileBytes) - 1:

    // Calculate the XOR key based on the current index and xorMask array
    xorKey = xorMask[i % 16] XOR (i AND 0xFF)    // You can cast to a byte instead of "AND 0xFF" if your language supports it

    // XOR each byte in maskedFileBytes with the xorKey
    fileBytes[i] = maskedFileBytes[i] XOR xorKey
```
## Metadata Strings
Some characters in metadata strings have restrictions to simplify serialization into INI files, affecting both keys and values. To include data with these characters, it's recommended to replace or remove them before converting into the SNG format.
- Semicolon (;)
  - Semicolons are used to denote comments in INI files. If a semicolon is allowed in a key, it will create confusion between a key-value pair and a comment, making it difficult to parse the INI file correctly.
- Newline characters (\r\n)
  - Newline characters mark the end of a line in a text file. Allowing newline characters in values makes it hard to tell where one key-value pair finishes and the next starts, causing problems when reading the file.

### Characters only disallowed for keys

- Equal sign (=)
  - In an INI file, an equal sign (=) separates keys and values. Allowing an equal sign in a key can cause confusion about where the key stops and  the value starts. This issue doesn't affect values, as only the  first equal sign on a line separates the key from the value. For instance, some song.ini tags use equal signs in values, like `<color=#00FF00>`, to indicate the color of a metadata string.

## Metadata values:

Metadata values are strings, but some are intended to be string representations of specific data types. Applications must decide how to parse the data type from the string. The following are a baseline of common types that should stay consistent between all applications:

### bool

Bool values take the form of 2 strings and they are case sensitive:

- True

- False

### integer

Integer values take the following form: (+/- sign)(digits)

1. sign  - Optional sign digit - or +

2. digits  - A sequence of digit characters 0 - 9

### float

Float values take the following form: (+/- sign)(integral_digits).(fractional_digits)

1. sign  - Optional sign digit - or +
2. integral_digits - A sequence of digit characters 0 - 9 representing whole numbers
3. period (.) - this is the delimiter between integral_digits and fractional_digits
4. fractional_digits - A sequence of digit characters 0 - 9 representing the fractional part of the number

### string

Strings are the final fallback data type any value that cannot be represented as above currently will be assumed as string as long as it follows the Metadata rules and the rules for the SNG utf-8 string type above (no null 0x00 characters).

## File Names
There are also some limitations to what is allowed for file names to prevent issues across operating systems when extracting files:

- `<` (less than)
- `>` (greater than)
- `:` (colon)
- `"` (double quote)
- `/` (forward slash)
- `\` (backslash)
- `|` (vertical bar or pipe)
- `?` (question mark)
- `*` (asterisk)
- Integer value characters below 31(`0x00 - 0x1F`), known as control characters
- `0x7f` (DEL character)
- `..` (Two consecutive periods)
- Do not end a file name with a period `.` or a space ` `
- the following names are prohibited due to windows reserved file name list, this includes when using file extensions. For example both CON and CON.txt are disallowed
  - `CON`
  - `PRN`
  - `AUX`
  - `NUL`
  - `COM0`
  - `COM1`
  - `COM2`
  - `COM3`
  - `COM4`
  - `COM5`
  - `COM6`
  - `COM7`
  - `COM8`
  - `COM9`
  - `LPT0`
  - `LPT1`
  - `LPT2`
  - `LPT3`
  - `LPT4`
  - `LPT5`
  - `LPT6`
  - `LPT7`
  - `LPT8`
  - `LPT9`

## Design Decisions
- The primary advantage of using a format like this is that it enables streaming audio data from the container without having to load the entire file into memory, thanks to the use of memory-mapped files. Since no compression is applied, the file offsets remain static, which simplifies reading data from the file.
  - In comparison, other projects often employ formats based on ZIP files. Although it's possible to seek within a ZIP file during runtime, the data typically needs to be decompressed and loaded into memory. In C#, this imposes a limit of 2GB of data per audio file. Additionally, because audio data is generally already compressed, using ZIP files introduces unnecessary overhead that slows down the process of loading audio. By opting for a format that does not rely on compression, these limitations can be avoided, allowing for more efficient streaming and access to audio data.
- The metadata is versatile and not confined to any particular application. It can encompass a multitude of properties, including those specific to individual applications. To prevent inadvertent property clashes among data from different applications, a metadata keys registry is also included in this repository. Metadata should be limited to what can be serialized into an INI file, as it must be capable of round-tripping to and from a song.ini file. For more complex metadata requiring additional data blobs, they should be managed as a separate file within the container.
- `.sng` is designed to be able to contain the binary contents of any set of files.
- File binaries are placed at the end of the format, and sections have lengths to allow programs to efficiently scan only the `.sng`'s `metadata` or `fileMeta` sections whichever may be required.
- The format has lengths defined in each major section to allow easily skipping over data that may not be required during parsing.
- The file binary is masked so that it can't be directly detected by programs that don't know how to parse the file.
- This format is exclusively in a LittleEndian byte ordering for efficiency on modern cpu architectures.
- File names are purposely limited to 255 bytes in length as most file systems have this length restriction.

## Registry

The registry is intended to be a helpful tool for anyone utilizing the format, participation ensures that others will not inadvertently use conflicting metadata keys. It is not required to register keys with the registry but it is in your best interest as an application developer to do so.

To submit a new entry for the registry submit a pull request to add any keys that your application may be making use of along with the recommended data type for this value. We recommend using a application prefix if it is something very specific to your application, however if it is a more general usage metadata value not including a prefix would make sense.

### Metadata registry

Currently there are no SNG specific metadata names registered but all the keys from these links should be considered taken thus far:

[GuitarGame_ChartFormats - Game Specific Tags](https://github.com/TheNathannator/GuitarGame_ChartFormats/blob/main/doc/FileFormats/song.ini/Game-Specific%20Tags.md)

[GuitarGame_ChartFormats - Typical Tags](https://github.com/TheNathannator/GuitarGame_ChartFormats/blob/main/doc/FileFormats/song.ini/Typical%20Tags.md)

### Filename Registry

The filename registry serves a similar purpose to the metadata one so that conflicting file names aren't utilized within the format that have different usages.

*note* that currently all registered file names are specified as lowercase, this is intentional. The reference encoding tool will force any files of these names to be lowercase within the output file, however any unknown file names will not be modified.

Additionally filenames can contain folders within the song seperated by the '/' character however currently there are no 'registered' folders as being used in the accepted format. The reference tool does support encoding folders when explicitly enabled.

- `notes.chart`
- `notes.mid`
- `song.ini` - Reserved, but will be not communicated through into sng file as this is the source of metadata in the SNG file
- `album.{png,jpg,jpeg}`
- `background.{png,jpg,jpeg}`
- `highway.{png,jpg,jpeg}`
- `video.{mp4,avi,webm,vp8,ogv,mpeg}`
- `guitar.{mp3,ogg,opus,wav}`
- `bass.{mp3,ogg,opus,wav}`
- `rhythm.{mp3,ogg,opus,wav}`
- `vocals.{mp3,ogg,opus,wav}`
- `vocals_1.{mp3,ogg,opus,wav}`
- `vocals_2.{mp3,ogg,opus,wav}`
- `drums.{mp3,ogg,opus,wav}`
- `drums_1.{mp3,ogg,opus,wav}`
- `drums_2.{mp3,ogg,opus,wav}`
- `drums_3.{mp3,ogg,opus,wav}`
- `drums_4.{mp3,ogg,opus,wav}`
- `keys.{mp3,ogg,opus,wav}`
- `song.{mp3,ogg,opus,wav}`
- `crowd.{mp3,ogg,opus,wav}`
- `preview.{mp3,ogg,opus,wav}`

