using MangaFusion.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MangaFusion.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the EF Core context using the configured provider. SQLite is the default;
    /// swapping to Postgres later is a matter of changing the connection string / provider call
    /// here (and adding the Npgsql package) — no domain or application code changes.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default") ?? "Data Source=data/mangafusion.db";

        EnsureSqliteDirectory(connectionString);

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        return services;
    }

    /// <summary>SQLite won't create missing parent directories for the DB file, so do it here.</summary>
    private static void EnsureSqliteDirectory(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
