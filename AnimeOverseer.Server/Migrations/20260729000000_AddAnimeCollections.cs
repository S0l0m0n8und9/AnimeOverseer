using System;
using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260729000000_AddAnimeCollections")]
public partial class AddAnimeCollections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AnimeCollections",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                FilterJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AnimeCollections", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AnimeCollectionItems",
            columns: table => new
            {
                CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                AnimeId = table.Column<int>(type: "INTEGER", nullable: false),
                AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnimeCollectionItems", x => new { x.CollectionId, x.AnimeId });
                table.ForeignKey("FK_AnimeCollectionItems_AnimeCollections_CollectionId", x => x.CollectionId, "AnimeCollections", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_AnimeCollectionItems_Animes_AnimeId", x => x.AnimeId, "Animes", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_AnimeCollectionItems_AnimeId", table: "AnimeCollectionItems", column: "AnimeId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AnimeCollectionItems");
        migrationBuilder.DropTable(name: "AnimeCollections");
    }
}
