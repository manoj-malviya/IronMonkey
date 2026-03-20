using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using BC = BCrypt.Net.BCrypt;

namespace IronMonkey.ApiService.Authentication.Endpoints;

public class SignupRequestEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/auth/signup", Handle)
        .WithSummary("Submit a new tenant signup request")
        .WithTags("Authentication")
        .AllowAnonymous()
        .WithRequestValidation<Request>();

    public record Request(
        string CompanyName,
        string AdminEmail,
        string AdminPassword,
        string Phone,
        string IndustryType,
        string CompanySize,
        string Address,
        string BillingContact);

    public record Response(Guid SignupRequestId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Company name is required.");
            RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().WithMessage("A valid admin email is required.");
            RuleFor(x => x.AdminPassword).NotEmpty().MinimumLength(8).WithMessage("Password must be at least 8 characters.");
            RuleFor(x => x.Phone).NotEmpty().WithMessage("Phone is required.");
            RuleFor(x => x.IndustryType).NotEmpty().WithMessage("Industry type is required.");
            RuleFor(x => x.CompanySize).NotEmpty().WithMessage("Company size is required.");
            RuleFor(x => x.Address).NotEmpty().WithMessage("Address is required.");
            RuleFor(x => x.BillingContact).NotEmpty().WithMessage("Billing contact is required.");
        }
    }

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        // Check for duplicate signup requests (non-rejected)
        var isDuplicate = await centralDb.SignupRequests
            .AnyAsync(r => r.AdminEmail == request.AdminEmail && r.Status != "Rejected", cancellationToken);

        if (isDuplicate)
        {
            return new ValidationError("A signup request with this email already exists.");
        }

        var passwordHash = BC.HashPassword(request.AdminPassword);

        var signupRequest = SignupRequest.Create(
            request.CompanyName,
            request.AdminEmail,
            passwordHash,
            request.Phone,
            request.IndustryType,
            request.CompanySize,
            request.Address,
            request.BillingContact);

        centralDb.SignupRequests.Add(signupRequest);
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/admin/signup/{signupRequest.Id}", new Response(signupRequest.Id, "Signup request submitted successfully."));
    }
}
