using Asp.Versioning;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using IronMonkey.ApiService.Authentication.Services;
using IronMonkey.ApiService.BackgroundJobs;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Auth;
using IronMonkey.ApiService.Common.Cache;
using IronMonkey.ApiService.Features.Communications;
using IronMonkey.ApiService.Features.Communications.Providers;
using IronMonkey.ApiService.Features.Communications.Webhooks;
using IronMonkey.ApiService.Features.Leads.Duplicates;
using IronMonkey.ApiService.Features.Leads.Ingestion.Api;
using IronMonkey.ApiService.Features.Leads.Ingestion.Csv;
using IronMonkey.ApiService.Features.Leads.Merge;
using IronMonkey.ApiService.Features.Leads.Ingestion.WebForm;
using IronMonkey.ApiService.Features.Activity;
using IronMonkey.ApiService.Features.Leads.Pipeline.Routing;
using IronMonkey.ApiService.Features.Configuration;
using IronMonkey.ApiService.Features.Leads.Pipeline.States;
using IronMonkey.ApiService.Features.Leads.Workflow.Execution;
using IronMonkey.ApiService.Features.Leads.Workflow.Rules;
using IronMonkey.ApiService.Interceptors;
using IronMonkey.ApiService.Notifications;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.Extensions;
using Microsoft.OpenApi;
using Serilog;
using Swashbuckle.AspNetCore.Filters;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using IronMonkey.Data.Communications;

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

            // Keeps /health unhealthy until the central database is migrated.
            builder.Services.AddSingleton<DatabaseReadinessState>();
            builder.Services.AddHealthChecks()
                .AddCheck<DatabaseReadinessHealthCheck>("central-db-ready");
            builder.Services.AddValidatorsFromAssembly(typeof(ConfigureServices).Assembly);
            builder.AddJwtAuthentication();
            builder.AddAuthorization();
            builder.AddCache();
            builder.AddCommunications();

            builder.addApiVersioning();
            builder.addCors();

            builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
            builder.AddPlatformAdmin();
            builder.Services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
            builder.Services.AddScoped<ILeadMergeService, LeadMergeService>();
            builder.Services.AddScoped<IWebFormService, WebFormService>();
            builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
            builder.Services.AddScoped<ILeadRoutingService, LeadRoutingService>();
            builder.Services.AddScoped<IConfigurationUsageService, ConfigurationUsageService>();
            builder.Services.AddScoped<IStateValidationService, StateValidationService>();
            // Phase 5: Activity tracking
            // Singleton + exposed as IInterceptor so TenantDbContextFactory (itself a
            // singleton) picks it up for every tenant context it creates.
            builder.Services.AddSingleton<ActivityChangeInterceptor>();
            builder.Services.AddSingleton<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>(
                sp => sp.GetRequiredService<ActivityChangeInterceptor>());
            builder.Services.AddScoped<IActivityTrackingService, ActivityTrackingService>();

            // Phase 4: Workflow engine and notifications
            builder.Services.AddScoped<INotificationService, NotificationService>();
            builder.Services.AddScoped<IWorkflowRuleEngine, WorkflowRuleEngine>();
            builder.Services.AddScoped<IWorkflowTriggerDispatcher, WorkflowTriggerDispatcher>();
            // Webhook actions call third-party URLs, so they get a short timeout of their
            // own — a slow endpoint must not hold a Hangfire worker open indefinitely.
            builder.Services.AddHttpClient(WorkflowRuleEngine.WebhookClientName,
                client => client.Timeout = TimeSpan.FromSeconds(10));
            builder.Services.AddScoped<WorkflowRuleEvaluationJob>();
            builder.Services.AddScoped<TimeElapsedRuleScanJob>();

            // Durable, tenant-visible execution history for workflow runs. TimeProvider is
            // registered rather than calling DateTime.UtcNow so the recorder's timestamps and
            // the maintenance job's staleness window are controllable in tests.
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.Configure<WorkflowExecutionLogOptions>(
                builder.Configuration.GetSection(WorkflowExecutionLogOptions.SectionName));
            builder.Services.AddScoped<IWorkflowExecutionRecorder, WorkflowExecutionRecorder>();
            builder.Services.AddScoped<WorkflowExecutionMaintenanceJob>();
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

            builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
            builder.Services.AddTransient<Jwt>();

            builder.Services.AddHttpContextAccessor();

            builder.Services.AddScoped<IUserContext, UserContext>();
            builder.Services.AddScoped<ITenantService, TenantService>();

            // Rebases stored tenant connection strings onto the live central server, so
            // Aspire's changing container port does not strand every provisioned tenant.
            builder.Services.AddSingleton<ITenantConnectionStringResolver, TenantConnectionStringResolver>();
        }

        private void AddAuthorization()
        {
            builder.Services.AddScoped<AuthorizationService>();

            builder.Services.AddTransient<IAuthorizationHandler, PermissionAuthorizationHandler>();

            builder.Services.AddTransient<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        }

        private void AddCache()
        {
            builder.Services.AddDistributedMemoryCache();

            builder.Services.AddSingleton<ICacheService, CacheService>();
        }

        private void AddPlatformAdmin()
        {
            builder.Services.Configure<PlatformAdminOptions>(builder.Configuration.GetSection("PlatformAdmin"));
            builder.Services.AddScoped<IPlatformAdminSeeder, PlatformAdminSeeder>();
        }

        /// <summary>
        /// Registers the communications layer.
        ///
        /// Providers are registered unconditionally and report their own configured state;
        /// the registry then indexes only the ones that can actually send. That is what makes
        /// absent credentials disable a channel cleanly rather than crashing at startup or
        /// half-configuring it — the same rule the PlatformAdmin seeder follows.
        /// </summary>
        private void AddCommunications()
        {
            builder.Services.Configure<CommunicationsOptions>(
                builder.Configuration.GetSection(CommunicationsOptions.SectionName));

            // Named client so provider calls get their own timeout and carry no ambient auth.
            builder.Services.AddHttpClient(TwilioMessageProvider.HttpClientName);

            var options = builder.Configuration
                .GetSection(CommunicationsOptions.SectionName)
                .Get<CommunicationsOptions>() ?? new CommunicationsOptions();

            if (options.UseNoopProviders)
            {
                // Explicit opt-in to no-op for every channel. Registered instead of, not
                // alongside, the real providers so a stray credential in user-secrets cannot
                // send a real message from a local run.
                foreach (var channel in Enum.GetValues<MessageChannel>())
                {
                    builder.Services.AddSingleton<IMessageProvider>(sp =>
                        new NoopMessageProvider(channel,
                            sp.GetRequiredService<ILogger<NoopMessageProvider>>()));
                }
            }
            else
            {
                builder.Services.AddSingleton<IMessageProvider, SmtpMessageProvider>();

                builder.Services.AddSingleton<IMessageProvider>(sp =>
                    new TwilioMessageProvider(MessageChannel.Sms,
                        sp.GetRequiredService<IHttpClientFactory>(),
                        sp.GetRequiredService<IOptions<CommunicationsOptions>>(),
                        sp.GetRequiredService<ILogger<TwilioMessageProvider>>()));

                builder.Services.AddSingleton<IMessageProvider>(sp =>
                    new TwilioMessageProvider(MessageChannel.WhatsApp,
                        sp.GetRequiredService<IHttpClientFactory>(),
                        sp.GetRequiredService<IOptions<CommunicationsOptions>>(),
                        sp.GetRequiredService<ILogger<TwilioMessageProvider>>()));
            }

            builder.Services.AddSingleton<IMessageProviderRegistry, MessageProviderRegistry>();
            builder.Services.AddScoped<ISuppressionService, SuppressionService>();
            builder.Services.AddScoped<IMergeFieldResolver, MergeFieldResolver>();
            builder.Services.AddScoped<IMessagingPolicyService, MessagingPolicyService>();
            builder.Services.AddScoped<IMessageDispatcher, MessageDispatcher>();

            // Invitation delivery. Goes through IMessageDispatcher like every other outbound
            // message, so an invitation cannot bypass consent or channel rules.
            builder.Services.AddScoped<IronMonkey.ApiService.Features.UserManagement.Invitations.IInvitationService,
                IronMonkey.ApiService.Features.UserManagement.Invitations.InvitationService>();
            builder.Services.AddScoped<IMessageSendScheduler, HangfireMessageSendScheduler>();
            builder.Services.AddScoped<IInboundMessageService, InboundMessageService>();
            builder.Services.AddScoped<IWebhookTenantResolver, WebhookTenantResolver>();
            builder.Services.AddScoped<MessageDeliveryJob>();
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
