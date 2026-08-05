using HilmaAgent.Core.Profiles;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Api.Notices;

/// <summary>
/// Everything the scorer compares against, editable. Nothing here is read from configuration at
/// runtime — the seed writes starting values once, and from then on this is the only way to change
/// them, so the UI and the API see the same profile.
/// </summary>
public record ProfileRequest(
    string Name,
    string? Description,
    List<string>? Technologies,
    List<string>? ReferenceProjects,
    List<string>? PreferredCpvCodes,
    List<string>? Regions,
    decimal? MinContractValue,
    decimal? MaxContractValue);

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profiles").WithTags("Profiles");

        // Response types are declared so they reach the OpenAPI document, and from there the
        // generated frontend types. Without them the document describes request bodies only, and
        // `npm run gen-types` silently produces a schema with no DTOs in it.
        group.MapGet("/", async (HilmaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.CompanyProfiles.AsNoTracking().OrderBy(p => p.CreatedAt).ToListAsync(ct)))
        .Produces<List<CompanyProfile>>()
        .WithName("ListProfiles")
        .WithSummary("Company profiles that notices are screened against.");

        group.MapGet("/{id:guid}", async (Guid id, HilmaDbContext db, CancellationToken ct) =>
        {
            var profile = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            return profile is null ? Results.NotFound() : Results.Ok(profile);
        })
        .Produces<CompanyProfile>()
        .WithName("GetProfile");

        group.MapPost("/", async (ProfileRequest request, HilmaDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { } error) return Results.BadRequest(new { error });

            var profile = new CompanyProfile { Name = request.Name, Id = Guid.NewGuid() };
            Apply(profile, request);
            profile.CreatedAt = profile.UpdatedAt = DateTimeOffset.UtcNow;

            db.CompanyProfiles.Add(profile);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/profiles/{profile.Id}", profile);
        })
        .Produces<CompanyProfile>(StatusCodes.Status201Created)
        .WithName("CreateProfile")
        .WithSummary("Adds a profile. Several can coexist; an assessment names the one it used.");

        group.MapPut("/{id:guid}", async (Guid id, ProfileRequest request, HilmaDbContext db, CancellationToken ct) =>
        {
            if (Validate(request) is { } error) return Results.BadRequest(new { error });

            var profile = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (profile is null) return Results.NotFound();

            Apply(profile, request);
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            // Past assessments are deliberately left alone. They record what was true when they were
            // made; silently re-scoring history to match an edited profile would destroy the audit trail.
            return Results.Ok(profile);
        })
        .Produces<CompanyProfile>()
        .WithName("UpdateProfile")
        .WithSummary("Edits a profile. Existing assessments are not re-scored — they record what was true then.");

        return app;
    }

    private static string? Validate(ProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Name is required.";

        if (request.MinContractValue is { } min && request.MaxContractValue is { } max && min > max)
            return "MinContractValue cannot exceed MaxContractValue.";

        // CPV codes are digits; a typo here silently costs every future score, so it is worth rejecting.
        var badCpv = request.PreferredCpvCodes?.FirstOrDefault(code => !code.All(char.IsDigit) || code.Length < 2);
        if (badCpv is not null) return $"'{badCpv}' is not a CPV code — expected at least two digits.";

        return null;
    }

    private static void Apply(CompanyProfile profile, ProfileRequest request)
    {
        profile.Name = request.Name.Trim();
        profile.Description = request.Description?.Trim();
        profile.Technologies = Clean(request.Technologies);
        profile.ReferenceProjects = Clean(request.ReferenceProjects);
        profile.PreferredCpvCodes = Clean(request.PreferredCpvCodes);
        profile.Regions = Clean(request.Regions).Select(region => region.ToUpperInvariant()).ToList();
        profile.MinContractValue = request.MinContractValue;
        profile.MaxContractValue = request.MaxContractValue;
    }

    private static List<string> Clean(List<string>? values) => values?
        .Select(value => value.Trim())
        .Where(value => value.Length > 0)
        .Distinct()
        .ToList() ?? [];
}
