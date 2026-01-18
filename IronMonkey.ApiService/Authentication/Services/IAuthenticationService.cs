using IronMonkey.Data.Types;

namespace IronMonkey.ApiService.Authentication.Services;

public interface IAuthenticationService
{
    Task<string> RegisterAsync(
        User user,
        string password,
        CancellationToken cancellationToken = default);
}