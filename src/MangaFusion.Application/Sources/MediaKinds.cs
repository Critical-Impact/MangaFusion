using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using ContractKind = MangaFusion.Contracts.Models.MediaKind;

namespace MangaFusion.Application.Sources;

/// <summary>Translates <see cref="MediaKind"/> across the Contracts/Domain boundary. Contracts keeps its
/// own copy of the enum so it needn't depend on Domain (the same arrangement as ContentRating and
/// PublicationStatus), and like those it's mapped explicitly rather than cast — a numeric cast would
/// silently produce garbage the day the two enums drift.</summary>
public static class MediaKinds
{
    // NOTE: these are discard-fallback switches, NOT exhaustive — the compiler will not flag a missing
    // arm when a new MediaKind is added. A forgotten arm silently maps the new kind to Manga (and, via
    // SourceRegistry.ForKind, resolves the manga sources for it). Every new kind must be added by hand
    // here; MediaKindsRoundTripTests guards against a value slipping through.
    public static ContractKind ToContract(MediaKind kind) => kind switch
    {
        MediaKind.Comic => ContractKind.Comic,
        MediaKind.LightNovel => ContractKind.LightNovel,
        _ => ContractKind.Manga,
    };

    public static MediaKind ToDomain(ContractKind kind) => kind switch
    {
        ContractKind.Comic => MediaKind.Comic,
        ContractKind.LightNovel => MediaKind.LightNovel,
        _ => MediaKind.Manga,
    };

    /// <summary>The library a source belongs to. Sources may declare several kinds, but each is registered
    /// for one library in practice; the first is the one its content lands in.</summary>
    public static MediaKind PrimaryKindOf(ISource source) =>
        source.SupportedKinds.Count > 0 ? ToDomain(source.SupportedKinds[0]) : MediaKind.Manga;

    /// <summary>The library a specific series from a source lands in. Honours the per-series
    /// <see cref="SourceSeries.Kind"/> hint when present <em>and</em> actually declared by the source
    /// (guarding against a source claiming a kind it never advertised), otherwise falls back to
    /// <see cref="PrimaryKindOf"/>. This is what routes a MangaUpdates "Novel" into the light-novel
    /// library while a manga from the same source stays in manga.</summary>
    public static MediaKind KindOf(ISource source, SourceSeries series) =>
        series.Kind is { } kind && source.SupportedKinds.Contains(kind)
            ? ToDomain(kind)
            : PrimaryKindOf(source);
}
