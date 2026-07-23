using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangaFusion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesSiteUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteUrl",
                table: "Series",
                type: "TEXT",
                nullable: true);

            // Backfill existing MangaDex-linked series: the URL is deterministic from the id (the same
            // pattern the frontend used to hardcode before SiteUrl existed), so there's no need to wait
            // for the next metadata refresh/monitor scan to populate it. Other sources (ComicVine, web
            // sources) aren't backfilled — their real detail-page URLs aren't derivable from the id alone
            // and will fill in on the series' next metadata refresh instead.
            migrationBuilder.Sql(
                """
                UPDATE "Series"
                SET "SiteUrl" = 'https://mangadex.org/title/' || (
                    SELECT "SourceSeriesId" FROM "SeriesSourceLinks"
                    WHERE "SeriesSourceLinks"."SeriesId" = "Series"."Id" AND "SeriesSourceLinks"."SourceId" = 'mangadex'
                    LIMIT 1
                )
                WHERE "SiteUrl" IS NULL
                  AND EXISTS (
                    SELECT 1 FROM "SeriesSourceLinks"
                    WHERE "SeriesSourceLinks"."SeriesId" = "Series"."Id" AND "SeriesSourceLinks"."SourceId" = 'mangadex'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteUrl",
                table: "Series");
        }
    }
}
