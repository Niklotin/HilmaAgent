namespace HilmaAgent.Core.Profiles;

/// <summary>
/// The company being screened for. Everything the deterministic scorer compares a notice against.
/// </summary>
public class CompanyProfile
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Free text, embedded for retrieval so the agent can search notices by capability.</summary>
    public string? Description { get; set; }

    public List<string> Technologies { get; set; } = [];

    /// <summary>Past work, in prose. The strongest evidence of capability a bid can point at.</summary>
    public List<string> ReferenceProjects { get; set; } = [];

    /// <summary>CPV codes the company can credibly bid on. Matched hierarchically, not by equality.</summary>
    public List<string> PreferredCpvCodes { get; set; } = [];

    /// <summary>NUTS codes the company will work in. Matched by prefix, so "FI1" covers all of Southern Finland.</summary>
    public List<string> Regions { get; set; } = [];

    /// <summary>Below this, a contract is not worth the bid effort.</summary>
    public decimal? MinContractValue { get; set; }

    /// <summary>Above this, the company lacks the capacity to deliver.</summary>
    public decimal? MaxContractValue { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
