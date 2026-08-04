using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HilmaAgent.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works without booting the API or Worker.
/// Only the provider matters here — no migration is ever executed against this connection string.
/// </summary>
public class HilmaDbContextFactory : IDesignTimeDbContextFactory<HilmaDbContext>
{
    public HilmaDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=hilma;Username=hilma;Password=hilma";

        var options = new DbContextOptionsBuilder<HilmaDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HilmaDbContext(options);
    }
}
