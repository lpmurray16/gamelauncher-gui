using GameLauncher.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<LibraryEntry> Entries => Set<LibraryEntry>();
    public DbSet<LaunchCompanion> LaunchCompanions => Set<LaunchCompanion>();
    public DbSet<LibraryCollection> Collections => Set<LibraryCollection>();
    public DbSet<CollectionEntry> CollectionEntries => Set<CollectionEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entry = modelBuilder.Entity<LibraryEntry>();
        entry.ToTable("Entries");
        entry.HasKey(x => x.Id);
        entry.Property(x => x.Title).HasMaxLength(200).IsRequired();
        entry.Property(x => x.TargetPath).HasMaxLength(32767).IsRequired();
        entry.Property(x => x.TargetKey).HasMaxLength(32767).IsRequired();
        entry.Property(x => x.Arguments).HasMaxLength(8192).IsRequired();
        entry.Property(x => x.WorkingDirectory).HasMaxLength(32767).IsRequired();
        entry.Property(x => x.CoverImageFile).HasMaxLength(512);
        entry.Property(x => x.HeroImageFile).HasMaxLength(512);
        entry.Property(x => x.ProviderName).HasMaxLength(50);
        entry.Property(x => x.ProviderTitle).HasMaxLength(200);
        entry.HasIndex(x => x.TargetKey).IsUnique();

        var collection = modelBuilder.Entity<LibraryCollection>();
        collection.ToTable("Collections");
        collection.HasKey(x => x.Id);
        collection.Property(x => x.Name).HasMaxLength(60).IsRequired();
        collection.Property(x => x.NameKey).HasMaxLength(60).IsRequired();
        collection.HasIndex(x => x.NameKey).IsUnique();
        var membership = modelBuilder.Entity<CollectionEntry>();
        membership.ToTable("CollectionEntries");
        membership.HasKey(x => new { x.CollectionId, x.EntryId });
        membership.HasIndex(x => x.EntryId);
        membership.HasOne<LibraryCollection>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
        membership.HasOne<LibraryEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);

        var companion = modelBuilder.Entity<LaunchCompanion>();
        companion.ToTable("LaunchCompanions");
        companion.HasKey(x => new { x.EntryId, x.CompanionId });
        companion.HasIndex(x => x.CompanionId);
        companion.HasOne<LibraryEntry>().WithMany().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);
        companion.HasOne<LibraryEntry>().WithMany().HasForeignKey(x => x.CompanionId).OnDelete(DeleteBehavior.Cascade);
    }
}
