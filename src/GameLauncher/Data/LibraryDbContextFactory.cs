using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameLauncher.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works without running the desktop shell.
/// Points at a scratch database; migrations themselves are database-agnostic.
/// </summary>
public sealed class LibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    public LibraryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = "scratch-design-time.db" }.ToString())
            .Options;
        return new LibraryDbContext(options);
    }
}
