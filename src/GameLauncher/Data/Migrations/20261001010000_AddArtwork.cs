using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
[Migration("20261001010000_AddArtwork")]
public sealed class AddArtwork : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("CoverImageFile", "Entries", "TEXT", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>("HeroImageFile", "Entries", "TEXT", maxLength: 512, nullable: true);
        migrationBuilder.AddColumn<string>("ProviderName", "Entries", "TEXT", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>("ProviderTitle", "Entries", "TEXT", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<int>("ProviderGameId", "Entries", "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("CoverImageFile", "Entries");
        migrationBuilder.DropColumn("HeroImageFile", "Entries");
        migrationBuilder.DropColumn("ProviderName", "Entries");
        migrationBuilder.DropColumn("ProviderTitle", "Entries");
        migrationBuilder.DropColumn("ProviderGameId", "Entries");
    }

    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        => CurrentSchema.Build(modelBuilder);
}
