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
    // a UI â€” deliberately API-first, no frontend until Phase 4.
    app.MapOpenApi();
}

// The built SPA, when one was baked into the image. In local development there is no wwwroot —
// Vite serves the app on :5173 and proxies /api here — so this is skipped rather than serving a
// stale build over the dev server's fresh one. Registered before the API routes because static
// middleware has to sit in the pipeline ahead of endpoint execution.
var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
var servesSpa = File.Exists(spaIndex);

if (servesSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.Logger.LogInformation(
    servesSpa ? "Serving the bundled SPA from wwwroot." : "No wwwroot/index.html; API only (run the Vite dev server for the UI).");

app.MapNoticeEndpoints();
app.MapSearchEndpoints();
app.MapAssessmentEndpoints();
app.MapProfileEndpoints();
app.MapApprovalEndpoints();
app.MapProviderEndpoints();

// Client-side routing: anything not matched by an API route is the SPA's own concern, not a 404.
if (servesSpa) app.MapFallbackToFile("index.html");

app.Run();

// Exposed so integration tests can host the API with WebApplicationFactory.
public partial class Program;

