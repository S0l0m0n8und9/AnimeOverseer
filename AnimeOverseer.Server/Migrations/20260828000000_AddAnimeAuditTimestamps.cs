using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260828000000_AddAnimeAuditTimestamps")]
public partial class AddAnimeAuditTimestamps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQLite only permits a constant default when adding a column. Capture
        // the migration time once so every existing record has a consistent
        // audit baseline; AnimeDbContext maintains values after that.
        var migrationTime = DateTime.UtcNow;
        migrationBuilder.AddColumn<DateTime>(
            name: "CreatedAt",
            table: "Animes",
            type: "TEXT",
            nullable: false,
            defaultValue: migrationTime);

        migrationBuilder.AddColumn<DateTime>(
            name: "ModifiedAt",
            table: "Animes",
            type: "TEXT",
            nullable: false,
            defaultValue: migrationTime);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CreatedAt", table: "Animes");
        migrationBuilder.DropColumn(name: "ModifiedAt", table: "Animes");
    }
}
