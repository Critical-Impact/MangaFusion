using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangaFusion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeriesSourceLinkPerKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeriesSourceLinks_SourceId_SourceSeriesId",
                table: "SeriesSourceLinks");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "SeriesSourceLinks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill Kind from each link's owning series before the per-kind unique index is built —
            // otherwise every existing comic/light-novel link would default to Manga (0) and could also
            // collide under the new index. Kind mirrors Series.Kind, which is immutable once set.
            migrationBuilder.Sql(
                "UPDATE SeriesSourceLinks " +
                "SET Kind = (SELECT s.Kind FROM Series s WHERE s.Id = SeriesSourceLinks.SeriesId);");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesSourceLinks_SourceId_SourceSeriesId_Kind",
                table: "SeriesSourceLinks",
                columns: new[] { "SourceId", "SourceSeriesId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeriesSourceLinks_SourceId_SourceSeriesId_Kind",
                table: "SeriesSourceLinks");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SeriesSourceLinks");

            migrationBuilder.CreateIndex(
                name: "IX_SeriesSourceLinks_SourceId_SourceSeriesId",
                table: "SeriesSourceLinks",
                columns: new[] { "SourceId", "SourceSeriesId" },
                unique: true);
        }
    }
}
