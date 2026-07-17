namespace MangaFusion.Domain.Settings;

/// <summary>A global runtime setting. When present it overrides the equivalent <c>appsettings</c>/env
/// configuration key; when absent the app falls back to configuration, then a built-in default.
/// <see cref="Key"/> is the configuration key it shadows (e.g. <c>Monitoring:Cron</c>).</summary>
public class Setting
{
    public string Key { get; set; } = default!;

    public string Value { get; set; } = default!;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
