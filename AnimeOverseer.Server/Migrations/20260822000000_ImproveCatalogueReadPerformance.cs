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
        // A failed SQLite migration can leave an earlier DDL statement in place
        // before the migration-history row is committed. These guards allow a
        // safe retry without requiring the operator to edit the database.
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Animes_StartDate\" ON \"Animes\" (\"StartDate\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Animes_AniListId\" ON \"Animes\" (\"AniListId\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Animes_MALId\" ON \"Animes\" (\"MALId\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Seasons_Year_Name\" ON \"Seasons\" (\"Year\", \"Name\");");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_CachedAnimeRelations_RootMalId\" ON \"CachedAnimeRelations\" (\"RootMalId\");");
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
