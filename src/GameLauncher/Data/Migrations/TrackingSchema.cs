using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data.Migrations;

// Extend the frozen collection schema; historical migration targets stay unchanged.
internal static class TrackingSchema
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        CollectionSchema.Build(modelBuilder);
        modelBuilder.Entity("GameLauncher.Domain.LibraryEntry", entity =>
            entity.Property<string?>("TrackingExecutablePath").HasMaxLength(32767).HasColumnType("TEXT"));
    }
}
