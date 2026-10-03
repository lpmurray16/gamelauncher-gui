using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
[Migration("20261002010000_AddCollections")]
public sealed class AddCollections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "Collections", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
            NameKey = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Collections", x => x.Id));
        migrationBuilder.CreateIndex("IX_Collections_NameKey", "Collections", "NameKey", unique: true);
        migrationBuilder.CreateTable(name: "CollectionEntries", columns: table => new
        {
            CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
            EntryId = table.Column<Guid>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CollectionEntries", x => new { x.CollectionId, x.EntryId });
            table.ForeignKey("FK_CollectionEntries_Collections_CollectionId", x => x.CollectionId,
                "Collections", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_CollectionEntries_Entries_EntryId", x => x.EntryId,
                "Entries", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_CollectionEntries_EntryId", "CollectionEntries", "EntryId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("CollectionEntries");
        migrationBuilder.DropTable("Collections");
    }
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => CollectionSchema.Build(modelBuilder);
}
