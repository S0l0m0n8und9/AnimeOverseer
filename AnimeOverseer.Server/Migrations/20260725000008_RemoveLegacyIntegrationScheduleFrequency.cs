using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000008_RemoveLegacyIntegrationScheduleFrequency")]
public partial class RemoveLegacyIntegrationScheduleFrequency : Migration
{
    // FrequencyHours belonged to the first schedule design. SQLite cannot execute
    // DropColumn, so rebuild the small schedule table without that legacy column.
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "__IntegrationSchedules_new" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_IntegrationSchedules" PRIMARY KEY AUTOINCREMENT,
                "Source" TEXT NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "LastQueuedAt" TEXT NULL,
                "Name" TEXT NOT NULL,
                "StartAt" TEXT NULL,
                "RecurrenceType" TEXT NOT NULL,
                "Interval" INTEGER NOT NULL,
                "WeekdaysJson" TEXT NOT NULL,
                "MonthlyMode" TEXT NOT NULL,
                "DayOfMonth" INTEGER NOT NULL,
                "NthWeek" INTEGER NOT NULL,
                "Weekday" INTEGER NOT NULL,
                "EndType" TEXT NOT NULL,
                "EndAfterOccurrences" INTEGER NOT NULL,
                "EndBy" TEXT NULL,
                "QueuedOccurrences" INTEGER NOT NULL,
                "YearOffset" INTEGER NOT NULL,
                "YearCount" INTEGER NOT NULL,
                "SeasonsJson" TEXT NOT NULL,
                "WorkType" TEXT NOT NULL
            );
            INSERT INTO "__IntegrationSchedules_new" ("Id", "Source", "Enabled", "LastQueuedAt", "Name", "StartAt", "RecurrenceType", "Interval", "WeekdaysJson", "MonthlyMode", "DayOfMonth", "NthWeek", "Weekday", "EndType", "EndAfterOccurrences", "EndBy", "QueuedOccurrences", "YearOffset", "YearCount", "SeasonsJson", "WorkType")
            SELECT "Id", "Source", "Enabled", "LastQueuedAt", "Name", "StartAt", "RecurrenceType", "Interval", "WeekdaysJson", "MonthlyMode", "DayOfMonth", "NthWeek", "Weekday", "EndType", "EndAfterOccurrences", "EndBy", "QueuedOccurrences", "YearOffset", "YearCount", "SeasonsJson", "WorkType"
            FROM "IntegrationSchedules";
            DROP TABLE "IntegrationSchedules";
            ALTER TABLE "__IntegrationSchedules_new" RENAME TO "IntegrationSchedules";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<int>(name: "FrequencyHours", table: "IntegrationSchedules", type: "INTEGER", nullable: false, defaultValue: 24);
}
