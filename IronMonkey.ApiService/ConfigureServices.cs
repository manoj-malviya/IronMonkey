using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.ApiService.Common.Email;
using IronMonkey.ApiService.Common.Services;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Extensions;
using IronMonkey.Data.Types;
using Serilog;
using Swashbuckle.AspNetCore.Filters;

namespace IronMonkey.ApiService;

public static class ConfigureServices
{
    public static void AddServices(this WebApplicationBuilder builder)
    {
        builder.AddSerilog();
        builder.AddSwagger();
        builder.Services.ConfigureDb(builder.Configuration);
        builder.Services.AddValidatorsFromAssembly(typeof(ConfigureServices).Assembly);
        builder.AddJwtAuthentication();
        builder.AddAuthorization();
        builder.AddCache();
        builder.AddEmailServices();
    }

    private static void AddSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration.ReadFrom.Configuration(context.Configuration);
        });
    }

    private static void AddSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
            options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
            });
            
            options.OperationFilter<SecurityRequirementsOperationFilter>();
        });
    }
    
    private static void AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication().AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = Jwt.SecurityKey(builder.Configuration["Jwt:Key"]!),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero
            };
        });
        builder.Services.AddAuthorization();
        
        //
        // builder.Services.AddIdentityApiEndpoints<User>(options =>
        //     {
        //         options.User.RequireUniqueEmail = true;
        //         options.SignIn.RequireConfirmedEmail = true;
        //     })
        //     .AddEntityFrameworkStores<AppDbContext>();
            // builder.Services.AddIdentity<User, IdentityRole>(options =>
            //     {
            //         options.User.RequireUniqueEmail = true;
            //         options.SignIn.RequireConfirmedEmail = true;   
            //     })
            //     .AddEntityFrameworkStores<AppDbContext>();
            //
            // builder.Services.AddAuthorization();
            // builder.Services.AddTransient<IEmailSender, EmailSender>();
            // builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration);
        
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
        builder.Services.AddTransient<Jwt>();
        
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddScoped<IUserContext, UserContext>();
    }
    
    private static void AddAuthorization(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<AuthorizationService>();

        builder.Services.AddTransient<IClaimsTransformation, CustomClaimsTransformation>();

        builder.Services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();

        builder.Services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
    }
    
    private static void AddCache(this WebApplicationBuilder builder)
    {
        builder.Services.AddDistributedMemoryCache();
        
        builder.Services.AddSingleton<ICacheService, CacheService>();
    }

    private static void AddEmailServices(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
        builder.Services.AddScoped<IEmailService, EmailService>();
    }
}