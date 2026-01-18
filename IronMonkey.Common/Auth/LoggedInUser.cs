namespace IronMonkey.Common.Auth;

public record LoggedInUser(string IdentityId, string Name, string Email, string Role);