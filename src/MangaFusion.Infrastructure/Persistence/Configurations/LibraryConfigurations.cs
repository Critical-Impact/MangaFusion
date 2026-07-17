using MangaFusion.Domain.Library;
using MangaFusion.Domain.Settings;
using MangaFusion.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MangaFusion.Infrastructure.Persistence.Configurations;

// List<string> properties (AltTitles, PreferredGroups, ScanlationGroups, Languages) map to JSON
// columns via EF Core's primitive-collection convention — no explicit converters needed. Tags and
// Authors/Artists are real many-to-many relations (see TagConfiguration/AuthorConfiguration), not
// string collections.

public class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        builder.ToTable("Series");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired();

        // Every library list/browse query filters on Kind, so it always leads the predicate.
        builder.HasIndex(x => x.Kind);

        builder.HasMany(x => x.Chapters).WithOne(x => x.Series)
            .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Artifacts).WithOne(x => x.Series)
            .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.SourceLinks).WithOne(x => x.Series)
            .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Tags).WithMany(t => t.Series)
            .UsingEntity(j => j.ToTable("SeriesTags"));
        builder.HasMany(x => x.Authors).WithMany(a => a.AuthorOf)
            .UsingEntity(j => j.ToTable("SeriesAuthors"));
        builder.HasMany(x => x.Artists).WithMany(a => a.ArtistOf)
            .UsingEntity(j => j.ToTable("SeriesArtists"));
    }
}

public class AuthorConfiguration : IEntityTypeConfiguration<Author>
{
    public void Configure(EntityTypeBuilder<Author> builder)
    {
        builder.ToTable("Authors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SourceId).HasMaxLength(64);
        builder.Property(x => x.SourceAuthorId).HasMaxLength(64);
        // Lookup index only — dedup for source-provided authors is enforced by the find-or-create
        // upsert in code (AuthorResolver), same as TagConfiguration's SourceId/SourceTagId index.
        builder.HasIndex(x => new { x.SourceId, x.SourceAuthorId });
    }
}

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Group).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceId).HasMaxLength(64);
        builder.Property(x => x.SourceTagId).HasMaxLength(64);
        // Lookup index only — uniqueness for source-provided tags is enforced by the find-or-create
        // upsert in code (a filtered unique index isn't portable across SQLite/Postgres here). Kind
        // leads because the tag catalog is always fetched for one library at a time.
        builder.HasIndex(x => new { x.Kind, x.SourceId, x.SourceTagId });
    }
}

public class SeriesSourceLinkConfiguration : IEntityTypeConfiguration<SeriesSourceLink>
{
    public void Configure(EntityTypeBuilder<SeriesSourceLink> builder)
    {
        builder.ToTable("SeriesSourceLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceSeriesId).IsRequired();
        builder.HasIndex(x => new { x.SourceId, x.SourceSeriesId }).IsUnique();
    }
}

public class ChapterConfiguration : IEntityTypeConfiguration<Chapter>
{
    public void Configure(EntityTypeBuilder<Chapter> builder)
    {
        builder.ToTable("Chapters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Language).HasMaxLength(16).IsRequired();
        builder.Property(x => x.NumberKey).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.SeriesId, x.Language, x.NumberKey }).IsUnique();

        builder.HasMany(x => x.Releases).WithOne(x => x.Chapter)
            .HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);

