namespace VideoHostingService.Models;

/// <summary>
/// Which half of the ladder a transcode job covers. The two stages travel on separate queues so
/// that a new upload's required rungs are never stuck behind another video's optional 4K encode.
/// </summary>
public enum TranscodeStage
{
    /// <summary>
    /// The rungs that make a video playable at all - see <see cref="TranscodeLadder.Required"/>.
    /// Short jobs, and the only ones a viewer is waiting on.
    /// </summary>
    Required = 0,

    /// <summary>
    /// Everything above the required rungs. Long jobs that only widen the quality menu of a video
    /// that already plays.
    /// </summary>
    Optional = 1,
}
