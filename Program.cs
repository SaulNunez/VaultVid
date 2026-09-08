using Microsoft.EntityFrameworkCore;
using VideoHostingService.Components;
using VideoHostingService.Models;
using VideoHostingService.Services;
using Microsoft.AspNetCore.Identity;
using VideoHostingService.Models.Identity;
using Minio;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // InputFile streams uploads over the circuit in chunks; the 32 KB default
    // receive limit is too small for them. This is not the max upload size:
    // that is enforced by MaxUploadSizes below.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 1024 * 1024);

// The scaffolded ASP.NET Core Identity UI under Areas/Identity is Razor Pages,
// not Blazor, so it needs the Razor Pages services and endpoints registered too.
builder.Services.AddRazorPages();

builder.Services.AddDbContext<ApplicationDbContext>(
    c => c.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.Configure<MaxUploadSizes>(builder.Configuration.GetSection(MaxUploadSizes.SectionName));

var objectStorageSection = builder.Configuration.GetSection(ObjectStorageConfiguration.SectionName);
builder.Services.Configure<ObjectStorageConfiguration>(objectStorageSection);

var minioConfig = objectStorageSection.Get<ObjectStorageConfiguration>();
if (minioConfig != null)
{
    builder.Services.AddMinio(configureClient => configureClient
            .WithEndpoint(minioConfig.Endpoint)
            .WithCredentials(minioConfig.AccessKey, minioConfig.SecretKey)
            .WithSSL(minioConfig.UseSsl)
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

builder.Services.AddDefaultIdentity<IdentityUser>(options =>
        options.SignIn.RequireConfirmedAccount =
            builder.Configuration.GetValue("Identity:RequireConfirmedAccount", true))
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Makes the authentication state available to components as a cascading value,
// which is what <AuthorizeRouteView> and <AuthorizeView> read.
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

builder.Services.AddScoped<IVideoService, VideoService>();
builder.Services.AddScoped<IVideoLikeService, VideoLikeService>();
builder.Services.AddScoped<IVideoCommentService, VideoCommentService>();
builder.Services.AddScoped<ICommentLikeService, CommentLikeService>();
builder.Services.AddScoped<IMediaUrlService, MediaUrlService>();

builder.Services.AddTransient<IHumanTimeService, HumanTimeService>();

// Prevent exception due to asp net not finding our antiforgery token
// Solution can be found here: https://stackoverflow.com/a/47143941/14228475
builder.Services.AddDataProtection()
    .SetApplicationName("vaultvid")
    .PersistKeysToFileSystem(new DirectoryInfo(@"/vaultvid/keys"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Authentication has to run before antiforgery and before the endpoints so that
// [Authorize] and AuthenticationStateProvider see a populated ClaimsPrincipal.
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapRazorPages();

// If the user updates their deployment, migrations will automatically update the DB
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.Run();
