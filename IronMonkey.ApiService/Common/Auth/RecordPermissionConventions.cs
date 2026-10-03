using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace IronMonkey.ApiService.Common.Auth;

/// <summary>
/// Applies a record type's read and write permissions to every endpoint in a group:
/// GET/HEAD require <c>read</c>, anything else requires <c>write</c>.
///
/// <para>Before this, CRM record endpoints required only authentication, so leads:read and
/// friends were labels rather than permissions — a role without them could still read and
/// edit every record. Applied per group rather than per endpoint so a newly added lead
/// endpoint inherits the gate instead of having to remember it. Endpoints keep their own
/// RequireAuthorization; both must pass.</para>
/// </summary>
public static class RecordPermissionConventions
{
    public static RouteGroupBuilder RequireRecordPermissions(this RouteGroupBuilder group, string read, string write)
    {
        ((IEndpointConventionBuilder)group).Add(builder =>
        {
            var methods = builder.Metadata.OfType<HttpMethodMetadata>().SelectMany(m => m.HttpMethods).ToList();
            var readOnly = methods.Count > 0 && methods.All(m => m is "GET" or "HEAD");
            builder.Metadata.Add(new AuthorizeAttribute(readOnly ? read : write));
        });
        return group;
    }
}
