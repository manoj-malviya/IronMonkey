using IronMonkey.Data;

namespace IronMonkey.ApiService.Notifications;

public interface INotificationService
{
    Task CreateAsync(TenantDbContext db, Guid tenantId, Guid recipientUserId, string message,
        Guid? leadId, CancellationToken cancellationToken);
}
