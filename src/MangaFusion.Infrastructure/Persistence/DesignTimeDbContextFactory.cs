using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MangaFusion.Infrastructure.Persistence;

/// <summary>
/// Lets EF Core CLI tooling (<c>dotnet ef migrations …</c>) construct the context without booting
/// the web host. Uses SQLite because migrations are authored per-provider and SQLite is the default.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=data/mangafusion.db",
                sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
