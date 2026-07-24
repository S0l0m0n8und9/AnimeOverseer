using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000001_DeduplicateTagNames")]
public partial class DeduplicateTagNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        Deduplicate(migrationBuilder, "Genres", "AnimeGenres", "GenreId");
        Deduplicate(migrationBuilder, "Themes", "AnimeThemes", "ThemeId");
        Deduplicate(migrationBuilder, "Demographics", "AnimeDemographics", "DemographicId");

        migrationBuilder.CreateIndex(name: "IX_Genres_Name", table: "Genres", column: "Name", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Themes_Name", table: "Themes", column: "Name", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Demographics_Name", table: "Demographics", column: "Name", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Genres_Name", table: "Genres");
        migrationBuilder.DropIndex(name: "IX_Themes_Name", table: "Themes");
        migrationBuilder.DropIndex(name: "IX_Demographics_Name", table: "Demographics");
    }

    private static void Deduplicate(MigrationBuilder migrationBuilder, string tagTable, string joinTable, string tagId)
    {
        migrationBuilder.Sql($"""
            DELETE FROM {joinTable}
            WHERE rowid NOT IN (
                SELECT MIN(link.rowid)
                FROM {joinTable} AS link
                INNER JOIN {tagTable} AS tag ON tag.Id = link.{tagId}
                GROUP BY link.AnimeId, lower(trim(tag.Name))
            );

            UPDATE {joinTable}
            SET {tagId} = (
                SELECT MIN(canonical.Id)
                FROM {tagTable} AS canonical
                WHERE lower(trim(canonical.Name)) = lower(trim((SELECT Name FROM {tagTable} WHERE Id = {joinTable}.{tagId})))
            );

            DELETE FROM {tagTable}
            WHERE Id NOT IN (
                SELECT MIN(Id)
                FROM {tagTable}
                GROUP BY lower(trim(Name))
            );
            """);
    }
}
