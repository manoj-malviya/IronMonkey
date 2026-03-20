namespace IronMonkey.ApiService.Common.Auth;

public interface ITenantService
{
    Guid GetCurrentTenantId();
    Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default);
}
