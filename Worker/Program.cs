using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using VideoHostingService.Models;
using VideoHostingService.Models.Identity;
using VideoHostingService.Services;
using VideoHostingService.Worker;

var builder = Host.CreateApplicationBuilder(args);

// The web app owns the schema and applies migrations on boot; the worker only reads and writes
// rows, so it must never call Migrate() itself.
builder.Services.AddDbContext<ApplicationDbContext>(
    c => c.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.Configure<ObjectStorageConfiguration>(
    builder.Configuration.GetSection(ObjectStorageConfiguration.SectionName));
builder.Services.Configure<RabbitMqConfiguration>(
    builder.Configuration.GetSection(RabbitMqConfiguration.SectionName));
builder.Services.Configure<TranscodeWorkerOptions>(
    builder.Configuration.GetSection(TranscodeWorkerOptions.SectionName));

var storage = builder.Configuration.GetSection(ObjectStorageConfiguration.SectionName)
    .Get<ObjectStorageConfiguration>();

if (storage is null || string.IsNullOrWhiteSpace(storage.Endpoint))
{
    // Unlike the web app, the worker has nothing useful to do without object storage.
    Console.Error.WriteLine("Object storage is not configured. Check section ObjectStorage in Configuration, either environment variables, or appsettings.json");
    return 1;
}

// Only the internal endpoint is needed here: the worker reads and writes objects directly and
// never presigns URLs for a browser.
builder.Services.AddMinio(configureClient => configureClient
    .WithEndpoint(storage.Endpoint)
    .WithCredentials(storage.AccessKey, storage.SecretKey)
    .WithSSL(storage.UseSsl)
    .Build());

builder.Services.AddSingleton<VideoProbe>();
builder.Services.AddSingleton<FfmpegTranscoder>();
builder.Services.AddScoped<MediaStorage>();
builder.Services.AddScoped<TranscodeProcessor>();

// Publishing the follow-up optional job needs the same client the web app uses.
builder.Services.AddSingleton<ITranscodeQueue, RabbitMqTranscodeQueue>();

// One consumer per lane this worker serves.
var lanes = builder.Configuration
    .GetSection(TranscodeWorkerOptions.SectionName)
    .Get<TranscodeWorkerOptions>()?.ParsedLanes ?? [TranscodeStage.Required, TranscodeStage.Optional];

if (lanes.Count == 0)
{
    Console.Error.WriteLine("TranscodeWorker:Lanes names no valid lane; expected Required, Optional, or both.");
    return 1;
}

foreach (var lane in lanes)
{
    builder.Services.AddSingleton<IHostedService>(sp => new TranscodeConsumer(
        lane,
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IOptions<RabbitMqConfiguration>>(),
        sp.GetRequiredService<IOptions<TranscodeWorkerOptions>>(),
        sp.GetRequiredService<ILogger<TranscodeConsumer>>()));
}

var host = builder.Build();
await host.RunAsync();

return 0;
