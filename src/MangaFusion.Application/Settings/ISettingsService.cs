namespace MangaFusion.Application.Settings;

/// <summary>Known runtime-setting keys (also the configuration keys they shadow).</summary>
public static class SettingKeys
{
    public const string MonitorCron = "Monitoring:Cron";
    public const string DefaultLanguages = "Monitoring:DefaultLanguages";
    public const string DefaultGraceDays = "Monitoring:DefaultGraceDays";
    public const string AllowSelfRegistration = "Auth:AllowSelfRegistration";

    /// <summary>Overrides the app-wide minimum log level (including the noisy EF Core/HttpClient/
    /// Hangfire categories, which are otherwise quiet); null/blank means "use the configured default".</summary>
    public const string MinimumLogLevel = "Logging:MinimumLevel";

    public static readonly IReadOnlyList<string> All =
        [MonitorCron, DefaultLanguages, DefaultGraceDays, AllowSelfRegistration, MinimumLogLevel];
}

/// <summary>The effective (DB-over-config-over-default) values for the editable settings.</summary>
public sealed record EffectiveSettings(
    string MonitorCron,
    IReadOnlyList<string> DefaultLanguages,
    int DefaultGraceDays,
    bool AllowSelfRegistration,
    string? MinimumLogLevel);

/// <summary>Reads and writes global settings. Reads resolve DB value → configuration → built-in
/// default; writes persist (or clear) the DB override.</summary>
public interface ISettingsService
{
    /// <summary>The stored DB override for a key, or null if none (i.e. using config/default).</summary>
    Task<string?> GetRawAsync(string key, CancellationToken ct = default);

    /// <summary>Persists a DB override; a null/blank value clears it so the key reverts to config/default.</summary>
    Task SetAsync(string key, string? value, CancellationToken ct = default);

    Task<string> GetMonitorCronAsync(CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetDefaultLanguagesAsync(CancellationToken ct = default);

    Task<int> GetDefaultGraceDaysAsync(CancellationToken ct = default);

    Task<bool> GetAllowSelfRegistrationAsync(CancellationToken ct = default);

    /// <summary>The stored minimum-log-level override, or null if none (using the configured default).</summary>
    Task<string?> GetMinimumLogLevelAsync(CancellationToken ct = default);

    Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default);
}
