using System;
using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000010_AddAnimeRecommendations")]
public partial class AddAnimeRecommendations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AnimeRecommendations",
            columns: table => new
            {
                SourceAnimeId = table.Column<int>(type: "INTEGER", nullable: false),
                RecommendedAnimeId = table.Column<int>(type: "INTEGER", nullable: false),
                Rating = table.Column<int>(type: "INTEGER", nullable: false),
                CachedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnimeRecommendations", x => new { x.SourceAnimeId, x.RecommendedAnimeId });
                table.ForeignKey("FK_AnimeRecommendations_Animes_RecommendedAnimeId", x => x.RecommendedAnimeId, "Animes", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_AnimeRecommendations_Animes_SourceAnimeId", x => x.SourceAnimeId, "Animes", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AnimeRecommendations_RecommendedAnimeId",
            table: "AnimeRecommendations",
            column: "RecommendedAnimeId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "AnimeRecommendations");
}
