namespace VideoHostingService.Models;

/// <summary>
/// Where a video is in the upload-to-playable pipeline. A video is only watchable once it
/// reaches <see cref="Ready"/>, which happens as soon as the required rungs of the transcode
/// ladder exist - see <see cref="TranscodeLadder.Required"/>.
/// </summary>
public enum VideoStatus
{
    /// <summary>Row and source object exist; the transcode job has not been picked up yet.</summary>
    Pending = 0,

    /// <summary>A worker is transcoding. Some renditions may already be available.</summary>
    Processing = 1,

    /// <summary>The required renditions exist. Higher rungs may still be in progress.</summary>
    Ready = 2,

    /// <summary>Transcoding failed permanently; see <see cref="Video.ProcessingError"/>.</summary>
    Failed = 3,
}