        // Active pointers: DB sets them null when the target is removed (clean replace / delete).
        builder.HasOne(x => x.ActiveArtifact).WithMany()
            .HasForeignKey(x => x.ActiveArtifactId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.ActiveRelease).WithMany()
            .HasForeignKey(x => x.ActiveReleaseId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ChapterReleaseConfiguration : IEntityTypeConfiguration<ChapterRelease>
{
    public void Configure(EntityTypeBuilder<ChapterRelease> builder)
    {
        builder.ToTable("ChapterReleases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceChapterId).IsRequired();
        builder.Property(x => x.GroupKey).HasMaxLength(256);
        builder.HasIndex(x => new { x.SourceId, x.SourceChapterId }).IsUnique();
    }
}

public class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.ToTable("Artifacts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Path).IsRequired();

        builder.HasMany(x => x.ChapterLinks).WithOne(x => x.Artifact)
            .HasForeignKey(x => x.ArtifactId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ArtifactChapterConfiguration : IEntityTypeConfiguration<ArtifactChapter>
{
    public void Configure(EntityTypeBuilder<ArtifactChapter> builder)
    {
        builder.ToTable("ArtifactChapters");
        builder.HasKey(x => new { x.ArtifactId, x.ChapterId });

        // Cascade from the artifact; chapter side is NoAction to avoid multiple cascade paths.
        builder.HasOne(x => x.Chapter).WithMany(x => x.ArtifactLinks)
            .HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class DownloadConfiguration : IEntityTypeConfiguration<Download>
{
    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.ToTable("Downloads");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
    }
}

public class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        builder.ToTable("Follows");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.SeriesId }).IsUnique();

        builder.HasOne(x => x.Series).WithMany()
            .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        // Collections are always listed for one user in one library at a time.
        builder.HasIndex(x => new { x.UserId, x.Kind });

        builder.HasMany(x => x.Items).WithOne()
            .HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CollectionItemConfiguration : IEntityTypeConfiguration<CollectionItem>
{
    public void Configure(EntityTypeBuilder<CollectionItem> builder)
    {
        builder.ToTable("CollectionItems");
        builder.HasKey(x => x.Id);
        // A series appears at most once per collection.
        builder.HasIndex(x => new { x.CollectionId, x.SeriesId }).IsUnique();

        builder.HasOne(x => x.Series).WithMany()
            .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired();
        // The bell is per-user and per-library: unread counts are always scoped to both.
        builder.HasIndex(x => new { x.UserId, x.Kind, x.ReadAt });

        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.Value).IsRequired();
    }
}

public class MigrationBatchConfiguration : IEntityTypeConfiguration<MigrationBatch>
{
    public void Configure(EntityTypeBuilder<MigrationBatch> builder)
    {
        builder.ToTable("MigrationBatches");
        builder.HasKey(x => x.Id);

        builder.HasMany(x => x.Series).WithOne(x => x.Batch)
            .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MigrationSeriesConfiguration : IEntityTypeConfiguration<MigrationSeries>
{
    public void Configure(EntityTypeBuilder<MigrationSeries> builder)
    {
        builder.ToTable("MigrationSeries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FolderName).IsRequired();
        builder.HasIndex(x => x.BatchId);

        builder.HasMany(x => x.Items).WithOne(x => x.Series)
            .HasForeignKey(x => x.MigrationSeriesId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MigrationItemConfiguration : IEntityTypeConfiguration<MigrationItem>
{
    public void Configure(EntityTypeBuilder<MigrationItem> builder)
    {
        builder.ToTable("MigrationItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).IsRequired();
        builder.Property(x => x.NumberKey).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.MigrationSeriesId, x.NumberKey });
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("ImportBatches");
        builder.HasKey(x => x.Id);

        builder.HasMany(x => x.Series).WithOne(x => x.Batch)
            .HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportSeriesConfiguration : IEntityTypeConfiguration<ImportSeries>
{
    public void Configure(EntityTypeBuilder<ImportSeries> builder)
    {
        builder.ToTable("ImportSeries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.GroupTitle).IsRequired();
        builder.HasIndex(x => x.BatchId);

        builder.HasMany(x => x.Items).WithOne(x => x.Series)
            .HasForeignKey(x => x.ImportSeriesId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ImportItemConfiguration : IEntityTypeConfiguration<ImportItem>
{
    public void Configure(EntityTypeBuilder<ImportItem> builder)
    {
        builder.ToTable("ImportItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FolderName).IsRequired();
        builder.Property(x => x.FileName).IsRequired();
        builder.HasIndex(x => x.ImportSeriesId);
    }
}

public class SeriesReadingEntryConfiguration : IEntityTypeConfiguration<SeriesReadingEntry>
{
    public void Configure(EntityTypeBuilder<SeriesReadingEntry> builder)
    {
        builder.ToTable("SeriesReadingEntries");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.SeriesId }).IsUnique();

        builder.HasOne(x => x.Series).WithMany()
            .HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReadingProgressConfiguration : IEntityTypeConfiguration<ReadingProgress>
{
    public void Configure(EntityTypeBuilder<ReadingProgress> builder)
    {
        builder.ToTable("ReadingProgress");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.ChapterId }).IsUnique();

        builder.HasOne(x => x.Chapter).WithMany()
            .HasForeignKey(x => x.ChapterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
