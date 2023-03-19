using System;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace SngCli
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
        public static byte[] EncodeImageToJpeg(string filePath, int quality = 75, bool upscale = false, SizeTiers size = SizeTiers.Size512x512)
        {
            var ms = new MemoryStream();
            using (var file = File.OpenRead(filePath))
            using (var image = Image.Load(file))
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
                image.SaveAsJpeg(ms, encoder);
                Console.WriteLine($"Image Size: {image.Width}x{image.Height} Compression Ratio: {file.Length / (float)ms.Length:0.00}x");
                ms.Seek(0, SeekOrigin.Begin);
                return ms.ToArray();
            }
        }
    }
}