using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260822000000_ImproveCatalogueReadPerformance")]
public partial class ImproveCatalogueReadPerformance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(name: "IX_Animes_StartDate", table: "Animes", column: "StartDate");
        migrationBuilder.CreateIndex(name: "IX_Animes_AniListId", table: "Animes", column: "AniListId");
        migrationBuilder.CreateIndex(name: "IX_Animes_MALId", table: "Animes", column: "MALId");
        migrationBuilder.CreateIndex(name: "IX_Seasons_Year_Name", table: "Seasons", columns: new[] { "Year", "Name" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_CachedAnimeRelations_RootMalId", table: "CachedAnimeRelations", column: "RootMalId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Animes_StartDate", table: "Animes");
        migrationBuilder.DropIndex(name: "IX_Animes_AniListId", table: "Animes");
        migrationBuilder.DropIndex(name: "IX_Animes_MALId", table: "Animes");
        migrationBuilder.DropIndex(name: "IX_Seasons_Year_Name", table: "Seasons");
        migrationBuilder.DropIndex(name: "IX_CachedAnimeRelations_RootMalId", table: "CachedAnimeRelations");
    }
}
