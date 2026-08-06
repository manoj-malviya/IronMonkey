using IronMonkey.Web;
using IronMonkey.Web.Authentication;
using IronMonkey.Web.CircuitHandlers;
using IronMonkey.Web.Components;
using IronMonkey.Web.HttpHandlers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// Authentication: JWT via ProtectedSessionStorage
builder.Services.AddCascadingAuthenticationState();

// Pages carrying [Authorize] route through AuthorizationMiddleware, which resolves
// IAuthenticationService to issue a challenge. This app authenticates in the Blazor
// circuit via AdminAuthenticationStateProvider rather than a cookie/JWT middleware,
// so register a minimal scheme that redirects to /login instead of throwing.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<AdminAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<AdminAuthenticationStateProvider>());

// HttpClient: named "AdminApi" with Bearer token injection
builder.Services.AddScoped<BearerTokenHandler>();
var adminApi = builder.Services.AddHttpClient("AdminApi", client =>
    {
        client.BaseAddress = new Uri("https+http://apiservice");
    })
    .AddHttpMessageHandler<BearerTokenHandler>();

if (builder.Environment.IsDevelopment())
{
    // The ASP.NET Core dev certificate is not in the OS trust store on Linux, so
    // server-to-server calls to the API fail the TLS handshake. Accept it in
    // Development only — never relax certificate validation outside local dev.
    adminApi.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
}

// Legacy typed client (kept for backward compat — ApiClient is currently empty)
builder.Services.AddHttpClient<ApiClient>(client =>
    {
        client.BaseAddress = new("https+http://apiservice");
    });

// Circuit handler: validates user identity on reconnect
builder.Services.AddScoped<CircuitHandler, IdentityValidationCircuitHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
