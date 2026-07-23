using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MangaFusion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesCoverUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CoverUpdatedAt",
                table: "Series",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverUpdatedAt",
                table: "Series");
        }
    }
}
