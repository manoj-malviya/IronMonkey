using Asp.Versioning;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.ApiService.Common.Services;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Features.Leads.Ingestion.Api;
using IronMonkey.ApiService.Features.Leads.Ingestion.Csv;
using IronMonkey.ApiService.Features.Leads.Merge;
using IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;
using IronMonkey.ApiService.Features.Leads.Pipeline.Routing;
using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Extensions;
using Microsoft.OpenApi;
using Serilog;
using Swashbuckle.AspNetCore.Filters;

namespace IronMonkey.ApiService;

public static class ConfigureServices
{
    extension(WebApplicationBuilder builder)
    {
        public void AddServices()
        {
            builder.AddSerilog();
            builder.AddSwagger();
            builder.Services.ConfigureDb(builder.Configuration);
            builder.Services.AddValidatorsFromAssembly(typeof(ConfigureServices).Assembly);
            builder.AddJwtAuthentication();
            builder.AddAuthorization();
            builder.AddCache();
            builder.AddEmailServices();
            
            builder.addApiVersioning();
            builder.addCors();

            builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
            builder.Services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
            builder.Services.AddScoped<ILeadMergeService, LeadMergeService>();
            builder.Services.AddScoped<IWebFormService, WebFormService>();
            builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
            builder.Services.AddScoped<ILeadRoutingService, LeadRoutingService>();
            builder.Services.AddScoped<IStateValidationService, StateValidationService>();
            builder.Services.AddScoped<CsvImportService>();
            builder.Services.AddScoped<CsvImportJob>();
            builder.AddHangfire();

            // Phase 3: Rate limiting middleware
            builder.AddRateLimiter();
        }

        private void AddSerilog()
        {
            builder.Host.UseSerilog((context, configuration) =>
            {
                configuration.ReadFrom.Configuration(context.Configuration);
            });
        }

        private void AddSwagger()
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

        private void AddJwtAuthentication()
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
        
            // builder.Services.AddIdentityApiEndpoints<User>(options =>
            // {
            //     options.User.RequireUniqueEmail = true;
            //     options.SignIn.RequireConfirmedEmail = true;
            // })
            // .AddEntityFrameworkStores<AppDbContext>();
            //
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
            builder.Services.AddScoped<ITenantService, TenantService>();
        }

        private void AddAuthorization()
        {
            builder.Services.AddScoped<AuthorizationService>();

            builder.Services.AddTransient<IClaimsTransformation, CustomClaimsTransformation>();

            builder.Services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();

            builder.Services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        }

        private void AddCache()
        {
            builder.Services.AddDistributedMemoryCache();
        
            builder.Services.AddSingleton<ICacheService, CacheService>();
        }

        private void AddEmailServices()
        {
            builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
            builder.Services.AddScoped<IEmailService, EmailService>();
        }
        
        private void addApiVersioning()
        {
            builder.Services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            });
        }
        
        private void addCors()
        {
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
        }

        private void AddRateLimiter()
        {
            builder.Services.AddRateLimiter(options =>
            {
                // REST API rate limit: 100 leads/min per API key (D-03)
                options.AddPolicy("api-key-limit", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Request.Headers["X-Api-Key"].ToString() is { Length: > 0 } key
                            ? key
                            : "anonymous-" + httpContext.Connection.RemoteIpAddress,
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        }));

                // Web form rate limit: 10 submissions/min per form token (D-03)
                options.AddPolicy("form-token-limit", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Request.RouteValues["token"]?.ToString() ?? "no-token",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                            QueueLimit = 0
                        }));

                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });
        }

        private void AddHangfire()
        {
            var connectionString = builder.Configuration.GetConnectionString("CentralDb")
                ?? throw new InvalidOperationException("CentralDb connection string required for Hangfire.");

            builder.Services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString))
            );

            builder.Services.AddHangfireServer(options =>
            {
                options.WorkerCount = Environment.ProcessorCount * 2;
                options.Queues = ["default", "tenant"];
            });

            builder.Services.AddScoped<ITenantRegistry, TenantRegistry>();
        }
    }
}