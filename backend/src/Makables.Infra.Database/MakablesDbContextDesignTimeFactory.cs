using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Makables.Infra.Database;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations add</c> can build the
/// model without a running host. The connection string is a placeholder —
/// <c>migrations add</c> never connects to the database (it only needs the
/// model graph); <c>database update</c> uses the real runtime
/// configuration. Per the EF Core design-time-factory pattern.
/// </summary>
public sealed class MakablesDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<MakablesDbContext>
{
    public MakablesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=makables_design;Username=makables;Password=makables";

        var options = new DbContextOptionsBuilder<MakablesDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MakablesDbContext(options);
    }
}
