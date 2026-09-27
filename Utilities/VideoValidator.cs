namespace VideoHostingService.Utilities;

public static class VideoValidator
{
    /// <summary>Bytes of the file that <see cref="IsValidVideoHeader"/> needs to see.</summary>
    public const int HeaderSize = 16;

    private static readonly byte[] Ftyp = [0x66, 0x74, 0x79, 0x70]; // "ftyp"
    private static readonly byte[] Riff = [0x52, 0x49, 0x46, 0x46]; // "RIFF"
    private static readonly byte[] Matroska = [0x1A, 0x45, 0xDF, 0xA3];
    private static readonly byte[] OggS = [0x4F, 0x67, 0x67, 0x53]; // "OggS"

    public static bool IsValidVideoHeader(ReadOnlySpan<byte> header, string extension)
    {
        if (header.IsEmpty)
        {
            return false;
        }

        return extension switch
        {
            // ISO base media (MP4/M4V/MOV): a 4-byte box size followed by "ftyp". The size varies
            // per file, so only the brand marker is worth matching.
            ".mp4" or ".m4v" or ".mov" => header.Length >= 8 && header[4..8].SequenceEqual(Ftyp),

            // Matroska / WebM
            ".mkv" or ".webm" => StartsWith(header, Matroska),

            // AVI: "RIFF" then "AVI " at bytes 8-11
            ".avi" or ".xvid" => StartsWith(header, Riff) && header.Length >= 12 &&
                                 header[8] == 0x41 && header[9] == 0x56 &&
                                 header[10] == 0x49 && header[11] == 0x20,

            // MPEG program/video stream start codes
            ".mpeg" or ".mpg" => header.Length >= 4 && header[0] == 0x00 && header[1] == 0x00 &&
                                 header[2] == 0x01 && (header[3] == 0xBA || header[3] == 0xB3),

            ".ogv" => StartsWith(header, OggS),

            _ => false,
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature)
        => header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
}
