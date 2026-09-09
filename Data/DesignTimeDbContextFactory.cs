using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using VideoHostingService.Models.Identity;

namespace VideoHostingService.Data;

/// <summary>
/// Lets `dotnet ef` build the model without starting the application host.
/// </summary>
/// <remarks>
/// Without this, EF constructs the host from Program.cs, which fails whenever MinIO or the broker
/// are unconfigured - exactly the situation you are usually in when adding a migration. It also
/// tells EF where to look now that ApplicationDbContext lives in VideoHostingService.Core rather
/// than in the startup assembly.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Scaffolding a migration only needs the provider, never a reachable server, so a
        // placeholder is fine when no connection string is configured.
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=vaultvid;Username=postgres;Password=postgres";

        // IdentityDbContext.OnModelCreating reads IdentityOptions.Stores.MaxLengthForKeys off the
        // context's *application* service provider to decide the key column widths. A context built
        // standalone here has no such provider, so it would model AspNetUserLogins/AspNetUserTokens
        // keys as unbounded text while the running app - where AddEntityFrameworkStores sets 128 -
        // models them as varchar(128). That mismatch makes Migrate() fail at boot with
        // PendingModelChangesWarning, so the same option has to be supplied at design time.
        var identityServices = new ServiceCollection();
        identityServices.AddOptions<IdentityOptions>()
            .Configure(identity => identity.Stores.MaxLengthForKeys = 128);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("VideoHostingService"))
            .UseApplicationServiceProvider(identityServices.BuildServiceProvider())
            .Options;

        return new ApplicationDbContext(options);
    }
}
