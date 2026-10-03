using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameLauncher.Data.Migrations;

[DbContext(typeof(LibraryDbContext))]
public sealed class LibraryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) => CollectionSchema.Build(modelBuilder);
}
