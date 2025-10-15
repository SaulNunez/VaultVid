using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VideoHostingService.Utilities;

public static class VideoValidator
{
    private static readonly Dictionary<string, byte[][]> VideoHeaders = new()
    {
        // MP4, M4V — ISO Base Media format
        // Usually starts with "....ftyp"
        [".mp4"] = new[] { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }, [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70] },
        [".m4v"] = new[] { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 } },

        // MKV and WebM — Matroska container starts with "1A 45 DF A3"
        [".mkv"] = new[] { new byte[] { 0x1A, 0x45, 0xDF, 0xA3 } },
        [".webm"] = new[] { new byte[] { 0x1A, 0x45, 0xDF, 0xA3 } },

        // AVI — starts with "RIFF....AVI "
        [".avi"] = new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } },

        // MOV — QuickTime format, often similar to MP4 ("ftypqt  ")
        [".mov"] = new[] { new byte[] { 0x00, 0x00, 0x00, 0x14, 0x66, 0x74, 0x79, 0x70, 0x71, 0x74 } },

        // MPEG/MPG — starts with 00 00 01 BA or 00 00 01 B3
        [".mpeg"] = new[] { new byte[] { 0x00, 0x00, 0x01, 0xBA }, [0x00, 0x00, 0x01, 0xB3] },
        [".mpg"] = new[] { new byte[] { 0x00, 0x00, 0x01, 0xBA }, [0x00, 0x00, 0x01, 0xB3] },

        // OGV (Ogg Video) — starts with "OggS"
        [".ogv"] = new[] { new byte[] { 0x4F, 0x67, 0x67, 0x53 } },

        // XVID/DivX AVI variant — still uses RIFF, but may include "XVID" or "DIVX" later.
        [".xvid"] = new[] { new byte[] { 0x52, 0x49, 0x46, 0x46 } },
    };

    public static bool IsValidVideoHeader(Stream fileStream, string extension)
    {
        if (!VideoHeaders.ContainsKey(extension))
            return false;

        // Read first 64 bytes (enough to detect most video formats)
        int maxHeaderSize = 64;
        byte[] header = new byte[maxHeaderSize];

        long originalPosition = fileStream.Position;
        fileStream.Seek(0, SeekOrigin.Begin);
        int bytesRead = fileStream.Read(header, 0, header.Length);
        fileStream.Seek(originalPosition, SeekOrigin.Begin);

        if (bytesRead == 0)
            return false;

        foreach (var signature in VideoHeaders[extension])
        {
            if (header.Take(signature.Length).SequenceEqual(signature))
            {
                // Additional format-specific validation
                if (extension == ".avi" || extension == ".xvid")
                {
                    // AVI files start with "RIFF" and contain "AVI " at bytes 8–11
                    return bytesRead >= 12 &&
                           header[8] == 0x41 && header[9] == 0x56 &&
                           header[10] == 0x49 && header[11] == 0x20;
                }

                if (extension == ".webm" || extension == ".mkv")
                {
                    // Matroska files should contain "matroska" or "webm" later, but magic number check is often enough
                    return true;
                }

                if (extension == ".mp4" || extension == ".m4v" || extension == ".mov")
                {
                    // Must contain "ftyp" within first 12 bytes
                    return header.Skip(4).Take(4).SequenceEqual(new byte[] { 0x66, 0x74, 0x79, 0x70 });
                }

                return true;
            }
        }

        return false;
    }
}
