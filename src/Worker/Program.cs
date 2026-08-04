using HilmaAgent.Infrastructure;
using HilmaAgent.Infrastructure.Persistence;
using HilmaAgent.Worker;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<IngestionWorkerOptions>(builder.Configuration.GetSection(IngestionWorkerOptions.SectionName));
builder.Services.AddHostedService<IngestionWorker>();

var host = builder.Build();

// The worker owns the schema here: it is the only writer, and it has to run before the API has
// anything to serve. Migrating at startup keeps `docker compose up` on a clean volume to one command.
await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HilmaDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
