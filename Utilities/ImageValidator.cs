namespace VideoHostingService.Utilities;

public static class ImageValidator
{
    /// <summary>Bytes of the file that <see cref="IsValidImageHeader"/> needs to see.</summary>
    public const int HeaderSize = 12;

    private static readonly Dictionary<string, byte[][]> ImageHeaders = new()
    {
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".gif"] = [[0x47, 0x49, 0x46, 0x38]],
        [".bmp"] = [[0x42, 0x4D]],
        // WebP starts with "RIFF....WEBP"
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]],
    };

    public static bool IsValidImageHeader(ReadOnlySpan<byte> header, string extension)
    {
        if (!ImageHeaders.TryGetValue(extension, out var signatures) || header.IsEmpty)
        {
            return false;
        }

        foreach (var signature in signatures)
        {
            if (header.Length < signature.Length || !header[..signature.Length].SequenceEqual(signature))
            {
                continue;
            }

            // Extra check for WebP: must contain "WEBP" at bytes 8-11
            if (extension == ".webp")
            {
                return header.Length >= 12 &&
                       header[8] == 0x57 && header[9] == 0x45 &&
                       header[10] == 0x42 && header[11] == 0x50;
            }

            return true;
        }

        return false;
    }
}
