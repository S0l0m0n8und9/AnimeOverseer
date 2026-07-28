using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260728000000_AddIntegrationScheduleProcessOneSeasonAtATime")]
public partial class AddIntegrationScheduleProcessOneSeasonAtATime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ProcessOneSeasonAtATime",
            table: "IntegrationSchedules",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        // Preserve existing migration schedules while making the behavior an option.
        migrationBuilder.Sql("UPDATE IntegrationSchedules SET ProcessOneSeasonAtATime = 1, WorkType = 'CatalogueSync' WHERE WorkType = 'InitialMigration'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProcessOneSeasonAtATime", table: "IntegrationSchedules");
    }
}
