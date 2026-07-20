using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangaFusion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMigrationCommitProgressAndConflictKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommitItemsDone",
                table: "MigrationSeries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommitItemsTotal",
                table: "MigrationSeries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConflictKind",
                table: "MigrationSeries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CommitSeriesDone",
                table: "MigrationBatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommitSeriesTotal",
                table: "MigrationBatches",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommitItemsDone",
                table: "MigrationSeries");

            migrationBuilder.DropColumn(
                name: "CommitItemsTotal",
                table: "MigrationSeries");

            migrationBuilder.DropColumn(
                name: "ConflictKind",
                table: "MigrationSeries");

            migrationBuilder.DropColumn(
                name: "CommitSeriesDone",
                table: "MigrationBatches");

            migrationBuilder.DropColumn(
                name: "CommitSeriesTotal",
                table: "MigrationBatches");
        }
    }
}
