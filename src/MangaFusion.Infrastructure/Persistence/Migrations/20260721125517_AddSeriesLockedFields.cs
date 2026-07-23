using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangaFusion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesLockedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LockedFields",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockedFields",
                table: "Series");
        }
    }
}
