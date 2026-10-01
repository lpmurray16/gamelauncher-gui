using GameLauncher.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Data;

public sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    public DbSet<LibraryEntry> Entries => Set<LibraryEntry>();

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
        entry.HasIndex(x => x.TargetKey).IsUnique();
    }
}
