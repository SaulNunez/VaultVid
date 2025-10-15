using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VideoHostingService.Utilities;
public static class ImageValidator
{
    private static readonly Dictionary<string, byte[][]> ImageHeaders = new()
    {
        [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".jpg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
        [".png"] = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
        [".gif"] = new[] { new byte[] { 0x47, 0x49, 0x46, 0x38 } },
        [".bmp"] = new[] { new byte[] { 0x42, 0x4D } },
        [".webp"] = new[]
        {
            // WebP starts with "RIFF....WEBP"
            new byte[] { 0x52, 0x49, 0x46, 0x46 } // "RIFF"
        }
    };

    public static bool IsValidImageHeader(Stream fileStream, string extension)
    {
        if (!ImageHeaders.ContainsKey(extension))
            return false;

        // Read enough bytes to cover the largest header (e.g. 12 for WebP)
        int maxHeaderSize = 12;
        byte[] header = new byte[maxHeaderSize];

        long originalPosition = fileStream.Position;
        fileStream.Seek(0, SeekOrigin.Begin);
        int bytesRead = fileStream.Read(header, 0, header.Length);
        fileStream.Seek(originalPosition, SeekOrigin.Begin);

        if (bytesRead == 0)
            return false;

        foreach (var signature in ImageHeaders[extension])
        {
            if (header.Take(signature.Length).SequenceEqual(signature))
            {
                // Extra check for WebP: must contain "WEBP" at bytes 8–11
                if (extension == ".webp")
                {
                    return header.Length >= 12 &&
                           header[8] == 0x57 && header[9] == 0x45 &&
                           header[10] == 0x42 && header[11] == 0x50;
                }
                return true;
            }
        }

        return false;
    }
}
