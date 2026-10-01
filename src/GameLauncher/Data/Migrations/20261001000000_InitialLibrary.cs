using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
[Migration("20261001000000_InitialLibrary")]
public sealed class InitialLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Entries", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
            TargetPath = table.Column<string>(type: "TEXT", maxLength: 32767, nullable: false),
            TargetKey = table.Column<string>(type: "TEXT", maxLength: 32767, nullable: false),
            Arguments = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
            WorkingDirectory = table.Column<string>(type: "TEXT", maxLength: 32767, nullable: false),
            Category = table.Column<int>(type: "INTEGER", nullable: false),
            IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
            AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            LastLaunchedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
        }, constraints: table => table.PrimaryKey("PK_Entries", x => x.Id));
        migrationBuilder.CreateIndex("IX_Entries_TargetKey", "Entries", "TargetKey", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Entries");

    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
        => InitialSchema.Build(modelBuilder);
}
