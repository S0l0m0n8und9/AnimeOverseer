using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260725000004_AddAnimeTitleAliases")]
public partial class AddAnimeTitleAliases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "HasEnglishTitle",
            table: "Animes",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "AnimeTitleAliases",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                AnimeId = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AnimeTitleAliases", x => x.Id);
                table.ForeignKey(
                    name: "FK_AnimeTitleAliases_Animes_AnimeId",
                    column: x => x.AnimeId,
                    principalTable: "Animes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AnimeTitleAliases_AnimeId_Title",
            table: "AnimeTitleAliases",
            columns: new[] { "AnimeId", "Title" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AnimeTitleAliases");
        migrationBuilder.DropColumn(name: "HasEnglishTitle", table: "Animes");
    }
}
