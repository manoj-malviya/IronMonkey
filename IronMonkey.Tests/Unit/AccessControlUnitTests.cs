using IronMonkey.ApiService.Common.Auth;
using IronMonkey.Common.Auth;
using IronMonkey.Data.Visibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMonkey.Tests.Unit;

public class AccessControlUnitTests
{
    // ── Team hierarchy ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_team_cannot_be_its_own_parent_or_close_a_cycle()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [a] = null, [b] = a, [c] = b };

        Assert.Contains("own parent", TeamHierarchy.ValidateParent(a, a, parents));
        Assert.Contains("cycle", TeamHierarchy.ValidateParent(a, c, parents));
        Assert.Null(TeamHierarchy.ValidateParent(c, a, parents));
    }

    [Fact]
    public void Hierarchy_depth_is_capped_counting_the_subtree_being_moved()
    {
        var chain = Enumerable.Range(0, TeamHierarchy.MaxDepth).Select(_ => Guid.NewGuid()).ToList();
        var parents = new Dictionary<Guid, Guid?>();
        for (var i = 0; i < chain.Count; i++) parents[chain[i]] = i == 0 ? null : chain[i - 1];

        var extra = Guid.NewGuid();
        parents[extra] = null;
        Assert.Contains("levels deep", TeamHierarchy.ValidateParent(extra, chain[^1], parents));

        // Moving the root of a 2-deep subtree under a 4-deep chain exceeds the cap too.
        var subRoot = Guid.NewGuid();
        var subChild = Guid.NewGuid();
        parents[subRoot] = null;
        parents[subChild] = subRoot;
        Assert.Contains("levels deep", TeamHierarchy.ValidateParent(subRoot, chain[^2], parents));
    }

    [Fact]
    public void Descendant_walk_is_bounded_and_survives_a_cycle_already_in_the_data()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var corrupt = new Dictionary<Guid, Guid?> { [a] = b, [b] = a };

        var reached = TeamHierarchy.Descendants([a], corrupt);
        Assert.Equal(new HashSet<Guid> { a, b }, reached);
    }

    // ── Visibility model ─────────────────────────────────────────────────────────────

    [Fact]
    public void Visibility_is_per_type_and_an_unowned_record_needs_an_all_scope()
    {
        var me = Guid.NewGuid();
        var v = RecordVisibility.Create(new Dictionary<VisibilityRecordType, IReadOnlyCollection<Guid>?>
        {
            [VisibilityRecordType.Lead] = [me],
            [VisibilityRecordType.Contact] = null
        });

        Assert.True(v.CanSee(VisibilityRecordType.Lead, me));
        Assert.False(v.CanSee(VisibilityRecordType.Lead, Guid.NewGuid()));
        Assert.False(v.CanSee(VisibilityRecordType.Lead, null));
        Assert.True(v.CanSee(VisibilityRecordType.Contact, null));
        Assert.True(v.CanSee(VisibilityRecordType.Opportunity, Guid.NewGuid()));
    }

    [Fact]
    public async Task Ambient_visibility_flows_across_awaits_and_is_restored()
    {
        var restricted = RecordVisibility.Create(new Dictionary<VisibilityRecordType, IReadOnlyCollection<Guid>?>
            { [VisibilityRecordType.Lead] = [Guid.NewGuid()] });

        using (RecordVisibility.Enter(restricted))
        {
            await Task.Yield();
            Assert.Same(restricted, RecordVisibility.Current);
        }

        Assert.Same(RecordVisibility.Unrestricted, RecordVisibility.Current);
    }

    // ── Record permission convention ─────────────────────────────────────────────────

    [Fact]
    public void Record_groups_require_read_for_gets_and_write_for_everything_else()
    {
        var app = WebApplication.CreateBuilder().Build();
        var group = app.MapGroup(string.Empty).RequireRecordPermissions(PermissionConstants.LeadsRead, PermissionConstants.LeadsWrite);
        group.MapGet("/x", () => "r");
        group.MapPost("/x", () => "w");
        group.MapDelete("/x/{id}", (string id) => "d");

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList();
        string Policy(string method) => endpoints
            .Single(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method))
            .Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Single(p => p is not null)!;

        Assert.Equal(PermissionConstants.LeadsRead, Policy("GET"));
        Assert.Equal(PermissionConstants.LeadsWrite, Policy("POST"));
        Assert.Equal(PermissionConstants.LeadsWrite, Policy("DELETE"));
    }
}
