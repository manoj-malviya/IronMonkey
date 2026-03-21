using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Common.Auth;
using IronMonkey.Data;
using IronMonkey.Data.Entities;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class CreateTenant : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/tenant", Handle)
        .WithSummary("Creates a new tenant")
        .WithRequestValidation<Request>();

    public record Request(string Name, string Slug, string SubscriptionPlan, string Status);
    public record Response(Guid TenantId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Tenant name is required.");
            RuleFor(x => x.Slug).NotEmpty().WithMessage("Slug is required.");
            RuleFor(x => x.SubscriptionPlan).NotEmpty().WithMessage("Subscription plan is required.");
            RuleFor(x => x.Status).NotEmpty().WithMessage("Status is required.");
        }
    }

    private static async Task<Results<Ok<Response>, ValidationError>> Handle(Request request, AppDbContext db, CancellationToken cancellationToken)
    {
        var isTenantExists = await db.Set<Tenant>()
            .AnyAsync(x => x.Name == request.Name, cancellationToken);

        if (isTenantExists)
        {
            return new ValidationError("A tenant with the same name already exists.");
        }

        var tenant = Tenant.Create(request.Name, request.Slug, request.SubscriptionPlan, request.Status);

        await db.Set<Tenant>().AddAsync(tenant, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(tenant.Id, "Tenant created successfully."));
    }
}