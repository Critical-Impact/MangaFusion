using MangaFusion.Contracts.Sources;
using ContractKind = MangaFusion.Contracts.Models.MediaKind;

namespace MangaFusion.Application.Sources;

/// <summary>Translates <see cref="MediaKind"/> across the Contracts/Domain boundary. Contracts keeps its
/// own copy of the enum so it needn't depend on Domain (the same arrangement as ContentRating and
/// PublicationStatus), and like those it's mapped explicitly rather than cast — a numeric cast would
/// silently produce garbage the day the two enums drift.</summary>
public static class MediaKinds
{
    public static ContractKind ToContract(MediaKind kind) => kind switch
    {
        MediaKind.Comic => ContractKind.Comic,
        _ => ContractKind.Manga,
    };

    public static MediaKind ToDomain(ContractKind kind) => kind switch
    {
        ContractKind.Comic => MediaKind.Comic,
        _ => MediaKind.Manga,
    };

    /// <summary>The library a source belongs to. Sources may declare several kinds, but each is registered
    /// for one library in practice; the first is the one its content lands in.</summary>
    public static MediaKind PrimaryKindOf(ISource source) =>
        source.SupportedKinds.Count > 0 ? ToDomain(source.SupportedKinds[0]) : MediaKind.Manga;
}
