using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260801000001_RestoreAnimeSeasonsFromStartDate")]
public partial class RestoreAnimeSeasonsFromStartDate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Recommendation and relation refreshes previously assigned an existing
        // anime to the source title's season. Restore only rows for which the
        // catalogue already has a matching season based on the title's start date.
        migrationBuilder.Sql("""
            UPDATE Animes
            SET SeasonId = (
                SELECT Id FROM Seasons
                WHERE Year = CAST(strftime('%Y', Animes.StartDate) AS INTEGER)
                  AND Name = CASE
                      WHEN CAST(strftime('%m', Animes.StartDate) AS INTEGER) <= 3 THEN 'winter'
                      WHEN CAST(strftime('%m', Animes.StartDate) AS INTEGER) <= 6 THEN 'spring'
                      WHEN CAST(strftime('%m', Animes.StartDate) AS INTEGER) <= 9 THEN 'summer'
                      ELSE 'fall'
                  END
            )
            WHERE StartDate IS NOT NULL
              AND EXISTS (
                  SELECT 1 FROM Seasons
                  WHERE Year = CAST(strftime('%Y', Animes.StartDate) AS INTEGER)
                    AND Name = CASE
                        WHEN CAST(strftime('%m', Animes.StartDate) AS INTEGER) <= 3 THEN 'winter'
                        WHEN CAST(strftime('%m', Animes.StartDate) AS INTEGER) <= 6 THEN 'spring'
                        WHEN CAST(strftime('%m', Animes.StartDate) AS INTEGER) <= 9 THEN 'summer'
                        ELSE 'fall'
                    END
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
