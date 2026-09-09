namespace VideoHostingService.Models;

public class RabbitMqConfiguration
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;

    public string User { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Durable queue for the rungs that make a video playable. Kept separate from
    /// <see cref="OptionalQueueName"/> so these short, viewer-facing jobs cannot queue behind a
    /// long 4K encode.
    /// </summary>
    public string RequiredQueueName { get; set; } = "vaultvid.transcode.required";

    /// <summary>Durable queue for the rungs above the required ones.</summary>
    public string OptionalQueueName { get; set; } = "vaultvid.transcode.optional";

    public string QueueFor(TranscodeStage stage)
        => stage == TranscodeStage.Required ? RequiredQueueName : OptionalQueueName;
}
