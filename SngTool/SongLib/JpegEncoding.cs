using System;
using Cysharp.Collections;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace SongLib
{
    public static class JpegEncoding
    {
        public enum SizeTiers
        {
            None = 0,
            Nearest = 1,
            Size256x256 = 256,
            Size384x384 = 384,
            Size512x512 = 512,
            Size768x768 = 768,
            Size1024x1024 = 1024,
            Size1536x1536 = 1536,
            Size2048x2048 = 2048,
        }

        private static SizeTiers FindNewSize(int originalSize)
        {
            if (originalSize < 256)
            {
                return SizeTiers.None;
            }
            else if (originalSize < 384)
            {
                return SizeTiers.Size256x256;
            }
            else if (originalSize < 512)
            {
                return SizeTiers.Size384x384;
            }
            else if (originalSize < 768)
            {
                return SizeTiers.Size512x512;
            }
            else if (originalSize < 1024)
            {
                return SizeTiers.Size768x768;
            }
            else if (originalSize < 1536)
            {
                return SizeTiers.Size1024x1024;
            }
            else if (originalSize < 2048)
            {
                return SizeTiers.Size1536x1536;
            }
            else // anything larger clamp to 2048
            {
                return SizeTiers.Size2048x2048;
            }
        }

        private static int CalculateFinalSize(SizeTiers sizeOption, bool upscale, int imageSize)
        {
            // rescaling forces the use of a specific resolution
            if (upscale && sizeOption > SizeTiers.Nearest)
            {
                return (int)sizeOption;
            }

            if (sizeOption == SizeTiers.Nearest)
            {
                var nearestSize = FindNewSize(imageSize);

                if (nearestSize == SizeTiers.None)
                {
                    return imageSize;
                }

                return (int)nearestSize;
            }
            else if (sizeOption > SizeTiers.Nearest)
            {
                // clamp size to max specified
                if (imageSize >= (int)sizeOption)
                {
                    return (int)sizeOption;
                }
                // If we are less than the requested size just use the original size
                else
                {
                    return imageSize;
                }
            }
            else
            {
                return imageSize;
            }
        }

        /// <summary>
        /// Encodes image to jpeg with resizing to nearest supported resolution
        /// 
        /// The supported resolutions have been chosen to be divisible
        /// by 4 and mostly are power of twos with a few between them to even out the range.
        /// resizing is automatically disabled if the image is not the same width/height
        /// </summary>
        /// <param name="filePath">File path of input image</param>
        /// <param name="quality">Image quality level</param>
        /// <param name="upscale">Enables image rescaling</param>
        /// <param name="size">Resize images to specific sizes or the nearest option lower</param>
        /// <returns>byte array of new image</returns>
        public async static Task<(string fileName, NativeByteArray?)> EncodeImageToJpeg(string filePath, int quality = 75, bool upscale = false, SizeTiers size = SizeTiers.Size512x512)
        {
            var output = new NativeByteArray(skipZeroClear: true);

            try
            {
                using (var file = File.OpenRead(filePath))
                using (var image = await Image.LoadAsync(file))
                {
                    // Don't resize if it's not square
                    if (image.Height == image.Width && size != SizeTiers.None)
                    {
                        var sizeVal = CalculateFinalSize(size, upscale, image.Height);
                        image.Mutate(x => x.Resize(sizeVal, sizeVal, KnownResamplers.CatmullRom));
                    }

                    JpegEncoder encoder = new JpegEncoder
                    {
                        Quality = quality,
                        ColorType = JpegEncodingColor.Rgb,
                        SkipMetadata = true
                    };
                    var jpgStream = output.AsStream(FileAccess.ReadWrite);
                    await image.SaveAsJpegAsync(jpgStream, encoder);
                    output.Resize(jpgStream.Position);

                    if (upscale || output.Length < file.Length)
                    {
                        var name = Path.GetFileNameWithoutExtension(filePath);
                        return ($"{name}.jpg", output);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error encoding {filePath}, falling back to original image. {e}");
            }

            // Return original file if it's smaller in size or if we encountered an error
            return (Path.GetFileName(filePath), await LargeFile.ReadAllBytesAsync(filePath));
        }
    }
}