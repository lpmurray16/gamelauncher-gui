using System;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data.Migrations;

internal static class BundledLaunchSchema
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        // Preserve the artwork migration's historical target model.
        CurrentSchema.Build(modelBuilder);
        modelBuilder.Entity("GameLauncher.Domain.LaunchCompanion", entity =>
        {
            entity.Property<Guid>("EntryId").HasColumnType("TEXT");
            entity.Property<Guid>("CompanionId").HasColumnType("TEXT");
            entity.HasKey("EntryId", "CompanionId");
            entity.HasIndex("CompanionId");
            entity.ToTable("LaunchCompanions");
            entity.HasOne("GameLauncher.Domain.LibraryEntry", null).WithMany()
                .HasForeignKey("EntryId").OnDelete(DeleteBehavior.Cascade).IsRequired();
            entity.HasOne("GameLauncher.Domain.LibraryEntry", null).WithMany()
                .HasForeignKey("CompanionId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });
    }
}
