using MangaFusion.Application.Settings;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.IntegrationTests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-settings-{Guid.NewGuid():N}.db");

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public async Task Falls_back_to_built_in_defaults_when_nothing_configured()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = new SettingsService(db, Config());

        Assert.Equal("0 * * * *", await svc.GetMonitorCronAsync());
        Assert.Equal(["en"], await svc.GetDefaultLanguagesAsync());
        Assert.Equal(7, await svc.GetDefaultGraceDaysAsync());
    }

    [Fact]
    public async Task Uses_configuration_when_no_db_override()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = new SettingsService(db, Config(
            ("Monitoring:Cron", "*/15 * * * *"),
            ("Monitoring:DefaultGraceDays", "3")));

        Assert.Equal("*/15 * * * *", await svc.GetMonitorCronAsync());
        Assert.Equal(3, await svc.GetDefaultGraceDaysAsync());
    }

    [Fact]
    public async Task Db_override_wins_over_configuration()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = new SettingsService(db, Config(("Monitoring:Cron", "*/15 * * * *")));

        await svc.SetAsync(SettingKeys.MonitorCron, "0 0 * * *");

        Assert.Equal("0 0 * * *", await svc.GetMonitorCronAsync());
    }

    [Fact]
    public async Task Clearing_an_override_reverts_to_configuration()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = new SettingsService(db, Config(("Monitoring:Cron", "*/15 * * * *")));

        await svc.SetAsync(SettingKeys.MonitorCron, "0 0 * * *");
        await svc.SetAsync(SettingKeys.MonitorCron, null); // clear → back to config

        Assert.Equal("*/15 * * * *", await svc.GetMonitorCronAsync());
        Assert.Null(await svc.GetRawAsync(SettingKeys.MonitorCron));
    }

    [Fact]
    public async Task Default_languages_round_trip_as_csv()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = new SettingsService(db, Config());

        await svc.SetAsync(SettingKeys.DefaultLanguages, "en, es , fr");

        Assert.Equal(["en", "es", "fr"], await svc.GetDefaultLanguagesAsync());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }
}
