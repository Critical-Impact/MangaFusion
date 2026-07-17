using MangaFusion.Application.Settings;
using MangaFusion.Domain.Settings;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Settings;

/// <summary>DB-over-config settings store. The <c>Settings</c> table is tiny and read infrequently
/// (recurring scan cadence, per-scan language defaults), so reads hit the DB directly rather than
/// caching — no invalidation to get wrong.</summary>
public sealed class SettingsService(AppDbContext db, IConfiguration config) : ISettingsService
{
    private const string DefaultCron = "0 * * * *";
    private const int DefaultGraceDays = 7;
    private const bool DefaultAllowSelfRegistration = true;

    public async Task<string?> GetRawAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct))?.Value;

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var existing = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing is not null)
            {
                db.Settings.Remove(existing); // clear the override → revert to config/default
            }
        }
        else if (existing is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<string> GetMonitorCronAsync(CancellationToken ct = default) =>
        await GetRawAsync(SettingKeys.MonitorCron, ct)
        ?? config[SettingKeys.MonitorCron]
        ?? DefaultCron;

    public async Task<IReadOnlyList<string>> GetDefaultLanguagesAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(SettingKeys.DefaultLanguages, ct);
        if (raw is not null)
        {
            return ParseLanguages(raw);
        }

        var configured = config.GetSection(SettingKeys.DefaultLanguages).Get<string[]>();
        return configured is { Length: > 0 } ? configured : ["en"];
    }

    public async Task<int> GetDefaultGraceDaysAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(SettingKeys.DefaultGraceDays, ct);
        if (int.TryParse(raw, out var stored) && stored >= 0)
        {
            return stored;
        }

        return config.GetValue(SettingKeys.DefaultGraceDays, DefaultGraceDays);
    }

    public async Task<bool> GetAllowSelfRegistrationAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(SettingKeys.AllowSelfRegistration, ct);
        if (bool.TryParse(raw, out var stored))
        {
            return stored;
        }

        return config.GetValue(SettingKeys.AllowSelfRegistration, DefaultAllowSelfRegistration);
    }

    public Task<string?> GetMinimumLogLevelAsync(CancellationToken ct = default) =>
        GetRawAsync(SettingKeys.MinimumLogLevel, ct);

    public async Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default) => new(
        await GetMonitorCronAsync(ct),
        await GetDefaultLanguagesAsync(ct),
        await GetDefaultGraceDaysAsync(ct),
        await GetAllowSelfRegistrationAsync(ct),
        await GetMinimumLogLevelAsync(ct));

    /// <summary>Languages are stored as a comma-separated list in the single Value column.</summary>
    internal static IReadOnlyList<string> ParseLanguages(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
