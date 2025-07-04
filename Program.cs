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

builder.Services.AddDbContext<VideoServiceContext>(
    c => c.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDbContext<ApplicationDbContext>(
    c => c.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDbContextConnection"))
);

var endpoint = builder.Configuration.GetSection("ObjectStorage")["Endpoint"];
var accessKey = builder.Configuration.GetSection("ObjectStorage")["AccessKey"];
var secretKey = builder.Configuration.GetSection("ObjectStorage")["SecretKey"];

builder.Services.AddMinio(configureClient => configureClient
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .Build());

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddSingleton<IVideoService, VideoService>();
builder.Services.AddSingleton<IVideoLikeService, VideoLikeService>();
builder.Services.AddSingleton<IVideoCommentService, VideoCommentService>();
builder.Services.AddSingleton<ICommentLikeService, CommentLikeService>();

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

app.Run();
