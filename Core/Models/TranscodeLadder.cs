namespace VideoHostingService.Models;

/// <summary>One step of the transcode ladder: an output height and the video bitrate it targets.</summary>
public readonly record struct LadderRung(int Height, int VideoBitrateKbps)
{
    /// <summary>Advertised bandwidth, video plus the fixed audio track, in bits per second.</summary>
    public int BandwidthBitsPerSecond => (VideoBitrateKbps + TranscodeLadder.AudioBitrateKbps) * 1000;

    public string Name => $"{Height}p";
}

/// <summary>
/// Decides which renditions a source deserves. The ladder is capped at the source resolution -
/// upscaling a 1080p source to 2160p costs a lot of CPU and storage to produce a worse-looking
/// stream - and the low rungs are ordered first so a video becomes watchable as early as possible.
/// </summary>
public static class TranscodeLadder
{
    public const int AudioBitrateKbps = 128;

    /// <summary>Every rung, ascending.</summary>
    public static readonly IReadOnlyList<LadderRung> All =
    [
        new(360, 800),
        new(480, 1400),
        new(720, 2800),
        new(1080, 5000),
        new(1440, 9000),
        new(2160, 16000),
    ];

    /// <summary>Rungs that must exist before a video is considered playable.</summary>
    private static readonly int[] RequiredHeights = [360, 480];

    /// <summary>
    /// The rungs to produce for a source of the given height, in the order they should be
    /// encoded: the required rungs first, then the remaining ones ascending.
    /// </summary>
    public static IReadOnlyList<LadderRung> Plan(int sourceHeight)
    {
        var applicable = All.Where(rung => rung.Height <= sourceHeight).ToList();

        if (applicable.Count == 0)
        {
            // A source shorter than the lowest rung still gets exactly one rendition, at its own
            // (even) height, so that the readiness gate below is satisfiable.
            return [new LadderRung(MakeEven(sourceHeight), All[0].VideoBitrateKbps)];
        }

        return
        [
            .. applicable.Where(rung => RequiredHeights.Contains(rung.Height)),
            .. applicable.Where(rung => !RequiredHeights.Contains(rung.Height)),
        ];
    }

    /// <summary>
    /// The rungs whose completion flips a video to <see cref="VideoStatus.Ready"/>. Normally 360p
    /// and 480p; for a source too small for either, the single rendition from <see cref="Plan"/>.
    /// </summary>
    public static IReadOnlyList<LadderRung> Required(int sourceHeight)
    {
        var required = All.Where(rung => RequiredHeights.Contains(rung.Height) && rung.Height <= sourceHeight).ToList();
        return required.Count > 0 ? required : Plan(sourceHeight);
    }

    /// <summary>
    /// The rungs above the required ones: everything <see cref="Plan"/> covers that
    /// <see cref="Required"/> does not. These only widen the quality menu of a video that already
    /// plays, so they are queued separately and encoded after it is live.
    /// </summary>
    public static IReadOnlyList<LadderRung> Optional(int sourceHeight)
    {
        var required = Required(sourceHeight).Select(rung => rung.Height).ToHashSet();
        return [.. Plan(sourceHeight).Where(rung => !required.Contains(rung.Height))];
    }

    /// <summary>
    /// Output width for a rung, preserving the source aspect ratio.
    /// </summary>
    /// <remarks>
    /// This has to reproduce what ffmpeg's <c>scale=-2:{height}</c> filter actually does, because
    /// the result is what gets advertised as RESOLUTION in the HLS master playlist. ffmpeg rounds
    /// the exact ratio to the *nearest* multiple of two, not down to it: a 1920x1080 source scaled
    /// to 480 lines is 853.33 wide, which becomes 854, not 852.
    /// </remarks>
    public static int WidthFor(LadderRung rung, int sourceWidth, int sourceHeight)
    {
        if (sourceHeight <= 0 || sourceWidth <= 0)
        {
            return 0;
        }

        var exact = (double)sourceWidth * rung.Height / sourceHeight;
        return Math.Max(2, 2 * (int)Math.Round(exact / 2, MidpointRounding.AwayFromZero));
    }

    private static int MakeEven(int value) => value - (value % 2);
}
