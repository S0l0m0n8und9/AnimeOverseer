using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000002_AddAnimeImages")]
public partial class AddAnimeImages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AnimeImages",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                AnimeId = table.Column<int>(type: "INTEGER", nullable: false),
                Source = table.Column<string>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", nullable: false),
                ImageUrl = table.Column<string>(type: "TEXT", nullable: false),
                LocalImagePath = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnimeImages", x => x.Id);
                table.ForeignKey("FK_AnimeImages_Animes_AnimeId", x => x.AnimeId, "Animes", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_AnimeImages_AnimeId_Source_Type_ImageUrl", table: "AnimeImages", columns: new[] { "AnimeId", "Source", "Type", "ImageUrl" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "AnimeImages");
}
