using HilmaAgent.Api.Notices;
using HilmaAgent.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Spec at /openapi/v1.json. Phases 1-3 are exercised through HilmaAgent.Api.http rather than
    // a UI — deliberately API-first, no frontend until Phase 4.
    app.MapOpenApi();
}

app.MapNoticeEndpoints();
app.MapSearchEndpoints();

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory.
public partial class Program;
