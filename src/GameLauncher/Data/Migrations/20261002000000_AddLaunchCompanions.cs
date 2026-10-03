using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
[Migration("20261002000000_AddLaunchCompanions")]
public sealed class AddLaunchCompanions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LaunchCompanions",
            columns: table => new
            {
                EntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                CompanionId = table.Column<Guid>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LaunchCompanions", x => new { x.EntryId, x.CompanionId });
                table.ForeignKey("FK_LaunchCompanions_Entries_EntryId", x => x.EntryId,
                    "Entries", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_LaunchCompanions_Entries_CompanionId", x => x.CompanionId,
                    "Entries", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_LaunchCompanions_CompanionId", "LaunchCompanions", "CompanionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("LaunchCompanions");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => BundledLaunchSchema.Build(modelBuilder);
}
