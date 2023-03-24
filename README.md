# SNG File Format

`.sng` is a simple and generic binary container format that groups a list of files and metadata into a single file.

This repository contains several items a reference tool for packing and unpacking sng files to and from the format, the file spec, and a Registry of used file metadata keys, and common file names.

# SNG File Specification


The following are definitions of the data types used in this file specification

| Data Type      | Description                                                                                                                         |
| -------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| byte           | 8-bit unsigned integer                                                                                                              |
| uint32         | 32-bit unsigned integer                                                                                                             |
| byteArr        | An array of bytes                                                                                                                   |
| uint64         | 64-bit unsigned integer                                                                                                             |
| int32          | 32-bit signed integer                                                                                                               |
| string         | A sequence of utf-8 characters a byte[] array excluding NULL 0x00 characters as many languages use these as end of string sequences |
| SngIdentify    | The SNGPKG file identifier. `0x53 0x4E 0x47 0x50 0x4b 0x47` as bytes. Used to identify the file format                              |
| byte[]         | An array of bytes                                                                                                                   |
| metadataPair[] | An array of metadataPair objects                                                                                                    |
| fileMeta[]     | An array of fileMeta objects                                                                                                        |
| file[]         | An array of file objects                                                                                                            |
| maskedByte[]   | An array of bytes representing masked file data                                                                                     |

## Structure Overview

Just a quick overview of the format before we get started with the detail specification. The file format is composed of a header plus 3 sections each of which follow the following basic format:

| Field         | Data Type | Size          | Description                                 |
| ------------- | --------- | ------------- | ------------------------------------------- |
| sectionLength | uint64    | 4             | Length of section in bytes after this field |
| sectionData   | byte[]    | sectionLength | Bytes that make up the section              |

These is the required ordering of each of these components:

| Section Type | Description                                                                         |
| ------------ | ----------------------------------------------------------------------------------- |
| Header       | The file header contains important details needed to parse the format               |
| Metadata     | Contains metadata information relevant to the rest of the data within the container |
| FileIndex    | Contains detailed information about how to read the files within the container      |
| FileData     | Contains detailed information about how to read the files within the container      |


## Sections

### Header
| Field          | Data Type   | Size | Description                               |
| -------------- | ----------- | ---- | ----------------------------------------- |
| fileIdentifier | SngIdentify | 6    | SNGPKG sequence to identify the file type |
| version        | uint32      | 4    | The .sng format version                   |
| seed           | byte[]      | 16   | Random bytes for masking files            |


### `Metadata`
| Field             | Data Type      | Size            | Description                                     |
| ----------------- | -------------- | --------------- | ----------------------------------------------- |
| metadataLen       | uint64         | 8               | Number of bytes in the section after this field |
| metadataCount     | uint64         | 8               | Number of metadata sections                     |
| metadataPairArray | metadataPair[] | metadataLen - 8 | Array of metadataPair sections                  |

### `[fileIndex]`
| Field         | Data Type  | Size            | Description                                     |
| ------------- | ---------- | --------------- | ----------------------------------------------- |
| fileMetaLen   | uint64     | 4               | Number of bytes in the section after this field |
| fileCount     | uint64     | 4               | Number of file and fileMeta sections            |
| fileMetaArray | fileMeta[] | fileMetaLen - 4 | Array of fileMeta sections                      |

### [fileData]
| Field         | Data Type | Size        | Description                            |
| ------------- | --------- | ----------- | -------------------------------------- |
| fileDataLen   | uint64    | 8           | Total length in bytes of all file data |
| fileDataArray | file[]    | fileDataLen | Concatenated file sections             |


### `[metadataPair]` (represents a single metadata string key/value pair)
| Field    | Data Type | Size     | Description                              |
| -------- | --------- | -------- | ---------------------------------------- |
| keyLen   | int32     | 4        | The number of bytes in the key           |
| key      | string    | keyLen   | utf-8 byte string for the metadata's key |
| valueLen | int32     | 4        | The number of bytes in the value         |
| value    | string    | valueLen | The metadata's value                     |


### `[fileMeta]` (contains the file index metadata for each `[file]` section)
| Field         | Data Type | Size        | Description                                                                       |
| ------------- | --------- | ----------- | --------------------------------------------------------------------------------- |
| filenameLen   | byte      | 1           | The number of bytes in the filename                                               |
| filename      | string    | filenameLen | The file's name                                                                   |
| contentsLen   | uint64    | 8           | The number of bytes in the file's contents                                        |
| contentsIndex | uint64    | 8           | The first byte index from the start of the file of the corresponding file section |


