using Microsoft.EntityFrameworkCore;
using VideoHostingService.Components;
using VideoHostingService.Models;
using VideoHostingService.Services;
using Microsoft.AspNetCore.Identity;
using VideoHostingService.Models.Identity;
using Minio;
using VideoHostingService.Utilities;
using VideoHostingService.VideoUploads;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDbContext<ApplicationDbContext>(
    c => c.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

var minioConfig = builder.Configuration.GetSection("ObjectStorage").Get<ObjectStorageConfiguration>();
if (minioConfig != null)
{
    builder.Services.AddMinio(configureClient => configureClient
            .WithEndpoint(minioConfig.Endpoint)
            .WithCredentials(minioConfig.AccessKey, minioConfig.SecretKey)
            .Build());
}
else
{
    Console.Error.WriteLine("Object storage could not be setup. Check section ObjectStorage in Configuration, either environment variables, or appsettings.json");
}

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisCacheConnection");
});

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<IVideoLikeService, VideoLikeService>();
builder.Services.AddScoped<IVideoCommentService, VideoCommentService>();
builder.Services.AddScoped<ICommentLikeService, CommentLikeService>();

builder.Services.AddTransient<IHumanTimeService, HumanTimeService>();
builder.Services.AddScoped<IVideoUploadService, VideoUploadService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// If the user updates their deployment, migrations will automatically update the DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
