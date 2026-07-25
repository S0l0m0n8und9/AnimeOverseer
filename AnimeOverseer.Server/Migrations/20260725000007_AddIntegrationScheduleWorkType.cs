using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000007_AddIntegrationScheduleWorkType")]
public partial class AddIntegrationScheduleWorkType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "WorkType", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "CatalogueSync");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "WorkType", table: "IntegrationSchedules");
}
