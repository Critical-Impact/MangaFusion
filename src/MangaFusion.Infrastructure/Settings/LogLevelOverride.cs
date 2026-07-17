using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Settings;

/// <summary>Thread-safe mutable holder for the app-wide minimum-log-level override, read live by the
/// per-category filter delegates registered against <c>WebApplicationBuilder.Logging</c> in
/// Program.cs. Must be constructed there — before <c>builder.Build()</c> — and registered as that
/// same singleton instance in <c>builder.Services</c>, so <see cref="DynamicLogLevelService"/> (an
/// ordinary DI-resolved service) updates the exact instance the filters close over.
///
/// This exists because mutating <c>IConfiguration</c> at runtime (even via the mutable
/// <c>ConfigurationManager</c> <c>WebApplicationBuilder.Configuration</c> exposes) does <b>not</b>
/// reliably re-trigger <c>Microsoft.Extensions.Logging</c>'s filter-rule reload — verified directly
/// before choosing this design over that simpler-looking alternative.</summary>
public sealed class LogLevelOverride
{
    /// <summary>Every category this app manages via the dynamic level, and what each defaults to with
    /// no override in effect — EF Core's SQL command logging, HttpClient request logging, and
    /// Hangfire's internal logging are all noisy at Information, so they stay quiet (Warning) unless
    /// explicitly opened up. A null category name is the logging system's "Default" catch-all rule.</summary>
    public static readonly IReadOnlyList<(string? Category, LogLevel Baseline)> ManagedCategories =
    [
        (null, LogLevel.Information),
        ("Microsoft.AspNetCore", LogLevel.Warning),
        ("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning),
        ("System.Net.Http.HttpClient", LogLevel.Warning),
        ("Hangfire", LogLevel.Warning),
    ];

    private volatile int _value = -1; // -1 = no override (each category uses its own baseline)

    public LogLevel? Current => _value < 0 ? null : (LogLevel)_value;

    public void Set(LogLevel? level) => _value = level.HasValue ? (int)level.Value : -1;

    /// <summary>The filter predicate for a managed category: shown if it clears whichever is more
    /// permissive of the override (when set) or that category's own quiet baseline.</summary>
    public bool IsEnabled(LogLevel baseline, LogLevel messageLevel) => messageLevel >= (Current ?? baseline);
}
