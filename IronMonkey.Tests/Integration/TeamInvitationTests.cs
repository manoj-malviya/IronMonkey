using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.UserManagement;
using IronMonkey.ApiService.Features.UserManagement.Invitations;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IronMonkey.Tests.Integration;

/// <summary>
/// The invitation lifecycle and the team-safety invariants around it.
///
/// These exercise the real entity and the real resolver — not a re-implementation — because
/// the properties under test (single use, expiry, tenant binding, the last-Admin guard) are
/// exactly the ones a plausible refactor breaks silently.
/// </summary>
[Collection("Integration")]
public class TeamInvitationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private readonly TenantDbContextFactory _factory = new();

    private async Task<string> CreateTenantDbAsync(Guid tenantId, string label)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var connectionString = fixture.ConnectionString.Replace("ironmonkey_test", $"inv_{label}_{suffix}");
        await using var db = _factory.CreateForTenant(connectionString, tenantId);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    private static TeamInvitation NewInvitation(
        Guid tenantId, string email, IssuedToken token, DateTime now, TimeSpan? lifetime = null)
        => TeamInvitation.Create(
            tenantId, email, "Invited Person", AdminSafetyGuard.AdminRoleId, null,
            token.Hash, token.Prefix, Guid.NewGuid(), now, lifetime ?? InvitationToken.Lifetime);

    // ---------------------------------------------------------------- token

    [Fact]
    public void A_token_is_never_stored_in_the_clear_and_verifies_only_against_its_own_hash()
    {
        var a = InvitationToken.Issue();
        var b = InvitationToken.Issue();

        // The hash must not be the token. If these were ever equal the table would be a
        // list of working invitations.
        Assert.NotEqual(a.Plaintext, a.Hash);
        Assert.StartsWith(a.Plaintext[..InvitationToken.PrefixLength], a.Prefix);

        Assert.True(InvitationToken.Verify(a.Plaintext, a.Hash));
        Assert.False(InvitationToken.Verify(b.Plaintext, a.Hash));

        // A cleared hash — what Revoke and Accept leave behind — refuses rather than throws.
        Assert.False(InvitationToken.Verify(a.Plaintext, string.Empty));
        Assert.False(InvitationToken.Verify(a.Plaintext, "not-a-bcrypt-hash"));
    }

    [Fact]
    public void Two_issues_never_produce_the_same_token()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => InvitationToken.Issue().Plaintext).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    // ------------------------------------------------------------- lifecycle

    [Fact]
    public void Resend_rotates_the_token_so_the_previous_link_stops_working()
    {
        var now = DateTime.UtcNow;
        var first = InvitationToken.Issue();
        var invitation = NewInvitation(Guid.NewGuid(), "a@b.com", first, now);

        Assert.True(InvitationToken.Verify(first.Plaintext, invitation.TokenHash));
        var firstExpiry = invitation.ExpiresAt;

        var second = InvitationToken.Issue();
        invitation.Rotate(second.Hash, second.Prefix, Guid.NewGuid(), now.AddHours(1), InvitationToken.Lifetime);

        // The whole point of a resend: the old link is dead, not merely extended.
        Assert.False(InvitationToken.Verify(first.Plaintext, invitation.TokenHash));
        Assert.True(InvitationToken.Verify(second.Plaintext, invitation.TokenHash));
        Assert.True(invitation.ExpiresAt > firstExpiry);
        Assert.Equal(2, invitation.SendCount);
    }

    [Fact]
    public void Resend_clears_a_previous_failed_state()
    {
        var now = DateTime.UtcNow;
        var invitation = NewInvitation(Guid.NewGuid(), "a@b.com", InvitationToken.Issue(), now);

        invitation.MarkDeliveryFailed("no provider", now);
        Assert.Equal(InvitationStatus.Failed, invitation.Status);

        var next = InvitationToken.Issue();
        invitation.Rotate(next.Hash, next.Prefix, Guid.NewGuid(), now, InvitationToken.Lifetime);

        Assert.Equal(InvitationStatus.Pending, invitation.Status);
        Assert.Null(invitation.FailureReason);
    }

    [Fact]
    public void A_failed_delivery_leaves_the_token_usable()
    {
        var now = DateTime.UtcNow;
        var token = InvitationToken.Issue();
        var invitation = NewInvitation(Guid.NewGuid(), "a@b.com", token, now);

        invitation.MarkDeliveryFailed("Channel not configured", now);

        // The invitation is real; only the notification did not arrive. An admin must still
        // be able to hand over the link, which is what makes local dev work with no SMTP.
        Assert.True(InvitationToken.Verify(token.Plaintext, invitation.TokenHash));
        Assert.True(invitation.IsRedeemable(now));
    }

    [Fact]
    public async Task A_failed_invitation_still_blocks_a_duplicate_for_the_same_address()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "faildup");
        var now = DateTime.UtcNow;

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var invitation = NewInvitation(tenantId, "dupe@x.com", InvitationToken.Issue(), now);
            invitation.MarkDeliveryFailed("Channel not configured", now);
            db.TeamInvitations.Add(invitation);
            await db.SaveChangesAsync();
        }

        // This mirrors the predicate InviteTeamMemberEndpoint uses. A Failed row's token is
        // still live, so a second invitation for the same address would leave TWO valid
        // tokens — the reason the check cannot look at Pending alone.
        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var outstanding = await db.TeamInvitations.FirstOrDefaultAsync(
                i => i.Email == "dupe@x.com"
                     && (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Failed)
                     && i.ExpiresAt > now);

            Assert.NotNull(outstanding);
        }
    }

    [Fact]
    public void Revoke_and_accept_both_clear_the_hash_so_the_token_cannot_match_again()
    {
        var now = DateTime.UtcNow;

        var revokedToken = InvitationToken.Issue();
        var revoked = NewInvitation(Guid.NewGuid(), "a@b.com", revokedToken, now);
        revoked.Revoke(now);
        Assert.False(InvitationToken.Verify(revokedToken.Plaintext, revoked.TokenHash));

        var acceptedToken = InvitationToken.Issue();
        var accepted = NewInvitation(Guid.NewGuid(), "c@d.com", acceptedToken, now);
        accepted.Accept(Guid.NewGuid(), now);
        Assert.False(InvitationToken.Verify(acceptedToken.Plaintext, accepted.TokenHash));
    }

    [Fact]
    public void Expiry_is_derived_from_the_clock_not_from_a_stored_flag()
    {
        var now = DateTime.UtcNow;
        var invitation = NewInvitation(Guid.NewGuid(), "a@b.com", InvitationToken.Issue(), now, TimeSpan.FromHours(1));

        Assert.False(invitation.IsExpired(now));
        Assert.True(invitation.IsRedeemable(now));

        // Nothing had to run for this to become true — no sweeper, no job.
        Assert.True(invitation.IsExpired(now.AddHours(2)));
        Assert.False(invitation.IsRedeemable(now.AddHours(2)));

        // A terminal row is never "expired": accepting does not become expiring.
        invitation.Accept(Guid.NewGuid(), now);
        Assert.False(invitation.IsExpired(now.AddYears(1)));
    }

    // -------------------------------------------------------------- resolver

    [Fact]
    public async Task A_valid_token_resolves_and_an_expired_one_does_not()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "resolve");
        var now = DateTime.UtcNow;

        var live = InvitationToken.Issue();
        var stale = InvitationToken.Issue();

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            db.TeamInvitations.Add(NewInvitation(tenantId, "live@x.com", live, now));
            // Issued two weeks ago with a one-week lifetime.
            db.TeamInvitations.Add(NewInvitation(tenantId, "stale@x.com", stale, now.AddDays(-14)));
            await db.SaveChangesAsync();
        }

        await using var check = _factory.CreateForTenant(connectionString, tenantId);

        var (okCheck, okInvitation) = await InvitationResolver.ResolveAsync(
            check, tenantId, live.Plaintext, "live@x.com", now, default);
        Assert.Equal(InvitationCheck.Valid, okCheck);
        Assert.NotNull(okInvitation);

        var (expiredCheck, expiredInvitation) = await InvitationResolver.ResolveAsync(
            check, tenantId, stale.Plaintext, "stale@x.com", now, default);
        Assert.Equal(InvitationCheck.Expired, expiredCheck);
        Assert.Null(expiredInvitation);
    }

    [Fact]
    public async Task A_token_can_only_be_redeemed_once()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "single");
        var now = DateTime.UtcNow;
        var token = InvitationToken.Issue();

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            db.TeamInvitations.Add(NewInvitation(tenantId, "once@x.com", token, now));
            await db.SaveChangesAsync();
        }

        // First redemption succeeds and marks the row spent, exactly as the endpoint does.
        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var (check, invitation) = await InvitationResolver.ResolveAsync(
                db, tenantId, token.Plaintext, "once@x.com", now, default);

            Assert.Equal(InvitationCheck.Valid, check);
            invitation!.Accept(Guid.NewGuid(), now);
            await db.SaveChangesAsync();
        }

        // Second redemption of the SAME plaintext must not resolve.
        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var (check, invitation) = await InvitationResolver.ResolveAsync(
                db, tenantId, token.Plaintext, "once@x.com", now, default);

            Assert.Equal(InvitationCheck.AlreadyUsed, check);
            Assert.Null(invitation);
        }
    }

    [Fact]
    public async Task A_revoked_token_is_refused()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "revoked");
        var now = DateTime.UtcNow;
        var token = InvitationToken.Issue();

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var invitation = NewInvitation(tenantId, "gone@x.com", token, now);
            invitation.Revoke(now);
            db.TeamInvitations.Add(invitation);
            await db.SaveChangesAsync();
        }

        await using var check = _factory.CreateForTenant(connectionString, tenantId);
        var (result, invitationOut) = await InvitationResolver.ResolveAsync(
            check, tenantId, token.Plaintext, "gone@x.com", now, default);

        Assert.Equal(InvitationCheck.Revoked, result);
        Assert.Null(invitationOut);
    }

    [Fact]
    public async Task A_token_bound_to_one_email_is_refused_for_another()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "email");
        var now = DateTime.UtcNow;
        var token = InvitationToken.Issue();

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            db.TeamInvitations.Add(NewInvitation(tenantId, "intended@x.com", token, now));
            await db.SaveChangesAsync();
        }

        await using var check = _factory.CreateForTenant(connectionString, tenantId);

        var (mismatch, _) = await InvitationResolver.ResolveAsync(
            check, tenantId, token.Plaintext, "someone.else@x.com", now, default);
        Assert.Equal(InvitationCheck.EmailMismatch, mismatch);

        // Case is not a mismatch — emails are compared case-insensitively everywhere.
        var (ok, _) = await InvitationResolver.ResolveAsync(
            check, tenantId, token.Plaintext, "INTENDED@x.com", now, default);
        Assert.Equal(InvitationCheck.Valid, ok);
    }

    [Fact]
    public async Task A_token_issued_by_one_tenant_cannot_be_redeemed_in_another()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connA = await CreateTenantDbAsync(tenantA, "iso_a");
        var connB = await CreateTenantDbAsync(tenantB, "iso_b");
        var now = DateTime.UtcNow;
        var token = InvitationToken.Issue();

        await using (var db = _factory.CreateForTenant(connA, tenantA))
        {
            db.TeamInvitations.Add(NewInvitation(tenantA, "shared@x.com", token, now));
            await db.SaveChangesAsync();
        }

        // Tenant B's database does not contain the row at all.
        await using (var db = _factory.CreateForTenant(connB, tenantB))
        {
            var (check, invitation) = await InvitationResolver.ResolveAsync(
                db, tenantB, token.Plaintext, "shared@x.com", now, default);

            Assert.Equal(InvitationCheck.NotFound, check);
            Assert.Null(invitation);
        }

        // And even pointed at tenant A's database, a mismatched tenant id filters it out:
        // the TenantId query filter is the binding, not a comparison the code could forget.
        await using (var db = _factory.CreateForTenant(connA, tenantB))
        {
            var (check, _) = await InvitationResolver.ResolveAsync(
                db, tenantB, token.Plaintext, "shared@x.com", now, default);

            Assert.NotEqual(InvitationCheck.Valid, check);
        }
    }

    [Fact]
    public async Task An_invitation_for_someone_who_has_since_joined_is_refused()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "dup");
        var now = DateTime.UtcNow;
        var token = InvitationToken.Issue();

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            db.TeamInvitations.Add(NewInvitation(tenantId, "already@x.com", token, now));

            var role = await db.Roles.SingleAsync(r => r.Id == AdminSafetyGuard.AdminRoleId);
            db.Users.Add(User.Create(tenantId, "Already There", "already@x.com", "hash", role));
            await db.SaveChangesAsync();
        }

        await using var check = _factory.CreateForTenant(connectionString, tenantId);
        var (result, _) = await InvitationResolver.ResolveAsync(
            check, tenantId, token.Plaintext, "already@x.com", now, default);

        Assert.Equal(InvitationCheck.AlreadyMember, result);
    }

    [Fact]
    public void Every_refusal_reports_one_undifferentiated_message()
    {
        // The enum distinguishes causes so the SERVER can log them, but the public message
        // must be the same for all of them: a caller who can tell "revoked" from "no such
        // token" learns which of their guesses were real invitations. The message is
        // deliberately a disjunction ("may have expired, been revoked, or already been
        // used"), never a statement about which one applies.
        var message = InvitationResolver.RefusalMessage;

        Assert.Contains("may have expired", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("been revoked", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already been used", message, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------ last-Admin guard

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated_or_demoted()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "lastadmin");

        Guid soleAdminId;

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var admin = await db.Roles.SingleAsync(r => r.Id == AdminSafetyGuard.AdminRoleId);
            var teleCaller = await db.Roles.SingleAsync(r => r.Id == 302);

            var soleAdmin = User.Create(tenantId, "Only Admin", "admin@x.com", "hash", admin);
            db.Users.Add(soleAdmin);
            db.Users.Add(User.Create(tenantId, "Agent", "agent@x.com", "hash", teleCaller));
            await db.SaveChangesAsync();

            soleAdminId = soleAdmin.Id;
        }

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            Assert.Equal(1, await AdminSafetyGuard.CountActiveAdminsAsync(db, tenantId, null, default));

            // Removing the only Admin would lock the tenant out of its own administration.
            Assert.True(await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantId, soleAdminId, default));

            // A non-Admin is never blocked by this guard, whatever the Admin count.
            var agentId = await db.Users.Where(u => u.Email == "agent@x.com").Select(u => u.Id).SingleAsync();
            Assert.False(await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantId, agentId, default));
        }
    }

    [Fact]
    public async Task A_second_admin_makes_the_first_removable()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "twoadmins");

        Guid firstAdminId;

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var adminRole = await db.Roles.SingleAsync(r => r.Id == AdminSafetyGuard.AdminRoleId);
            var first = User.Create(tenantId, "First", "first@x.com", "hash", adminRole);
            db.Users.Add(first);
            db.Users.Add(User.Create(tenantId, "Second", "second@x.com", "hash", adminRole));
            await db.SaveChangesAsync();
            firstAdminId = first.Id;
        }

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            Assert.Equal(2, await AdminSafetyGuard.CountActiveAdminsAsync(db, tenantId, null, default));
            Assert.False(await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantId, firstAdminId, default));
        }
    }

    [Fact]
    public async Task A_deactivated_admin_does_not_count_as_cover_for_the_last_one()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "deadadmin");

        Guid activeAdminId;

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var adminRole = await db.Roles.SingleAsync(r => r.Id == AdminSafetyGuard.AdminRoleId);

            var active = User.Create(tenantId, "Active", "active@x.com", "hash", adminRole);
            var retired = User.Create(tenantId, "Retired", "retired@x.com", "hash", adminRole);
            retired.Deactivate();

            db.Users.Add(active);
            db.Users.Add(retired);
            await db.SaveChangesAsync();

            activeAdminId = active.Id;
        }

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            // The soft-deleted Admin must NOT be counted: they cannot log in, so they are
            // no cover at all.
            Assert.Equal(1, await AdminSafetyGuard.CountActiveAdminsAsync(db, tenantId, null, default));
            Assert.True(await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantId, activeAdminId, default));
        }
    }

    [Fact]
    public async Task The_admin_guard_is_scoped_to_one_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connA = await CreateTenantDbAsync(tenantA, "guard_a");
        var connB = await CreateTenantDbAsync(tenantB, "guard_b");

        Guid adminAId;

        await using (var db = _factory.CreateForTenant(connA, tenantA))
        {
            var role = await db.Roles.SingleAsync(r => r.Id == AdminSafetyGuard.AdminRoleId);
            var a = User.Create(tenantA, "A Admin", "a@x.com", "hash", role);
            db.Users.Add(a);
            await db.SaveChangesAsync();
            adminAId = a.Id;
        }

        await using (var db = _factory.CreateForTenant(connB, tenantB))
        {
            var role = await db.Roles.SingleAsync(r => r.Id == AdminSafetyGuard.AdminRoleId);
            db.Users.Add(User.Create(tenantB, "B Admin 1", "b1@x.com", "hash", role));
            db.Users.Add(User.Create(tenantB, "B Admin 2", "b2@x.com", "hash", role));
            await db.SaveChangesAsync();
        }

        // Tenant B having two Admins must not make tenant A's sole Admin removable.
        await using (var db = _factory.CreateForTenant(connA, tenantA))
        {
            Assert.Equal(1, await AdminSafetyGuard.CountActiveAdminsAsync(db, tenantA, null, default));
            Assert.True(await AdminSafetyGuard.WouldRemoveLastAdminAsync(db, tenantA, adminAId, default));
        }
    }

    // ------------------------------------------------------------ isolation

    [Fact]
    public async Task One_tenants_invitations_and_audit_rows_are_invisible_to_another()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var connA = await CreateTenantDbAsync(tenantA, "vis_a");
        var connB = await CreateTenantDbAsync(tenantB, "vis_b");
        var now = DateTime.UtcNow;

        await using (var db = _factory.CreateForTenant(connA, tenantA))
        {
            db.TeamInvitations.Add(NewInvitation(tenantA, "a@x.com", InvitationToken.Issue(), now));
            db.UserAuditLogs.Add(UserAuditLog.Record(
                tenantA, UserAuditEvent.Invited, "a@x.com", now, actorUserId: Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateForTenant(connB, tenantB))
        {
            Assert.Empty(await db.TeamInvitations.ToListAsync());
            Assert.Empty(await db.UserAuditLogs.ToListAsync());
        }

        // Even reading tenant A's own database under tenant B's identity returns nothing:
        // the global query filter is the enforcement, not a predicate a caller supplies.
        await using (var db = _factory.CreateForTenant(connA, tenantB))
        {
            Assert.Empty(await db.TeamInvitations.ToListAsync());
            Assert.Empty(await db.UserAuditLogs.ToListAsync());
        }
    }

    // ---------------------------------------------------------- permissions

    [Fact]
    public async Task A_tenant_admin_can_manage_the_team_but_never_reaches_the_platform()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "perms");

        await using var db = _factory.CreateForTenant(connectionString, tenantId);

        var adminPermissions = await db.Roles
            .Where(r => r.Id == AdminSafetyGuard.AdminRoleId)
            .SelectMany(r => r.Permissions)
            .Select(p => p.Name)
            .ToListAsync();

        // The invitation endpoints are gated on users:write, which the seeded Admin holds.
        Assert.Contains(PermissionConstants.UsersWrite, adminPermissions);
        Assert.Contains(PermissionConstants.UsersRead, adminPermissions);

        // Deactivate is deliberately gated on users:write and NOT users:delete, because the
        // seeded tenant Admin does not hold users:delete — only the platform SuperAdmin does.
        // If this ever starts failing, the deactivate gate must be revisited with it.
        Assert.DoesNotContain(PermissionConstants.UsersDelete, adminPermissions);

        // And the platform boundary holds: a tenant Admin never has admin:access.
        Assert.DoesNotContain(PermissionConstants.AdminAccess, adminPermissions);
    }

    [Fact]
    public void A_tenant_can_never_invite_into_the_platform_role()
    {
        // The invite and update endpoints both gate on this, so SuperAdmin cannot be
        // assigned by a tenant even with a hand-crafted request.
        Assert.False(TenantRoleRules.IsVisibleToTenant(1));
        Assert.True(TenantRoleRules.IsVisibleToTenant(AdminSafetyGuard.AdminRoleId));
    }

    // ------------------------------------------------------------ audit trail

    [Fact]
    public async Task A_role_change_is_recorded_in_the_audit_trail()
    {
        var tenantId = Guid.NewGuid();
        var connectionString = await CreateTenantDbAsync(tenantId, "audit");
        var now = DateTime.UtcNow;
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            db.UserAuditLogs.Add(UserAuditLog.Record(
                tenantId, UserAuditEvent.RoleChanged, "target@x.com", now,
                targetUserId: targetId, actorUserId: actorId, detail: "TeleCaller -> Admin"));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateForTenant(connectionString, tenantId))
        {
            var row = await db.UserAuditLogs.SingleAsync();
            Assert.Equal(UserAuditEvent.RoleChanged, row.EventType);
            Assert.Equal(targetId, row.TargetUserId);
            Assert.Equal(actorId, row.ActorUserId);
            Assert.Equal("TeleCaller -> Admin", row.Detail);
        }
    }

    [Fact]
    public void An_unauthenticated_actor_is_recorded_as_absent_not_as_the_empty_guid()
    {
        // Acceptance happens before the invitee has a session, so there is no acting user.
        var row = UserAuditLog.Record(
            Guid.NewGuid(), UserAuditEvent.InvitationAccepted, "new@x.com", DateTime.UtcNow,
            actorUserId: Guid.Empty);

        Assert.Null(row.ActorUserId);
    }
}
