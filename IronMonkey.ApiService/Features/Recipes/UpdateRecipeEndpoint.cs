using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IronMonkey.ApiService.Common;
using IronMonkey.ApiService.Common.Extensions;
using IronMonkey.ApiService.Common.Results;
using IronMonkey.Data;
using IronMonkey.Data.RecipeContent;

namespace IronMonkey.ApiService.Features.Recipes;

public class UpdateRecipeEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) => app
        .MapPut("/api/recipes/{id}", Handle)
        .WithSummary("Update a recipe (full replacement)")
        .WithTags("Platform Admin")
        .RequireAuthorization()
        .WithRequestValidation<Request>();

    public record Request(RecipeContentModel Content);

    public record Response(Guid RecipeId, int NewVersion, string Message);

    public class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(x => x.Content).SetValidator(new RecipeContentValidator());
        }
    }

    private static async Task<Results<Ok<Response>, NotFound, ValidationError>> Handle(
        Guid id,
        Request request,
        CentralDbContext centralDb,
        CancellationToken cancellationToken)
    {
        var recipe = await centralDb.IndustryRecipes
            .FirstOrDefaultAsync(r => r.Id == id && r.IsActive, cancellationToken);

        if (recipe is null)
            return TypedResults.NotFound();

        recipe.UpdateContent(request.Content);
        await centralDb.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new Response(recipe.Id, recipe.Version, "Recipe updated successfully."));
    }
}
