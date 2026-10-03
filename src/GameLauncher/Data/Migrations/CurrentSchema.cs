// Historical artwork schema used by AddArtwork and as the base for BundledLaunchSchema.
// Keep this target frozen; extend the latest schema and snapshot with new migrations.
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data.Migrations;

internal static class CurrentSchema
{
    internal static void Build(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        modelBuilder.Entity("GameLauncher.Domain.LibraryEntry", entity =>
        {
            entity.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("TEXT");
            entity.Property<string>("Title").IsRequired().HasMaxLength(200).HasColumnType("TEXT");
            entity.Property<string>("TargetPath").IsRequired().HasMaxLength(32767).HasColumnType("TEXT");
            entity.Property<string>("TargetKey").IsRequired().HasMaxLength(32767).HasColumnType("TEXT");
            entity.Property<string>("Arguments").IsRequired().HasMaxLength(8192).HasColumnType("TEXT");
            entity.Property<string>("WorkingDirectory").IsRequired().HasMaxLength(32767).HasColumnType("TEXT");
            entity.Property<int>("Category").HasColumnType("INTEGER");
            entity.Property<bool>("IsFavorite").HasColumnType("INTEGER");
            entity.Property<string?>("CoverImageFile").HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string?>("HeroImageFile").HasMaxLength(512).HasColumnType("TEXT");
            entity.Property<string?>("ProviderName").HasMaxLength(50).HasColumnType("TEXT");
            entity.Property<string?>("ProviderTitle").HasMaxLength(200).HasColumnType("TEXT");
            entity.Property<int?>("ProviderGameId").HasColumnType("INTEGER");
            entity.Property<DateTime>("AddedUtc").HasColumnType("TEXT");
            entity.Property<DateTime?>("LastLaunchedUtc").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("TargetKey").IsUnique();
            entity.ToTable("Entries");
        });
    }
}
