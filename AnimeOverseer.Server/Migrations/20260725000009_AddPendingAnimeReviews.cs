using System;
using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000009_AddPendingAnimeReviews")]
public partial class AddPendingAnimeReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PendingAnimeReviews",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                SyncJobId = table.Column<int>(type: "INTEGER", nullable: true),
                SeasonId = table.Column<int>(type: "INTEGER", nullable: false),
                SourceName = table.Column<string>(type: "TEXT", nullable: false),
                SourceAniListId = table.Column<int>(type: "INTEGER", nullable: true),
                SourceMalId = table.Column<int>(type: "INTEGER", nullable: true),
                Reason = table.Column<string>(type: "TEXT", nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                CandidateAnimeIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PendingAnimeReviews", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PendingAnimeReviews_Status_CreatedAt",
            table: "PendingAnimeReviews",
            columns: new[] { "Status", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "PendingAnimeReviews");
}
