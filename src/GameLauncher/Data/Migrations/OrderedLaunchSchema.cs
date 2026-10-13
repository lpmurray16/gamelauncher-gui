using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data.Migrations;

// Extend, rather than mutate, the frozen historical target models.
internal static class OrderedLaunchSchema
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        TrackingSchema.Build(modelBuilder);
        modelBuilder.Entity("GameLauncher.Domain.LibraryEntry", entity =>
        {
            entity.Property<int>("LaunchPosition").HasColumnType("INTEGER")
                .HasDefaultValue(int.MaxValue).HasSentinel(int.MaxValue);
            entity.Property<int>("LaunchDelaySeconds").HasColumnType("INTEGER").HasDefaultValue(0);
        });
        modelBuilder.Entity("GameLauncher.Domain.LaunchCompanion", entity =>
        {
            entity.Property<int>("Position").HasColumnType("INTEGER").HasDefaultValue(0);
            entity.Property<int>("DelaySeconds").HasColumnType("INTEGER").HasDefaultValue(0);
        });
    }
}
