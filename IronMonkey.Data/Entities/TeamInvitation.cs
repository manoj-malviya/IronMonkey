using IronMonkey.Data.Abstractions;

namespace IronMonkey.Data.Entities;

/// <summary>
/// Where an invitation has got to.
///
/// Expiry is deliberately NOT a stored state that something has to transition into: a row
/// stays <see cref="Pending"/> in the database past its expiry, and <see cref="IsExpired"/>
/// derives the truth from the clock. A stored flag would need a sweeper to be correct, and a
/// token would remain acceptable for however long that sweeper was late.
/// </summary>
public enum InvitationStatus
{
    /// <summary>Issued, not yet used. May still be expired — see <see cref="TeamInvitation.IsExpired"/>.</summary>
    Pending = 0,

    /// <summary>The invitee set a password and the tenant user exists. Terminal.</summary>
    Accepted = 1,

    /// <summary>Withdrawn by an admin. The token no longer validates. Terminal.</summary>
    Revoked = 2,

    /// <summary>
    /// The invitation exists and the token is valid, but the notification could not be
    /// delivered — no provider configured, or the provider refused it. Distinct from
    /// <see cref="Pending"/> because the admin has to act: resend, or hand over the link.
    /// The link still works, which is what makes local dev usable with no credentials.
    /// </summary>
    Failed = 3
}

/// <summary>
/// An outstanding invitation for someone to join this tenant.
///
/// Lives in the TENANT database, so it inherits database-per-tenant isolation and the global
/// TenantId query filter: an invitation is never visible to, or acceptable by, another tenant.
///
/// Only the BCrypt hash of the token is stored, following <see cref="ApiKey"/>. The plaintext
/// exists once, in the response to the issuing request, and is never recoverable afterwards —
/// so a dump of this table does not let anyone join a tenant. Resend rotates the token rather
/// than re-showing the old one for exactly that reason.
/// </summary>
public sealed class TeamInvitation : BaseTenantEntity
{
    private TeamInvitation() { }

    /// <summary>Normalised (trimmed, lower-cased) — the address the token is bound to.</summary>
    public string Email { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public int RoleId { get; private set; }

    /// <summary>Optional note from the inviter, shown on the acceptance page.</summary>
    public string? Message { get; private set; }

    /// <summary>
    /// BCrypt hash of the current token. Never the plaintext, and rotated by
    /// <see cref="Rotate"/> on every resend so a leaked old link stops working.
    /// </summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// First 8 characters of the token, for locating the row without a table scan of
    /// BCrypt comparisons. Not a secret and not sufficient to accept — the full token is
    /// still verified against <see cref="TokenHash"/>.
    /// </summary>
    public string TokenPrefix { get; private set; } = string.Empty;

    public InvitationStatus Status { get; private set; }

    public DateTime SentAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>The tenant user created on acceptance. Null until then.</summary>
    public Guid? AcceptedUserId { get; private set; }

    /// <summary>The user who issued or last resent this invitation.</summary>
    public Guid InvitedByUserId { get; private set; }

    /// <summary>How many times it has been sent, including the first. Resend increments.</summary>
    public int SendCount { get; private set; }

    /// <summary>Redacted reason the last delivery attempt failed. Null unless Status is Failed.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// The states in which the token is still live.
    ///
    /// <see cref="InvitationStatus.Failed"/> counts, and that is the whole point of the
    /// state: the invitation is real and its token is valid — only the NOTIFICATION did not
    /// arrive. An admin who copies the link out of the UI must be able to hand it over, which
    /// is what makes the feature usable in an environment with no mail provider configured.
    /// Only the terminal states (Accepted, Revoked) take the token out of play.
    /// </summary>
    private bool IsLive => Status is InvitationStatus.Pending or InvitationStatus.Failed;

    /// <summary>
    /// True once the expiry has passed, for a row whose token is still live. A terminal row
    /// is never "expired" — an accepted invitation does not become expired by the clock
    /// moving.
    /// </summary>
    public bool IsExpired(DateTime nowUtc) => IsLive && ExpiresAt <= nowUtc;

    /// <summary>The states in which a token may be redeemed.</summary>
    public bool IsRedeemable(DateTime nowUtc) => IsLive && ExpiresAt > nowUtc;

    public static TeamInvitation Create(
        Guid tenantId,
        string email,
        string name,
        int roleId,
        string? message,
        string tokenHash,
        string tokenPrefix,
        Guid invitedByUserId,
        DateTime nowUtc,
        TimeSpan lifetime)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email,
            Name = name,
            RoleId = roleId,
            Message = message,
            TokenHash = tokenHash,
            TokenPrefix = tokenPrefix,
            Status = InvitationStatus.Pending,
            SentAt = nowUtc,
            ExpiresAt = nowUtc.Add(lifetime),
            InvitedByUserId = invitedByUserId,
            SendCount = 1
        };

    /// <summary>
    /// Issues a fresh token and expiry for a resend.
    ///
    /// The old hash is overwritten, so the previously mailed link stops validating. Reusing
    /// the old token would mean an invitation forwarded to the wrong person stays live after
    /// the admin has "resent" it — the resend is also the remedy for a leaked link.
    /// Resetting the status clears a previous Failed so the row reads as freshly sent.
    /// </summary>
    public void Rotate(string tokenHash, string tokenPrefix, Guid resentByUserId, DateTime nowUtc, TimeSpan lifetime)
    {
        TokenHash = tokenHash;
        TokenPrefix = tokenPrefix;
        Status = InvitationStatus.Pending;
        FailureReason = null;
        SentAt = nowUtc;
        ExpiresAt = nowUtc.Add(lifetime);
        InvitedByUserId = resentByUserId;
        SendCount += 1;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Withdraws the invitation. The hash is cleared as well as the status changed, so the
    /// token cannot validate even if a later code path forgets to check the status.
    /// </summary>
    public void Revoke(DateTime nowUtc)
    {
        Status = InvitationStatus.Revoked;
        RevokedAt = nowUtc;
        TokenHash = string.Empty;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Marks the token spent. Clearing the hash is the single-use guarantee at the data
    /// level: a second redemption of the same plaintext cannot match an empty hash even
    /// before the status check is reached.
    /// </summary>
    public void Accept(Guid userId, DateTime nowUtc)
    {
        Status = InvitationStatus.Accepted;
        AcceptedAt = nowUtc;
        AcceptedUserId = userId;
        TokenHash = string.Empty;
        UpdatedAt = nowUtc;
    }

    /// <summary>
    /// Records that delivery failed. The token is untouched and still redeemable — the
    /// invitation is real, only the notification did not arrive, and the admin can hand the
    /// link over directly.
    /// </summary>
    public void MarkDeliveryFailed(string reason, DateTime nowUtc)
    {
        Status = InvitationStatus.Failed;
        FailureReason = reason;
        UpdatedAt = nowUtc;
    }
}
