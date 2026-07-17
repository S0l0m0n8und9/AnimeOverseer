using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260713000000_AddSyncJobCancellation")]
public partial class AddSyncJobCancellation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CancellationRequested",
            table: "SyncJobs",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CancellationRequested",
            table: "SyncJobs");
    }
}
