using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
[Migration("20261004000000_AddTrackingExecutablePath")]
public sealed class AddTrackingExecutablePath : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "TrackingExecutablePath", table: "Entries",
            type: "TEXT", maxLength: 32767, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "TrackingExecutablePath", table: "Entries");

    protected override void BuildTargetModel(ModelBuilder modelBuilder) => TrackingSchema.Build(modelBuilder);
}
