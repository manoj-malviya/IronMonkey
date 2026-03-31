using IronMonkey.Web;
using IronMonkey.Web.Authentication;
using IronMonkey.Web.CircuitHandlers;
using IronMonkey.Web.Components;
using IronMonkey.Web.HttpHandlers;
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
builder.Services.AddScoped<AdminAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<AdminAuthenticationStateProvider>());

// HttpClient: named "AdminApi" with Bearer token injection
builder.Services.AddScoped<BearerTokenHandler>();
builder.Services.AddHttpClient("AdminApi", client =>
    {
        client.BaseAddress = new Uri("https+http://apiservice");
    })
    .AddHttpMessageHandler<BearerTokenHandler>();

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
app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
