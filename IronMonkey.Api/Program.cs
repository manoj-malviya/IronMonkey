using System.Text;
using IronMonkey.Api.Entities.Models;
using IronMonkey.Api.JwtFeatures;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using IronMonkey.Api.Extensions;
using IronMonkey.Api.Infrastructures.Tenants;
using IronMonkey.Api.Infrastructures.Middlewares;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpLogging(logging => {
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
});

// tenant setter & getter
builder.Services.AddScopedAs<TenantService>(new[] {typeof(ITenantGetter), typeof(ITenantSetter)});
// IOptions version of tenants
builder.Services.Configure<TenantConfigurationSection>(builder.Configuration);
// middleware that sets the current tenant
builder.Services.AddScoped<MultiTenantServiceMiddleware>();

builder.Services.ConfigureCors();
// builder.Services.ConfigureSqlContext(builder.Configuration);
builder.Services.ConfigureMongoContext(builder.Configuration);
builder.Services.ConfigureRepositoryManager();
builder.Services.AddAutoMapper(typeof(Program));
builder.Services.ConfigureServices();
builder.Services.ConfigureMongoIdentity();

builder.Services.Configure<DataProtectionTokenProviderOptions>(opt =>
    opt.TokenLifespan = TimeSpan.FromHours(2));

var jwtSettings = builder.Configuration.GetSection("JwtSettings");
builder.Services.AddAuthentication(opt =>
{
    opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["validIssuer"],
        ValidAudience = jwtSettings["validAudience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8
            .GetBytes(jwtSettings.GetSection("securityKey").Value))
    };
});

builder.Services.AddScoped<JwtHandler>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// middleware that reads and sets the tenant
app.UseMiddleware<MultiTenantServiceMiddleware>();

app.ApplyMigrations();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseHttpLogging();

app.UseCors("CorsPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();
