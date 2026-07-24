using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000000_AddSyncJobSyncedAnimeIds")]
public partial class AddSyncJobSyncedAnimeIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SyncedAnimeIds",
            table: "SyncJobs",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SyncedAnimeIds",
            table: "SyncJobs");
    }
}
