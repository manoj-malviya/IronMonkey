using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Notifications;

/// <summary>
/// Creates in-app notifications only. No email delivery per D-10.
/// </summary>
public class NotificationService : INotificationService
{
    public async Task CreateAsync(TenantDbContext db, Guid tenantId, Guid recipientUserId,
        string message, Guid? leadId, CancellationToken cancellationToken)
    {
        var notification = Notification.Create(tenantId, recipientUserId, message, leadId);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
    }
}
