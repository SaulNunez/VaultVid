namespace VideoHostingService.Models;

/// <summary>
/// Job message published when a video's source object has been stored. Serialised as JSON onto
/// <see cref="RabbitMqConfiguration.QueueName"/>; shared so the web app and worker cannot drift.
/// </summary>
/// <param name="VideoId">Primary key, and the prefix component for this video's HLS objects.</param>
/// <param name="PublicId">Used only for logging and correlating with URLs.</param>
/// <param name="SourceObjectName">Key of the uploaded source in the media bucket.</param>
public record TranscodeRequest(Guid VideoId, Guid PublicId, string SourceObjectName);
