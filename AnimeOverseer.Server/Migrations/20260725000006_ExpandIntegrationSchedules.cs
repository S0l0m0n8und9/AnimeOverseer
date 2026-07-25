using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000006_ExpandIntegrationSchedules")]
public partial class ExpandIntegrationSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_IntegrationSchedules_Source", table: "IntegrationSchedules");
        migrationBuilder.AddColumn<string>(name: "Name", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "Catalogue schedule");
        // SQLite only permits constant defaults when adding a column. Populate the
        // existing rows explicitly instead of using CURRENT_TIMESTAMP as a default.
        migrationBuilder.AddColumn<DateTime>(name: "StartAt", table: "IntegrationSchedules", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "RecurrenceType", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "Daily");
        migrationBuilder.AddColumn<int>(name: "Interval", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>(name: "WeekdaysJson", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "[]");
        migrationBuilder.AddColumn<string>(name: "MonthlyMode", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "DayOfMonth");
        migrationBuilder.AddColumn<int>(name: "DayOfMonth", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "NthWeek", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "Weekday", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "EndType", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "Never");
        migrationBuilder.AddColumn<int>(name: "EndAfterOccurrences", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 10);
        migrationBuilder.AddColumn<DateTime>(name: "EndBy", table: "IntegrationSchedules", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "QueuedOccurrences", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "YearOffset", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "YearCount", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>(name: "SeasonsJson", table: "IntegrationSchedules", type: "TEXT", nullable: false, defaultValue: "[\"winter\",\"spring\",\"summer\",\"fall\"]");
        migrationBuilder.Sql("UPDATE IntegrationSchedules SET StartAt = CURRENT_TIMESTAMP WHERE StartAt IS NULL");
        migrationBuilder.Sql("UPDATE IntegrationSchedules SET Name = 'Nightly AniList catalogue', YearCount = 4 WHERE Source = 'AniList'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Name", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "StartAt", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "RecurrenceType", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "Interval", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "WeekdaysJson", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "MonthlyMode", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "DayOfMonth", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "NthWeek", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "Weekday", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "EndType", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "EndAfterOccurrences", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "EndBy", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "QueuedOccurrences", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "YearOffset", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "YearCount", table: "IntegrationSchedules");
        migrationBuilder.DropColumn(name: "SeasonsJson", table: "IntegrationSchedules");
        migrationBuilder.CreateIndex(name: "IX_IntegrationSchedules_Source", table: "IntegrationSchedules", column: "Source", unique: true);
    }
}