### `[file]` (contains the binary contents of a single file)
| Field           | Data Type    | Size        | Description                       |
| --------------- | ------------ | ----------- | --------------------------------- |
| maskedFileBytes | maskedByte[] | contentsLen | The file's binary contents masked |

## Masking
File Data is masked using a fairly simple algorithm which utilizes the seed field from the file header and the byte pos within that file. The following is example pseudo code to convert to the file's true contents.
```js
 // Iterate through the indices of the maskedFileBytes array
 for i = 0 to size(maskedFileBytes) - 1:

    // Calculate the XOR key based on the current index and seed array
    xorKey = seed[i % 16] XOR (i AND 0xFF)    // You can cast to a byte instead of "AND 0xFF" if your language supports it

    // XOR each byte in maskedFileBytes with the xorKey
    fileBytes[i] = maskedFileBytes[i] XOR xorKey
```
## Metadata Strings
There are some limitations to what characters are allowed within metadata strings in order to make the format simpler to serialize to ini files. These limitations apply to both keys and values. A recommendation for including data that may have these characters within it is to replace or remove the characters before serializing into the SNG format. 
- Semicolon (;)
  - Semicolons are used to denote comments in INI files. If a semicolon is allowed in a key, it will create confusion between a key-value pair and a comment, making it difficult to parse the INI file correctly.
- Newline characters (\r\n)
  - Newline characters denote the end of a line in a text file. Allowing newline characters in unquoted values would make it difficult to determine where a key-value pair ends and the next one begins, leading to parsing issues.

### Characters only disallowed for keys

- Equal sign (=)
  - The equal sign is used as a delimiter to separate keys and values in an INI file. If an equal sign is allowed in a key, it will create ambiguity in determining where the key ends and the value begins. For values this is not typically an issue because only the first = sign on a line is used to delimit the key from a value. Some values in song.ini tags include = for example if a song is including rich text tags such as `<color=#00FF00>` for indicating color for a metadata string.

## Metadata values:

Metadata values are strings but are designed to take a number of specific formats.

### bool

Bool values take the form of 2 strings and they are case sensitive:

- True

- False

### int

Int values take the following form: (+/- sign)(digits)

1. sign  - Optional sign digit - or +

2. digits  - A sequence of digit chracters 0 - 9

Int values also fall into the range -2147483648 to  2147483647 the size of a int32 data type

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
- Integer value characters below 31(0x00-0x1F), known as control characters
- `0x7f` (DEL character)
- `..` (Two consecutive periods)
- Do not end a file name with a period or a space
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
- The metadata is arbitrary and not tied to any specific application. There can be any number of properties. Application-specific properties can be added. However, to avoid unintentional property conflicts between the data of different applications there is a metadata keys registry also contained in this repo. Metadata should be kept to what can be serialized into an ini file. As any metadata needs to be able to round trip to/from a song.ini file. Any additional data blobs for more complex metadata should be handled as a file within the container.
- `.sng` is designed to be able to contain the binary contents of any set of files.
- File binaries are placed at the end of the format to allow programs to efficiently scan only the `.sng`'s `metadata` or `fileMeta` sections.
- The format has lengths defined in each major section to allow easily skipping over data that may not be required during parsing.
- The file binary is masked so that it can't be directly detected by programs that don't know how to parse the file.
- This format is exclusively in a LittleEndian byte ordering for efficiency on modern cpu architectures.
- File names are purposely limited to 255 bytes in length as most file systems have this length restriction.

## Registry

The registry is intended to be a helpful tool for anyone utilizing the format, participation ensures that other will not inadvertently use conflicting metadata keys. It is not required to register keys with the registry but it is in your best interest as an application developer to do so.

To submit a new entry for the registry submit a pull request to add any keys that your application may be making use of along with the recommended data type for this value. We recommend using a application prefix if it is something very specific to your application, however if it is a more general usage metadata value not including a prefix would make sense.

## Metadata registry

Currently there are no sng specific metadata names registered but all the keys from these links should be considered taken thus far:

https://github.com/TheNathannator/GuitarGame_ChartFormats/blob/main/doc/FileFormats/song.ini/Game-Specific%20Tags.md
https://github.com/TheNathannator/GuitarGame_ChartFormats/blob/main/doc/FileFormats/song.ini/Core%20Infrastructure.md