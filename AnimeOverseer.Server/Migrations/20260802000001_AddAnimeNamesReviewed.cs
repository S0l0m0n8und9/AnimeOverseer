using AnimeOverseer.Server.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnimeOverseer.Server.Migrations;

[DbContext(typeof(AnimeDbContext))]
[Migration("20260802000001_AddAnimeNamesReviewed")]
public partial class AddAnimeNamesReviewed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>(
            name: "NamesReviewed",
            table: "Animes",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "NamesReviewed", table: "Animes");
}
