using MangaFusion.Domain.Library;
using MangaFusion.Domain.Settings;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MangaFusion.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context. Backs both ASP.NET Identity and (in later milestones) the manga
/// library domain. Provider is chosen at registration time (SQLite by default, Npgsql later),
/// so nothing here is provider-specific.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<SourceCredential> SourceCredentials => Set<SourceCredential>();

    public DbSet<Series> Series => Set<Series>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Chapter> Chapters => Set<Chapter>();
    public DbSet<ChapterRelease> ChapterReleases => Set<ChapterRelease>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<ArtifactChapter> ArtifactChapters => Set<ArtifactChapter>();
    public DbSet<Download> Downloads => Set<Download>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<ReadingProgress> ReadingProgress => Set<ReadingProgress>();
    public DbSet<SeriesReadingEntry> SeriesReadingEntries => Set<SeriesReadingEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Setting> Settings => Set<Setting>();

    public DbSet<MigrationBatch> MigrationBatches => Set<MigrationBatch>();
    public DbSet<MigrationSeries> MigrationSeries => Set<MigrationSeries>();
    public DbSet<MigrationItem> MigrationItems => Set<MigrationItem>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportSeries> ImportSeries => Set<ImportSeries>();
    public DbSet<ImportItem> ImportItems => Set<ImportItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply any IEntityTypeConfiguration<T> defined in this assembly. Domain entity
        // configurations (Series, Chapter, Follow, ReadingProgress, ...) are added in
        // later milestones and will be picked up automatically here.
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // SQLite has no native offset-aware date type, so its EF Core provider refuses to
        // translate ORDER BY / comparisons on DateTimeOffset columns into SQL (it can't guarantee
        // correct results across arbitrary offsets from a plain TEXT column). Every timestamp in
        // this app is written via DateTimeOffset.UtcNow, so storing as UTC DateTime instead is
        // lossless and makes those columns sortable/comparable in SQL. Postgres maps
        // DateTimeOffset to `timestamptz` natively (no such restriction), so this conversion is
        // SQLite-only — Npgsql keeps the richer native type once that provider lands.
        if (Database.IsSqlite())
        {
            var converter = new ValueConverter<DateTimeOffset, DateTime>(
                v => v.UtcDateTime,
                v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            var nullableConverter = new ValueConverter<DateTimeOffset?, DateTime?>(
                v => v.HasValue ? v.Value.UtcDateTime : null,
                v => v.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : null);

            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(converter);
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(nullableConverter);
                    }
                }
            }
        }
    }
}
