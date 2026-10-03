using System;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data.Migrations;

internal static class CollectionSchema
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        BundledLaunchSchema.Build(modelBuilder);
        modelBuilder.Entity("GameLauncher.Domain.LibraryCollection", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            entity.Property<string>("Name").IsRequired().HasMaxLength(60).HasColumnType("TEXT");
            entity.Property<string>("NameKey").IsRequired().HasMaxLength(60).HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("NameKey").IsUnique();
            entity.ToTable("Collections");
        });
        modelBuilder.Entity("GameLauncher.Domain.CollectionEntry", entity =>
        {
            entity.Property<Guid>("CollectionId").HasColumnType("TEXT");
            entity.Property<Guid>("EntryId").HasColumnType("TEXT");
            entity.HasKey("CollectionId", "EntryId");
            entity.HasIndex("EntryId");
            entity.ToTable("CollectionEntries");
            entity.HasOne("GameLauncher.Domain.LibraryCollection", null).WithMany()
                .HasForeignKey("CollectionId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne("GameLauncher.Domain.LibraryEntry", null).WithMany()
                .HasForeignKey("EntryId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
    }
}
