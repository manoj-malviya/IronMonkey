using Microsoft.AspNetCore.Authorization;

namespace IronMonkey.ApiService.Common.Auth;

public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(permission);