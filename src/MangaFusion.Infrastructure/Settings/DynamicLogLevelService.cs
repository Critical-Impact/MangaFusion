using MangaFusion.Application.Settings;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Settings;

/// <summary>Persists and applies the app's minimum-log-level override, backed by the shared
/// <see cref="LogLevelOverride"/> the logging pipeline's per-category filters read live (see that
/// type for why a plain <c>IConfiguration</c> mutation doesn't work for this).</summary>
public sealed class DynamicLogLevelService(LogLevelOverride overrideState, ISettingsService settings)
{
    /// <summary>Re-applies whatever level is currently persisted (or clears it) — call once at
    /// startup so a restart doesn't silently drop an admin-chosen override.</summary>
    public async Task InitializeAsync(CancellationToken ct = default) =>
        overrideState.Set(Parse(await settings.GetMinimumLogLevelAsync(ct)));

    /// <summary>Sets (or, when <paramref name="levelName"/> is null/blank, clears) the override, both
    /// persisting it and applying it live. Throws if <paramref name="levelName"/> isn't a valid
    /// <see cref="LogLevel"/> name.</summary>
    public async Task ApplyAsync(string? levelName, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(levelName) && !Enum.TryParse<LogLevel>(levelName, ignoreCase: true, out _))
        {
            throw new InvalidOperationException(
                $"'{levelName}' is not a valid log level (Trace, Debug, Information, Warning, Error, Critical, None).");
        }

        await settings.SetAsync(SettingKeys.MinimumLogLevel, levelName, ct);
        overrideState.Set(Parse(levelName));
    }

    private static LogLevel? Parse(string? levelName) =>
        !string.IsNullOrWhiteSpace(levelName) && Enum.TryParse<LogLevel>(levelName, ignoreCase: true, out var parsed)
            ? parsed
            : null;
}
