using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.Entities;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.ApiService.Features.Recipes;

public class CreateRecipeEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPost("/api/recipes", Handle)
        .WithSummary("Create a new recipe")
        .WithTags("Platform Admin")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(
        string Name,
        string Description,
        string IconIdentifier,
        string IndustrySlug,
        bool IsBlank,
        RecipeContentModel Content);

    public record Response(Guid RecipeId, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).NotEmpty().MaximumLength(2000);
            RuleFor(x => x.IconIdentifier).NotEmpty().MaximumLength(100);
            RuleFor(x => x.IndustrySlug)
                .NotEmpty()
                .MaximumLength(100)
                .Matches(@"^[a-z0-9\-]+$").WithMessage("IndustrySlug must be lowercase letters, numbers, and hyphens only.");
            RuleFor(x => x.Content).SetValidator(new RecipeContentValidator());
        }
    }

    private static async Task<Results<Created<Response>, ValidationError>> Handle(
        Request request,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var slugExists = await centralDb.IndustryRecipes
            .AnyAsync(r => r.IndustrySlug == request.IndustrySlug && r.IsActive, cancellationToken);

        if (slugExists)
            return new ValidationError("IndustrySlug must be unique among active recipes.");

        var recipe = IndustryRecipe.Create(
            request.Name,
            request.Description,
            request.IndustrySlug,
            request.IconIdentifier,
            request.IsBlank,
            request.Content);

        centralDb.IndustryRecipes.Add(recipe);
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/recipes/{recipe.Id}", new Response(recipe.Id, "Recipe created successfully."));
    }
}
