namespace VideoHostingService.Models;

public class RabbitMqConfiguration
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5672;

    public string User { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Durable queue transcode jobs are published to and consumed from.</summary>
    public string QueueName { get; set; } = "vaultvid.transcode";
}
