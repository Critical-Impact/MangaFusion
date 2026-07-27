using MangaFusion.Application.Sources;
using MangaFusion.Infrastructure.Library;
using ContractKind = MangaFusion.Contracts.Models.MediaKind;
using DomainKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.UnitTests.Sources;

/// <summary>Guards the discard-fallback switches that translate <see cref="DomainKind"/> across the
/// Domain/Contracts boundary and onto disk. None of them is exhaustive — each has a <c>_ =&gt; Manga</c>
/// (or <c>"manga"</c>) arm — so the compiler will not flag a new <see cref="MediaKind"/> that someone
/// forgets to wire in; it would instead silently behave as manga. These tests are that missing
/// compiler check: a new kind that isn't added to every switch fails here.</summary>
public class MediaKindsRoundTripTests
{
    public static TheoryData<DomainKind> AllDomainKinds()
    {
        var data = new TheoryData<DomainKind>();
        foreach (var kind in Enum.GetValues<DomainKind>())
        {
            data.Add(kind);
        }

        return data;
    }

    public static TheoryData<ContractKind> AllContractKinds()
    {
        var data = new TheoryData<ContractKind>();
        foreach (var kind in Enum.GetValues<ContractKind>())
        {
            data.Add(kind);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllDomainKinds))]
    public void Domain_kind_round_trips_through_contract(DomainKind kind) =>
        // A forgotten arm makes ToContract(kind) fall through to Manga; ToDomain then yields Manga, so a
        // non-Manga kind that isn't wired up fails this identity.
        Assert.Equal(kind, MediaKinds.ToDomain(MediaKinds.ToContract(kind)));

    [Theory]
    [MemberData(nameof(AllContractKinds))]
    public void Contract_kind_round_trips_through_domain(ContractKind kind) =>
        Assert.Equal(kind, MediaKinds.ToContract(MediaKinds.ToDomain(kind)));

    [Fact]
    public void The_two_enums_have_the_same_members() =>
        // Drift in either direction (a kind added to one copy only) means the round-trips above can't cover
        // every value on both sides — catch it head-on.
        Assert.Equal(Enum.GetNames<DomainKind>().Length, Enum.GetNames<ContractKind>().Length);

    [Theory]
    [MemberData(nameof(AllDomainKinds))]
    public void Primary_kind_of_a_single_kind_source_is_that_kind(DomainKind kind)
    {
        var source = new SingleKindSource(MediaKinds.ToContract(kind));
        Assert.Equal(kind, MediaKinds.PrimaryKindOf(source));
    }

    [Fact]
    public void Each_kind_maps_to_a_distinct_on_disk_folder()
    {
        // MediaKindFolder.For is a `_ => "manga"` discard switch too — a forgotten kind would silently share
        // the manga directory. Distinct folder names per kind proves every kind was given its own arm.
        var folders = Enum.GetValues<DomainKind>().Select(MediaKindFolder.For).ToList();
        Assert.Equal(folders.Count, folders.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void KindOf_uses_the_per_series_hint_when_the_source_declares_that_kind()
    {
        var source = new MultiKindSource(ContractKind.Manga, ContractKind.LightNovel);
        var novel = Series(ContractKind.LightNovel);
        Assert.Equal(DomainKind.LightNovel, MediaKinds.KindOf(source, novel));
    }

    [Fact]
    public void KindOf_falls_back_to_primary_when_the_series_has_no_hint()
    {
        var source = new MultiKindSource(ContractKind.Manga, ContractKind.LightNovel);
        Assert.Equal(DomainKind.Manga, MediaKinds.KindOf(source, Series(null)));
    }

    [Fact]
    public void KindOf_ignores_a_hint_the_source_never_declared()
    {
        // Guard: a source can't route a series into a library it doesn't even serve.
        var mangaOnly = new MultiKindSource(ContractKind.Manga);
        Assert.Equal(DomainKind.Manga, MediaKinds.KindOf(mangaOnly, Series(ContractKind.LightNovel)));
    }

    private static MangaFusion.Contracts.Models.SourceSeries Series(ContractKind? kind) =>
        new() { SourceId = "s", SourceSeriesId = "1", Title = "T", Kind = kind };

    private sealed class SingleKindSource(ContractKind kind) : MangaFusion.Contracts.Sources.ISource
    {
        public string Id => "single";
        public string DisplayName => "Single";
        public MangaFusion.Contracts.Models.SourceCapabilities Capabilities =>
            MangaFusion.Contracts.Models.SourceCapabilities.None;

        public IReadOnlyList<ContractKind> SupportedKinds { get; } = [kind];
    }

    private sealed class MultiKindSource(params ContractKind[] kinds) : MangaFusion.Contracts.Sources.ISource
    {
        public string Id => "multi";
        public string DisplayName => "Multi";
        public MangaFusion.Contracts.Models.SourceCapabilities Capabilities =>
            MangaFusion.Contracts.Models.SourceCapabilities.Metadata;

        public IReadOnlyList<ContractKind> SupportedKinds { get; } = kinds;
    }
}
