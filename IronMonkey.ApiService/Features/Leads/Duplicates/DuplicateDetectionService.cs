using System.Text.RegularExpressions;
using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Data;

namespace IronMonkey.ApiService.Features.Leads.Duplicates;

public class DuplicateDetectionService : IDuplicateDetectionService
{
    private readonly ITenantService _tenantService;
    private readonly ITenantDbContextFactory _dbContextFactory;

    public DuplicateDetectionService(ITenantService tenantService, ITenantDbContextFactory dbContextFactory)
    {
        _tenantService = tenantService;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<List<DuplicateCandidate>> FindCandidatesAsync(
        Guid tenantId, string? email, string? phone, string? name, CancellationToken ct = default)
    {
        var connectionString = await _tenantService.GetConnectionStringAsync(ct);
        await using var db = _dbContextFactory.CreateForTenant(connectionString, tenantId);

        var candidates = new List<DuplicateCandidate>();

        // Exact email match — confidence 100
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLower();
            var emailMatches = await db.Leads
                .Where(l => l.Email.ToLower() == normalizedEmail)
                .ToListAsync(ct);

            foreach (var lead in emailMatches)
            {
                candidates.Add(new DuplicateCandidate(
                    lead.Id,
                    $"{lead.FirstName} {lead.LastName}",
                    lead.Email,
                    "Email match",
                    100));
            }
        }

        // Exact phone match — confidence 95 (normalize both sides by stripping non-digits)
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var normalizedPhone = Regex.Replace(phone, @"\D", "");
            var allLeads = await db.Leads.ToListAsync(ct);

            foreach (var lead in allLeads)
            {
                if (string.IsNullOrWhiteSpace(lead.Mobile)) continue;
                var leadPhone = Regex.Replace(lead.Mobile, @"\D", "");
                if (leadPhone == normalizedPhone)
                {
                    // Avoid duplicates if already added via email
                    if (!candidates.Any(c => c.LeadId == lead.Id))
                    {
                        candidates.Add(new DuplicateCandidate(
                            lead.Id,
                            $"{lead.FirstName} {lead.LastName}",
                            lead.Email,
                            "Phone match",
                            95));
                    }
                }
            }
        }

        // Fuzzy name match — only when no other candidates found, confidence = score
        if (candidates.Count == 0 && !string.IsNullOrWhiteSpace(name))
        {
            // Use IgnoreQueryFilters but apply explicit tenant + deleted filter for safety
            var allLeads = await db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && !l.IsDeleted)
                .ToListAsync(ct);

            foreach (var lead in allLeads)
            {
                var fullName = $"{lead.FirstName} {lead.LastName}";
                var score = Fuzz.TokenSetRatio(name, fullName);
                if (score >= 75)
                {
                    candidates.Add(new DuplicateCandidate(
                        lead.Id,
                        fullName,
                        lead.Email,
                        "Name fuzzy match",
                        score));
                }
            }
        }

        return candidates.OrderByDescending(c => c.ConfidenceScore).ToList();
    }
}
