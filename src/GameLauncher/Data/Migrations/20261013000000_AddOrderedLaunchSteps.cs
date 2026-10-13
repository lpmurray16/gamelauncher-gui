using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
[Migration("20261013000000_AddOrderedLaunchSteps")]
public sealed class AddOrderedLaunchSteps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Equal companion positions retain the service's ordinal title/ID ordering.
        migrationBuilder.AddColumn<int>(name: "LaunchPosition", table: "Entries",
            type: "INTEGER", nullable: false, defaultValue: int.MaxValue);
        migrationBuilder.AddColumn<int>(name: "LaunchDelaySeconds", table: "Entries",
            type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "Position", table: "LaunchCompanions",
            type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "DelaySeconds", table: "LaunchCompanions",
            type: "INTEGER", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "LaunchPosition", table: "Entries");
        migrationBuilder.DropColumn(name: "LaunchDelaySeconds", table: "Entries");
        migrationBuilder.DropColumn(name: "Position", table: "LaunchCompanions");
        migrationBuilder.DropColumn(name: "DelaySeconds", table: "LaunchCompanions");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => OrderedLaunchSchema.Build(modelBuilder);
}
