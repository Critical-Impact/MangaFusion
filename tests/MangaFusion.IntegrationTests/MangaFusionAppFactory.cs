using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MangaFusion.IntegrationTests;

/// <summary>Boots the real Web app against an isolated temp SQLite DB and Data Protection key ring,
/// so tests don't touch the developer's local database.</summary>
public sealed class MangaFusionAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mf-itest-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "test.db")}");
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(_dir, "keys"));
        builder.UseSetting("Hangfire:ConnectionString", Path.Combine(_dir, "hangfire.db"));
        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
