using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RegReturns.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> create the context to add migrations. The connection string is a placeholder:
/// migrations are generated from the model and never connect to a database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RegReturnsDbContext>
{
    public RegReturnsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RegReturnsDbContext>()
            .UseSqlServer("Server=design-time-only;Database=RegReturns;Integrated Security=true")
            .Options;
        return new RegReturnsDbContext(options);
    }
}
